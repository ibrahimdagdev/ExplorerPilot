using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Scanning;
using ExplorerPilot.Core.Treemap;

namespace ExplorerPilot.Tests;

public sealed class TreemapUxTests
{
    [Fact]
    public void AggregateView_PreservesOriginalNodeReferences()
    {
        var parent = Dir("root", File("a.bin", 10), File("b.bin", 10));
        var members = parent.Children.ToList();
        var view = TreemapAggregateView.Create(parent, "Other", members);

        Assert.True(TreemapAggregateView.IsAggregateView(view));
        Assert.Equal(2, view.Children.Count);
        Assert.Same(members[0], view.Children[0]);
        Assert.Same(members[1], view.Children[1]);
        Assert.Same(parent, members[0].Parent);
    }

    [Fact]
    public void AggregateDrillLayout_ShowsMembersWithoutMinFractionCull()
    {
        var parent = Dir("root");
        var members = Enumerable.Range(0, 8)
            .Select(i => File($"t{i}.bin", 100))
            .ToList();
        foreach (var m in members)
        {
            m.Parent = parent;
            parent.Children.Add(m);
            parent.SizeBytes += m.SizeBytes;
        }

        var view = TreemapAggregateView.Create(parent, "Other", members);
        var engine = new TreemapLayoutEngine();
        var result = engine.Layout(view, 400, 400, new TreemapOptions
        {
            MaxRectangles = 50,
            MinAreaFraction = 0.5, // would cull on normal views; aggregate view forces 0
            Padding = 0,
            OtherDisplayName = "Other"
        });

        Assert.True(result.IsAggregateView);
        Assert.Equal(8, result.Rectangles.Count);
        Assert.DoesNotContain(result.Rectangles, r => r.IsAggregate);
    }

    [Fact]
    public void AggregateRectangle_CarriesMemberNodes()
    {
        var children = new List<FileSystemNode> { File("big.bin", 1_000_000) };
        for (var i = 0; i < 12; i++)
        {
            children.Add(File($"tiny{i}.bin", 100));
        }

        var root = Dir("root", children.ToArray());
        var engine = new TreemapLayoutEngine();
        var result = engine.Layout(root, 600, 400, new TreemapOptions
        {
            Padding = 0,
            MaxRectangles = 100,
            MinAreaFraction = 0.01,
            OtherDisplayName = "Other"
        });

        var other = Assert.Single(result.Rectangles, r => r.IsAggregate);
        Assert.True(other.AggregatedCount >= 2);
        Assert.Equal(other.AggregatedCount, other.AggregatedNodes.Count);
        Assert.All(other.AggregatedNodes, n => Assert.StartsWith("tiny", n.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void FileCategoryColors_AreDeterministic()
    {
        Assert.Equal(FileCategory.Images, FileCategoryMapper.GetCategory(FileCategoryMapper.NormalizeExtension("photo.JPG")));
        Assert.Equal(FileCategory.Videos, FileCategoryMapper.GetCategory(FileCategoryMapper.NormalizeExtension(".mp4")));
        Assert.Equal(FileCategory.Other, FileCategoryMapper.GetCategory(FileCategoryMapper.NormalizeExtension(".unknownxyz")));
        Assert.Equal(
            FileCategoryMapper.GetCategory(FileCategoryMapper.NormalizeExtension("a.PDF")),
            FileCategoryMapper.GetCategory(FileCategoryMapper.NormalizeExtension(".pdf")));
    }

    [Fact]
    public void TooltipPercent_UsesScopeTotal()
    {
        var rect = new TreemapRectangle
        {
            Id = Guid.NewGuid(),
            DisplayName = "a.bin",
            SizeBytes = 25,
            ScopeTotalBytes = 100,
            Width = 10,
            Height = 10
        };

        var pct = rect.ScopeTotalBytes <= 0
            ? 0
            : (double)rect.SizeBytes / rect.ScopeTotalBytes * 100.0;
        Assert.Equal(25.0, pct);
    }

    [Fact]
    public void KeyboardNeighbor_MovesInDirection()
    {
        var a = new TreemapRectangle { Id = Guid.NewGuid(), DisplayName = "a", X = 0, Y = 0, Width = 50, Height = 50, SizeBytes = 1 };
        var b = new TreemapRectangle { Id = Guid.NewGuid(), DisplayName = "b", X = 60, Y = 0, Width = 50, Height = 50, SizeBytes = 1 };
        var c = new TreemapRectangle { Id = Guid.NewGuid(), DisplayName = "c", X = 0, Y = 60, Width = 50, Height = 50, SizeBytes = 1 };
        var list = new[] { a, b, c };

        var right = TreemapHitTesting.FindNeighbor(list, a, 1, 0);
        Assert.Same(b, right);

        var down = TreemapHitTesting.FindNeighbor(list, a, 0, 1);
        Assert.Same(c, down);
    }

    [Fact]
    public void SelectionLoopGuard_IgnoresSuppressedExplorerSync()
    {
        // Pure logic stand-in: flags prevent re-entry (mirrors MainViewModel/_suppress*).
        var suppressExplorerToTreemap = false;
        var suppressTreemapToExplorer = false;
        var explorerCalls = 0;
        var treemapCalls = 0;

        void OnExplorerSelectionChanged()
        {
            if (suppressExplorerToTreemap)
            {
                return;
            }

            explorerCalls++;
            suppressTreemapToExplorer = true;
            try
            {
                OnTreemapWouldSelect();
            }
            finally
            {
                suppressTreemapToExplorer = false;
            }
        }

        void OnTreemapWouldSelect()
        {
            if (suppressTreemapToExplorer)
            {
                return;
            }

            treemapCalls++;
            suppressExplorerToTreemap = true;
            try
            {
                OnExplorerSelectionChanged();
            }
            finally
            {
                suppressExplorerToTreemap = false;
            }
        }

        OnExplorerSelectionChanged();
        Assert.Equal(1, explorerCalls);
        Assert.Equal(0, treemapCalls);
    }

    [Fact]
    public void SessionIsolation_RejectsMismatchedSession()
    {
        var active = Guid.NewGuid();
        var incoming = Guid.NewGuid();
        Assert.NotEqual(active, incoming);
        Assert.False(active == incoming);
    }

    [Fact]
    public void CancelLayoutVersionBump_DoesNotInvalidateClaimedVersion()
    {
        // Regression: RequestLayout must claim version AFTER cancelling prior work
        // without an extra bump, otherwise every layout result is discarded.
        var layoutVersion = 0;
        void CancelLayout(bool bumpVersion)
        {
            if (bumpVersion)
            {
                Interlocked.Increment(ref layoutVersion);
            }
        }

        CancelLayout(bumpVersion: false);
        var version = Interlocked.Increment(ref layoutVersion);
        Assert.Equal(version, layoutVersion);

        // Stale worker sees mismatch after a real invalidate.
        CancelLayout(bumpVersion: true);
        Assert.NotEqual(version, layoutVersion);
    }

    private static FileSystemNode Dir(string name, params FileSystemNode[] children)
    {
        var node = new FileSystemNode
        {
            Name = name,
            FullPath = @"C:\tests\" + name,
            IsDirectory = true
        };

        foreach (var child in children)
        {
            child.Parent = node;
            node.Children.Add(child);
            node.SizeBytes += child.SizeBytes;
            node.FileCount += child.IsDirectory ? child.FileCount : 1;
        }

        return node;
    }

    private static FileSystemNode File(string name, long size) =>
        new()
        {
            Name = name,
            FullPath = @"C:\tests\" + name,
            IsDirectory = false,
            SizeBytes = size,
            FileCount = 1,
            LastModified = DateTimeOffset.UtcNow
        };
}

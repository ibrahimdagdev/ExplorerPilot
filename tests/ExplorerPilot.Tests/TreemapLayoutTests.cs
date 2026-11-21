using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Treemap;

namespace ExplorerPilot.Tests;

public sealed class TreemapLayoutTests
{
    private readonly TreemapLayoutEngine _engine = new();

    [Fact]
    public void Layout_AreasProportionalToSize()
    {
        var root = Dir("root",
            File("a.bin", 100),
            File("b.bin", 300));

        var result = _engine.Layout(root, 400, 200, new TreemapOptions { Padding = 0, MaxRectangles = 50 });
        Assert.Equal(2, result.Rectangles.Count);

        var totalArea = result.Rectangles.Sum(r => r.Area);
        var a = result.Rectangles.Single(r => r.DisplayName == "a.bin");
        var b = result.Rectangles.Single(r => r.DisplayName == "b.bin");

        Assert.InRange(a.Area / totalArea, 0.24, 0.26);
        Assert.InRange(b.Area / totalArea, 0.74, 0.76);
    }

    [Fact]
    public void Layout_IsDeterministic_ForIdenticalInput()
    {
        var root = Dir("root",
            File("x.bin", 50),
            File("y.bin", 50),
            File("z.bin", 100));

        var a = Snapshot(_engine.Layout(root, 640, 480, new TreemapOptions { Padding = 1 }));
        var b = Snapshot(_engine.Layout(root, 640, 480, new TreemapOptions { Padding = 1 }));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Layout_IgnoresZeroSizedNodes()
    {
        var root = Dir("root",
            File("empty.bin", 0),
            File("data.bin", 1000));

        var result = _engine.Layout(root, 300, 300, new TreemapOptions { Padding = 0 });
        Assert.Single(result.Rectangles);
        Assert.Equal("data.bin", result.Rectangles[0].DisplayName);
    }

    [Fact]
    public void Layout_EmptyDirectory_ReturnsNoRectangles()
    {
        var root = Dir("empty");
        var result = _engine.Layout(root, 200, 200);
        Assert.Empty(result.Rectangles);
    }

    [Fact]
    public void Layout_NestedDirectories_UsesChildSizes()
    {
        var photos = Dir("photos",
            File("a.jpg", 200),
            File("b.jpg", 200));
        var root = Dir("root", photos, File("readme.txt", 100));

        var result = _engine.Layout(root, 500, 400, new TreemapOptions { Padding = 0 });
        Assert.Equal(2, result.Rectangles.Count);
        Assert.Contains(result.Rectangles, r => r.DisplayName == "photos" && r.IsDirectory && r.SizeBytes == 400);
        Assert.Contains(result.Rectangles, r => r.DisplayName == "readme.txt" && !r.IsDirectory);
    }

    [Fact]
    public void Layout_LargeChildCount_RespectsMaxRectangles()
    {
        var children = Enumerable.Range(0, 1000)
            .Select(i => File($"f{i:D4}.bin", 1000 + i))
            .ToArray();
        var root = Dir("root", children);

        var result = _engine.Layout(root, 1000, 800, new TreemapOptions
        {
            Padding = 1,
            MaxRectangles = 50,
            MinAreaFraction = 0
        });

        Assert.True(result.Rectangles.Count <= 50);
        Assert.Contains(result.Rectangles, r => r.IsAggregate);
        Assert.True(result.Rectangles.Sum(r => r.SizeBytes) <= root.SizeBytes);
    }

    [Fact]
    public void Layout_RectanglesStayWithinBounds()
    {
        var root = Dir("root",
            File("a.bin", 10),
            File("b.bin", 20),
            File("c.bin", 30),
            File("d.bin", 40));

        const double w = 320;
        const double h = 240;
        var result = _engine.Layout(root, w, h, new TreemapOptions { Padding = 0 });

        foreach (var r in result.Rectangles)
        {
            Assert.True(r.X >= -0.01);
            Assert.True(r.Y >= -0.01);
            Assert.True(r.X + r.Width <= w + 0.01);
            Assert.True(r.Y + r.Height <= h + 0.01);
            Assert.True(r.Width > 0);
            Assert.True(r.Height > 0);
        }
    }

    [Fact]
    public void Layout_RectanglesDoNotOverlap()
    {
        var root = Dir("root",
            File("a.bin", 100),
            File("b.bin", 200),
            File("c.bin", 300),
            File("d.bin", 400));

        var result = _engine.Layout(root, 400, 400, new TreemapOptions { Padding = 0 });
        for (var i = 0; i < result.Rectangles.Count; i++)
        {
            for (var j = i + 1; j < result.Rectangles.Count; j++)
            {
                Assert.False(Overlaps(result.Rectangles[i], result.Rectangles[j]),
                    $"{result.Rectangles[i].DisplayName} overlaps {result.Rectangles[j].DisplayName}");
            }
        }
    }

    [Fact]
    public void Layout_AggregatesTinyNodes()
    {
        var children = new List<FileSystemNode> { File("big.bin", 1_000_000) };
        for (var i = 0; i < 20; i++)
        {
            children.Add(File($"tiny{i}.bin", 100));
        }

        var root = Dir("root", children.ToArray());
        var result = _engine.Layout(root, 600, 400, new TreemapOptions
        {
            Padding = 0,
            MaxRectangles = 100,
            MinAreaFraction = 0.01,
            OtherDisplayName = "Other"
        });

        Assert.Contains(result.Rectangles, r => r.IsAggregate && r.DisplayName == "Other" && r.AggregatedCount >= 2);
        Assert.DoesNotContain(result.Rectangles, r => r.DisplayName.StartsWith("tiny", StringComparison.Ordinal));
    }

    [Fact]
    public void HitTest_ReturnsContainingRectangle()
    {
        var root = Dir("root", File("only.bin", 100));
        var result = _engine.Layout(root, 100, 100, new TreemapOptions { Padding = 0 });
        Assert.Single(result.Rectangles);

        var hit = TreemapLayoutEngine.HitTest(result.Rectangles, 50, 50);
        Assert.NotNull(hit);
        Assert.Equal("only.bin", hit!.DisplayName);

        Assert.Null(TreemapLayoutEngine.HitTest(result.Rectangles, -1, -1));
    }

    [Fact]
    public void Layout_InvalidViewport_ReturnsEmpty()
    {
        var root = Dir("root", File("a.bin", 10));
        Assert.Empty(_engine.Layout(root, 0, 100).Rectangles);
        Assert.Empty(_engine.Layout(root, 100, 0).Rectangles);
    }

    [Fact]
    public void NavigationStack_Semantics_ParentChild()
    {
        // Pure data navigation used by TreemapTabViewModel.
        var child = Dir("child", File("f.bin", 10));
        var root = Dir("root", child);
        var stack = new List<FileSystemNode> { root };
        stack.Add(child);
        Assert.Equal(2, stack.Count);
        stack.RemoveAt(stack.Count - 1);
        Assert.Same(root, stack[^1]);
        stack.Clear();
        stack.Add(root);
        Assert.Single(stack);
    }

    [Fact]
    public void Layout_Performance_LargeFanout_CompletesQuickly()
    {
        var children = Enumerable.Range(0, 5000)
            .Select(i => File($"n{i}.bin", 100 + (i % 50)))
            .ToArray();
        var root = Dir("root", children);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = _engine.Layout(root, 1200, 800, new TreemapOptions { MaxRectangles = 400, Padding = 1 });
        sw.Stop();

        Assert.True(result.Rectangles.Count <= 400);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"Layout took {sw.ElapsedMilliseconds} ms");
    }

    private static string Snapshot(TreemapLayoutResult result) =>
        string.Join("|", result.Rectangles
            .OrderBy(r => r.DisplayName, StringComparer.Ordinal)
            .Select(r =>
                $"{r.DisplayName}:{r.SizeBytes}:{r.X:0.###},{r.Y:0.###},{r.Width:0.###},{r.Height:0.###}:{r.IsAggregate}"));

    private static bool Overlaps(TreemapRectangle a, TreemapRectangle b)
    {
        const double eps = 0.01;
        return a.X < b.X + b.Width - eps
               && a.X + a.Width > b.X + eps
               && a.Y < b.Y + b.Height - eps
               && a.Y + a.Height > b.Y + eps;
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
            node.DirectoryCount += child.IsDirectory ? child.DirectoryCount + 1 : 0;
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
            FileCount = 1
        };
}

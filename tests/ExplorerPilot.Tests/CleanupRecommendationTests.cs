using ExplorerPilot.Core.Cleanup;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExplorerPilot.Tests;

public sealed class CleanupRecommendationTests
{
    private static readonly DateTimeOffset FixedNow = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Detects_LargeAndOldFiles()
    {
        var root = Root(@"D:\Data",
            File("big.bin", 200L * 1024 * 1024, FixedNow.AddDays(-10)),
            File("ancient.txt", 1024, FixedNow.AddDays(-400)));

        var result = await Analyze(root, largeMb: 100, oldDays: 365);

        Assert.Contains(result.Candidates, c => c.Category == CleanupCategory.LargeFiles && c.Name == "big.bin");
        Assert.Contains(result.Candidates, c => c.Category == CleanupCategory.OldFiles && c.Name == "ancient.txt");
    }

    [Fact]
    public async Task Classifies_TemporaryAndOldLogs()
    {
        var root = Root(@"D:\Data",
            File("cache.tmp", 50, FixedNow.AddDays(-1)),
            File("app.log", 80, FixedNow.AddDays(-120)));

        var result = await Analyze(root, largeMb: 500, oldDays: 365);

        Assert.Contains(result.Candidates, c => c.Category == CleanupCategory.TemporaryFiles && c.Name == "cache.tmp");
        Assert.Contains(result.Candidates, c => c.Category == CleanupCategory.OldLogFiles && c.Name == "app.log");
        Assert.All(
            result.Candidates.Where(c => c.Category is CleanupCategory.TemporaryFiles or CleanupCategory.OldLogFiles),
            c => Assert.Equal(CleanupRiskLevel.Low, c.Risk));
    }

    [Fact]
    public async Task Detects_EmptyFolders()
    {
        var empty = Dir("empty");
        empty.ScanStatus = NodeScanStatus.Completed;
        var root = Root(@"D:\Data", empty, File("keep.txt", 10, FixedNow));

        var result = await Analyze(root, largeMb: 500, oldDays: 3650);

        Assert.Contains(result.Candidates, c => c.Category == CleanupCategory.EmptyFolders && c.Name == "empty");
    }

    [Fact]
    public async Task Excludes_ProtectedSystemPaths()
    {
        Assert.True(CleanupProtectedPaths.IsExcluded(@"C:\Windows\System32\drivers\etc\hosts"));
        Assert.True(CleanupProtectedPaths.IsExcluded(@"C:\Program Files\App\readme.txt"));
        Assert.False(CleanupProtectedPaths.IsExcluded(@"D:\Projects\work.tmp"));

        var root = Root(@"C:\Windows",
            File("legacy.tmp", 100, FixedNow.AddDays(-10)));

        // Children under excluded root segments are skipped when walking from a non-root match;
        // walking starts at C:\Windows which is the scan root (allowed), but child under Windows segment...
        // Root itself is not excluded via isRoot; child path still contains Windows → excluded.
        var result = await Analyze(root, largeMb: 1, oldDays: 1);
        Assert.DoesNotContain(result.Candidates, c => c.Name.Equals("legacy.tmp", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.ExcludedProtectedPaths > 0);
    }

    [Fact]
    public async Task VerifiedDuplicates_AndHardLinks_DoNotOvercountSavings()
    {
        var root = Root(@"D:\Data",
            File("a.bin", 1000, FixedNow),
            File("b.bin", 1000, FixedNow),
            File("c-hardlink.bin", 1000, FixedNow));

        var group = new DuplicateGroup
        {
            ContentHashSha256 = "abc",
            SizeBytes = 1000,
            Files =
            [
                new DuplicateFileInfo
                {
                    Name = "a.bin",
                    FullPath = @"D:\Data\a.bin",
                    SizeBytes = 1000,
                    FileIdentityKey = "vol:1"
                },
                new DuplicateFileInfo
                {
                    Name = "b.bin",
                    FullPath = @"D:\Data\b.bin",
                    SizeBytes = 1000,
                    FileIdentityKey = "vol:2"
                },
                new DuplicateFileInfo
                {
                    Name = "c-hardlink.bin",
                    FullPath = @"D:\Data\c-hardlink.bin",
                    SizeBytes = 1000,
                    FileIdentityKey = "vol:1" // hard-link to a
                }
            ],
            PotentialSavingsBytes = 1000
        };

        var result = await Analyze(root, largeMb: 500, oldDays: 3650, groups: [group]);
        var dupes = result.Candidates.Where(c => c.Category == CleanupCategory.VerifiedDuplicates).ToList();

        Assert.Single(dupes); // only b.bin (extra physical); hard-link extra skipped (0 savings)
        Assert.Equal(1000, dupes[0].PotentialSavingsBytes);
        Assert.Equal("b.bin", dupes[0].Name);
    }

    [Fact]
    public async Task DoubleCount_SamePathInMultipleCategories_CountsOnce()
    {
        // Large + old for same file.
        var root = Root(@"D:\Data",
            File("big-old.bin", 200L * 1024 * 1024, FixedNow.AddDays(-400)));

        var result = await Analyze(root, largeMb: 100, oldDays: 365);
        Assert.True(result.Candidates.Count(c => c.Name == "big-old.bin") >= 2);

        var unique = CleanupSavingsAggregator.SumUniquePotential(result.Candidates.Where(c => c.Name == "big-old.bin"));
        Assert.Equal(200L * 1024 * 1024, unique);
        Assert.Equal(unique, result.UniquePotentialSavingsBytes);
    }

    [Fact]
    public void SelectedTotals_UseUniquePathSavings()
    {
        var a1 = Candidate("a", CleanupCategory.LargeFiles, 100);
        var a2 = Candidate("a", CleanupCategory.OldFiles, 100);
        var b = Candidate("b", CleanupCategory.TemporaryFiles, 40);

        Assert.Equal(140, CleanupSavingsAggregator.SumUniquePotential([a1, a2, b]));
        Assert.Equal(2, CleanupSavingsAggregator.CountUniquePaths([a1, a2, b]));
    }

    [Fact]
    public async Task Cancellation_Throws()
    {
        var files = Enumerable.Range(0, 2_000).Select(i => File($"f{i}.bin", 10, FixedNow)).ToArray();
        var root = Root(@"D:\Big", files);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CleanupRecommendationService(NullLogger<CleanupRecommendationService>.Instance)
                .AnalyzeAsync(root, new CleanupRecommendationCriteria
                {
                    ScanSessionId = Guid.NewGuid(),
                    LargeFileMinBytes = 1,
                    OldFileMinAge = TimeSpan.FromDays(1),
                    UtcNow = FixedNow,
                    MaxRecommendations = 50_000
                }, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task LargeScan_CompletesWithinBudget()
    {
        var files = Enumerable.Range(0, 6_000)
            .Select(i => File($"n{i}.dat", 50 + i, FixedNow.AddDays(-(i % 500))))
            .ToArray();
        var root = Root(@"D:\Perf", files);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await Analyze(root, largeMb: 1, oldDays: 30, max: 5_000);
        sw.Stop();

        Assert.Equal(5_000, result.Candidates.Count);
        Assert.True(result.ResultsTruncated);
        // Full walk completes even when bounded (top-N keeps largest).
        Assert.True(result.WalkedNodes >= 6_000);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"Took {sw.Elapsed}");
    }

    [Fact]
    public async Task Skips_ReparseAndInaccessibleNodes()
    {
        var bad = File("ghost.tmp", 10, FixedNow);
        bad.ScanStatus = NodeScanStatus.SkippedReparsePoint;
        var root = Root(@"D:\Data", bad, File("ok.tmp", 10, FixedNow));

        var result = await Analyze(root, largeMb: 500, oldDays: 3650);
        Assert.DoesNotContain(result.Candidates, c => c.Name == "ghost.tmp");
        Assert.Contains(result.Candidates, c => c.Name == "ok.tmp");
        Assert.True(result.SkippedUnsafeNodes >= 1);
    }

    [Fact]
    public async Task HighRisk_ExecutablesFlagged()
    {
        Assert.True(CleanupProtectedPaths.IsHighRiskExtension("setup.exe"));
        var result = await Analyze(Root(@"D:\Data", File("tool.exe", 200L * 1024 * 1024, FixedNow)), 100, 3650);
        Assert.Equal(CleanupRiskLevel.High, result.Candidates.Single(c => c.Name == "tool.exe").Risk);
    }

    private static async Task<CleanupRecommendationResult> Analyze(
        FileSystemNode root,
        int largeMb,
        int oldDays,
        int max = 5_000,
        IReadOnlyList<DuplicateGroup>? groups = null)
    {
        var service = new CleanupRecommendationService(NullLogger<CleanupRecommendationService>.Instance);
        return await service.AnalyzeAsync(
            root,
            new CleanupRecommendationCriteria
            {
                ScanSessionId = Guid.NewGuid(),
                LargeFileMinBytes = largeMb * 1024L * 1024L,
                OldFileMinAge = TimeSpan.FromDays(oldDays),
                OldLogMinAge = TimeSpan.FromDays(90),
                MaxRecommendations = max,
                UtcNow = FixedNow
            },
            groups);
    }

    private static CleanupCandidate Candidate(string name, CleanupCategory category, long savings) =>
        new()
        {
            Id = $"{(int)category}|D:\\Data\\{name}",
            Name = name,
            FullPath = $@"D:\Data\{name}",
            PathKey = $@"D:\Data\{name}",
            SizeBytes = savings,
            PotentialSavingsBytes = savings,
            Category = category,
            Risk = CleanupRiskLevel.Low,
            ReasonKey = "x"
        };

    private static FileSystemNode Root(string path, params FileSystemNode[] children)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : path;
        var root = new FileSystemNode
        {
            Name = name,
            FullPath = path,
            IsDirectory = true,
            ScanStatus = NodeScanStatus.Completed
        };

        foreach (var child in children)
        {
            Attach(root, child);
        }

        return root;
    }

    private static FileSystemNode Dir(string name) =>
        new()
        {
            Name = name,
            FullPath = @"D:\tests\" + name,
            IsDirectory = true,
            ScanStatus = NodeScanStatus.Completed
        };

    private static FileSystemNode File(string name, long size, DateTimeOffset modified) =>
        new()
        {
            Name = name,
            FullPath = @"D:\tests\" + name,
            IsDirectory = false,
            SizeBytes = size,
            FileCount = 1,
            LastModified = modified,
            ScanStatus = NodeScanStatus.Completed
        };

    private static void Attach(FileSystemNode parent, FileSystemNode child)
    {
        FileSystemNode attached;
        if (child.IsDirectory)
        {
            attached = new FileSystemNode
            {
                Name = child.Name,
                FullPath = Path.Combine(parent.FullPath, child.Name),
                IsDirectory = true,
                SizeBytes = child.SizeBytes,
                FileCount = child.FileCount,
                DirectoryCount = child.DirectoryCount,
                LastModified = child.LastModified,
                ScanStatus = child.ScanStatus
            };
            foreach (var nested in child.Children)
            {
                Attach(attached, nested);
            }
        }
        else
        {
            attached = new FileSystemNode
            {
                Name = child.Name,
                FullPath = Path.Combine(parent.FullPath, child.Name),
                IsDirectory = false,
                SizeBytes = child.SizeBytes,
                FileCount = 1,
                LastModified = child.LastModified,
                ScanStatus = child.ScanStatus
            };
        }

        attached.Parent = parent;
        parent.Children.Add(attached);
        parent.SizeBytes += attached.SizeBytes;
        parent.FileCount += attached.IsDirectory ? attached.FileCount : 1;
        parent.DirectoryCount += attached.IsDirectory ? 1 + attached.DirectoryCount : 0;
    }
}

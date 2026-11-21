using System.Text;
using System.Text.Json;
using ExplorerPilot.Core.Cleanup;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Reporting;
using ExplorerPilot.Infrastructure.Reporting;
using ExplorerPilot.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExplorerPilot.Tests;

public sealed class CleanupStabilizationTests
{
    private static readonly DateTimeOffset FixedNow = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PathKey_NormalizesWindowsPaths()
    {
        var a = CleanupProtectedPaths.NormalizeKey(@"c:/Users\\Pc//Desktop\file.TXT");
        var b = CleanupProtectedPaths.NormalizeKey(@"C:\Users\Pc\Desktop\file.TXT");
        Assert.Equal(a, b, ignoreCase: true);
        Assert.StartsWith("C:", a);
        Assert.False(a.Contains('/'));
        Assert.False(a.Contains(@"\\", StringComparison.Ordinal));
        Assert.True(CleanupProtectedPaths.PathKeysEqual(
            @"\\?\C:\Data\a.tmp",
            @"C:\Data\a.tmp"));
    }

    [Fact]
    public void PathKey_DedupesAcrossCategories()
    {
        var key = CleanupProtectedPaths.NormalizeKey(@"D:\Data\big-old.bin");
        var rows = new[]
        {
            Cand(key, CleanupCategory.LargeFiles, 200, CleanupRiskLevel.Medium),
            Cand(key, CleanupCategory.OldFiles, 200, CleanupRiskLevel.Medium)
        };

        Assert.Equal(200, CleanupSavingsAggregator.SumUniquePotential(rows));
        Assert.Equal(200, CleanupSavingsAggregator.SumUniqueSize(rows));
        Assert.Equal(1, CleanupSavingsAggregator.CountUniquePaths(rows));
    }

    [Fact]
    public void ImportantUserProfilePaths_AreHighRisk()
    {
        Assert.True(CleanupProtectedPaths.IsImportantUserProfilePath(@"C:\Users\Ada\Documents\thesis.docx"));
        Assert.True(CleanupProtectedPaths.IsImportantUserProfilePath(@"C:\Users\Ada\Desktop\shot.png"));
        Assert.True(CleanupProtectedPaths.IsImportantUserProfilePath(@"C:\Users\Ada\Pictures\img.jpg"));
        Assert.True(CleanupProtectedPaths.IsHighRiskLocation(@"C:\Users\Ada\Downloads\setup.exe"));
        Assert.False(CleanupProtectedPaths.IsImportantUserProfilePath(@"D:\Scratch\temp.tmp"));
    }

    [Fact]
    public async Task UserProfileDocuments_ReceiveHighRisk()
    {
        var root = Root(@"C:\Users\Ada\Documents",
            MakeFile("old.txt", 50, FixedNow.AddDays(-400)));
        var result = await Analyze(root, largeMb: 500, oldDays: 365);
        var candidate = Assert.Single(result.Candidates, c => c.Name == "old.txt");
        Assert.Equal(CleanupRiskLevel.High, candidate.Risk);
    }

    [Fact]
    public async Task TopN_KeepsLargeCandidateEncounteredLate()
    {
        var small = Enumerable.Range(0, 80)
            .Select(i => MakeFile($"small{i}.tmp", 1_000, FixedNow))
            .ToArray();
        var huge = MakeFile("huge.tmp", 50_000_000, FixedNow);
        var root = Root(@"D:\Perf", small.Concat([huge]).ToArray());

        var result = await Analyze(root, largeMb: 500, oldDays: 3650, max: 20);

        Assert.Equal(20, result.Candidates.Count);
        Assert.True(result.ResultsTruncated);
        Assert.Contains(result.Candidates, c => c.Name == "huge.tmp");
        Assert.Equal(50_000_000, result.Candidates.Max(c => c.PotentialSavingsBytes));
    }

    [Fact]
    public async Task IncompleteScan_IsFlaggedOnResult()
    {
        var root = Root(@"D:\Data", MakeFile("a.tmp", 10, FixedNow));
        var service = new CleanupRecommendationService(NullLogger<CleanupRecommendationService>.Instance);
        var result = await service.AnalyzeAsync(
            root,
            new CleanupRecommendationCriteria
            {
                ScanSessionId = Guid.NewGuid(),
                LargeFileMinBytes = 500L * 1024 * 1024,
                OldFileMinAge = TimeSpan.FromDays(3650),
                UtcNow = FixedNow,
                MaxRecommendations = 100
            },
            fromIncompleteScan: true);

        Assert.True(result.FromIncompleteScan);
        Assert.Contains(result.Candidates, c => c.Category == CleanupCategory.TemporaryFiles);
    }

    [Fact]
    public void SelectionTotals_IncludeRiskDistribution()
    {
        var selected = new[]
        {
            Cand(@"D:\a", CleanupCategory.TemporaryFiles, 10, CleanupRiskLevel.Low),
            Cand(@"D:\a", CleanupCategory.OldFiles, 10, CleanupRiskLevel.Medium), // same path, higher risk wins
            Cand(@"D:\b", CleanupCategory.LargeFiles, 100, CleanupRiskLevel.High),
            Cand(@"D:\c", CleanupCategory.OldLogFiles, 5, CleanupRiskLevel.Low)
        };

        var (low, medium, high) = CleanupSavingsAggregator.CountUniquePathsByHighestRisk(selected);
        Assert.Equal(1, low);
        Assert.Equal(1, medium); // D:\a
        Assert.Equal(1, high);
        Assert.Equal(3, CleanupSavingsAggregator.CountUniquePaths(selected));
        Assert.Equal(115, CleanupSavingsAggregator.SumUniquePotential(selected));
    }

    [Fact]
    public async Task HardLinkDuplicates_DoNotExceedGroupBudget()
    {
        var root = Root(@"D:\Data",
            MakeFile("a.bin", 1000, FixedNow),
            MakeFile("b.bin", 1000, FixedNow),
            MakeFile("c.bin", 1000, FixedNow));

        var group = new DuplicateGroup
        {
            ContentHashSha256 = "hash",
            SizeBytes = 1000,
            Files =
            [
                new DuplicateFileInfo { Name = "a.bin", FullPath = @"D:\Data\a.bin", SizeBytes = 1000, FileIdentityKey = "1" },
                new DuplicateFileInfo { Name = "b.bin", FullPath = @"D:\Data\b.bin", SizeBytes = 1000, FileIdentityKey = "2" },
                new DuplicateFileInfo { Name = "c.bin", FullPath = @"D:\Data\c.bin", SizeBytes = 1000, FileIdentityKey = "1" }
            ],
            PotentialSavingsBytes = 1000
        };

        var result = await Analyze(root, largeMb: 500, oldDays: 3650, groups: [group]);
        var dupes = result.Candidates.Where(c => c.Category == CleanupCategory.VerifiedDuplicates).ToList();
        Assert.Single(dupes);
        Assert.Equal(1000, dupes.Sum(d => d.PotentialSavingsBytes));
    }

    [Theory]
    [InlineData(ReportFormat.Csv)]
    [InlineData(ReportFormat.Html)]
    [InlineData(ReportFormat.Json)]
    public async Task Export_WritesDisclaimerAndNoPartialOnCancel(ReportFormat format)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ep-cleanup-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ext = format switch
            {
                ReportFormat.Html => ".html",
                ReportFormat.Json => ".json",
                _ => ".csv"
            };
            var path = Path.Combine(dir, "preview" + ext);
            var candidates = new[]
            {
                Cand(@"D:\Data\a.tmp", CleanupCategory.TemporaryFiles, 42, CleanupRiskLevel.Low)
            };

            var service = new CleanupExportService(NullLogger<CleanupExportService>.Instance);
            var result = await service.ExportAsync(new CleanupExportOptions
            {
                Candidates = candidates,
                Format = format,
                DestinationPath = path,
                ScanSessionId = Guid.NewGuid(),
                UniquePotentialSavingsBytes = 42,
                UniquePathCount = 1,
                Labels = new CleanupExportLabels
                {
                    Disclaimer = "NOT A DELETE INSTRUCTION",
                    ReasonFormatter = c => "review"
                }
            });

            Assert.True(result.Succeeded);
            Assert.True(File.Exists(path));
            var text = await File.ReadAllTextAsync(path, Encoding.UTF8);
            Assert.Contains("NOT A DELETE INSTRUCTION", text, StringComparison.Ordinal);

            if (format == ReportFormat.Json)
            {
                using var doc = JsonDocument.Parse(text);
                Assert.False(doc.RootElement.GetProperty("isDeletionInstruction").GetBoolean());
            }

            // Cancelled export must not leave destination.
            var cancelPath = Path.Combine(dir, "cancel" + ext);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var cancelled = await service.ExportAsync(new CleanupExportOptions
            {
                Candidates = candidates,
                Format = format,
                DestinationPath = cancelPath,
                ScanSessionId = Guid.NewGuid(),
                Labels = new CleanupExportLabels()
            }, cancellationToken: cts.Token);
            Assert.True(cancelled.WasCancelled);
            Assert.False(File.Exists(cancelPath));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Export_StaleSession_DoesNotFinalize()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ep-cleanup-stale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "stale.csv");
            var session = Guid.NewGuid();
            var service = new CleanupExportService(NullLogger<CleanupExportService>.Instance);
            var result = await service.ExportAsync(
                new CleanupExportOptions
                {
                    Candidates = [Cand(@"D:\a.tmp", CleanupCategory.TemporaryFiles, 1, CleanupRiskLevel.Low)],
                    Format = ReportFormat.Csv,
                    DestinationPath = path,
                    ScanSessionId = session,
                    Labels = new CleanupExportLabels()
                },
                sessionStillValid: _ => false);

            Assert.True(result.WasStaleSession);
            Assert.False(File.Exists(path));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ProtectedPaths_StillExcluded()
    {
        Assert.True(CleanupProtectedPaths.IsExcluded(@"C:\Windows\explorer.exe"));
        Assert.True(CleanupProtectedPaths.IsExcluded(@"C:\Program Files\App\a.dll"));
        Assert.False(CleanupProtectedPaths.IsExcluded(@"D:\Games\cache.tmp"));
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

    private static CleanupCandidate Cand(
        string pathKey,
        CleanupCategory category,
        long bytes,
        CleanupRiskLevel risk) =>
        new()
        {
            Id = $"{(int)category}|{pathKey}",
            Name = Path.GetFileName(pathKey),
            FullPath = pathKey,
            PathKey = CleanupProtectedPaths.NormalizeKey(pathKey),
            SizeBytes = bytes,
            PotentialSavingsBytes = bytes,
            Category = category,
            Risk = risk,
            ReasonKey = "Cleanup_Reason_Temporary"
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

    private static FileSystemNode MakeFile(string name, long size, DateTimeOffset modified) =>
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
        var attached = new FileSystemNode
        {
            Name = child.Name,
            FullPath = Path.Combine(parent.FullPath, child.Name),
            IsDirectory = child.IsDirectory,
            SizeBytes = child.SizeBytes,
            FileCount = child.IsDirectory ? child.FileCount : 1,
            DirectoryCount = child.DirectoryCount,
            LastModified = child.LastModified,
            ScanStatus = child.ScanStatus
        };
        attached.Parent = parent;
        parent.Children.Add(attached);
        parent.SizeBytes += attached.SizeBytes;
        parent.FileCount += attached.IsDirectory ? attached.FileCount : 1;
        parent.DirectoryCount += attached.IsDirectory ? 1 + attached.DirectoryCount : 0;
    }
}

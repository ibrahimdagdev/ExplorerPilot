using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Snapshots;
using ExplorerPilot.Infrastructure.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExplorerPilot.Tests;

public sealed class SnapshotTrendTests
{
    [Fact]
    public void BucketStart_DailyWeeklyMonthly_AreDeterministic()
    {
        var ts = new DateTimeOffset(2024, 3, 15, 18, 30, 0, TimeSpan.Zero); // Friday
        Assert.Equal(
            new DateTimeOffset(2024, 3, 15, 0, 0, 0, TimeSpan.Zero),
            SnapshotTrendService.BucketStart(ts, TrendGranularity.Daily));
        Assert.Equal(
            new DateTimeOffset(2024, 3, 11, 0, 0, 0, TimeSpan.Zero), // Monday
            SnapshotTrendService.BucketStart(ts, TrendGranularity.Weekly));
        Assert.Equal(
            new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero),
            SnapshotTrendService.BucketStart(ts, TrendGranularity.Monthly));
    }

    [Fact]
    public void SafePercentChange_HandlesZeroAndOverflow()
    {
        Assert.Null(SnapshotMath.SafePercentChange(100, 0));
        Assert.Equal(100.0, SnapshotMath.SafePercentChange(200, 100));
        Assert.Equal(-50.0, SnapshotMath.SafePercentChange(50, 100));
        Assert.Equal(long.MaxValue, SnapshotMath.SafeDelta(long.MaxValue, long.MinValue));
    }

    [Fact]
    public async Task Analyze_GroupsDaily_AndPicksLatestInBucket()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, dir);
            var day = new DateTimeOffset(2024, 5, 1, 8, 0, 0, TimeSpan.Zero);
            await SaveSized(store, @"D:\Data", 100, day);
            await SaveSized(store, @"D:\Data", 250, day.AddHours(5)); // same day — wins
            await SaveSized(store, @"D:\Data", 300, day.AddDays(1));

            var list = await store.ListAsync();
            var service = new SnapshotTrendService(store, NullLogger<SnapshotTrendService>.Instance);
            var result = await service.AnalyzeAsync(list, new SnapshotTrendRequest
            {
                Granularity = TrendGranularity.Daily,
                RootPathFilter = @"D:\Data",
                IncludeDetailBreakdowns = false
            });

            var series = Assert.Single(result.Series);
            Assert.Equal(2, series.Points.Count);
            Assert.Equal(250, series.Points[0].TotalBytes);
            Assert.True(series.Points[0].BucketHadMultipleSnapshots);
            Assert.Equal(300, series.Points[1].TotalBytes);
            Assert.Equal(50, series.Points[1].DeltaBytes);
            Assert.Equal(20.0, series.Points[1].DeltaPercent);
            Assert.Equal(50, series.NetDeltaBytes);
            Assert.Equal(20.0, series.GrowthPercent);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Analyze_SeparatesDifferentRoots()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, dir);
            await SaveSized(store, @"D:\A", 100, DateTimeOffset.UtcNow.AddDays(-2));
            await SaveSized(store, @"E:\B", 200, DateTimeOffset.UtcNow.AddDays(-1));

            var list = await store.ListAsync();
            var service = new SnapshotTrendService(store, NullLogger<SnapshotTrendService>.Instance);
            var result = await service.AnalyzeAsync(list, new SnapshotTrendRequest
            {
                IncludeDetailBreakdowns = false
            });

            Assert.Equal(2, result.Series.Count);
            Assert.Equal(2, result.AvailableRoots.Count);
            Assert.DoesNotContain(result.Series, s => s.Points.Count > 1 && s.Points.Select(p => p.TotalBytes).Distinct().Count() > 1
                && SnapshotPathNormalizer.RootsEquivalent(@"D:\A", @"E:\B"));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Analyze_MarksIncompleteSnapshots()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, dir);
            var scan = CreateScan(@"D:\Data", MakeFile("a.bin", 10));
            scan = WithStatus(scan, ScanPhase.Cancelled);
            await store.SaveAsync(scan, "incomplete");

            var list = await store.ListAsync();
            var service = new SnapshotTrendService(store, NullLogger<SnapshotTrendService>.Instance);
            var result = await service.AnalyzeAsync(list, new SnapshotTrendRequest
            {
                IncludeDetailBreakdowns = false
            });

            var series = Assert.Single(result.Series);
            Assert.True(series.ContainsIncompleteSnapshots);
            Assert.Contains(series.Points, p => p.IsIncomplete);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Analyze_CategoryAndFolderGrowth()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, dir);
            var day1 = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
            var day2 = day1.AddDays(2);

            await SaveFull(store, @"D:\Data", day1, "t1",
                categories: [("Documents", 50), ("Other", 100)],
                Dir("grow", MakeFile("a.bin", 100)),
                MakeFile("doc.txt", 50));
            await SaveFull(store, @"D:\Data", day2, "t2",
                categories: [("Documents", 80), ("Other", 400)],
                Dir("grow", MakeFile("a.bin", 400)),
                MakeFile("doc.txt", 80));

            var list = await store.ListAsync();
            var service = new SnapshotTrendService(store, NullLogger<SnapshotTrendService>.Instance);
            var result = await service.AnalyzeAsync(list, new SnapshotTrendRequest
            {
                RootPathFilter = @"D:\Data",
                IncludeDetailBreakdowns = true
            });

            var series = Assert.Single(result.Series);
            Assert.Equal(2, series.Points.Count);
            Assert.Contains(series.CategoryTrends, c => c.CategoryCode == "Other" && c.DeltaBytes == 300);
            var grow = Assert.Single(
                series.FastestGrowingFolders,
                f => f.RelativePath.Equals("grow", StringComparison.OrdinalIgnoreCase));
            Assert.True(grow.DeltaBytes > 0);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task CatalogNotifier_FiresOnSaveAndDelete()
    {
        var dir = CreateTempDir();
        try
        {
            var notifier = new SnapshotCatalogNotifier();
            var hits = 0;
            notifier.CatalogChanged += (_, _) => hits++;
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, dir, notifier);

            var info = await store.SaveAsync(CreateScan(@"D:\Data", MakeFile("a.bin", 1)), "n");
            Assert.Equal(1, hits);
            Assert.True(await store.DeleteAsync(info.Id));
            Assert.Equal(2, hits);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Analyze_Cancellation_Throws()
    {
        var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, CreateTempDir());
        var service = new SnapshotTrendService(store, NullLogger<SnapshotTrendService>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.AnalyzeAsync([], new SnapshotTrendRequest(), cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Analyze_LargeHistory_CompletesWithinBudget()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, dir);
            var baseDay = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
            for (var i = 0; i < 40; i++)
            {
                await SaveSized(store, @"D:\Perf", 1000 + i * 10, baseDay.AddDays(i));
            }

            var list = await store.ListAsync();
            var service = new SnapshotTrendService(store, NullLogger<SnapshotTrendService>.Instance);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await service.AnalyzeAsync(list, new SnapshotTrendRequest
            {
                Granularity = TrendGranularity.Daily,
                RootPathFilter = @"D:\Perf",
                MaxTimelinePoints = 30,
                IncludeDetailBreakdowns = true
            });
            sw.Stop();

            var series = Assert.Single(result.Series);
            Assert.Equal(40, series.Points.Count);
            Assert.True(series.ChartPoints.Count <= 30);
            Assert.True(result.WasDownsampled);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"Took {sw.Elapsed}");
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void Downsample_PreservesFirstLastAndPeaks_Over60()
    {
        var points = new List<SnapshotTrendPoint>();
        for (var i = 0; i < 90; i++)
        {
            var total = 1000L + i;
            if (i == 40)
            {
                total = 50_000; // major peak
            }

            if (i == 70)
            {
                total = 100; // major valley
            }

            var prev = i == 0 ? total : points[i - 1].TotalBytes;
            points.Add(new SnapshotTrendPoint
            {
                BucketStartUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i),
                SnapshotCreatedAtUtc = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero).AddDays(i),
                SnapshotId = Guid.NewGuid(),
                DisplayName = "p" + i,
                TotalBytes = total,
                DeltaBytes = i == 0 ? 0 : SnapshotMath.SafeDelta(total, prev),
                IsIncomplete = i == 55
            });
        }

        var sampled = TrendDownsampler.Downsample(points, 60);
        Assert.True(sampled.Count <= 60);
        Assert.Equal(points[0].SnapshotId, sampled[0].SnapshotId);
        Assert.Equal(points[^1].SnapshotId, sampled[^1].SnapshotId);
        Assert.Contains(sampled, p => p.TotalBytes == 50_000);
        Assert.Contains(sampled, p => p.TotalBytes == 100);
        Assert.Contains(sampled, p => p.IsIncomplete);
    }

    [Fact]
    public async Task Analyze_DoesNotZeroFillMissingBuckets()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, dir);
            var day = new DateTimeOffset(2024, 7, 1, 12, 0, 0, TimeSpan.Zero);
            await SaveSized(store, @"D:\Gaps", 100, day);
            await SaveSized(store, @"D:\Gaps", 200, day.AddDays(5)); // skip days 2-5

            var list = await store.ListAsync();
            var service = new SnapshotTrendService(store, NullLogger<SnapshotTrendService>.Instance);
            var result = await service.AnalyzeAsync(list, new SnapshotTrendRequest
            {
                RootPathFilter = @"D:\Gaps",
                Granularity = TrendGranularity.Daily,
                IncludeDetailBreakdowns = false
            });

            var series = Assert.Single(result.Series);
            Assert.Equal(2, series.Points.Count);
            Assert.DoesNotContain(series.Points, p => p.TotalBytes == 0 && p.DisplayName is null);
            Assert.Equal(100, series.Points[0].TotalBytes);
            Assert.Equal(200, series.Points[1].TotalBytes);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void ChartLayout_NavigateAndHitTest_AreDeterministic()
    {
        Assert.Equal(0, TrendChartLayout.Navigate(-1, 5, 1));
        Assert.Equal(4, TrendChartLayout.Navigate(-1, 5, -1));
        Assert.Equal(2, TrendChartLayout.Navigate(1, 5, 1));
        Assert.Equal(0, TrendChartLayout.Navigate(0, 5, -1));

        var points = new List<SnapshotTrendPoint>
        {
            MakePoint(100, 0),
            MakePoint(200, 1),
            MakePoint(150, 2)
        };
        var metrics = TrendChartLayout.Compute(400, 200, points);
        var locations = new List<(double X, double Y)>();
        for (var i = 0; i < points.Count; i++)
        {
            locations.Add(TrendChartLayout.MapPoint(metrics, i, points.Count, points[i].TotalBytes));
        }

        var mid = locations[1];
        Assert.Equal(1, TrendChartLayout.FindNearestIndex(locations, mid.X, mid.Y, 20));
        Assert.Equal(-1, TrendChartLayout.FindNearestIndex(locations, -100, -100, 5));

        // Resize changes mapping coordinates.
        var metrics2 = TrendChartLayout.Compute(800, 400, points);
        var (x2, _) = TrendChartLayout.MapPoint(metrics2, 1, points.Count, points[1].TotalBytes);
        Assert.NotEqual(mid.X, x2);
    }

    [Fact]
    public async Task Analyze_FirstLastDates_AndFolderOrderByDelta()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, dir);
            var day1 = new DateTimeOffset(2024, 8, 1, 10, 0, 0, TimeSpan.Zero);
            var day2 = day1.AddDays(3);
            await SaveFull(store, @"D:\Data", day1, "t1",
                categories: [("Documents", 10)],
                Dir("small", MakeFile("a.bin", 10)),
                Dir("big", MakeFile("b.bin", 20)));
            await SaveFull(store, @"D:\Data", day2, "t2",
                categories: [("Documents", 40)],
                Dir("small", MakeFile("a.bin", 15)),
                Dir("big", MakeFile("b.bin", 200)));

            var list = await store.ListAsync();
            var service = new SnapshotTrendService(store, NullLogger<SnapshotTrendService>.Instance);
            var result = await service.AnalyzeAsync(list, new SnapshotTrendRequest
            {
                RootPathFilter = @"D:\Data",
                IncludeDetailBreakdowns = true
            });

            var series = Assert.Single(result.Series);
            Assert.Equal(day1, series.FirstSnapshotCreatedAtUtc);
            Assert.Equal(day2, series.LastSnapshotCreatedAtUtc);
            Assert.True(series.FastestGrowingFolders.Count >= 2);
            for (var i = 1; i < series.FastestGrowingFolders.Count; i++)
            {
                Assert.True(series.FastestGrowingFolders[i - 1].DeltaBytes
                            >= series.FastestGrowingFolders[i].DeltaBytes);
            }

            var big = Assert.Single(
                series.FastestGrowingFolders,
                f => f.RelativePath.Equals("big", StringComparison.OrdinalIgnoreCase));
            var small = Assert.Single(
                series.FastestGrowingFolders,
                f => f.RelativePath.Equals("small", StringComparison.OrdinalIgnoreCase));
            Assert.True(big.DeltaBytes > small.DeltaBytes);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    private static SnapshotTrendPoint MakePoint(long bytes, int day) =>
        new()
        {
            BucketStartUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(day),
            SnapshotCreatedAtUtc = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero).AddDays(day),
            SnapshotId = Guid.NewGuid(),
            DisplayName = "p" + day,
            TotalBytes = bytes
        };

    [Fact]
    public async Task LoadTrendPayload_SkipsFullFileMaterialization()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, dir);
            var info = await store.SaveAsync(
                CreateScan(@"D:\Data", Dir("sub", MakeFile("a.bin", 10)), MakeFile("b.bin", 5)),
                "payload");
            var payload = await store.LoadTrendPayloadAsync(info.Id);
            Assert.Equal(info.Id, payload.Id);
            Assert.Contains(payload.DirectorySizes.Keys, k => k.Equals("sub", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(payload.DirectorySizes.Keys, k => k.Equals("b.bin", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    private static Task SaveSized(
        SnapshotStore store,
        string root,
        long size,
        DateTimeOffset createdAtUtc) =>
        SaveFull(store, root, createdAtUtc, "s", categories: null, MakeFile("f.bin", size));

    private static async Task SaveFull(
        SnapshotStore store,
        string root,
        DateTimeOffset createdAtUtc,
        string name,
        (string Code, long Bytes)[]? categories,
        params FileSystemNode[] children)
    {
        var scan = CreateScan(root, children);
        if (categories is not null)
        {
            scan = WithCategories(scan, categories);
        }

        var snap = DiskSnapshotBuilder.Build(scan, name);
        var fixedSnap = new DiskSnapshot
        {
            SchemaVersion = snap.SchemaVersion,
            Id = Guid.NewGuid(),
            CreatedAtUtc = createdAtUtc,
            DisplayName = snap.DisplayName,
            RootPath = snap.RootPath,
            RootName = snap.RootName,
            NormalizedRootPath = snap.NormalizedRootPath,
            TotalBytes = snap.TotalBytes,
            TotalFiles = snap.TotalFiles,
            TotalDirectories = snap.TotalDirectories,
            DurationSeconds = snap.DurationSeconds,
            FinalStatus = snap.FinalStatus,
            CompletenessCode = snap.CompletenessCode,
            IsIncomplete = snap.IsIncomplete,
            Categories = snap.Categories,
            Extensions = snap.Extensions,
            Entries = snap.Entries
        };

        var path = Path.Combine(store.StorageDirectory, fixedSnap.Id.ToString("N") + SnapshotSchema.FileExtension);
        await using var stream = File.Create(path);
        await System.Text.Json.JsonSerializer.SerializeAsync(stream, fixedSnap, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) }
        });
    }

    private static ScanResult WithStatus(ScanResult scan, ScanPhase status) =>
        new()
        {
            RootNode = scan.RootNode,
            TotalFiles = scan.TotalFiles,
            TotalDirectories = scan.TotalDirectories,
            TotalBytes = scan.TotalBytes,
            Duration = scan.Duration,
            FinalStatus = status,
            CategoryStatistics = scan.CategoryStatistics,
            ExtensionStatistics = scan.ExtensionStatistics,
            LargestFiles = scan.LargestFiles,
            Errors = scan.Errors
        };

    private static ScanResult WithCategories(ScanResult scan, params (string Code, long Bytes)[] categories)
    {
        var stats = categories.Select(c => new FileCategoryStatistics
        {
            Category = Enum.TryParse<FileCategory>(c.Code, out var cat) ? cat : FileCategory.Other,
            FileCount = 1,
            TotalSizeBytes = c.Bytes,
            PercentageOfTotalBytes = scan.TotalBytes == 0 ? 0 : 100.0 * c.Bytes / scan.TotalBytes
        }).ToList();

        return new ScanResult
        {
            RootNode = scan.RootNode,
            TotalFiles = scan.TotalFiles,
            TotalDirectories = scan.TotalDirectories,
            TotalBytes = scan.TotalBytes,
            Duration = scan.Duration,
            FinalStatus = scan.FinalStatus,
            CategoryStatistics = stats,
            ExtensionStatistics = scan.ExtensionStatistics,
            LargestFiles = scan.LargestFiles,
            Errors = scan.Errors
        };
    }

    private static ScanResult CreateScan(string rootPath, params FileSystemNode[] children)
    {
        var rootName = Path.GetFileName(rootPath.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : rootPath;
        var root = new FileSystemNode { Name = rootName, FullPath = rootPath, IsDirectory = true };
        foreach (var child in children)
        {
            Attach(root, child);
        }

        return new ScanResult
        {
            RootNode = root,
            TotalFiles = root.FileCount,
            TotalDirectories = root.DirectoryCount,
            TotalBytes = root.SizeBytes,
            Duration = TimeSpan.FromSeconds(1),
            FinalStatus = ScanPhase.Completed,
            CategoryStatistics =
            [
                new FileCategoryStatistics
                {
                    Category = FileCategory.Other,
                    FileCount = root.FileCount,
                    TotalSizeBytes = root.SizeBytes,
                    PercentageOfTotalBytes = 100
                }
            ],
            ExtensionStatistics =
            [
                new FileExtensionStatistics
                {
                    Extension = ".bin",
                    Category = FileCategory.Other,
                    FileCount = root.FileCount,
                    TotalSizeBytes = root.SizeBytes,
                    PercentageOfTotalBytes = 100
                }
            ]
        };
    }

    private static FileSystemNode Dir(string name, params FileSystemNode[] children)
    {
        var node = new FileSystemNode { Name = name, FullPath = @"D:\t\" + name, IsDirectory = true };
        foreach (var child in children)
        {
            Attach(node, child);
        }

        return node;
    }

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
                ScanStatus = NodeScanStatus.Completed
            };
            foreach (var nested in child.Children.ToList())
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
                ScanStatus = NodeScanStatus.Completed
            };
        }

        attached.Parent = parent;
        parent.Children.Add(attached);
        parent.SizeBytes += attached.SizeBytes;
        parent.FileCount += attached.IsDirectory ? attached.FileCount : 1;
        parent.DirectoryCount += attached.IsDirectory ? 1 + attached.DirectoryCount : 0;
    }

    private static FileSystemNode MakeFile(string name, long size) =>
        new()
        {
            Name = name,
            FullPath = @"D:\t\" + name,
            IsDirectory = false,
            SizeBytes = size,
            FileCount = 1,
            ScanStatus = NodeScanStatus.Completed
        };

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ep-trend-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
        catch
        {
            // ignore
        }
    }
}

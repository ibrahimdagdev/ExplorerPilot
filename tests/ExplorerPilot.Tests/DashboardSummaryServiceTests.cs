using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Infrastructure;
using ExplorerPilot.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ExplorerPilot.Tests;

public sealed class DashboardSummaryServiceTests
{
    [Fact]
    public void Di_RegistersDashboardSummaryService()
    {
        var services = new ServiceCollection();
        DependencyInjection.AddExplorerPilotInfrastructure(services);
        using var provider = services.BuildServiceProvider();
        Assert.IsType<DashboardSummaryService>(provider.GetRequiredService<IDashboardSummaryService>());
    }

    [Fact]
    public async Task BuildAsync_AggregatesReadyDriveCapacity_ExcludesUnknownZero()
    {
        var drives = new[]
        {
            ReadyDrive(@"C:\", 1000, 400),
            ReadyDrive(@"D:\", 2000, 500),
            new DriveInformation
            {
                Name = @"E:\",
                RootPath = @"E:\",
                Kind = VolumeDriveKind.Removable,
                Status = DriveReadyStatus.NotReady
            },
            new DriveInformation
            {
                Name = @"F:\",
                RootPath = @"F:\",
                Kind = VolumeDriveKind.Fixed,
                Status = DriveReadyStatus.Ready,
                TotalSizeBytes = 0,
                AvailableFreeSpaceBytes = 0,
                UsedSpaceBytes = 0
            }
        };

        var summary = await new DashboardSummaryService().BuildAsync(new DashboardBuildRequest
        {
            Drives = drives
        });

        Assert.Equal(4, summary.Capacity.DriveCount);
        Assert.Equal(3, summary.Capacity.ReadyDriveCount);
        Assert.Equal(3000, summary.Capacity.TotalCapacityBytes);
        Assert.Equal(2100, summary.Capacity.UsedSpaceBytes); // (1000-400)+(2000-500)
        Assert.Equal(900, summary.Capacity.FreeSpaceBytes);
        Assert.Equal(@"D:\", summary.Capacity.HighestUsageDrive!.RootPath);
        Assert.Null(summary.LastScan);
    }

    [Fact]
    public async Task BuildAsync_LargeScan_ProducesTopNAndCategories()
    {
        var root = BuildTree();
        var scan = new ScanResult
        {
            RootNode = root,
            TotalBytes = root.SizeBytes,
            TotalFiles = 5,
            TotalDirectories = 3,
            Duration = TimeSpan.FromSeconds(2),
            FinalStatus = ScanPhase.Completed,
            LargestFiles =
            [
                new LargestFileInfo { Name = "a.bin", FullPath = @"C:\t\a.bin", SizeBytes = 500 },
                new LargestFileInfo { Name = "b.bin", FullPath = @"C:\t\b.bin", SizeBytes = 400 }
            ],
            CategoryStatistics =
            [
                new FileCategoryStatistics
                {
                    Category = FileCategory.Documents,
                    FileCount = 2,
                    TotalSizeBytes = 300,
                    PercentageOfTotalBytes = 60
                }
            ]
        };

        var session = Guid.NewGuid();
        var summary = await new DashboardSummaryService().BuildAsync(new DashboardBuildRequest
        {
            Scan = scan,
            ScanSessionId = session,
            TopFiles = 10,
            TopFolders = 2
        });

        Assert.Equal(session, summary.ScanSessionId);
        Assert.NotNull(summary.LastScan);
        Assert.Equal(2, summary.LargestFiles.Count);
        Assert.True(summary.LargestFolders.Count <= 2);
        Assert.Single(summary.Categories);
        Assert.Equal(FileCategory.Documents, summary.Categories[0].Category);
    }

    [Fact]
    public async Task BuildAsync_RespectsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DashboardSummaryService().BuildAsync(new DashboardBuildRequest(), cts.Token));
    }

    [Fact]
    public async Task BuildAsync_IncompleteScan_FlagsSummary()
    {
        var root = new FileSystemNode
        {
            Name = "t",
            FullPath = @"C:\t",
            IsDirectory = true,
            SizeBytes = 10
        };
        var summary = await new DashboardSummaryService().BuildAsync(new DashboardBuildRequest
        {
            Scan = new ScanResult
            {
                RootNode = root,
                FinalStatus = ScanPhase.Cancelled,
                TotalBytes = 10
            },
            ScanIncomplete = true,
            ScanSessionId = Guid.NewGuid()
        });

        Assert.True(summary.LastScan!.IsIncomplete);
    }

    [Fact]
    public async Task BuildAsync_AttachesSnapshotAndCleanupInputs()
    {
        var snapshot = new DashboardSnapshotSummary
        {
            HasMatchingSnapshots = true,
            LastSnapshotAtUtc = DateTimeOffset.UtcNow,
            DeltaBytesVsPrevious = 100,
            GrowthPercent = 5,
            MatchedRootPath = @"C:\",
            SnapshotCount = 2
        };
        var cleanup = new DashboardCleanupSummary
        {
            UniquePotentialSavingsBytes = 1234,
            RecommendationCount = 3,
            LowRiskCount = 1,
            MediumRiskCount = 1,
            HighRiskCount = 1,
            IsPreviewOnly = true
        };

        var summary = await new DashboardSummaryService().BuildAsync(new DashboardBuildRequest
        {
            Snapshot = snapshot,
            Cleanup = cleanup
        });

        Assert.Same(snapshot, summary.Snapshot);
        Assert.Same(cleanup, summary.Cleanup);
        Assert.True(summary.Cleanup!.IsPreviewOnly);
    }

    private static DriveInformation ReadyDrive(string root, long total, long available)
    {
        var used = DriveCapacityMath.SafeUsedBytes(total, available);
        return new DriveInformation
        {
            Name = root,
            RootPath = root,
            Kind = VolumeDriveKind.Fixed,
            Status = DriveReadyStatus.Ready,
            TotalSizeBytes = total,
            AvailableFreeSpaceBytes = available,
            TotalFreeSpaceBytes = available,
            UsedSpaceBytes = used,
            UsageRatio = DriveCapacityMath.SafeUsageRatio(total, used),
            UsagePercentage = DriveCapacityMath.SafeUsagePercentage(total, used)
        };
    }

    private static FileSystemNode BuildTree()
    {
        var root = new FileSystemNode { Name = "t", FullPath = @"C:\t", IsDirectory = true };
        var big = new FileSystemNode
        {
            Name = "big",
            FullPath = @"C:\t\big",
            IsDirectory = true,
            Parent = root,
            SizeBytes = 900,
            FileCount = 2
        };
        var small = new FileSystemNode
        {
            Name = "small",
            FullPath = @"C:\t\small",
            IsDirectory = true,
            Parent = root,
            SizeBytes = 100,
            FileCount = 1
        };
        root.Children.Add(big);
        root.Children.Add(small);
        root.SizeBytes = 1000;
        root.DirectoryCount = 2;
        root.FileCount = 3;
        return root;
    }
}

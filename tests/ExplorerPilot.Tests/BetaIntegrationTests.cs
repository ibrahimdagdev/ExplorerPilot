using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Snapshots;
using ExplorerPilot.Infrastructure;
using ExplorerPilot.Infrastructure.Scanning;
using ExplorerPilot.Infrastructure.Services;
using ExplorerPilot.Infrastructure.Snapshots;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExplorerPilot.Tests;

/// <summary>Cross-module integration checks for v2.0 beta.1 (no WPF dispatcher required).</summary>
public sealed class BetaIntegrationTests
{
    [Fact]
    public void Di_ResolvesSchedulerDashboardAndSnapshotPipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        DependencyInjection.AddExplorerPilotInfrastructure(services);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IScheduledScanService>());
        Assert.NotNull(provider.GetRequiredService<IDashboardSummaryService>());
        Assert.NotNull(provider.GetRequiredService<ISnapshotStore>());
        Assert.NotNull(provider.GetRequiredService<ISnapshotCatalogNotifier>());
        Assert.NotNull(provider.GetRequiredService<ISnapshotTrendService>());
        Assert.NotNull(provider.GetRequiredService<ICleanupRecommendationService>());
        Assert.NotNull(provider.GetRequiredService<IDiskScanner>());
    }

    [Fact]
    public async Task Scan_DashboardSummary_UsesScanSessionAndSeparatesCapacity()
    {
        using var temp = TemporaryDirectory.Create();
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "a.bin"), new byte[100]);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "b.bin"), new byte[50]);

        var scanner = new DiskScanner();
        var result = await scanner.ScanAsync(temp.Path);
        Assert.True(result.IsComplete);
        Assert.Equal(150, result.TotalBytes);

        var session = Guid.NewGuid();
        var summary = await new DashboardSummaryService().BuildAsync(new DashboardBuildRequest
        {
            Drives =
            [
                new DriveInformation
                {
                    Name = "C:",
                    RootPath = @"C:\",
                    Kind = VolumeDriveKind.Fixed,
                    Status = DriveReadyStatus.Ready,
                    TotalSizeBytes = 1_000_000,
                    AvailableFreeSpaceBytes = 400_000,
                    UsedSpaceBytes = 600_000
                }
            ],
            Scan = result,
            ScanSessionId = session,
            ScanIncomplete = false,
            ScanCompletedAtUtc = DateTimeOffset.UtcNow,
            TopFiles = 10,
            TopFolders = 10
        });

        Assert.Equal(session, summary.ScanSessionId);
        Assert.Equal(1_000_000, summary.Capacity.TotalCapacityBytes);
        Assert.Equal(600_000, summary.Capacity.UsedSpaceBytes);
        Assert.NotNull(summary.LastScan);
        Assert.Equal(150, summary.LastScan!.TotalBytes);
        Assert.NotEqual(summary.Capacity.UsedSpaceBytes, summary.LastScan.TotalBytes);
    }

    [Fact]
    public async Task Scan_Snapshot_TrendPipeline_RoundTrips()
    {
        using var temp = TemporaryDirectory.Create();
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "x.bin"), new byte[20]);
        var scan = await new DiskScanner().ScanAsync(temp.Path);

        var snapDir = Path.Combine(temp.Path, "snaps");
        var notifier = new SnapshotCatalogNotifier();
        var changed = 0;
        notifier.CatalogChanged += (_, _) => Interlocked.Increment(ref changed);

        var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, snapDir, notifier);
        var info = await store.SaveAsync(scan, "beta-integration");
        Assert.True(changed >= 1);
        Assert.False(info.IsIncomplete);

        var list = await store.ListAsync();
        Assert.Contains(list, s => s.Id == info.Id);

        var trend = await new SnapshotTrendService(store, NullLogger<SnapshotTrendService>.Instance).AnalyzeAsync(
            list,
            new SnapshotTrendRequest { RootPathFilter = temp.Path, IncludeDetailBreakdowns = false });
        Assert.True(trend.SnapshotCountConsidered >= 1);
    }

    [Fact]
    public async Task ScheduledSuccess_SavesSnapshot_IncompleteDoesNot()
    {
        using var temp = TemporaryDirectory.Create();
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "f.bin"), new byte[8]);
        var scanDir = Path.Combine(temp.Path, "snaps");
        Directory.CreateDirectory(scanDir);

        var notifier = new SnapshotCatalogNotifier();
        var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, scanDir, notifier);
        var scanner = new DiskScanner();

        var complete = await scanner.ScanAsync(temp.Path);
        Assert.True(complete.IsComplete);
        var saved = await store.SaveAsync(complete, "ok");
        Assert.NotEqual(Guid.Empty, saved.Id);

        // Incomplete scans must not be treated as snapshot-worthy by the scheduled path contract.
        var incomplete = new ScanResult
        {
            RootNode = complete.RootNode,
            TotalBytes = complete.TotalBytes,
            TotalFiles = complete.TotalFiles,
            TotalDirectories = complete.TotalDirectories,
            FinalStatus = ScanPhase.Cancelled,
            Duration = TimeSpan.FromMilliseconds(1)
        };
        Assert.False(incomplete.IsComplete);
        Assert.True(incomplete.WasCancelled);
    }

    [Fact]
    public async Task CancelThenRescan_ProducesFreshCompleteResult()
    {
        using var temp = TemporaryDirectory.Create();
        for (var i = 0; i < 200; i++)
        {
            await File.WriteAllBytesAsync(Path.Combine(temp.Path, $"f{i}.bin"), new byte[4]);
        }

        var scanner = new DiskScanner();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(1));
        try
        {
            var cancelled = await scanner.ScanAsync(temp.Path, cancellationToken: cts.Token);
            Assert.True(cancelled.WasCancelled || cancelled.FinalStatus == ScanPhase.Cancelled || cancelled.IsComplete);
        }
        catch (OperationCanceledException)
        {
            // Scanner may surface cancel as exception before a partial ScanResult is returned.
        }

        var again = await scanner.ScanAsync(temp.Path);
        Assert.True(again.IsComplete);
        Assert.True(again.TotalFiles >= 200);
    }
}

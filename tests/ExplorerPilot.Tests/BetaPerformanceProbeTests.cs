using System.Diagnostics;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Snapshots;
using ExplorerPilot.Infrastructure.Scanning;
using ExplorerPilot.Infrastructure.Services;
using ExplorerPilot.Infrastructure.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace ExplorerPilot.Tests;

/// <summary>
/// Measures key beta.1 timings. Does not fail on absolute thresholds — records evidence for the delivery report.
/// </summary>
public sealed class BetaPerformanceProbeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Probe_ScanApplyDashboardSnapshot_10kFiles()
    {
        using var temp = TemporaryDirectory.Create();
        const int fileCount = 10_000;
        for (var i = 0; i < fileCount; i++)
        {
            await File.WriteAllBytesAsync(Path.Combine(temp.Path, $"f{i}.bin"), new byte[i % 64]);
        }

        var scanner = new DiskScanner();
        var sw = Stopwatch.StartNew();
        var scan = await scanner.ScanAsync(temp.Path);
        var scanMs = sw.ElapsedMilliseconds;
        Assert.True(scan.IsComplete);
        Assert.True(scan.TotalFiles >= fileCount);

        // ApplyResult proxy: build tree root VM is WPF-bound; measure summary build instead.
        sw.Restart();
        var summary = await new DashboardSummaryService().BuildAsync(new DashboardBuildRequest
        {
            Drives = [],
            Scan = scan,
            ScanSessionId = Guid.NewGuid(),
            ScanIncomplete = false,
            ScanCompletedAtUtc = DateTimeOffset.UtcNow,
            TopFiles = 10,
            TopFolders = 10
        });
        var dashboardMs = sw.ElapsedMilliseconds;
        Assert.NotNull(summary.LastScan);

        var snapDir = Path.Combine(temp.Path, "_snaps");
        var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, snapDir, new SnapshotCatalogNotifier());
        sw.Restart();
        var info = await store.SaveAsync(scan, "perf-10k");
        var saveMs = sw.ElapsedMilliseconds;
        sw.Restart();
        _ = await store.LoadAsync(info.Id);
        var loadMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var list = await store.ListAsync();
        var trend = await new SnapshotTrendService(store, NullLogger<SnapshotTrendService>.Instance).AnalyzeAsync(
            list,
            new SnapshotTrendRequest { RootPathFilter = temp.Path, IncludeDetailBreakdowns = false });
        var trendMs = sw.ElapsedMilliseconds;

        output.WriteLine(
            "PERF 10k: ScanCore={0}ms DashboardBuild={1}ms SnapshotSave={2}ms SnapshotLoad={3}ms Trends={4}ms files={5}",
            scanMs, dashboardMs, saveMs, loadMs, trendMs, scan.TotalFiles);

        // Soft sanity: these should finish in a reasonable interactive window on CI/dev boxes.
        Assert.True(scanMs < 120_000, $"ScanCore too slow: {scanMs}ms");
        Assert.True(dashboardMs < 30_000, $"Dashboard BuildAsync too slow: {dashboardMs}ms");
        Assert.True(saveMs < 60_000, $"Snapshot save too slow: {saveMs}ms");
        Assert.NotNull(trend);
    }

    [Fact]
    public async Task Probe_SchedulerIdle_DoesNotSpin()
    {
        using var temp = TemporaryDirectory.Create();
        var store = new ExplorerPilot.Infrastructure.Scheduling.ScheduledScanStore(Path.Combine(temp.Path, "schedules.json"));
        await using var service = new ExplorerPilot.Infrastructure.Scheduling.ScheduledScanService(
            store,
            options: new ExplorerPilot.Core.Scheduling.ScheduledScanOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(50)
            },
            timeZone: TimeZoneInfo.Utc);

        await service.StartAsync();
        await service.AddAsync(new ExplorerPilot.Core.Scheduling.ScheduledScanDefinition
        {
            Name = "Future",
            TargetPath = temp.Path,
            Frequency = ExplorerPilot.Core.Scheduling.ScheduledScanFrequency.Daily,
            IsEnabled = true,
            LocalTimeOfDay = new TimeSpan(23, 59, 0)
        });

        var calls = 0;
        service.Executor = new CountingExecutor(() => Interlocked.Increment(ref calls));
        await Task.Delay(400);
        await service.StopAsync();
        Assert.Equal(0, calls);
        output.WriteLine("PERF idle scheduler: executorCalls={0} over ~400ms with 50ms poll", calls);
    }

    private sealed class CountingExecutor(Action onCall) : ExplorerPilot.Core.Scheduling.IScheduledScanExecutor
    {
        public Task<ExplorerPilot.Core.Scheduling.ScheduledScanRunResult> ExecuteAsync(
            ExplorerPilot.Core.Scheduling.ScheduledScanDefinition plan,
            CancellationToken cancellationToken = default)
        {
            onCall();
            return Task.FromResult(new ExplorerPilot.Core.Scheduling.ScheduledScanRunResult
            {
                PlanId = plan.Id,
                Outcome = ExplorerPilot.Core.Scheduling.ScheduledScanRunOutcome.Succeeded
            });
        }
    }
}

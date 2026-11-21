using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Scheduling;
using ExplorerPilot.Infrastructure.Scheduling;

namespace ExplorerPilot.Tests;

public sealed class ScheduledScanServiceTests
{
    [Fact]
    public async Task Start_MarksMissedWhileClosed_DoesNotAutoRun()
    {
        await using var harness = await Harness.CreateAsync();
        var past = DateTimeOffset.UtcNow.AddHours(-2);
        await harness.Store.SaveAsync([
            new ScheduledScanDefinition
            {
                Name = "Missed",
                TargetPath = harness.Target,
                Frequency = ScheduledScanFrequency.Daily,
                IsEnabled = true,
                LocalTimeOfDay = new TimeSpan(1, 0, 0),
                NextRunUtc = past
            }
        ]);

        var executor = new RecordingExecutor();
        harness.Service.Executor = executor;
        await harness.Service.StartAsync();

        var plan = Assert.Single(harness.Service.Plans);
        Assert.True(plan.MissedWhileClosed);
        Assert.NotNull(plan.NextRunUtc);
        Assert.True(plan.NextRunUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Empty(executor.Calls);

        await harness.Service.StopAsync();
    }

    [Fact]
    public async Task DisabledPlan_HasNoNextRun_AndIsNotExecuted()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.Service.StartAsync();
        var added = await harness.Service.AddAsync(new ScheduledScanDefinition
        {
            Name = "Off",
            TargetPath = harness.Target,
            Frequency = ScheduledScanFrequency.Daily,
            IsEnabled = false,
            LocalTimeOfDay = TimeSpan.FromHours(9)
        });

        Assert.Null(added.NextRunUtc);
        Assert.Null(Assert.Single(harness.Service.Plans).NextRunUtc);

        var executor = new RecordingExecutor { Outcome = ScheduledScanRunOutcome.Succeeded };
        harness.Service.Executor = executor;
        // Force a due next-run into the in-memory plan via update + enable then disable
        await harness.Service.SetEnabledAsync(added.Id, true);
        await harness.Service.SetEnabledAsync(added.Id, false);
        Assert.Null(Assert.Single(harness.Service.Plans).NextRunUtc);
        Assert.Empty(executor.Calls);
        await harness.Service.StopAsync();
    }

    [Fact]
    public async Task ConcurrencyGuard_SkippedBusy_DoesNotAdvanceSchedule()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.Service.StartAsync();
        var plan = await harness.Service.AddAsync(new ScheduledScanDefinition
        {
            Name = "Busy",
            TargetPath = harness.Target,
            Frequency = ScheduledScanFrequency.Daily,
            IsEnabled = true,
            LocalTimeOfDay = TimeSpan.FromHours(9)
        });

        var executor = new RecordingExecutor { Outcome = ScheduledScanRunOutcome.SkippedBusy };
        harness.Service.Executor = executor;

        var before = harness.Service.Plans.Single(p => p.Id == plan.Id).NextRunUtc;
        var result = await harness.Service.RunNowAsync(plan.Id);
        Assert.Equal(ScheduledScanRunOutcome.SkippedBusy, result.Outcome);

        var after = harness.Service.Plans.Single(p => p.Id == plan.Id);
        Assert.Equal(ScheduledScanRunOutcome.SkippedBusy, after.LastOutcome);
        Assert.Equal(before, after.NextRunUtc);
        await harness.Service.StopAsync();
    }

    [Fact]
    public async Task Cancellation_ReportsCancelled()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.Service.StartAsync();
        var plan = await harness.Service.AddAsync(new ScheduledScanDefinition
        {
            Name = "Cancel",
            TargetPath = harness.Target,
            Frequency = ScheduledScanFrequency.Manual,
            IsEnabled = true
        });

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new RecordingExecutor
        {
            Handler = async (_, ct) =>
            {
                gate.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                catch (OperationCanceledException)
                {
                    return new ScheduledScanRunResult
                    {
                        PlanId = plan.Id,
                        Outcome = ScheduledScanRunOutcome.Cancelled
                    };
                }

                return new ScheduledScanRunResult
                {
                    PlanId = plan.Id,
                    Outcome = ScheduledScanRunOutcome.Succeeded
                };
            }
        };
        harness.Service.Executor = executor;

        var runTask = harness.Service.RunNowAsync(plan.Id);
        await gate.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Service.CancelActiveAsync();
        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ScheduledScanRunOutcome.Cancelled, result.Outcome);
        Assert.Equal(ScheduledScanRunOutcome.Cancelled, harness.Service.Plans.Single().LastOutcome);
        await harness.Service.StopAsync();
    }

    [Fact]
    public async Task SnapshotSuccessAndFailure_AreRecordedSeparately()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.Service.StartAsync();
        var plan = await harness.Service.AddAsync(new ScheduledScanDefinition
        {
            Name = "Snap",
            TargetPath = harness.Target,
            Frequency = ScheduledScanFrequency.Manual,
            IsEnabled = true
        });

        var snapId = Guid.NewGuid();
        var executor = new RecordingExecutor
        {
            Outcome = ScheduledScanRunOutcome.Succeeded,
            SnapshotId = snapId
        };
        harness.Service.Executor = executor;
        var ok = await harness.Service.RunNowAsync(plan.Id);
        Assert.Equal(ScheduledScanRunOutcome.Succeeded, ok.Outcome);
        Assert.Equal(snapId, ok.SnapshotId);
        Assert.Equal(snapId, harness.Service.Plans.Single().LastSnapshotId);

        executor.Outcome = ScheduledScanRunOutcome.SnapshotFailed;
        executor.SnapshotId = null;
        executor.ErrorMessage = "disk full";
        var fail = await harness.Service.RunNowAsync(plan.Id);
        Assert.Equal(ScheduledScanRunOutcome.SnapshotFailed, fail.Outcome);
        Assert.Equal("disk full", harness.Service.Plans.Single().LastError);
        await harness.Service.StopAsync();
    }

    [Fact]
    public async Task IncompleteScan_DoesNotClaimSuccess()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.Service.StartAsync();
        var plan = await harness.Service.AddAsync(new ScheduledScanDefinition
        {
            Name = "Incomplete",
            TargetPath = harness.Target,
            Frequency = ScheduledScanFrequency.Manual,
            IsEnabled = true
        });

        harness.Service.Executor = new RecordingExecutor
        {
            Outcome = ScheduledScanRunOutcome.Incomplete,
            ErrorMessage = "partial"
        };
        var result = await harness.Service.RunNowAsync(plan.Id);
        Assert.Equal(ScheduledScanRunOutcome.Incomplete, result.Outcome);
        Assert.Null(result.SnapshotId);
        await harness.Service.StopAsync();
    }

    [Fact]
    public async Task Delete_RemovesOnlyPlanDefinition()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.Service.StartAsync();
        var plan = await harness.Service.AddAsync(new ScheduledScanDefinition
        {
            Name = "Temp",
            TargetPath = harness.Target,
            Frequency = ScheduledScanFrequency.Manual,
            IsEnabled = true
        });

        Assert.True(await harness.Service.DeleteAsync(plan.Id));
        Assert.Empty(harness.Service.Plans);
        Assert.True(Directory.Exists(harness.Target));
        Assert.True(File.Exists(Path.Combine(harness.Target, "keep.txt")));
        await harness.Service.StopAsync();
    }

    [Fact]
    public async Task Stop_CancelsActiveRun_AndStopsLoop()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.Service.StartAsync();
        var plan = await harness.Service.AddAsync(new ScheduledScanDefinition
        {
            Name = "Shutdown",
            TargetPath = harness.Target,
            Frequency = ScheduledScanFrequency.Manual,
            IsEnabled = true
        });

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Service.Executor = new RecordingExecutor
        {
            Handler = async (_, ct) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return new ScheduledScanRunResult
                {
                    PlanId = plan.Id,
                    Outcome = ScheduledScanRunOutcome.Succeeded
                };
            }
        };

        var runTask = harness.Service.RunNowAsync(plan.Id);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Service.StopAsync();
        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ScheduledScanRunOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public async Task Add_RejectsInvalidPath()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.Service.StartAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => harness.Service.AddAsync(new ScheduledScanDefinition
        {
            Name = "Bad",
            TargetPath = "relative\\path",
            Frequency = ScheduledScanFrequency.Manual
        }));
        await harness.Service.StopAsync();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly TemporaryDirectory _temp;

        private Harness(TemporaryDirectory temp, ScheduledScanStore store, ScheduledScanService service, string target)
        {
            _temp = temp;
            Store = store;
            Service = service;
            Target = target;
        }

        public ScheduledScanStore Store { get; }
        public ScheduledScanService Service { get; }
        public string Target { get; }

        public static async Task<Harness> CreateAsync()
        {
            var temp = TemporaryDirectory.Create();
            var target = Path.Combine(temp.Path, "scan-root");
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "keep.txt"), "safe");
            var storePath = Path.Combine(temp.Path, "schedules", "scheduled-scans.json");
            var store = new ScheduledScanStore(storePath);
            var service = new ScheduledScanService(
                store,
                options: new ScheduledScanOptions { PollInterval = TimeSpan.FromHours(1) },
                timeZone: TimeZoneInfo.Utc);
            return new Harness(temp, store, service, target);
        }

        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            _temp.Dispose();
        }
    }

    private sealed class RecordingExecutor : IScheduledScanExecutor
    {
        public ScheduledScanRunOutcome Outcome { get; set; } = ScheduledScanRunOutcome.Succeeded;
        public Guid? SnapshotId { get; set; }
        public string? ErrorMessage { get; set; }
        public List<Guid> Calls { get; } = [];
        public Func<ScheduledScanDefinition, CancellationToken, Task<ScheduledScanRunResult>>? Handler { get; set; }

        public async Task<ScheduledScanRunResult> ExecuteAsync(
            ScheduledScanDefinition plan,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(plan.Id);
            if (Handler is not null)
            {
                return await Handler(plan, cancellationToken);
            }

            return new ScheduledScanRunResult
            {
                PlanId = plan.Id,
                Outcome = Outcome,
                SnapshotId = SnapshotId,
                ErrorMessage = ErrorMessage
            };
        }
    }
}

using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Scheduling;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.Infrastructure.Scheduling;

/// <summary>
/// In-process scheduler. Runs only while the host app is open — no Windows Service / Task Scheduler.
/// </summary>
public sealed class ScheduledScanService : IScheduledScanService, IDisposable
{
    private readonly IScheduledScanStore _store;
    private readonly ILogger<ScheduledScanService>? _logger;
    private readonly ScheduledScanOptions _options;
    private readonly TimeZoneInfo _timeZone;
    private readonly object _sync = new();
    private readonly List<ScheduledScanDefinition> _plans = [];
    private readonly SemaphoreSlim _runGate = new(1, 1);

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private CancellationTokenSource? _activeRunCts;
    private int _persistVersion;
    private bool _started;
    private bool _disposed;
    private DateTimeOffset? _lastTickUtc;
    private TimeSpan _lastUtcOffset;

    public ScheduledScanService(
        IScheduledScanStore store,
        ILogger<ScheduledScanService>? logger = null,
        ScheduledScanOptions? options = null,
        TimeZoneInfo? timeZone = null)
    {
        _store = store;
        _logger = logger;
        _options = options ?? new ScheduledScanOptions();
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        _lastUtcOffset = _timeZone.GetUtcOffset(DateTime.UtcNow);
    }

    public IReadOnlyList<ScheduledScanDefinition> Plans
    {
        get
        {
            lock (_sync)
            {
                return _plans.Select(Clone).ToList();
            }
        }
    }

    public IScheduledScanExecutor? Executor { get; set; }

    public event EventHandler? PlansChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        var loaded = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            _plans.Clear();
            _plans.AddRange(loaded.Select(Clone));
            MarkMissedWhileClosed_NoLock(DateTimeOffset.UtcNow);
            RecalculateNextRuns_NoLock(DateTimeOffset.UtcNow);
            _started = true;
        }

        await PersistAsync(cancellationToken).ConfigureAwait(false);
        RaiseChanged();

        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => PollLoopAsync(_loopCts.Token), CancellationToken.None);
        _logger?.LogInformation("Scheduled scan service started with {Count} plan(s)", Plans.Count);
    }

    public async Task StopAsync()
    {
        // Cancel the in-flight scan first so the poll loop can unwind quickly.
        await CancelActiveAsync().ConfigureAwait(false);

        try
        {
            _loopCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _started = false;
        _logger?.LogInformation("Scheduled scan service stopped");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _runGate.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async Task<ScheduledScanDefinition> AddAsync(
        ScheduledScanDefinition plan,
        CancellationToken cancellationToken = default)
    {
        ValidatePlan(plan);
        plan.Id = plan.Id == Guid.Empty ? Guid.NewGuid() : plan.Id;
        plan.CreatedAtUtc = DateTimeOffset.UtcNow;
        plan.UpdatedAtUtc = plan.CreatedAtUtc;
        plan.MissedWhileClosed = false;
        plan.NextRunUtc = ScheduledScanScheduleMath.ComputeNextRunUtc(plan, DateTimeOffset.UtcNow, _timeZone);

        lock (_sync)
        {
            if (_plans.Any(p => p.Id == plan.Id))
            {
                throw new InvalidOperationException("A plan with this id already exists.");
            }

            _plans.Add(Clone(plan));
        }

        await PersistAsync(cancellationToken).ConfigureAwait(false);
        RaiseChanged();
        return Clone(plan);
    }

    public async Task<ScheduledScanDefinition> UpdateAsync(
        ScheduledScanDefinition plan,
        CancellationToken cancellationToken = default)
    {
        ValidatePlan(plan);
        plan.UpdatedAtUtc = DateTimeOffset.UtcNow;
        plan.NextRunUtc = ScheduledScanScheduleMath.ComputeNextRunUtc(plan, DateTimeOffset.UtcNow, _timeZone);

        lock (_sync)
        {
            var index = _plans.FindIndex(p => p.Id == plan.Id);
            if (index < 0)
            {
                throw new KeyNotFoundException("Plan was not found.");
            }

            var existing = _plans[index];
            plan.CreatedAtUtc = existing.CreatedAtUtc;
            plan.LastRunUtc = existing.LastRunUtc;
            plan.LastOutcome = existing.LastOutcome;
            plan.LastError = existing.LastError;
            plan.LastSnapshotId = existing.LastSnapshotId;
            // Editing clears the missed banner once the user updates the schedule.
            plan.MissedWhileClosed = false;
            _plans[index] = Clone(plan);
        }

        await PersistAsync(cancellationToken).ConfigureAwait(false);
        RaiseChanged();
        return Clone(plan);
    }

    public async Task<bool> DeleteAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        bool removed;
        lock (_sync)
        {
            removed = _plans.RemoveAll(p => p.Id == planId) > 0;
        }

        if (!removed)
        {
            return false;
        }

        await PersistAsync(cancellationToken).ConfigureAwait(false);
        RaiseChanged();
        return true;
    }

    public async Task SetEnabledAsync(Guid planId, bool enabled, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var plan = _plans.FirstOrDefault(p => p.Id == planId)
                       ?? throw new KeyNotFoundException("Plan was not found.");
            plan.IsEnabled = enabled;
            plan.UpdatedAtUtc = DateTimeOffset.UtcNow;
            plan.MissedWhileClosed = false;
            plan.NextRunUtc = ScheduledScanScheduleMath.ComputeNextRunUtc(plan, DateTimeOffset.UtcNow, _timeZone);
        }

        await PersistAsync(cancellationToken).ConfigureAwait(false);
        RaiseChanged();
    }

    public async Task<ScheduledScanRunResult> RunNowAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        ScheduledScanDefinition plan;
        lock (_sync)
        {
            plan = Clone(_plans.FirstOrDefault(p => p.Id == planId)
                         ?? throw new KeyNotFoundException("Plan was not found."));
        }

        return await ExecutePlanAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    public Task CancelActiveAsync()
    {
        try
        {
            _activeRunCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        return Task.CompletedTask;
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await TickAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        List<ScheduledScanDefinition> due;
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            MaybeRecalculateForClockChange_NoLock(now);

            due = _plans
                .Where(p => p.IsEnabled
                            && p.Frequency != ScheduledScanFrequency.Manual
                            && p.NextRunUtc is { } next
                            && next <= now)
                .Select(Clone)
                .ToList();

            _lastTickUtc = now;
        }

        foreach (var plan in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ExecutePlanAsync(plan, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ScheduledScanRunResult> ExecutePlanAsync(
        ScheduledScanDefinition plan,
        CancellationToken cancellationToken)
    {
        // Serialize RunNow + timer ticks so CTS disposal cannot race and the same plan
        // cannot start twice. A second caller gets SkippedBusy without touching the executor.
        if (!await _runGate.WaitAsync(0).ConfigureAwait(false))
        {
            var busy = new ScheduledScanRunResult
            {
                PlanId = plan.Id,
                Outcome = ScheduledScanRunOutcome.SkippedBusy,
                ErrorMessage = "Another scheduled run is already in progress."
            };
            await ApplyRunResultAsync(plan.Id, busy, cancellationToken).ConfigureAwait(false);
            return busy;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Plan may have been deleted/edited while waiting — re-check under lock.
            lock (_sync)
            {
                if (_plans.All(p => p.Id != plan.Id))
                {
                    return new ScheduledScanRunResult
                    {
                        PlanId = plan.Id,
                        Outcome = ScheduledScanRunOutcome.Cancelled,
                        ErrorMessage = "Plan was deleted before the run started."
                    };
                }
            }

            var executor = Executor;
            if (executor is null)
            {
                var skipped = new ScheduledScanRunResult
                {
                    PlanId = plan.Id,
                    Outcome = ScheduledScanRunOutcome.Failed,
                    ErrorMessage = "Scan executor is not attached."
                };
                await ApplyRunResultAsync(plan.Id, skipped, cancellationToken).ConfigureAwait(false);
                return skipped;
            }

            var previousCts = _activeRunCts;
            _activeRunCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            previousCts?.Dispose();

            ScheduledScanRunResult result;
            try
            {
                result = await executor.ExecuteAsync(plan, _activeRunCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result = new ScheduledScanRunResult
                {
                    PlanId = plan.Id,
                    Outcome = ScheduledScanRunOutcome.Cancelled
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Scheduled scan {PlanId} failed", plan.Id);
                result = new ScheduledScanRunResult
                {
                    PlanId = plan.Id,
                    Outcome = ScheduledScanRunOutcome.Failed,
                    ErrorMessage = ex.Message
                };
            }

            await ApplyRunResultAsync(plan.Id, result, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _runGate.Release();
        }
    }

    private async Task ApplyRunResultAsync(
        Guid planId,
        ScheduledScanRunResult result,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            var plan = _plans.FirstOrDefault(p => p.Id == planId);
            if (plan is null)
            {
                return;
            }

            plan.LastRunUtc = result.CompletedAtUtc;
            plan.LastOutcome = result.Outcome;
            plan.LastError = result.ErrorMessage;
            // SnapshotFailed / Incomplete keep the previous successful snapshot id (if any).
            if (result.SnapshotId is { } snapId)
            {
                plan.LastSnapshotId = snapId;
            }

            plan.MissedWhileClosed = false;
            plan.UpdatedAtUtc = DateTimeOffset.UtcNow;

            if (result.Outcome == ScheduledScanRunOutcome.SkippedBusy)
            {
                var delay = _options.BusyRetryDelay <= TimeSpan.Zero
                    ? TimeSpan.FromMinutes(2)
                    : _options.BusyRetryDelay;
                plan.NextRunUtc = DateTimeOffset.UtcNow + delay;
            }
            else
            {
                plan.NextRunUtc = ScheduledScanScheduleMath.ComputeNextRunUtc(
                    plan,
                    DateTimeOffset.UtcNow,
                    _timeZone);
            }
        }

        await PersistAsync(cancellationToken).ConfigureAwait(false);
        RaiseChanged();
    }

    private void MaybeRecalculateForClockChange_NoLock(DateTimeOffset now)
    {
        var offset = _timeZone.GetUtcOffset(now.UtcDateTime);
        var offsetChanged = offset != _lastUtcOffset;
        var jumped = false;
        if (_lastTickUtc is { } previous)
        {
            var delta = now - previous;
            jumped = delta < TimeSpan.Zero || delta > _options.ClockJumpThreshold;
        }

        if (!offsetChanged && !jumped)
        {
            return;
        }

        _lastUtcOffset = offset;
        _logger?.LogInformation(
            "Recalculating scheduled next-runs after clock/offset change (jumped={Jumped}, offsetChanged={OffsetChanged})",
            jumped,
            offsetChanged);
        RecalculateNextRuns_NoLock(now);
    }

    private void MarkMissedWhileClosed_NoLock(DateTimeOffset utcNow)
    {
        foreach (var plan in _plans)
        {
            if (!plan.IsEnabled || plan.Frequency == ScheduledScanFrequency.Manual)
            {
                plan.MissedWhileClosed = false;
                continue;
            }

            plan.MissedWhileClosed = plan.NextRunUtc is { } next && next < utcNow;
        }
    }

    private void RecalculateNextRuns_NoLock(DateTimeOffset utcNow)
    {
        foreach (var plan in _plans)
        {
            plan.NextRunUtc = ScheduledScanScheduleMath.ComputeNextRunUtc(plan, utcNow, _timeZone);
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var version = Interlocked.Increment(ref _persistVersion);
        List<ScheduledScanDefinition> snapshot;
        lock (_sync)
        {
            snapshot = _plans.Select(Clone).ToList();
        }

        await _store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (version != _persistVersion)
        {
            // A newer save supersedes; ignore.
        }
    }

    private void RaiseChanged() => PlansChanged?.Invoke(this, EventArgs.Empty);

    private static void ValidatePlan(ScheduledScanDefinition plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(plan.Name))
        {
            throw new ArgumentException("Plan name is required.", nameof(plan));
        }

        if (!ScheduledScanPathValidator.TryNormalize(plan.TargetPath, out var normalized, out var error))
        {
            throw new ArgumentException(error ?? "Invalid target path.", nameof(plan));
        }

        plan.TargetPath = normalized;
        plan.LocalTimeOfDay = ScheduledScanScheduleMath.NormalizeTimeOfDay(plan.LocalTimeOfDay);
        plan.MonthlyDay = Math.Clamp(plan.MonthlyDay, 1, 31);
    }

    private static ScheduledScanDefinition Clone(ScheduledScanDefinition source) =>
        new()
        {
            Id = source.Id,
            Name = source.Name,
            TargetPath = source.TargetPath,
            Frequency = source.Frequency,
            IsEnabled = source.IsEnabled,
            LocalTimeOfDay = source.LocalTimeOfDay,
            WeeklyDay = source.WeeklyDay,
            MonthlyDay = source.MonthlyDay,
            LastRunUtc = source.LastRunUtc,
            NextRunUtc = source.NextRunUtc,
            LastOutcome = source.LastOutcome,
            LastError = source.LastError,
            LastSnapshotId = source.LastSnapshotId,
            MissedWhileClosed = source.MissedWhileClosed,
            CreatedAtUtc = source.CreatedAtUtc,
            UpdatedAtUtc = source.UpdatedAtUtc
        };
}

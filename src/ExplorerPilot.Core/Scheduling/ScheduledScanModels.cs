namespace ExplorerPilot.Core.Scheduling;

public enum ScheduledScanFrequency
{
    Manual = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3
}

public enum ScheduledScanRunOutcome
{
    Succeeded = 0,
    Failed = 1,
    Cancelled = 2,
    Incomplete = 3,
    SkippedBusy = 4,
    SnapshotFailed = 5
}

/// <summary>
/// Persisted user-defined scan plan. Timer runs only while the app is open.
/// </summary>
public sealed class ScheduledScanDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute folder or drive-root path.</summary>
    public string TargetPath { get; set; } = string.Empty;

    public ScheduledScanFrequency Frequency { get; set; } = ScheduledScanFrequency.Daily;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Local wall-clock time of day for Daily/Weekly/Monthly plans.</summary>
    public TimeSpan LocalTimeOfDay { get; set; } = new(9, 0, 0);

    public DayOfWeek WeeklyDay { get; set; } = DayOfWeek.Monday;

    /// <summary>1–31; clamped to the last day of the target month.</summary>
    public int MonthlyDay { get; set; } = 1;

    public DateTimeOffset? LastRunUtc { get; set; }

    public DateTimeOffset? NextRunUtc { get; set; }

    public ScheduledScanRunOutcome? LastOutcome { get; set; }

    public string? LastError { get; set; }

    public Guid? LastSnapshotId { get; set; }

    /// <summary>True when NextRunUtc elapsed while the app was closed (informational only).</summary>
    public bool MissedWhileClosed { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ScheduledScanOptions
{
    /// <summary>How often the in-process timer checks due plans.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>When true, successful completed scheduled scans save a snapshot.</summary>
    public bool AutoSnapshotOnSuccess { get; init; } = true;
}

public sealed class ScheduledScanRunResult
{
    public required Guid PlanId { get; init; }

    public required ScheduledScanRunOutcome Outcome { get; init; }

    public string? ErrorMessage { get; init; }

    public Guid? SnapshotId { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Executes an exclusive scan for a scheduled plan. Implemented by the UI host (MainViewModel)
/// so manual and scheduled scans share the same concurrency + ApplyResult pipeline.
/// </summary>
public interface IScheduledScanExecutor
{
    /// <summary>
    /// Attempts to run a scan. Returns <see cref="ScheduledScanRunOutcome.SkippedBusy"/> when another scan is active.
    /// </summary>
    Task<ScheduledScanRunResult> ExecuteAsync(
        ScheduledScanDefinition plan,
        CancellationToken cancellationToken = default);
}

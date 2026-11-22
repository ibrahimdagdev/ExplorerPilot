using ExplorerPilot.Core.Scheduling;

namespace ExplorerPilot.Core.Interfaces;

public interface IScheduledScanService : IAsyncDisposable
{
    IReadOnlyList<ScheduledScanDefinition> Plans { get; }

    event EventHandler? PlansChanged;

    /// <summary>Optional host that runs exclusive scans + UI apply.</summary>
    IScheduledScanExecutor? Executor { get; set; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();

    Task<ScheduledScanDefinition> AddAsync(ScheduledScanDefinition plan, CancellationToken cancellationToken = default);

    Task<ScheduledScanDefinition> UpdateAsync(ScheduledScanDefinition plan, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid planId, CancellationToken cancellationToken = default);

    Task SetEnabledAsync(Guid planId, bool enabled, CancellationToken cancellationToken = default);

    Task<ScheduledScanRunResult> RunNowAsync(Guid planId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the in-flight run (if any) and waits briefly for it to unwind.</summary>
    Task CancelActiveAsync();
}

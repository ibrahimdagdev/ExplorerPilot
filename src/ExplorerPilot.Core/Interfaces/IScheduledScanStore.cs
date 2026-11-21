using ExplorerPilot.Core.Scheduling;

namespace ExplorerPilot.Core.Interfaces;

public interface IScheduledScanStore
{
    string StorageFilePath { get; }

    Task<IReadOnlyList<ScheduledScanDefinition>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<ScheduledScanDefinition> plans, CancellationToken cancellationToken = default);
}

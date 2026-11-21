using ExplorerPilot.Core.Snapshots;

namespace ExplorerPilot.Core.Interfaces;

public interface ISnapshotComparisonService
{
    Task<SnapshotComparisonResult> CompareAsync(
        DiskSnapshot left,
        DiskSnapshot right,
        SnapshotCompareOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

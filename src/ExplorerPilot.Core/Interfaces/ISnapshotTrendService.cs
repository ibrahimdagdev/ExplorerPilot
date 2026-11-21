using ExplorerPilot.Core.Snapshots;

namespace ExplorerPilot.Core.Interfaces;

/// <summary>
/// Builds disk usage trends from saved snapshots without rescanning the disk.
/// </summary>
public interface ISnapshotTrendService
{
    Task<SnapshotTrendResult> AnalyzeAsync(
        IReadOnlyList<SnapshotInfo> snapshots,
        SnapshotTrendRequest request,
        IProgress<SnapshotTrendProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

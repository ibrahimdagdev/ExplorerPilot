using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Models;

/// <summary>
/// Final outcome of a disk scan, including aggregates and any path-level errors.
/// </summary>
public sealed class ScanResult
{
    public required FileSystemNode RootNode { get; init; }

    public long TotalFiles { get; init; }

    public long TotalDirectories { get; init; }

    /// <summary>
    /// Aggregate logical size in bytes. Physical allocated size on disk may differ.
    /// </summary>
    public long TotalBytes { get; init; }

    public TimeSpan Duration { get; init; }

    public IReadOnlyList<ScanError> Errors { get; init; } = [];

    /// <summary>
    /// Up to <see cref="ExplorerPilot.Core.Scanning.LargestFilesOptions.MaxTrackedFiles"/> largest files
    /// collected during the scan, already sorted by size descending.
    /// </summary>
    public IReadOnlyList<LargestFileInfo> LargestFiles { get; init; } = [];

    /// <summary>
    /// Extension statistics collected during the scan, sorted by total size descending.
    /// </summary>
    public IReadOnlyList<FileExtensionStatistics> ExtensionStatistics { get; init; } = [];

    /// <summary>
    /// Category statistics aggregated from extensions, sorted by total size descending.
    /// </summary>
    public IReadOnlyList<FileCategoryStatistics> CategoryStatistics { get; init; } = [];

    public ScanPhase FinalStatus { get; init; } = ScanPhase.Completed;

    public bool IsComplete => FinalStatus == ScanPhase.Completed;

    public bool WasCancelled => FinalStatus == ScanPhase.Cancelled;
}

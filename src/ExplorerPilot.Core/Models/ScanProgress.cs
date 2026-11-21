using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Models;

/// <summary>
/// Progress snapshot reported while a scan is running.
/// Completion percentage is intentionally omitted because total file count is unknown upfront.
/// </summary>
public sealed class ScanProgress
{
    public string CurrentPath { get; init; } = string.Empty;

    public long ScannedFileCount { get; init; }

    public long ScannedDirectoryCount { get; init; }

    public long TotalBytesScanned { get; init; }

    public TimeSpan ElapsedTime { get; init; }

    public ScanPhase CurrentStatus { get; init; } = ScanPhase.Idle;

    public string StatusMessage { get; init; } = string.Empty;
}

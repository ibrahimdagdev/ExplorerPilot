namespace ExplorerPilot.Core.Enums;

/// <summary>
/// High-level status of a disk scan operation.
/// </summary>
public enum ScanPhase
{
    Idle,
    Preparing,
    Scanning,
    Finalizing,
    Completed,
    Cancelled,
    Failed
}

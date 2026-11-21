namespace ExplorerPilot.Core.Enums;

/// <summary>
/// Status of an individual filesystem node after (or during) a scan.
/// </summary>
public enum NodeScanStatus
{
    Pending,
    Scanning,
    Completed,
    SkippedReparsePoint,
    Inaccessible,
    Error
}

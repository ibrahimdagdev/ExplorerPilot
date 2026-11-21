namespace ExplorerPilot.Core.Enums;

/// <summary>
/// Categorized scan error without storing full exception instances.
/// </summary>
public enum ScanErrorType
{
    AccessDenied,
    NotFound,
    PathTooLong,
    ReparsePointSkipped,
    IOError,
    InvalidPath,
    Unknown
}

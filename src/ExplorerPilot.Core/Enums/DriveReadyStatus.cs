namespace ExplorerPilot.Core.Enums;

/// <summary>
/// Readiness of a logical volume for capacity queries and scanning.
/// </summary>
public enum DriveReadyStatus
{
    Ready = 0,
    NotReady = 1,
    Unavailable = 2,
    TimedOut = 3
}

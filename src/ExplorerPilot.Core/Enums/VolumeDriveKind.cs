namespace ExplorerPilot.Core.Enums;

/// <summary>
/// Logical volume / drive kind for multi-drive overview (not physical disk hardware).
/// </summary>
public enum VolumeDriveKind
{
    Unknown = 0,
    Fixed = 1,
    Removable = 2,
    Network = 3,
    Optical = 4,
    Ram = 5
}

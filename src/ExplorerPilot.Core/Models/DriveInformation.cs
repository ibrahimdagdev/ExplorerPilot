using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Models;

/// <summary>
/// Discovered logical volume for multi-drive overview and scan targeting.
/// Capacity fields describe the volume; they are not file-scan aggregates.
/// </summary>
public sealed class DriveInformation
{
    public required string Name { get; init; }

    public required string RootPath { get; init; }

    public string VolumeLabel { get; init; } = string.Empty;

    public VolumeDriveKind Kind { get; init; }

    public DriveReadyStatus Status { get; init; }

    /// <summary>File system name when ready (e.g. NTFS); empty otherwise.</summary>
    public string FileSystem { get; init; } = string.Empty;

    public long TotalSizeBytes { get; init; }

    public long AvailableFreeSpaceBytes { get; init; }

    public long TotalFreeSpaceBytes { get; init; }

    public long UsedSpaceBytes { get; init; }

    /// <summary>0–100 when capacity is known; otherwise 0.</summary>
    public double UsagePercentage { get; init; }

    /// <summary>0–1 for progress bars.</summary>
    public double UsageRatio { get; init; }

    public bool IsReady => Status == DriveReadyStatus.Ready;

    /// <summary>True when the volume can be offered as a scan root.</summary>
    public bool CanScan => IsReady && !string.IsNullOrWhiteSpace(RootPath);
}

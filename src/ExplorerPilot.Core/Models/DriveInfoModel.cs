namespace ExplorerPilot.Core.Models;

/// <summary>
/// Display-friendly drive information for the UI drive selector.
/// </summary>
public sealed class DriveInfoModel
{
    public required string Name { get; init; }

    public required string RootPath { get; init; }

    public string VolumeLabel { get; init; } = string.Empty;

    public string DriveType { get; init; } = string.Empty;

    public string DriveFormat { get; init; } = string.Empty;

    public long TotalSizeBytes { get; init; }

    public long TotalFreeSpaceBytes { get; init; }

    public long AvailableFreeSpaceBytes { get; init; }

    public long UsedSpaceBytes => Math.Max(0, TotalSizeBytes - TotalFreeSpaceBytes);

    public bool IsReady { get; init; }

    /// <summary>
    /// Human-readable label for drive selectors, e.g.
    /// <c>C:\ (Windows) — 3.35 GB free of 930.28 GB</c>
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (!IsReady)
            {
                var type = string.IsNullOrWhiteSpace(DriveType) ? "Unavailable" : DriveType;
                return $"{Name} ({type}) — Not ready";
            }

            var label = string.IsNullOrWhiteSpace(VolumeLabel) ? "Local Disk" : VolumeLabel;
            return $"{Name} ({label}) — {FormatBytes(TotalFreeSpaceBytes)} free of {FormatBytes(TotalSizeBytes)}";
        }
    }

    /// <summary>
    /// Ensures ComboBox closed-state rendering remains readable even if a template
    /// falls back to object.ToString().
    /// </summary>
    public override string ToString() => DisplayName;

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}

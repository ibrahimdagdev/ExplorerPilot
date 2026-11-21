namespace ExplorerPilot.Core.Models;

/// <summary>
/// Overflow-safe capacity helpers. Volume capacity ≠ file-scan logical totals.
/// </summary>
public static class DriveCapacityMath
{
    /// <summary>
    /// Used = Total − Available free, clamped to [0, Total]. Invalid totals yield 0.
    /// </summary>
    public static long SafeUsedBytes(long totalSizeBytes, long availableFreeSpaceBytes)
    {
        if (totalSizeBytes <= 0)
        {
            return 0;
        }

        var available = availableFreeSpaceBytes;
        if (available < 0)
        {
            available = 0;
        }

        if (available > totalSizeBytes)
        {
            available = totalSizeBytes;
        }

        return totalSizeBytes - available;
    }

    /// <summary>
    /// Usage ratio in [0, 1]. Zero / invalid capacity → 0.
    /// </summary>
    public static double SafeUsageRatio(long totalSizeBytes, long usedSpaceBytes)
    {
        if (totalSizeBytes <= 0 || usedSpaceBytes <= 0)
        {
            return 0d;
        }

        var used = usedSpaceBytes;
        if (used > totalSizeBytes)
        {
            used = totalSizeBytes;
        }

        return (double)used / totalSizeBytes;
    }

    /// <summary>
    /// Usage percentage in [0, 100].
    /// </summary>
    public static double SafeUsagePercentage(long totalSizeBytes, long usedSpaceBytes) =>
        SafeUsageRatio(totalSizeBytes, usedSpaceBytes) * 100d;
}

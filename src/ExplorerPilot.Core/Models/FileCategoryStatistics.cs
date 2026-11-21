using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Models;

/// <summary>
/// Aggregated statistics for a file category.
/// </summary>
public sealed record FileCategoryStatistics
{
    public FileCategory Category { get; init; }

    public long FileCount { get; init; }

    public long TotalSizeBytes { get; init; }

    /// <summary>
    /// Percentage of all successfully scanned file bytes (0–100).
    /// </summary>
    public double PercentageOfTotalBytes { get; init; }
}

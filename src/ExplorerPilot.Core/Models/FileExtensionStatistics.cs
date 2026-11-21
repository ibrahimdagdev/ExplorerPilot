using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Models;

/// <summary>
/// Aggregated statistics for a single normalized file extension.
/// </summary>
public sealed record FileExtensionStatistics
{
    /// <summary>
    /// Normalized extension including the leading dot (e.g. <c>.mp4</c>).
    /// Empty string represents files without an extension.
    /// </summary>
    public required string Extension { get; init; }

    public FileCategory Category { get; init; }

    public long FileCount { get; init; }

    public long TotalSizeBytes { get; init; }

    /// <summary>
    /// Percentage of all successfully scanned file bytes (0–100).
    /// </summary>
    public double PercentageOfTotalBytes { get; init; }

    public double AverageSizeBytes { get; init; }
}

namespace ExplorerPilot.Core.Models;

/// <summary>
/// Lightweight metadata for a file retained in the largest-files ranking.
/// </summary>
public sealed record LargestFileInfo
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public long SizeBytes { get; init; }

    /// <summary>
    /// Extension including the leading dot when present (e.g. <c>.txt</c>); empty when none.
    /// </summary>
    public string Extension { get; init; } = string.Empty;

    public DateTimeOffset? LastModifiedUtc { get; init; }
}

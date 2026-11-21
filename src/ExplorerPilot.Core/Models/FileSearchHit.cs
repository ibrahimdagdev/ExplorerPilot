namespace ExplorerPilot.Core.Models;

/// <summary>
/// Lightweight search hit. Does not retain a live reference to the scan tree node.
/// </summary>
public sealed record FileSearchHit
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public bool IsDirectory { get; init; }

    public long SizeBytes { get; init; }

    public DateTimeOffset? LastModifiedUtc { get; init; }

    public string ParentPath { get; init; } = string.Empty;

    public string Extension { get; init; } = string.Empty;
}

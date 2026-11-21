namespace ExplorerPilot.Core.Models;

/// <summary>
/// A single file path that participates in a duplicate content group.
/// </summary>
public sealed class DuplicateFileInfo
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public long SizeBytes { get; init; }

    public DateTimeOffset? LastModified { get; init; }

    /// <summary>
    /// Windows file identity (volume + file index) when available; used for hard-link awareness.
    /// </summary>
    public string? FileIdentityKey { get; init; }
}

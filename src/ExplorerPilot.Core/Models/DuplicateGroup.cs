namespace ExplorerPilot.Core.Models;

/// <summary>
/// Files that share the same size and full SHA-256 content hash.
/// </summary>
public sealed class DuplicateGroup
{
    public required string ContentHashSha256 { get; init; }

    public long SizeBytes { get; init; }

    public required IReadOnlyList<DuplicateFileInfo> Files { get; init; }

    public int DuplicateCount => Files.Count;

    /// <summary>
    /// Potential reclaimable bytes keeping one unique physical copy
    /// (hard-linked paths share identity and do not inflate savings).
    /// </summary>
    public long PotentialSavingsBytes { get; init; }
}

using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Models;

/// <summary>
/// Criteria for searching an in-memory <see cref="FileSystemNode"/> tree.
/// Matching uses case-insensitive literal substring search (not regex).
/// </summary>
public sealed class FileSearchCriteria
{
    public const int DefaultPageSize = 100;

    /// <summary>
    /// Soft cap on collected matches to bound search-result memory.
    /// Exact global sorting and counts apply only within this collected set.
    /// </summary>
    public const int DefaultMaxCollectibleMatches = 50_000;

    /// <summary>
    /// Identifies the scan session these criteria target.
    /// </summary>
    public Guid ScanSessionId { get; init; }

    public string? Query { get; init; }

    public SearchScope Scope { get; init; } = SearchScope.NameOrPath;

    /// <summary>
    /// Optional extension filter (e.g. <c>.mp4</c> or <c>mp4</c>). Applied to files only.
    /// </summary>
    public string? ExtensionFilter { get; init; }

    public long? MinSizeBytes { get; init; }

    public long? MaxSizeBytes { get; init; }

    public DateTimeOffset? ModifiedFromUtc { get; init; }

    public DateTimeOffset? ModifiedToUtc { get; init; }

    public SearchItemKind ItemKind { get; init; } = SearchItemKind.All;

    public SearchSortField SortBy { get; init; } = SearchSortField.Size;

    public SearchSortDirection SortDirection { get; init; } = SearchSortDirection.Descending;

    public int PageIndex { get; init; }

    public int PageSize { get; init; } = DefaultPageSize;

    public int MaxCollectibleMatches { get; init; } = DefaultMaxCollectibleMatches;
}

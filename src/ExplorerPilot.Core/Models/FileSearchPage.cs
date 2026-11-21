namespace ExplorerPilot.Core.Models;

/// <summary>
/// One page of search results plus metadata about the collected match set.
/// </summary>
public sealed class FileSearchPage
{
    public required IReadOnlyList<FileSearchHit> Items { get; init; }

    public int PageIndex { get; init; }

    public int PageSize { get; init; }

    /// <summary>
    /// Number of matches collected (may be capped by <see cref="FileSearchCriteria.MaxCollectibleMatches"/>).
    /// </summary>
    public int CollectedMatchCount { get; init; }

    public bool ResultsTruncated { get; init; }

    public int MaxCollectibleMatches { get; init; }

    public int TotalPages { get; init; }

    public Guid ScanSessionId { get; init; }

    public bool FromIncompleteScan { get; init; }

    public TimeSpan Duration { get; init; }

    public bool HasPreviousPage => PageIndex > 0;

    public bool HasNextPage => PageIndex + 1 < TotalPages;
}

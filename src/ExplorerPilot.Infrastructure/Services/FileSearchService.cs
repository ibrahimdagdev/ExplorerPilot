using System.Diagnostics;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Scanning;

namespace ExplorerPilot.Infrastructure.Services;

/// <summary>
/// Walks an in-memory <see cref="FileSystemNode"/> tree. Does not touch the filesystem.
/// </summary>
/// <remarks>
/// Matching hits are collected up to <see cref="FileSearchCriteria.MaxCollectibleMatches"/> so
/// result memory stays bounded. Exact counts and global sorting apply only to that collected set;
/// when truncated, additional matches beyond the cap are not retained.
/// </remarks>
public sealed class FileSearchService : IFileSearchService
{
    public Task<FileSearchPage> SearchAsync(
        FileSystemNode root,
        FileSearchCriteria criteria,
        bool fromIncompleteScan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(criteria);

        return Task.Run(
            () => SearchCore(root, criteria, fromIncompleteScan, cancellationToken),
            cancellationToken);
    }

    private static FileSearchPage SearchCore(
        FileSystemNode root,
        FileSearchCriteria criteria,
        bool fromIncompleteScan,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var pageSize = Math.Max(1, criteria.PageSize);
        var maxCollect = Math.Max(pageSize, criteria.MaxCollectibleMatches);
        var query = criteria.Query?.Trim() ?? string.Empty;
        var hasQuery = query.Length > 0;
        var extension = NormalizeExtensionFilter(criteria.ExtensionFilter);

        var matches = new List<FileSearchHit>(Math.Min(1024, maxCollect));
        var truncated = false;

        var stack = new Stack<FileSystemNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var node = stack.Pop();

            if (Matches(node, criteria, query, hasQuery, extension))
            {
                if (matches.Count >= maxCollect)
                {
                    truncated = true;
                }
                else
                {
                    matches.Add(ToHit(node));
                    if (matches.Count >= maxCollect)
                    {
                        // Continue walking only to detect truncation? Spec wants bounded memory.
                        // Stop collecting further hits but we can stop the walk entirely once capped
                        // since we cannot retain more matches anyway.
                        truncated = true;
                        break;
                    }
                }
            }

            // Push children in reverse so left-to-right order is roughly preserved before sorting.
            var children = node.Children;
            for (var i = children.Count - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }

        // If we broke early due to cap, mark truncated (more nodes may exist).
        if (matches.Count >= maxCollect)
        {
            truncated = true;
        }

        SortStable(matches, criteria.SortBy, criteria.SortDirection);

        var collected = matches.Count;
        var totalPages = collected == 0 ? 0 : (int)Math.Ceiling(collected / (double)pageSize);
        var pageIndex = Math.Clamp(criteria.PageIndex, 0, Math.Max(0, totalPages - 1));
        var skip = pageIndex * pageSize;
        var pageItems = collected == 0
            ? (IReadOnlyList<FileSearchHit>)[]
            : matches.Skip(skip).Take(pageSize).ToList();

        stopwatch.Stop();

        return new FileSearchPage
        {
            Items = pageItems,
            PageIndex = pageIndex,
            PageSize = pageSize,
            CollectedMatchCount = collected,
            ResultsTruncated = truncated,
            MaxCollectibleMatches = maxCollect,
            TotalPages = totalPages,
            ScanSessionId = criteria.ScanSessionId,
            FromIncompleteScan = fromIncompleteScan,
            Duration = stopwatch.Elapsed
        };
    }

    internal static bool Matches(
        FileSystemNode node,
        FileSearchCriteria criteria,
        string query,
        bool hasQuery,
        string? normalizedExtension)
    {
        if (criteria.ItemKind == SearchItemKind.FilesOnly && node.IsDirectory)
        {
            return false;
        }

        if (criteria.ItemKind == SearchItemKind.FoldersOnly && !node.IsDirectory)
        {
            return false;
        }

        if (normalizedExtension is not null)
        {
            if (node.IsDirectory)
            {
                return false;
            }

            var nodeExt = FileCategoryMapper.NormalizeExtension(node.Name);
            if (!string.Equals(nodeExt, normalizedExtension, StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (criteria.MinSizeBytes is { } min && node.SizeBytes < min)
        {
            return false;
        }

        if (criteria.MaxSizeBytes is { } max && node.SizeBytes > max)
        {
            return false;
        }

        if (criteria.ModifiedFromUtc is { } from)
        {
            if (node.LastModified is null || node.LastModified < from)
            {
                return false;
            }
        }

        if (criteria.ModifiedToUtc is { } to)
        {
            // Inclusive end-of-day style: compare against provided instant.
            if (node.LastModified is null || node.LastModified > to)
            {
                return false;
            }
        }

        if (!hasQuery)
        {
            return true;
        }

        return criteria.Scope switch
        {
            SearchScope.Name => ContainsIgnoreCase(node.Name, query),
            SearchScope.FolderName => MatchesFolderName(node, query),
            SearchScope.FullPath => ContainsIgnoreCase(node.FullPath, query),
            SearchScope.NameOrPath => ContainsIgnoreCase(node.Name, query)
                                      || ContainsIgnoreCase(node.FullPath, query),
            _ => ContainsIgnoreCase(node.Name, query) || ContainsIgnoreCase(node.FullPath, query)
        };
    }

    private static bool MatchesFolderName(FileSystemNode node, string query)
    {
        if (node.IsDirectory)
        {
            return ContainsIgnoreCase(node.Name, query);
        }

        var parentName = node.Parent?.Name;
        return !string.IsNullOrEmpty(parentName) && ContainsIgnoreCase(parentName, query);
    }

    private static bool ContainsIgnoreCase(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeExtensionFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        var value = filter.Trim();
        if (!value.StartsWith('.'))
        {
            value = "." + value;
        }

        return value.ToLowerInvariant();
    }

    private static FileSearchHit ToHit(FileSystemNode node) =>
        new()
        {
            Name = node.Name,
            FullPath = node.FullPath,
            IsDirectory = node.IsDirectory,
            SizeBytes = node.SizeBytes,
            LastModifiedUtc = node.LastModified,
            ParentPath = node.Parent?.FullPath
                         ?? Path.GetDirectoryName(node.FullPath)
                         ?? string.Empty,
            Extension = node.IsDirectory
                ? string.Empty
                : FileCategoryMapper.NormalizeExtension(node.Name)
        };

    private static void SortStable(
        List<FileSearchHit> matches,
        SearchSortField sortBy,
        SearchSortDirection direction)
    {
        // LINQ OrderBy is stable.
        IOrderedEnumerable<FileSearchHit> ordered = sortBy switch
        {
            SearchSortField.Name => direction == SearchSortDirection.Ascending
                ? matches.OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
                : matches.OrderByDescending(h => h.Name, StringComparer.OrdinalIgnoreCase),
            SearchSortField.Modified => direction == SearchSortDirection.Ascending
                ? matches.OrderBy(h => h.LastModifiedUtc ?? DateTimeOffset.MinValue)
                : matches.OrderByDescending(h => h.LastModifiedUtc ?? DateTimeOffset.MinValue),
            _ => direction == SearchSortDirection.Ascending
                ? matches.OrderBy(h => h.SizeBytes)
                : matches.OrderByDescending(h => h.SizeBytes)
        };

        var sorted = ordered
            .ThenBy(h => h.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        matches.Clear();
        matches.AddRange(sorted);
    }
}

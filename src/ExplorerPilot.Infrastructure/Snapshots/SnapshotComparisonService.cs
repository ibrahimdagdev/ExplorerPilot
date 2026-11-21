using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Snapshots;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.Infrastructure.Snapshots;

public sealed class SnapshotComparisonService : ISnapshotComparisonService
{
    private readonly ILogger<SnapshotComparisonService> _logger;

    public SnapshotComparisonService(ILogger<SnapshotComparisonService> logger)
    {
        _logger = logger;
    }

    public Task<SnapshotComparisonResult> CompareAsync(
        DiskSnapshot left,
        DiskSnapshot right,
        SnapshotCompareOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Compare(left, right, options ?? new SnapshotCompareOptions(), progress, cancellationToken), cancellationToken);

    private SnapshotComparisonResult Compare(
        DiskSnapshot left,
        DiskSnapshot right,
        SnapshotCompareOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        progress?.Report(0.05);

        var leftMap = IndexEntries(left.Entries, cancellationToken);
        progress?.Report(0.25);
        var rightMap = IndexEntries(right.Entries, cancellationToken);
        progress?.Report(0.45);

        var added = new List<FilePresenceChange>();
        var removed = new List<FilePresenceChange>();
        var changed = new List<FileSizeChange>();
        var growing = new List<FolderSizeChange>();
        var shrinking = new List<FolderSizeChange>();

        var allKeys = new HashSet<string>(leftMap.Keys, SnapshotPathNormalizer.PathComparer);
        foreach (var key in rightMap.Keys)
        {
            allKeys.Add(key);
        }

        var processed = 0;
        foreach (var key in allKeys)
        {
            if ((++processed & 0x3FF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            leftMap.TryGetValue(key, out var leftEntry);
            rightMap.TryGetValue(key, out var rightEntry);

            if (leftEntry is null && rightEntry is not null)
            {
                if (!rightEntry.IsDirectory)
                {
                    added.Add(ToPresence(rightEntry));
                }

                continue;
            }

            if (leftEntry is not null && rightEntry is null)
            {
                if (!leftEntry.IsDirectory)
                {
                    removed.Add(ToPresence(leftEntry));
                }

                continue;
            }

            if (leftEntry is null || rightEntry is null)
            {
                continue;
            }

            // Type flip (file <-> folder): treat as remove + add for files.
            if (leftEntry.IsDirectory != rightEntry.IsDirectory)
            {
                if (!leftEntry.IsDirectory)
                {
                    removed.Add(ToPresence(leftEntry));
                }

                if (!rightEntry.IsDirectory)
                {
                    added.Add(ToPresence(rightEntry));
                }

                continue;
            }

            if (leftEntry.IsDirectory)
            {
                var delta = SnapshotMath.SafeDelta(rightEntry.SizeBytes, leftEntry.SizeBytes);
                if (delta > 0)
                {
                    growing.Add(ToFolderChange(leftEntry, rightEntry, delta));
                }
                else if (delta < 0)
                {
                    shrinking.Add(ToFolderChange(leftEntry, rightEntry, delta));
                }
            }
            else if (leftEntry.SizeBytes != rightEntry.SizeBytes)
            {
                changed.Add(new FileSizeChange
                {
                    RelativePath = key,
                    Name = rightEntry.Name,
                    LeftSizeBytes = leftEntry.SizeBytes,
                    RightSizeBytes = rightEntry.SizeBytes,
                    DeltaBytes = SnapshotMath.SafeDelta(rightEntry.SizeBytes, leftEntry.SizeBytes),
                    Extension = rightEntry.Extension,
                    Category = rightEntry.Category
                });
            }
        }

        progress?.Report(0.75);

        growing.Sort(static (a, b) => b.DeltaBytes.CompareTo(a.DeltaBytes));
        shrinking.Sort(static (a, b) => a.DeltaBytes.CompareTo(b.DeltaBytes));
        changed.Sort(static (a, b) => SnapshotMath.AbsClamped(b.DeltaBytes).CompareTo(SnapshotMath.AbsClamped(a.DeltaBytes)));
        added.Sort(static (a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        removed.Sort(static (a, b) => b.SizeBytes.CompareTo(a.SizeBytes));

        var categoryChanges = CompareCategories(left.Categories, right.Categories);
        var extensionChanges = CompareExtensions(left.Extensions, right.Extensions);
        progress?.Report(0.95);

        var summary = new SnapshotComparisonSummary
        {
            LeftTotalBytes = left.TotalBytes,
            RightTotalBytes = right.TotalBytes,
            TotalBytesDelta = SnapshotMath.SafeDelta(right.TotalBytes, left.TotalBytes),
            LeftTotalFiles = left.TotalFiles,
            RightTotalFiles = right.TotalFiles,
            AddedFileCount = added.Count,
            RemovedFileCount = removed.Count,
            ChangedFileCount = changed.Count,
            GrowingFolderCount = growing.Count,
            ShrinkingFolderCount = shrinking.Count,
            RootsDiffer = !SnapshotPathNormalizer.RootsEquivalent(left.NormalizedRootPath, right.NormalizedRootPath),
            LeftIncomplete = left.IsIncomplete,
            RightIncomplete = right.IsIncomplete,
            LeftCreatedAtUtc = left.CreatedAtUtc,
            RightCreatedAtUtc = right.CreatedAtUtc,
            LeftRootPath = left.RootPath,
            RightRootPath = right.RootPath,
            LeftDisplayName = left.DisplayName,
            RightDisplayName = right.DisplayName
        };

        _logger.LogInformation(
            "Compared snapshots {Left} vs {Right}: delta={Delta} added={Added} removed={Removed} changed={Changed}",
            left.Id,
            right.Id,
            summary.TotalBytesDelta,
            summary.AddedFileCount,
            summary.RemovedFileCount,
            summary.ChangedFileCount);

        progress?.Report(1.0);

        return new SnapshotComparisonResult
        {
            Left = SnapshotJson.ToInfo(left, string.Empty),
            Right = SnapshotJson.ToInfo(right, string.Empty),
            Summary = summary,
            GrowingFolders = Take(growing, options.MaxGrowingFolders),
            ShrinkingFolders = Take(shrinking, options.MaxShrinkingFolders),
            AddedFiles = Take(added, options.MaxAddedFiles),
            RemovedFiles = Take(removed, options.MaxRemovedFiles),
            ChangedFiles = Take(changed, options.MaxChangedFiles),
            CategoryChanges = categoryChanges,
            ExtensionChanges = extensionChanges
        };
    }

    private static Dictionary<string, SnapshotEntry> IndexEntries(
        IReadOnlyList<SnapshotEntry> entries,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, SnapshotEntry>(entries.Count, SnapshotPathNormalizer.PathComparer);
        for (var i = 0; i < entries.Count; i++)
        {
            if ((i & 0x3FF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var entry = entries[i];
            if (!SnapshotPathNormalizer.TryNormalizeRelative(entry.RelativePath, out var key))
            {
                // Skip unsafe/malformed keys rather than aborting the whole comparison.
                continue;
            }

            // Duplicate relative paths (case-insensitive): keep the later entry.
            map[key] = entry;
        }

        return map;
    }

    private static FilePresenceChange ToPresence(SnapshotEntry entry) =>
        new()
        {
            RelativePath = entry.RelativePath,
            Name = entry.Name,
            SizeBytes = entry.SizeBytes,
            Extension = entry.Extension,
            Category = entry.Category
        };

    private static FolderSizeChange ToFolderChange(SnapshotEntry left, SnapshotEntry right, long delta) =>
        new()
        {
            RelativePath = right.RelativePath,
            Name = right.Name,
            LeftSizeBytes = left.SizeBytes,
            RightSizeBytes = right.SizeBytes,
            DeltaBytes = delta
        };

    private static List<CategorySizeChange> CompareCategories(
        IReadOnlyList<SnapshotCategoryStat> left,
        IReadOnlyList<SnapshotCategoryStat> right)
    {
        var map = new Dictionary<FileCategory, CategorySizeChange>();
        foreach (var item in left)
        {
            map[item.Category] = new CategorySizeChange
            {
                Category = item.Category,
                CategoryCode = item.CategoryCode,
                LeftSizeBytes = item.TotalSizeBytes,
                LeftFileCount = item.FileCount,
                RightSizeBytes = 0,
                RightFileCount = 0,
                DeltaBytes = SnapshotMath.SafeDelta(0, item.TotalSizeBytes)
            };
        }

        foreach (var item in right)
        {
            if (map.TryGetValue(item.Category, out var existing))
            {
                map[item.Category] = new CategorySizeChange
                {
                    Category = item.Category,
                    CategoryCode = item.CategoryCode,
                    LeftSizeBytes = existing.LeftSizeBytes,
                    LeftFileCount = existing.LeftFileCount,
                    RightSizeBytes = item.TotalSizeBytes,
                    RightFileCount = item.FileCount,
                    DeltaBytes = SnapshotMath.SafeDelta(item.TotalSizeBytes, existing.LeftSizeBytes)
                };
            }
            else
            {
                map[item.Category] = new CategorySizeChange
                {
                    Category = item.Category,
                    CategoryCode = item.CategoryCode,
                    LeftSizeBytes = 0,
                    LeftFileCount = 0,
                    RightSizeBytes = item.TotalSizeBytes,
                    RightFileCount = item.FileCount,
                    DeltaBytes = item.TotalSizeBytes
                };
            }
        }

        return map.Values.OrderByDescending(c => SnapshotMath.AbsClamped(c.DeltaBytes)).ToList();
    }

    private static List<ExtensionSizeChange> CompareExtensions(
        IReadOnlyList<SnapshotExtensionStat> left,
        IReadOnlyList<SnapshotExtensionStat> right)
    {
        var map = new Dictionary<string, ExtensionSizeChange>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in left)
        {
            var key = item.Extension ?? string.Empty;
            map[key] = new ExtensionSizeChange
            {
                Extension = key,
                Category = item.Category,
                CategoryCode = item.CategoryCode,
                LeftSizeBytes = item.TotalSizeBytes,
                LeftFileCount = item.FileCount,
                RightSizeBytes = 0,
                RightFileCount = 0,
                DeltaBytes = SnapshotMath.SafeDelta(0, item.TotalSizeBytes)
            };
        }

        foreach (var item in right)
        {
            var key = item.Extension ?? string.Empty;
            if (map.TryGetValue(key, out var existing))
            {
                map[key] = new ExtensionSizeChange
                {
                    Extension = key,
                    Category = item.Category,
                    CategoryCode = item.CategoryCode,
                    LeftSizeBytes = existing.LeftSizeBytes,
                    LeftFileCount = existing.LeftFileCount,
                    RightSizeBytes = item.TotalSizeBytes,
                    RightFileCount = item.FileCount,
                    DeltaBytes = SnapshotMath.SafeDelta(item.TotalSizeBytes, existing.LeftSizeBytes)
                };
            }
            else
            {
                map[key] = new ExtensionSizeChange
                {
                    Extension = key,
                    Category = item.Category,
                    CategoryCode = item.CategoryCode,
                    LeftSizeBytes = 0,
                    LeftFileCount = 0,
                    RightSizeBytes = item.TotalSizeBytes,
                    RightFileCount = item.FileCount,
                    DeltaBytes = item.TotalSizeBytes
                };
            }
        }

        return map.Values.OrderByDescending(c => SnapshotMath.AbsClamped(c.DeltaBytes)).Take(200).ToList();
    }

    private static IReadOnlyList<T> Take<T>(List<T> source, int max) =>
        max <= 0 ? Array.Empty<T>() : source.Take(max).ToList();
}

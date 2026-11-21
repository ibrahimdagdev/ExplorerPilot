using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Scanning;

namespace ExplorerPilot.Core.Snapshots;

/// <summary>Builds a <see cref="DiskSnapshot"/> from an existing scan (no disk I/O).</summary>
public static class DiskSnapshotBuilder
{
    public static DiskSnapshot Build(
        ScanResult scan,
        string? displayName = null,
        Guid? id = null,
        DateTimeOffset? createdAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);

        var root = scan.RootNode;
        var normalizedRoot = SnapshotPathNormalizer.NormalizeAbsolute(root.FullPath);
        var entries = new List<SnapshotEntry>(Math.Max(64, (int)Math.Min(scan.TotalFiles + scan.TotalDirectories + 1, int.MaxValue)));
        var visited = 0;
        Flatten(root, normalizedRoot, entries, ref visited, cancellationToken);

        var incomplete = scan.WasCancelled || scan.FinalStatus == ScanPhase.Failed;
        var completeness = incomplete
            ? (scan.WasCancelled ? "Cancelled" : "Failed")
            : (scan.Errors.Count > 0 ? "Warnings" : "Complete");

        var name = string.IsNullOrWhiteSpace(displayName)
            ? $"{root.Name} — {(createdAtUtc ?? DateTimeOffset.UtcNow).LocalDateTime:yyyy-MM-dd HH:mm}"
            : displayName.Trim();

        return new DiskSnapshot
        {
            SchemaVersion = SnapshotSchema.CurrentVersion,
            Id = id ?? Guid.NewGuid(),
            CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow,
            DisplayName = name,
            RootPath = root.FullPath,
            RootName = root.Name,
            NormalizedRootPath = normalizedRoot,
            TotalBytes = scan.TotalBytes,
            TotalFiles = scan.TotalFiles,
            TotalDirectories = scan.TotalDirectories,
            DurationSeconds = scan.Duration.TotalSeconds,
            FinalStatus = scan.FinalStatus,
            CompletenessCode = completeness,
            IsIncomplete = incomplete,
            Categories = scan.CategoryStatistics
                .Select(c => new SnapshotCategoryStat
                {
                    Category = c.Category,
                    CategoryCode = c.Category.ToString(),
                    FileCount = c.FileCount,
                    TotalSizeBytes = c.TotalSizeBytes,
                    PercentageOfTotalBytes = c.PercentageOfTotalBytes
                })
                .ToList(),
            Extensions = scan.ExtensionStatistics
                .Select(e => new SnapshotExtensionStat
                {
                    Extension = e.Extension,
                    Category = e.Category,
                    CategoryCode = e.Category.ToString(),
                    FileCount = e.FileCount,
                    TotalSizeBytes = e.TotalSizeBytes,
                    PercentageOfTotalBytes = e.PercentageOfTotalBytes
                })
                .ToList(),
            Entries = entries
        };
    }

    private static void Flatten(
        FileSystemNode node,
        string normalizedRoot,
        List<SnapshotEntry> entries,
        ref int visited,
        CancellationToken cancellationToken)
    {
        if ((++visited & 0x3FF) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        var relative = SnapshotPathNormalizer.MakeRelative(normalizedRoot, node.FullPath);
        var extension = node.IsDirectory
            ? string.Empty
            : FileCategoryMapper.NormalizeExtension(node.Name);
        var category = node.IsDirectory
            ? FileCategory.Other
            : FileCategoryMapper.GetCategory(extension);

        entries.Add(new SnapshotEntry
        {
            RelativePath = relative,
            Name = node.Name,
            IsDirectory = node.IsDirectory,
            SizeBytes = node.SizeBytes,
            FileCount = node.FileCount,
            DirectoryCount = node.DirectoryCount,
            LastModifiedUtc = node.LastModified?.ToUniversalTime(),
            Extension = extension,
            Category = category
        });

        foreach (var child in node.Children)
        {
            Flatten(child, normalizedRoot, entries, ref visited, cancellationToken);
        }
    }
}

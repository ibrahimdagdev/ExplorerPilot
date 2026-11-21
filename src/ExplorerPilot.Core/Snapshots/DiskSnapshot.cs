using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Snapshots;

/// <summary>
/// Versioned disk usage snapshot produced from an existing <see cref="Models.ScanResult"/>.
/// Schema <see cref="SnapshotSchema.CurrentVersion"/>.
/// </summary>
public sealed class DiskSnapshot
{
    public required string SchemaVersion { get; init; }

    public required Guid Id { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required string DisplayName { get; init; }

    public required string RootPath { get; init; }

    public required string RootName { get; init; }

    /// <summary>Normalized root path used for comparisons.</summary>
    public required string NormalizedRootPath { get; init; }

    public long TotalBytes { get; init; }

    public long TotalFiles { get; init; }

    public long TotalDirectories { get; init; }

    public double DurationSeconds { get; init; }

    public ScanPhase FinalStatus { get; init; }

    public required string CompletenessCode { get; init; }

    public bool IsIncomplete { get; init; }

    public IReadOnlyList<SnapshotCategoryStat> Categories { get; init; } = [];

    public IReadOnlyList<SnapshotExtensionStat> Extensions { get; init; } = [];

    /// <summary>Flattened file and directory entries keyed by relative path.</summary>
    public IReadOnlyList<SnapshotEntry> Entries { get; init; } = [];
}

public sealed class SnapshotEntry
{
    /// <summary>
    /// Path relative to the snapshot root using backslashes.
    /// Empty string represents the root directory itself.
    /// </summary>
    public required string RelativePath { get; init; }

    public required string Name { get; init; }

    public bool IsDirectory { get; init; }

    public long SizeBytes { get; init; }

    public long FileCount { get; init; }

    public long DirectoryCount { get; init; }

    public DateTimeOffset? LastModifiedUtc { get; init; }

    /// <summary>Normalized extension including leading dot; empty for directories/no extension.</summary>
    public string Extension { get; init; } = string.Empty;

    public FileCategory Category { get; init; } = FileCategory.Other;
}

public sealed class SnapshotCategoryStat
{
    public FileCategory Category { get; init; }

    public required string CategoryCode { get; init; }

    public long FileCount { get; init; }

    public long TotalSizeBytes { get; init; }

    public double PercentageOfTotalBytes { get; init; }
}

public sealed class SnapshotExtensionStat
{
    public required string Extension { get; init; }

    public FileCategory Category { get; init; }

    public required string CategoryCode { get; init; }

    public long FileCount { get; init; }

    public long TotalSizeBytes { get; init; }

    public double PercentageOfTotalBytes { get; init; }
}

/// <summary>Lightweight metadata for listing without loading full entry payloads.</summary>
public sealed class SnapshotInfo
{
    public required Guid Id { get; init; }

    public required string DisplayName { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required string RootPath { get; init; }

    public long TotalBytes { get; init; }

    public long TotalFiles { get; init; }

    public long TotalDirectories { get; init; }

    public bool IsIncomplete { get; init; }

    public required string SchemaVersion { get; init; }

    public required string FilePath { get; init; }
}

using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Snapshots;

public sealed class SnapshotComparisonResult
{
    public required SnapshotInfo Left { get; init; }

    public required SnapshotInfo Right { get; init; }

    public required SnapshotComparisonSummary Summary { get; init; }

    public IReadOnlyList<FolderSizeChange> GrowingFolders { get; init; } = [];

    public IReadOnlyList<FolderSizeChange> ShrinkingFolders { get; init; } = [];

    public IReadOnlyList<FilePresenceChange> AddedFiles { get; init; } = [];

    public IReadOnlyList<FilePresenceChange> RemovedFiles { get; init; } = [];

    public IReadOnlyList<FileSizeChange> ChangedFiles { get; init; } = [];

    public IReadOnlyList<CategorySizeChange> CategoryChanges { get; init; } = [];

    public IReadOnlyList<ExtensionSizeChange> ExtensionChanges { get; init; } = [];
}

public sealed class SnapshotComparisonSummary
{
    public long LeftTotalBytes { get; init; }

    public long RightTotalBytes { get; init; }

    public long TotalBytesDelta { get; init; }

    public long LeftTotalFiles { get; init; }

    public long RightTotalFiles { get; init; }

    public int AddedFileCount { get; init; }

    public int RemovedFileCount { get; init; }

    public int ChangedFileCount { get; init; }

    public int GrowingFolderCount { get; init; }

    public int ShrinkingFolderCount { get; init; }

    public bool RootsDiffer { get; init; }

    public bool LeftIncomplete { get; init; }

    public bool RightIncomplete { get; init; }

    public bool IsDefinitive => !RootsDiffer && !LeftIncomplete && !RightIncomplete;

    public DateTimeOffset LeftCreatedAtUtc { get; init; }

    public DateTimeOffset RightCreatedAtUtc { get; init; }

    public required string LeftRootPath { get; init; }

    public required string RightRootPath { get; init; }

    public required string LeftDisplayName { get; init; }

    public required string RightDisplayName { get; init; }
}

public sealed class FolderSizeChange
{
    public required string RelativePath { get; init; }

    public required string Name { get; init; }

    public long LeftSizeBytes { get; init; }

    public long RightSizeBytes { get; init; }

    public long DeltaBytes { get; init; }
}

public sealed class FilePresenceChange
{
    public required string RelativePath { get; init; }

    public required string Name { get; init; }

    public long SizeBytes { get; init; }

    public string Extension { get; init; } = string.Empty;

    public FileCategory Category { get; init; }
}

public sealed class FileSizeChange
{
    public required string RelativePath { get; init; }

    public required string Name { get; init; }

    public long LeftSizeBytes { get; init; }

    public long RightSizeBytes { get; init; }

    public long DeltaBytes { get; init; }

    public string Extension { get; init; } = string.Empty;

    public FileCategory Category { get; init; }
}

public sealed class CategorySizeChange
{
    public FileCategory Category { get; init; }

    public required string CategoryCode { get; init; }

    public long LeftSizeBytes { get; init; }

    public long RightSizeBytes { get; init; }

    public long DeltaBytes { get; init; }

    public long LeftFileCount { get; init; }

    public long RightFileCount { get; init; }
}

public sealed class ExtensionSizeChange
{
    public required string Extension { get; init; }

    public FileCategory Category { get; init; }

    public required string CategoryCode { get; init; }

    public long LeftSizeBytes { get; init; }

    public long RightSizeBytes { get; init; }

    public long DeltaBytes { get; init; }

    public long LeftFileCount { get; init; }

    public long RightFileCount { get; init; }
}

public sealed class SnapshotCompareOptions
{
    public int MaxGrowingFolders { get; init; } = 100;

    public int MaxShrinkingFolders { get; init; } = 100;

    public int MaxAddedFiles { get; init; } = 500;

    public int MaxRemovedFiles { get; init; } = 500;

    public int MaxChangedFiles { get; init; } = 500;
}

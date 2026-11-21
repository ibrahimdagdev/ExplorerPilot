using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Reporting;

/// <summary>
/// Versioned, culture-agnostic disk analysis report document produced from an existing <see cref="Models.ScanResult"/>.
/// Schema version <see cref="CurrentSchemaVersion"/> documents the JSON shape.
/// </summary>
public sealed class DiskAnalysisReport
{
    /// <summary>Current JSON/document schema version (semver major.minor).</summary>
    public const string CurrentSchemaVersion = "1.0";

    public required string SchemaVersion { get; init; }

    public required DateTimeOffset GeneratedAtUtc { get; init; }

    public required ReportScanSummary Summary { get; init; }

    public IReadOnlyList<ReportFileEntry> LargestFiles { get; init; } = [];

    public IReadOnlyList<ReportFolderEntry> LargestFolders { get; init; } = [];

    public IReadOnlyList<ReportCategoryEntry> Categories { get; init; } = [];

    public IReadOnlyList<ReportExtensionEntry> Extensions { get; init; } = [];

    public IReadOnlyList<ReportErrorEntry> Errors { get; init; } = [];

    /// <summary>Optional detailed directory tree; null when not requested.</summary>
    public ReportDirectoryNode? DirectoryTree { get; init; }
}

public sealed class ReportScanSummary
{
    public required string RootPath { get; init; }

    public required string RootName { get; init; }

    public long TotalBytes { get; init; }

    public long TotalFiles { get; init; }

    public long TotalDirectories { get; init; }

    public double DurationSeconds { get; init; }

    public ScanPhase FinalStatus { get; init; }

    public ReportCompleteness Completeness { get; init; }

    public required string CompletenessCode { get; init; }

    public int ErrorCount { get; init; }

    public bool IsIncomplete { get; init; }
}

public sealed class ReportFileEntry
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public long SizeBytes { get; init; }

    public required string Extension { get; init; }

    public FileCategory Category { get; init; }

    public DateTimeOffset? LastModifiedUtc { get; init; }
}

public sealed class ReportFolderEntry
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public long SizeBytes { get; init; }

    public long FileCount { get; init; }

    public long DirectoryCount { get; init; }
}

public sealed class ReportCategoryEntry
{
    public FileCategory Category { get; init; }

    public required string CategoryCode { get; init; }

    public long FileCount { get; init; }

    public long TotalSizeBytes { get; init; }

    public double PercentageOfTotalBytes { get; init; }
}

public sealed class ReportExtensionEntry
{
    public required string Extension { get; init; }

    public FileCategory Category { get; init; }

    public required string CategoryCode { get; init; }

    public long FileCount { get; init; }

    public long TotalSizeBytes { get; init; }

    public double PercentageOfTotalBytes { get; init; }
}

public sealed class ReportErrorEntry
{
    public required string Path { get; init; }

    public ScanErrorType ErrorType { get; init; }

    public required string ErrorTypeCode { get; init; }

    public required string Message { get; init; }
}

public sealed class ReportDirectoryNode
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public bool IsDirectory { get; init; }

    public long SizeBytes { get; init; }

    public long FileCount { get; init; }

    public long DirectoryCount { get; init; }

    public NodeScanStatus ScanStatus { get; init; }

    public required string ScanStatusCode { get; init; }

    public IReadOnlyList<ReportDirectoryNode> Children { get; init; } = [];
}

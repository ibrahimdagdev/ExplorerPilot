using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;

namespace ExplorerPilot.Core.Reporting;

/// <summary>Parameters for exporting a disk analysis report from an existing scan.</summary>
public sealed class ReportExportOptions
{
    public required ScanResult ScanResult { get; init; }

    public required ReportFormat Format { get; init; }

    public required string DestinationPath { get; init; }

    /// <summary>When true, embeds a full directory tree (can be large).</summary>
    public bool IncludeDirectoryTree { get; init; }

    public bool IncludeCategories { get; init; } = true;

    public bool IncludeExtensions { get; init; } = true;

    public bool IncludeErrors { get; init; } = true;

    public int MaxLargestFiles { get; init; } = 100;

    public int MaxLargestFolders { get; init; } = 100;

    /// <summary>Session id captured when export started; used to detect stale scans.</summary>
    public Guid ScanSessionId { get; init; }

    /// <summary>Optional localized labels for human-readable formats (CSV/HTML).</summary>
    public ReportDisplayLabels Labels { get; init; } = ReportDisplayLabels.InvariantEnglish;

    public ReportBuildOptions ToBuildOptions() =>
        new()
        {
            IncludeDirectoryTree = IncludeDirectoryTree,
            IncludeCategories = IncludeCategories,
            IncludeExtensions = IncludeExtensions,
            IncludeErrors = IncludeErrors,
            MaxLargestFiles = MaxLargestFiles,
            MaxLargestFolders = MaxLargestFolders
        };
}

/// <summary>In-memory report composition flags (no I/O).</summary>
public sealed class ReportBuildOptions
{
    public bool IncludeDirectoryTree { get; init; }

    public bool IncludeCategories { get; init; } = true;

    public bool IncludeExtensions { get; init; } = true;

    public bool IncludeErrors { get; init; } = true;

    public int MaxLargestFiles { get; init; } = 100;

    public int MaxLargestFolders { get; init; } = 100;

    public static ReportBuildOptions Default { get; } = new();
}

/// <summary>Human-readable labels for CSV/HTML. JSON uses invariant codes.</summary>
public sealed class ReportDisplayLabels
{
    public static ReportDisplayLabels InvariantEnglish { get; } = new();

    public string ReportTitle { get; init; } = "Disk Analysis Report";

    public string IncompleteBanner { get; init; } = "This report is based on an incomplete or cancelled scan.";

    public string SummarySection { get; init; } = "Summary";

    public string LargestFilesSection { get; init; } = "Largest Files";

    public string LargestFoldersSection { get; init; } = "Largest Folders";

    public string CategoriesSection { get; init; } = "File Categories";

    public string ExtensionsSection { get; init; } = "File Extensions";

    public string ErrorsSection { get; init; } = "Errors / Skipped Paths";

    public string DirectoryTreeSection { get; init; } = "Directory Tree";

    public string RootPath { get; init; } = "Root path";

    public string GeneratedAt { get; init; } = "Generated (UTC)";

    public string TotalSize { get; init; } = "Total size (bytes)";

    public string TotalFiles { get; init; } = "Total files";

    public string TotalFolders { get; init; } = "Total folders";

    public string Duration { get; init; } = "Duration (seconds)";

    public string Completeness { get; init; } = "Completeness";

    public string ErrorCount { get; init; } = "Error count";

    public string Name { get; init; } = "Name";

    public string Path { get; init; } = "Path";

    public string SizeBytes { get; init; } = "SizeBytes";

    public string Extension { get; init; } = "Extension";

    public string Category { get; init; } = "Category";

    public string FileCount { get; init; } = "FileCount";

    public string DirectoryCount { get; init; } = "DirectoryCount";

    public string Percentage { get; init; } = "Percentage";

    public string LastModifiedUtc { get; init; } = "LastModifiedUtc";

    public string ErrorType { get; init; } = "ErrorType";

    public string Message { get; init; } = "Message";

    public string ScanStatus { get; init; } = "ScanStatus";

    public string CompletenessComplete { get; init; } = "Complete";

    public string CompletenessCancelled { get; init; } = "Cancelled";

    public string CompletenessFailed { get; init; } = "Failed";

    public string CompletenessWarnings { get; init; } = "Completed with warnings";

    public IReadOnlyDictionary<FileCategory, string> CategoryNames { get; init; } =
        new Dictionary<FileCategory, string>
        {
            [FileCategory.Videos] = "Videos",
            [FileCategory.Images] = "Images",
            [FileCategory.Audio] = "Audio",
            [FileCategory.Documents] = "Documents",
            [FileCategory.Archives] = "Archives",
            [FileCategory.Applications] = "Applications",
            [FileCategory.Other] = "Other"
        };

    public string CategoryName(FileCategory category) =>
        CategoryNames.TryGetValue(category, out var name) ? name : category.ToString();

    public string CompletenessText(ReportCompleteness completeness) =>
        completeness switch
        {
            ReportCompleteness.Complete => CompletenessComplete,
            ReportCompleteness.Cancelled => CompletenessCancelled,
            ReportCompleteness.Failed => CompletenessFailed,
            ReportCompleteness.Warnings => CompletenessWarnings,
            _ => CompletenessComplete
        };
}

public sealed class ReportExportProgress
{
    public double Percent { get; init; }

    public required string Stage { get; init; }
}

public sealed class ReportExportResult
{
    public bool Succeeded { get; init; }

    public string? DestinationPath { get; init; }

    public string? ErrorMessage { get; init; }

    public bool WasCancelled { get; init; }

    public bool WasStaleSession { get; init; }

    public static ReportExportResult Success(string path) =>
        new() { Succeeded = true, DestinationPath = path };

    public static ReportExportResult Cancelled() =>
        new() { Succeeded = false, WasCancelled = true };

    public static ReportExportResult StaleSession() =>
        new() { Succeeded = false, WasStaleSession = true, ErrorMessage = "Scan session changed during export." };

    public static ReportExportResult Failed(string message) =>
        new() { Succeeded = false, ErrorMessage = message };
}

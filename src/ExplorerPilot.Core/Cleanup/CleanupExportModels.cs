using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Reporting;

namespace ExplorerPilot.Core.Cleanup;

public sealed class CleanupExportOptions
{
    public required IReadOnlyList<CleanupCandidate> Candidates { get; init; }

    public required ReportFormat Format { get; init; }

    public required string DestinationPath { get; init; }

    public Guid ScanSessionId { get; init; }

    public bool FromIncompleteScan { get; init; }

    public bool ResultsTruncated { get; init; }

    public long UniquePotentialSavingsBytes { get; init; }

    public int UniquePathCount { get; init; }

    public required CleanupExportLabels Labels { get; init; }
}

public sealed class CleanupExportLabels
{
    public string Title { get; init; } = "Cleanup Recommendations Preview";

    public string Disclaimer { get; init; } =
        "This export is a preview only. It is not an automatic deletion instruction. ExplorerPilot does not delete, move, or rename files.";

    public string IncompleteBanner { get; init; } =
        "Based on an incomplete or cancelled scan — recommendations are not definitive.";

    public string TruncatedBanner { get; init; } =
        "The recommendation list was truncated to the configured maximum (top potential savings).";

    public string GeneratedAt { get; init; } = "Generated (UTC)";

    public string UniquePotential { get; init; } = "Unique potential savings (bytes)";

    public string UniquePaths { get; init; } = "Unique paths";

    public string CandidateCount { get; init; } = "Recommendation rows";

    public string Name { get; init; } = "Name";

    public string Path { get; init; } = "FullPath";

    public string SizeBytes { get; init; } = "SizeBytes";

    public string PotentialSavingsBytes { get; init; } = "PotentialSavingsBytes";

    public string LastModifiedUtc { get; init; } = "LastModifiedUtc";

    public string Category { get; init; } = "Category";

    public string Risk { get; init; } = "Risk";

    public string Reason { get; init; } = "Reason";

    public string IsDirectory { get; init; } = "IsDirectory";

    public Func<CleanupCategory, string> CategoryFormatter { get; init; } = static c => c.ToString();

    public Func<CleanupRiskLevel, string> RiskFormatter { get; init; } = static r => r.ToString();

    public Func<CleanupCandidate, string> ReasonFormatter { get; init; } = static c => c.ReasonKey;
}

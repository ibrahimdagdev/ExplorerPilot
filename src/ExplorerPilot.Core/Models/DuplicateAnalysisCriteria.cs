using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Models;

public sealed class DuplicateAnalysisCriteria
{
    public Guid ScanSessionId { get; init; }

    /// <summary>Optional name substring filter (case-insensitive). Applied after detection.</summary>
    public string? NameFilter { get; init; }

    /// <summary>Optional extension filter (with or without leading dot).</summary>
    public string? ExtensionFilter { get; init; }

    /// <summary>Skip files smaller than this size during hashing (performance).</summary>
    public long? MinimumSizeBytes { get; init; }

    /// <summary>Hide groups with fewer than this many paths after detection.</summary>
    public int MinimumDuplicateCount { get; init; } = 2;

    public DuplicateSortField SortBy { get; init; } = DuplicateSortField.PotentialSavings;

    public bool SortDescending { get; init; } = true;

    /// <summary>Maximum concurrent file hash operations.</summary>
    public int MaxDegreeOfParallelism { get; init; } = 2;
}

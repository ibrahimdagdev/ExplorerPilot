using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Snapshots;

public sealed class SnapshotTrendRequest
{
    public TrendGranularity Granularity { get; init; } = TrendGranularity.Daily;

    /// <summary>When set, only snapshots whose normalized root matches are included.</summary>
    public string? RootPathFilter { get; init; }

    public DateTimeOffset? FromUtc { get; init; }

    public DateTimeOffset? ToUtc { get; init; }

    /// <summary>Max points drawn on the chart after visual downsampling.</summary>
    public int MaxTimelinePoints { get; init; } = 60;

    /// <summary>Safety cap on raw bucketed points before downsampling (full span kept when possible).</summary>
    public int MaxSourceBuckets { get; init; } = 2_000;

    public int MaxCategorySeries { get; init; } = 8;

    public int MaxExtensionSeries { get; init; } = 10;

    public int MaxGrowingFolders { get; init; } = 25;

    /// <summary>When false, only metadata timeline is computed (no category/folder loads).</summary>
    public bool IncludeDetailBreakdowns { get; init; } = true;
}

public sealed class SnapshotTrendProgress
{
    public double Fraction { get; init; }

    public string? Phase { get; init; }
}

public sealed class SnapshotTrendResult
{
    public required IReadOnlyList<SnapshotTrendSeries> Series { get; init; }

    public required SnapshotTrendSeries? SelectedSeries { get; init; }

    public IReadOnlyList<string> AvailableRoots { get; init; } = [];

    /// <summary>True when chart points were visually downsampled (history not discarded wholesale).</summary>
    public bool WasDownsampled { get; init; }

    public int SnapshotCountConsidered { get; init; }
}

public sealed class SnapshotTrendSeries
{
    public required string RootPath { get; init; }

    public required string NormalizedRootPath { get; init; }

    /// <summary>Full analytical timeline (bucketed). Not gap-filled for missing dates.</summary>
    public required IReadOnlyList<SnapshotTrendPoint> Points { get; init; }

    /// <summary>Downsampled view for chart rendering; preserves first/last/peaks.</summary>
    public required IReadOnlyList<SnapshotTrendPoint> ChartPoints { get; init; }

    public long FirstTotalBytes { get; init; }

    public long LastTotalBytes { get; init; }

    public long NetDeltaBytes { get; init; }

    /// <summary>Percent change from first to last; null when first total is 0.</summary>
    public double? GrowthPercent { get; init; }

    public DateTimeOffset? FirstSnapshotCreatedAtUtc { get; init; }

    public DateTimeOffset? LastSnapshotCreatedAtUtc { get; init; }

    public bool ContainsIncompleteSnapshots { get; init; }

    public bool WasDownsampled { get; init; }

    public bool WasSourceCapped { get; init; }

    public IReadOnlyList<SnapshotCategoryTrend> CategoryTrends { get; init; } = [];

    public IReadOnlyList<SnapshotExtensionTrend> ExtensionTrends { get; init; } = [];

    public IReadOnlyList<SnapshotFolderTrend> FastestGrowingFolders { get; init; } = [];
}

public sealed class SnapshotTrendPoint
{
    public required DateTimeOffset BucketStartUtc { get; init; }

    public required DateTimeOffset SnapshotCreatedAtUtc { get; init; }

    public required Guid SnapshotId { get; init; }

    public required string DisplayName { get; init; }

    public long TotalBytes { get; init; }

    public long TotalFiles { get; init; }

    /// <summary>Delta vs previous point in the series; 0 for the first point.</summary>
    public long DeltaBytes { get; init; }

    /// <summary>Percent vs previous point; null when previous total is 0 or first point.</summary>
    public double? DeltaPercent { get; init; }

    public bool IsIncomplete { get; init; }

    /// <summary>True when multiple snapshots fell into this bucket and the latest was chosen.</summary>
    public bool BucketHadMultipleSnapshots { get; init; }
}

public sealed class SnapshotCategoryTrend
{
    public required string CategoryCode { get; init; }

    public long FirstBytes { get; init; }

    public long LastBytes { get; init; }

    public long DeltaBytes { get; init; }

    public double? GrowthPercent { get; init; }
}

public sealed class SnapshotExtensionTrend
{
    public required string Extension { get; init; }

    public long FirstBytes { get; init; }

    public long LastBytes { get; init; }

    public long DeltaBytes { get; init; }

    public double? GrowthPercent { get; init; }
}

public sealed class SnapshotFolderTrend
{
    public required string RelativePath { get; init; }

    public required string Name { get; init; }

    public long FirstBytes { get; init; }

    public long LastBytes { get; init; }

    public long DeltaBytes { get; init; }

    public double? GrowthPercent { get; init; }
}

/// <summary>Lightweight payload for trend detail without full file entry materialization.</summary>
public sealed class SnapshotTrendPayload
{
    public required Guid Id { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required string RootPath { get; init; }

    public required string NormalizedRootPath { get; init; }

    public long TotalBytes { get; init; }

    public bool IsIncomplete { get; init; }

    public IReadOnlyList<SnapshotCategoryStat> Categories { get; init; } = [];

    public IReadOnlyList<SnapshotExtensionStat> Extensions { get; init; } = [];

    /// <summary>Directory entries only (relative path → size), typically capped.</summary>
    public IReadOnlyDictionary<string, long> DirectorySizes { get; init; } =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
}

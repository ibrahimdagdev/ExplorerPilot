using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Models;

/// <summary>
/// Aggregate dashboard payload. Volume capacity and scan totals are intentionally separate.
/// </summary>
public sealed class DashboardSummary
{
    public required DashboardCapacitySummary Capacity { get; init; }

    public DashboardScanSummary? LastScan { get; init; }

    public IReadOnlyList<DashboardPathEntry> LargestFiles { get; init; } = [];

    public IReadOnlyList<DashboardPathEntry> LargestFolders { get; init; } = [];

    public IReadOnlyList<DashboardCategorySummary> Categories { get; init; } = [];

    public DashboardSnapshotSummary? Snapshot { get; init; }

    public DashboardCleanupSummary? Cleanup { get; init; }

    public Guid ScanSessionId { get; init; }

    public DateTimeOffset BuiltAtUtc { get; init; }
}

public sealed class DashboardCapacitySummary
{
    public int DriveCount { get; init; }

    public int ReadyDriveCount { get; init; }

    /// <summary>Only ready drives with TotalSizeBytes &gt; 0.</summary>
    public long TotalCapacityBytes { get; init; }

    public long UsedSpaceBytes { get; init; }

    public long FreeSpaceBytes { get; init; }

    public double UsagePercentage { get; init; }

    public DashboardDriveSummary? HighestUsageDrive { get; init; }

    public IReadOnlyList<DashboardDriveSummary> Drives { get; init; } = [];
}

public sealed class DashboardDriveSummary
{
    public required string Name { get; init; }

    public required string RootPath { get; init; }

    public string VolumeLabel { get; init; } = string.Empty;

    public VolumeDriveKind Kind { get; init; }

    public DriveReadyStatus Status { get; init; }

    public long TotalSizeBytes { get; init; }

    public long UsedSpaceBytes { get; init; }

    public long AvailableFreeSpaceBytes { get; init; }

    public double UsagePercentage { get; init; }

    public bool IsReady { get; init; }

    public bool IncludeInCapacityTotals { get; init; }

    public bool CanScan { get; init; }
}

public sealed class DashboardScanSummary
{
    public required string RootPath { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public long TotalBytes { get; init; }

    public long TotalFiles { get; init; }

    public long TotalDirectories { get; init; }

    public TimeSpan Duration { get; init; }

    public ScanPhase FinalStatus { get; init; }

    public bool IsIncomplete { get; init; }

    public int ErrorCount { get; init; }
}

public sealed class DashboardPathEntry
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public long SizeBytes { get; init; }

    public bool IsDirectory { get; init; }
}

public sealed class DashboardCategorySummary
{
    public FileCategory Category { get; init; }

    public long TotalSizeBytes { get; init; }

    public long FileCount { get; init; }

    public double PercentageOfTotalBytes { get; init; }
}

public sealed class DashboardSnapshotSummary
{
    public bool HasMatchingSnapshots { get; init; }

    public DateTimeOffset? LastSnapshotAtUtc { get; init; }

    public long? DeltaBytesVsPrevious { get; init; }

    public double? GrowthPercent { get; init; }

    public string? MatchedRootPath { get; init; }

    public bool ContainsIncompleteSnapshots { get; init; }

    public int SnapshotCount { get; init; }
}

public sealed class DashboardCleanupSummary
{
    public long UniquePotentialSavingsBytes { get; init; }

    public int RecommendationCount { get; init; }

    public int LowRiskCount { get; init; }

    public int MediumRiskCount { get; init; }

    public int HighRiskCount { get; init; }

    public bool FromIncompleteScan { get; init; }

    public bool IsPreviewOnly { get; init; } = true;
}

public sealed class DashboardBuildRequest
{
    public IReadOnlyList<DriveInformation> Drives { get; init; } = [];

    public ScanResult? Scan { get; init; }

    public Guid ScanSessionId { get; init; }

    public bool ScanIncomplete { get; init; }

    public DateTimeOffset? ScanCompletedAtUtc { get; init; }

    public DashboardSnapshotSummary? Snapshot { get; init; }

    public DashboardCleanupSummary? Cleanup { get; init; }

    public int TopFiles { get; init; } = 10;

    public int TopFolders { get; init; } = 10;
}

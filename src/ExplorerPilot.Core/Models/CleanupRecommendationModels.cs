using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Models;

public sealed class CleanupRecommendationCriteria
{
    public Guid ScanSessionId { get; init; }

    /// <summary>Minimum size for the Large Files category.</summary>
    public long LargeFileMinBytes { get; init; } = 100L * 1024 * 1024;

    /// <summary>Minimum age for the Old Files category.</summary>
    public TimeSpan OldFileMinAge { get; init; } = TimeSpan.FromDays(365);

    /// <summary>Minimum age for old log files.</summary>
    public TimeSpan OldLogMinAge { get; init; } = TimeSpan.FromDays(90);

    /// <summary>Hard cap on emitted candidates (across all categories).</summary>
    public int MaxRecommendations { get; init; } = 5_000;

    public bool IncludeEmptyFolders { get; init; } = true;

    public bool IncludeVerifiedDuplicates { get; init; } = true;

    /// <summary>UTC reference time for age calculations (tests inject a fixed clock).</summary>
    public DateTimeOffset UtcNow { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class CleanupRecommendationProgress
{
    public double Fraction { get; init; }

    public string? Phase { get; init; }

    public int CandidatesFound { get; init; }
}

public sealed class CleanupCandidate
{
    /// <summary>Stable id: category + normalized path (allows multi-category rows).</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string FullPath { get; init; }

    /// <summary>Case-insensitive normalized path used for double-count prevention.</summary>
    public required string PathKey { get; init; }

    public bool IsDirectory { get; init; }

    public long SizeBytes { get; init; }

    /// <summary>
    /// Estimated reclaimable bytes for this candidate alone.
    /// For hard-linked duplicate extras this may be 0.
    /// </summary>
    public long PotentialSavingsBytes { get; init; }

    public DateTimeOffset? LastModified { get; init; }

    public CleanupCategory Category { get; init; }

    public CleanupRiskLevel Risk { get; init; }

    /// <summary>Localization key for the human-readable reason.</summary>
    public required string ReasonKey { get; init; }

    public string? DuplicateGroupHash { get; init; }

    public string? FileIdentityKey { get; init; }
}

public sealed class CleanupRecommendationResult
{
    public required Guid ScanSessionId { get; init; }

    public required IReadOnlyList<CleanupCandidate> Candidates { get; init; }

    public bool FromIncompleteScan { get; init; }

    public bool ResultsTruncated { get; init; }

    public int WalkedNodes { get; init; }

    public int ExcludedProtectedPaths { get; init; }

    public int SkippedUnsafeNodes { get; init; }

    /// <summary>Sum of unique-path potential savings (no double-count across categories).</summary>
    public long UniquePotentialSavingsBytes { get; init; }

    public int UniquePathCount { get; init; }

    public int LargeFileCount { get; init; }

    public int OldFileCount { get; init; }

    public int TemporaryFileCount { get; init; }

    public int OldLogFileCount { get; init; }

    public int EmptyFolderCount { get; init; }

    public int VerifiedDuplicateCount { get; init; }
}

/// <summary>Aggregates potential savings without counting the same path twice.</summary>
public static class CleanupSavingsAggregator
{
    public static long SumUniquePotential(IEnumerable<CleanupCandidate> candidates)
    {
        var best = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.PathKey))
            {
                continue;
            }

            if (!best.TryGetValue(candidate.PathKey, out var existing)
                || candidate.PotentialSavingsBytes > existing)
            {
                best[candidate.PathKey] = candidate.PotentialSavingsBytes;
            }
        }

        long total = 0;
        foreach (var value in best.Values)
        {
            total = SafeAdd(total, value);
        }

        return total;
    }

    /// <summary>Unique-path size total using the maximum SizeBytes seen per PathKey.</summary>
    public static long SumUniqueSize(IEnumerable<CleanupCandidate> candidates)
    {
        var best = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.PathKey))
            {
                continue;
            }

            if (!best.TryGetValue(candidate.PathKey, out var existing)
                || candidate.SizeBytes > existing)
            {
                best[candidate.PathKey] = candidate.SizeBytes;
            }
        }

        long total = 0;
        foreach (var value in best.Values)
        {
            total = SafeAdd(total, value);
        }

        return total;
    }

    public static int CountUniquePaths(IEnumerable<CleanupCandidate> candidates)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate.PathKey))
            {
                set.Add(candidate.PathKey);
            }
        }

        return set.Count;
    }

    /// <summary>
    /// Risk counts for unique paths — each path contributes its highest risk among selected rows.
    /// </summary>
    public static (int Low, int Medium, int High) CountUniquePathsByHighestRisk(
        IEnumerable<CleanupCandidate> candidates)
    {
        var highest = new Dictionary<string, CleanupRiskLevel>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.PathKey))
            {
                continue;
            }

            if (!highest.TryGetValue(candidate.PathKey, out var existing)
                || candidate.Risk > existing)
            {
                highest[candidate.PathKey] = candidate.Risk;
            }
        }

        var low = 0;
        var medium = 0;
        var high = 0;
        foreach (var risk in highest.Values)
        {
            switch (risk)
            {
                case CleanupRiskLevel.Low:
                    low++;
                    break;
                case CleanupRiskLevel.Medium:
                    medium++;
                    break;
                default:
                    high++;
                    break;
            }
        }

        return (low, medium, high);
    }

    private static long SafeAdd(long left, long right)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }
}

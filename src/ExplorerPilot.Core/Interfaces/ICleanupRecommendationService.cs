using ExplorerPilot.Core.Models;

namespace ExplorerPilot.Core.Interfaces;

/// <summary>
/// Builds advisory cleanup recommendations from an existing in-memory scan tree.
/// Never deletes, moves, or modifies files on disk.
/// </summary>
public interface ICleanupRecommendationService
{
    Task<CleanupRecommendationResult> AnalyzeAsync(
        FileSystemNode root,
        CleanupRecommendationCriteria criteria,
        IReadOnlyList<DuplicateGroup>? verifiedDuplicateGroups = null,
        bool fromIncompleteScan = false,
        IProgress<CleanupRecommendationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

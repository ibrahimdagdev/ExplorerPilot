using ExplorerPilot.Core.Cleanup;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Scanning;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.Infrastructure.Services;

public sealed class CleanupRecommendationService : ICleanupRecommendationService
{
    private static readonly HashSet<string> TemporaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp", ".temp", ".partial", ".crdownload", ".download", ".~tmp", ".bak", ".old"
    };

    private static readonly HashSet<string> LogExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".log", ".log.txt"
    };

    private readonly ILogger<CleanupRecommendationService> _logger;

    public CleanupRecommendationService(ILogger<CleanupRecommendationService> logger)
    {
        _logger = logger;
    }

    public Task<CleanupRecommendationResult> AnalyzeAsync(
        FileSystemNode root,
        CleanupRecommendationCriteria criteria,
        IReadOnlyList<DuplicateGroup>? verifiedDuplicateGroups = null,
        bool fromIncompleteScan = false,
        IProgress<CleanupRecommendationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Analyze(root, criteria, verifiedDuplicateGroups, fromIncompleteScan, progress, cancellationToken),
            cancellationToken);

    private CleanupRecommendationResult Analyze(
        FileSystemNode root,
        CleanupRecommendationCriteria criteria,
        IReadOnlyList<DuplicateGroup>? verifiedDuplicateGroups,
        bool fromIncompleteScan,
        IProgress<CleanupRecommendationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(criteria);

        var max = Math.Clamp(criteria.MaxRecommendations, 1, 50_000);
        var buffer = new CleanupTopNBuffer(max);
        var walked = 0;
        var excluded = 0;
        var skippedUnsafe = 0;
        var utcNow = criteria.UtcNow;

        progress?.Report(new CleanupRecommendationProgress { Fraction = 0.02, Phase = "walk" });

        Walk(
            root,
            isRoot: true,
            criteria,
            utcNow,
            buffer,
            ref walked,
            ref excluded,
            ref skippedUnsafe,
            cancellationToken);

        progress?.Report(new CleanupRecommendationProgress
        {
            Fraction = 0.7,
            Phase = "duplicates",
            CandidatesFound = buffer.Count
        });

        if (criteria.IncludeVerifiedDuplicates && verifiedDuplicateGroups is { Count: > 0 })
        {
            AddVerifiedDuplicates(verifiedDuplicateGroups, buffer, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var candidates = buffer.ToSortedList();
        var uniqueBytes = CleanupSavingsAggregator.SumUniquePotential(candidates);
        var uniquePaths = CleanupSavingsAggregator.CountUniquePaths(candidates);

        progress?.Report(new CleanupRecommendationProgress
        {
            Fraction = 1.0,
            Phase = "done",
            CandidatesFound = candidates.Count
        });

        _logger.LogInformation(
            "Cleanup analysis session {Session}: {Count} candidates (offered {Offered}), unique savings {Bytes}, walked {Walked}",
            criteria.ScanSessionId,
            candidates.Count,
            buffer.OfferedCount,
            uniqueBytes,
            walked);

        return new CleanupRecommendationResult
        {
            ScanSessionId = criteria.ScanSessionId,
            Candidates = candidates,
            FromIncompleteScan = fromIncompleteScan,
            ResultsTruncated = buffer.IsTruncated,
            WalkedNodes = walked,
            ExcludedProtectedPaths = excluded,
            SkippedUnsafeNodes = skippedUnsafe,
            UniquePotentialSavingsBytes = uniqueBytes,
            UniquePathCount = uniquePaths,
            LargeFileCount = candidates.Count(c => c.Category == CleanupCategory.LargeFiles),
            OldFileCount = candidates.Count(c => c.Category == CleanupCategory.OldFiles),
            TemporaryFileCount = candidates.Count(c => c.Category == CleanupCategory.TemporaryFiles),
            OldLogFileCount = candidates.Count(c => c.Category == CleanupCategory.OldLogFiles),
            EmptyFolderCount = candidates.Count(c => c.Category == CleanupCategory.EmptyFolders),
            VerifiedDuplicateCount = candidates.Count(c => c.Category == CleanupCategory.VerifiedDuplicates)
        };
    }

    private static void Walk(
        FileSystemNode node,
        bool isRoot,
        CleanupRecommendationCriteria criteria,
        DateTimeOffset utcNow,
        CleanupTopNBuffer buffer,
        ref int walked,
        ref int excluded,
        ref int skippedUnsafe,
        CancellationToken cancellationToken)
    {
        if ((++walked & 0x3FF) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (node.ScanStatus is NodeScanStatus.SkippedReparsePoint
            or NodeScanStatus.Inaccessible
            or NodeScanStatus.Error)
        {
            skippedUnsafe++;
            return;
        }

        if (!isRoot && CleanupProtectedPaths.IsExcluded(node.FullPath))
        {
            excluded++;
            return;
        }

        if (node.IsDirectory)
        {
            if (!isRoot && criteria.IncludeEmptyFolders && IsEmptyFolder(node))
            {
                buffer.Offer(CreateCandidate(
                    node,
                    CleanupCategory.EmptyFolders,
                    RiskFor(node, CleanupCategory.EmptyFolders),
                    "Cleanup_Reason_EmptyFolder",
                    potentialSavings: 0));
            }

            foreach (var child in node.Children)
            {
                Walk(
                    child,
                    isRoot: false,
                    criteria,
                    utcNow,
                    buffer,
                    ref walked,
                    ref excluded,
                    ref skippedUnsafe,
                    cancellationToken);
            }

            return;
        }

        var ext = Path.GetExtension(node.Name);
        var name = node.Name;

        if (IsTemporaryName(name, ext))
        {
            buffer.Offer(CreateCandidate(
                node,
                CleanupCategory.TemporaryFiles,
                RiskFor(node, CleanupCategory.TemporaryFiles),
                "Cleanup_Reason_Temporary",
                potentialSavings: Math.Max(0, node.SizeBytes)));
        }

        if (IsLogFile(name, ext) && IsAtLeastAge(node.LastModified, utcNow, criteria.OldLogMinAge))
        {
            buffer.Offer(CreateCandidate(
                node,
                CleanupCategory.OldLogFiles,
                RiskFor(node, CleanupCategory.OldLogFiles),
                "Cleanup_Reason_OldLog",
                potentialSavings: Math.Max(0, node.SizeBytes)));
        }

        if (node.SizeBytes >= criteria.LargeFileMinBytes)
        {
            buffer.Offer(CreateCandidate(
                node,
                CleanupCategory.LargeFiles,
                RiskFor(node, CleanupCategory.LargeFiles),
                "Cleanup_Reason_LargeFile",
                potentialSavings: Math.Max(0, node.SizeBytes)));
        }

        if (IsAtLeastAge(node.LastModified, utcNow, criteria.OldFileMinAge)
            && !IsTemporaryName(name, ext)
            && !IsLogFile(name, ext))
        {
            buffer.Offer(CreateCandidate(
                node,
                CleanupCategory.OldFiles,
                RiskFor(node, CleanupCategory.OldFiles),
                "Cleanup_Reason_OldFile",
                potentialSavings: Math.Max(0, node.SizeBytes)));
        }
    }

    private static void AddVerifiedDuplicates(
        IReadOnlyList<DuplicateGroup> groups,
        CleanupTopNBuffer buffer,
        CancellationToken cancellationToken)
    {
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Files.Count < 2)
            {
                continue;
            }

            var keptIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keptAnonymous = false;
            var ordered = group.Files
                .OrderBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Cap group-level reclaimable bytes using the shared calculator (hard-link aware).
            var groupBudget = DuplicateSavingsCalculator.CalculateGroupSavings(group.SizeBytes, group.Files);
            long allocated = 0;

            foreach (var file in ordered)
            {
                if (CleanupProtectedPaths.IsExcluded(file.FullPath))
                {
                    continue;
                }

                var identity = file.FileIdentityKey;
                var isKeeper = false;
                if (!string.IsNullOrWhiteSpace(identity))
                {
                    if (keptIdentities.Count == 0)
                    {
                        keptIdentities.Add(identity);
                        isKeeper = true;
                    }
                    else if (keptIdentities.Contains(identity))
                    {
                        // Hard-link to a kept physical copy — no reclaimable bytes.
                        continue;
                    }
                }
                else if (!keptAnonymous)
                {
                    keptAnonymous = true;
                    isKeeper = true;
                }

                if (isKeeper)
                {
                    continue;
                }

                var savings = Math.Max(0, group.SizeBytes);
                if (allocated >= groupBudget)
                {
                    break;
                }

                if (allocated + savings > groupBudget)
                {
                    savings = groupBudget - allocated;
                }

                if (savings <= 0)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(identity))
                {
                    keptIdentities.Add(identity);
                }

                allocated += savings;
                var pathKey = CleanupProtectedPaths.NormalizeKey(file.FullPath);
                buffer.Offer(new CleanupCandidate
                {
                    Id = $"dup|{pathKey}",
                    Name = file.Name,
                    FullPath = file.FullPath,
                    PathKey = pathKey,
                    IsDirectory = false,
                    SizeBytes = file.SizeBytes,
                    PotentialSavingsBytes = savings,
                    LastModified = file.LastModified,
                    Category = CleanupCategory.VerifiedDuplicates,
                    Risk = RiskForPath(file.FullPath, file.Name, CleanupCategory.VerifiedDuplicates),
                    ReasonKey = "Cleanup_Reason_VerifiedDuplicate",
                    DuplicateGroupHash = group.ContentHashSha256,
                    FileIdentityKey = file.FileIdentityKey
                });
            }
        }
    }

    private static CleanupCandidate CreateCandidate(
        FileSystemNode node,
        CleanupCategory category,
        CleanupRiskLevel risk,
        string reasonKey,
        long potentialSavings)
    {
        var pathKey = CleanupProtectedPaths.NormalizeKey(node.FullPath);
        return new CleanupCandidate
        {
            Id = $"{(int)category}|{pathKey}",
            Name = node.Name,
            FullPath = node.FullPath,
            PathKey = pathKey,
            IsDirectory = node.IsDirectory,
            SizeBytes = Math.Max(0, node.SizeBytes),
            PotentialSavingsBytes = Math.Max(0, potentialSavings),
            LastModified = node.LastModified,
            Category = category,
            Risk = risk,
            ReasonKey = reasonKey
        };
    }

    private static CleanupRiskLevel RiskFor(FileSystemNode node, CleanupCategory category) =>
        RiskForPath(node.FullPath, node.Name, category);

    private static CleanupRiskLevel RiskForPath(string fullPath, string name, CleanupCategory category)
    {
        if (CleanupProtectedPaths.IsHighRiskExtension(name)
            || CleanupProtectedPaths.IsHighRiskLocation(fullPath))
        {
            return CleanupRiskLevel.High;
        }

        return category switch
        {
            CleanupCategory.TemporaryFiles => CleanupRiskLevel.Low,
            CleanupCategory.OldLogFiles => CleanupRiskLevel.Low,
            CleanupCategory.EmptyFolders => CleanupRiskLevel.Low,
            CleanupCategory.VerifiedDuplicates => CleanupRiskLevel.Medium,
            CleanupCategory.LargeFiles => CleanupRiskLevel.Medium,
            CleanupCategory.OldFiles => CleanupRiskLevel.Medium,
            _ => CleanupRiskLevel.Medium
        };
    }

    private static bool IsEmptyFolder(FileSystemNode node) =>
        node.IsDirectory
        && node.Children.Count == 0
        && node.FileCount == 0
        && node.DirectoryCount == 0
        && node.ScanStatus == NodeScanStatus.Completed;

    private static bool IsTemporaryName(string name, string extension)
    {
        if (TemporaryExtensions.Contains(extension))
        {
            return true;
        }

        if (name.StartsWith("~$", StringComparison.Ordinal)
            || name.StartsWith(".~", StringComparison.Ordinal)
            || name.EndsWith("~", StringComparison.Ordinal))
        {
            return true;
        }

        return name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLogFile(string name, string extension)
    {
        if (LogExtensions.Contains(extension))
        {
            return true;
        }

        return name.EndsWith(".log", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAtLeastAge(DateTimeOffset? lastModified, DateTimeOffset utcNow, TimeSpan minAge)
    {
        if (lastModified is null || minAge <= TimeSpan.Zero)
        {
            return false;
        }

        return utcNow - lastModified.Value >= minAge;
    }
}

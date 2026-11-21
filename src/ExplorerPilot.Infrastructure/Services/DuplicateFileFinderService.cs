using System.Collections.Concurrent;
using System.Security.Cryptography;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Scanning;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.Infrastructure.Services;

public sealed class DuplicateFileFinderService : IDuplicateFileFinderService
{
    private readonly ILogger<DuplicateFileFinderService> _logger;

    public DuplicateFileFinderService(ILogger<DuplicateFileFinderService> logger)
    {
        _logger = logger;
    }

    public Task<DuplicateAnalysisResult> AnalyzeAsync(
        FileSystemNode root,
        DuplicateAnalysisCriteria criteria,
        bool fromIncompleteScan,
        IProgress<DuplicateAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(criteria);

        var parallelism = Math.Clamp(criteria.MaxDegreeOfParallelism, 1, 4);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(CreateCancelledResult(criteria.ScanSessionId, fromIncompleteScan));
        }

        return Task.Run(
            () =>
            {
                try
                {
                    return AnalyzeCore(root, criteria, fromIncompleteScan, parallelism, progress, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return CreateCancelledResult(criteria.ScanSessionId, fromIncompleteScan);
                }
            },
            CancellationToken.None);
    }

    private static DuplicateAnalysisResult CreateCancelledResult(Guid sessionId, bool fromIncompleteScan) =>
        new()
        {
            ScanSessionId = sessionId,
            Groups = [],
            FinalStatus = DuplicateAnalysisPhase.Cancelled,
            FromIncompleteScan = fromIncompleteScan
        };

    private DuplicateAnalysisResult AnalyzeCore(
        FileSystemNode root,
        DuplicateAnalysisCriteria criteria,
        bool fromIncompleteScan,
        int parallelism,
        IProgress<DuplicateAnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        long skipped = 0;
        long failed = 0;
        long hashed = 0;

        try
        {
            Report(progress, DuplicateAnalysisPhase.GroupingBySize, 0, 0, 0, 0, null);

            // Stage 1 — group by size from the in-memory scan tree (no directory re-enumeration).
            var sizeGroups = GroupBySize(root, criteria.MinimumSizeBytes, ref skipped, cancellationToken);
            var analyzed = sizeGroups.Values.Sum(g => g.Count);
            var candidates = sizeGroups.Values.Where(g => g.Count >= 2).SelectMany(g => g).ToList();

            Report(progress, DuplicateAnalysisPhase.PartialHashing, analyzed, candidates.Count, hashed, skipped, null);

            if (candidates.Count == 0)
            {
                return BuildEmptyResult(criteria.ScanSessionId, analyzed, skipped, failed, fromIncompleteScan);
            }

            // Stage 2 — partial SHA-256 to eliminate non-matches cheaply.
            var partialGroups = ComputePartialHashGroups(
                candidates,
                parallelism,
                ref hashed,
                ref skipped,
                ref failed,
                progress,
                analyzed,
                cancellationToken);

            var fullCandidates = partialGroups.Values.Where(g => g.Count >= 2).SelectMany(g => g).ToList();
            Report(progress, DuplicateAnalysisPhase.FullHashing, analyzed, fullCandidates.Count, hashed, skipped, null);

            if (fullCandidates.Count == 0)
            {
                return BuildEmptyResult(criteria.ScanSessionId, analyzed, skipped, failed, fromIncompleteScan);
            }

            // Stage 3 — full SHA-256; identical size + hash => duplicates.
            var duplicateGroups = ComputeFullHashGroups(
                fullCandidates,
                parallelism,
                ref hashed,
                ref skipped,
                ref failed,
                progress,
                analyzed,
                cancellationToken);

            Report(progress, DuplicateAnalysisPhase.Finalizing, analyzed, fullCandidates.Count, hashed, skipped, null);

            var filtered = ApplyFiltersAndSort(duplicateGroups, criteria);

            var result = new DuplicateAnalysisResult
            {
                ScanSessionId = criteria.ScanSessionId,
                Groups = filtered,
                AnalyzedFiles = analyzed,
                DuplicateGroupCount = filtered.Count,
                DuplicateFileCount = filtered.Sum(g => g.DuplicateCount),
                PotentialSavingsBytes = DuplicateSavingsCalculator.SumGroupSavings(filtered),
                SkippedFiles = skipped,
                FailedFiles = failed,
                FinalStatus = DuplicateAnalysisPhase.Completed,
                FromIncompleteScan = fromIncompleteScan
            };

            Report(progress, DuplicateAnalysisPhase.Completed, analyzed, fullCandidates.Count, hashed, skipped, null);
            return result;
        }
        catch (OperationCanceledException)
        {
            Report(progress, DuplicateAnalysisPhase.Cancelled, 0, 0, hashed, skipped, null);
            return new DuplicateAnalysisResult
            {
                ScanSessionId = criteria.ScanSessionId,
                Groups = [],
                AnalyzedFiles = 0,
                SkippedFiles = skipped,
                FailedFiles = failed,
                FinalStatus = DuplicateAnalysisPhase.Cancelled,
                FromIncompleteScan = fromIncompleteScan
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Duplicate analysis failed");
            Report(progress, DuplicateAnalysisPhase.Failed, 0, 0, hashed, skipped, ex.Message);
            return new DuplicateAnalysisResult
            {
                ScanSessionId = criteria.ScanSessionId,
                Groups = [],
                SkippedFiles = skipped,
                FailedFiles = failed,
                FinalStatus = DuplicateAnalysisPhase.Failed,
                FromIncompleteScan = fromIncompleteScan
            };
        }
    }

    private static Dictionary<long, List<Candidate>> GroupBySize(
        FileSystemNode root,
        long? minimumSizeBytes,
        ref long skipped,
        CancellationToken cancellationToken)
    {
        var groups = new Dictionary<long, List<Candidate>>();
        var stack = new Stack<FileSystemNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = stack.Pop();

            if (node.IsDirectory)
            {
                var children = node.Children;
                for (var i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push(children[i]);
                }

                continue;
            }

            if (node.ScanStatus is NodeScanStatus.SkippedReparsePoint
                or NodeScanStatus.Inaccessible
                or NodeScanStatus.Error)
            {
                skipped++;
                continue;
            }

            if (minimumSizeBytes is { } min && node.SizeBytes < min)
            {
                skipped++;
                continue;
            }

            if (!groups.TryGetValue(node.SizeBytes, out var list))
            {
                list = [];
                groups[node.SizeBytes] = list;
            }

            list.Add(new Candidate(node));
        }

        return groups;
    }

    private Dictionary<(long Size, string PartialHex), List<Candidate>> ComputePartialHashGroups(
        List<Candidate> candidates,
        int parallelism,
        ref long hashed,
        ref long skipped,
        ref long failed,
        IProgress<DuplicateAnalysisProgress>? progress,
        long analyzed,
        CancellationToken cancellationToken)
    {
        var bag = new ConcurrentBag<(Candidate Candidate, string? PartialHex, bool Skipped, bool Failed)>();
        var hashedLocal = 0L;
        var skippedLocal = 0L;
        var failedLocal = 0L;
        var skippedBaseline = skipped;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = parallelism,
            CancellationToken = cancellationToken
        };

        Parallel.ForEach(candidates, options, candidate =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!TryValidateUnchanged(candidate, out var reason))
                {
                    Interlocked.Increment(ref skippedLocal);
                    bag.Add((candidate, null, true, false));
                    _logger.LogDebug("Skipping {Path}: {Reason}", candidate.Node.FullPath, reason);
                    return;
                }

                Report(progress, DuplicateAnalysisPhase.PartialHashing, analyzed, candidates.Count,
                    Interlocked.Read(ref hashedLocal), Interlocked.Read(ref skippedLocal) + skippedBaseline,
                    candidate.Node.FullPath);

                var hash = FileContentHasher.ComputePartialSha256Async(
                        candidate.Node.FullPath,
                        candidate.Node.SizeBytes,
                        cancellationToken)
                    .GetAwaiter()
                    .GetResult();

                // Re-check after hashing.
                if (!TryValidateUnchanged(candidate, out reason))
                {
                    Interlocked.Increment(ref skippedLocal);
                    bag.Add((candidate, null, true, false));
                    return;
                }

                Interlocked.Increment(ref hashedLocal);
                bag.Add((candidate, FileContentHasher.ToHex(hash), false, false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failedLocal);
                bag.Add((candidate, null, false, true));
                _logger.LogDebug(ex, "Partial hash failed for {Path}", candidate.Node.FullPath);
            }
        });

        hashed += hashedLocal;
        skipped += skippedLocal;
        failed += failedLocal;

        var groups = new Dictionary<(long, string), List<Candidate>>();
        foreach (var item in bag)
        {
            if (item.PartialHex is null)
            {
                continue;
            }

            var key = (item.Candidate.Node.SizeBytes, item.PartialHex);
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }

            list.Add(item.Candidate);
        }

        return groups;
    }

    private List<DuplicateGroup> ComputeFullHashGroups(
        List<Candidate> candidates,
        int parallelism,
        ref long hashed,
        ref long skipped,
        ref long failed,
        IProgress<DuplicateAnalysisProgress>? progress,
        long analyzed,
        CancellationToken cancellationToken)
    {
        var bag = new ConcurrentBag<(Candidate Candidate, string? FullHex, string? Identity, bool Skipped, bool Failed)>();
        var hashedLocal = 0L;
        var skippedLocal = 0L;
        var failedLocal = 0L;
        var skippedBaseline = skipped;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = parallelism,
            CancellationToken = cancellationToken
        };

        Parallel.ForEach(candidates, options, candidate =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!TryValidateUnchanged(candidate, out var reason))
                {
                    Interlocked.Increment(ref skippedLocal);
                    bag.Add((candidate, null, null, true, false));
                    _logger.LogDebug("Skipping {Path}: {Reason}", candidate.Node.FullPath, reason);
                    return;
                }

                if (IsReparsePoint(candidate.Node.FullPath))
                {
                    Interlocked.Increment(ref skippedLocal);
                    bag.Add((candidate, null, null, true, false));
                    return;
                }

                Report(progress, DuplicateAnalysisPhase.FullHashing, analyzed, candidates.Count,
                    Interlocked.Read(ref hashedLocal), Interlocked.Read(ref skippedLocal) + skippedBaseline,
                    candidate.Node.FullPath);

                var hash = FileContentHasher.ComputeFullSha256Async(
                        candidate.Node.FullPath,
                        candidate.Node.SizeBytes,
                        cancellationToken)
                    .GetAwaiter()
                    .GetResult();

                if (!TryValidateUnchanged(candidate, out reason))
                {
                    Interlocked.Increment(ref skippedLocal);
                    bag.Add((candidate, null, null, true, false));
                    return;
                }

                var identity = WindowsFileIdentity.TryGetIdentityKey(candidate.Node.FullPath);
                Interlocked.Increment(ref hashedLocal);
                bag.Add((candidate, FileContentHasher.ToHex(hash), identity, false, false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failedLocal);
                bag.Add((candidate, null, null, false, true));
                _logger.LogDebug(ex, "Full hash failed for {Path}", candidate.Node.FullPath);
            }
        });

        hashed += hashedLocal;
        skipped += skippedLocal;
        failed += failedLocal;

        var byHash = new Dictionary<(long Size, string Hash), List<(Candidate Candidate, string? Identity)>>();
        foreach (var item in bag)
        {
            if (item.FullHex is null)
            {
                continue;
            }

            var key = (item.Candidate.Node.SizeBytes, item.FullHex);
            if (!byHash.TryGetValue(key, out var list))
            {
                list = [];
                byHash[key] = list;
            }

            list.Add((item.Candidate, item.Identity));
        }

        var groups = new List<DuplicateGroup>();
        foreach (var ((size, hash), members) in byHash)
        {
            if (members.Count < 2)
            {
                continue;
            }

            var files = members
                .Select(m => new DuplicateFileInfo
                {
                    Name = m.Candidate.Node.Name,
                    FullPath = m.Candidate.Node.FullPath,
                    SizeBytes = size,
                    LastModified = m.Candidate.Node.LastModified,
                    FileIdentityKey = m.Identity
                })
                .OrderBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            groups.Add(new DuplicateGroup
            {
                ContentHashSha256 = hash,
                SizeBytes = size,
                Files = files,
                PotentialSavingsBytes = DuplicateSavingsCalculator.CalculateGroupSavings(size, files)
            });
        }

        return groups;
    }

    private static List<DuplicateGroup> ApplyFiltersAndSort(
        List<DuplicateGroup> groups,
        DuplicateAnalysisCriteria criteria)
    {
        IEnumerable<DuplicateGroup> query = groups;

        var minCount = Math.Max(2, criteria.MinimumDuplicateCount);
        query = query.Where(g => g.DuplicateCount >= minCount);

        if (!string.IsNullOrWhiteSpace(criteria.NameFilter))
        {
            var name = criteria.NameFilter.Trim();
            query = query.Where(g =>
                g.Files.Any(f => f.Name.Contains(name, StringComparison.OrdinalIgnoreCase)
                                 || f.FullPath.Contains(name, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(criteria.ExtensionFilter))
        {
            var ext = NormalizeExtension(criteria.ExtensionFilter);
            query = query.Where(g =>
                g.Files.Any(f =>
                {
                    var fileExt = Path.GetExtension(f.Name);
                    return fileExt.Equals(ext, StringComparison.OrdinalIgnoreCase);
                }));
        }

        query = criteria.SortBy switch
        {
            DuplicateSortField.FileSize => criteria.SortDescending
                ? query.OrderByDescending(g => g.SizeBytes).ThenBy(g => g.Files[0].Name, StringComparer.OrdinalIgnoreCase)
                : query.OrderBy(g => g.SizeBytes).ThenBy(g => g.Files[0].Name, StringComparer.OrdinalIgnoreCase),
            DuplicateSortField.DuplicateCount => criteria.SortDescending
                ? query.OrderByDescending(g => g.DuplicateCount).ThenByDescending(g => g.PotentialSavingsBytes)
                : query.OrderBy(g => g.DuplicateCount).ThenByDescending(g => g.PotentialSavingsBytes),
            DuplicateSortField.FileName => criteria.SortDescending
                ? query.OrderByDescending(g => g.Files[0].Name, StringComparer.OrdinalIgnoreCase)
                : query.OrderBy(g => g.Files[0].Name, StringComparer.OrdinalIgnoreCase),
            _ => criteria.SortDescending
                ? query.OrderByDescending(g => g.PotentialSavingsBytes).ThenByDescending(g => g.SizeBytes)
                : query.OrderBy(g => g.PotentialSavingsBytes).ThenByDescending(g => g.SizeBytes)
        };

        return query.ToList();
    }

    private static string NormalizeExtension(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }

    private static bool TryValidateUnchanged(Candidate candidate, out string reason)
    {
        reason = string.Empty;
        try
        {
            var info = new FileInfo(candidate.Node.FullPath);
            if (!info.Exists)
            {
                reason = "File not found";
                return false;
            }

            if (info.Length != candidate.Node.SizeBytes)
            {
                reason = "Size changed";
                return false;
            }

            if (candidate.Node.LastModified is { } expected)
            {
                var actual = new DateTimeOffset(DateTime.SpecifyKind(info.LastWriteTimeUtc, DateTimeKind.Utc));
                if (Math.Abs((actual - expected).TotalSeconds) > 2)
                {
                    reason = "Modified during analysis";
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            return attrs.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }

    private static DuplicateAnalysisResult BuildEmptyResult(
        Guid sessionId,
        long analyzed,
        long skipped,
        long failed,
        bool fromIncompleteScan) =>
        new()
        {
            ScanSessionId = sessionId,
            Groups = [],
            AnalyzedFiles = analyzed,
            DuplicateGroupCount = 0,
            DuplicateFileCount = 0,
            PotentialSavingsBytes = 0,
            SkippedFiles = skipped,
            FailedFiles = failed,
            FinalStatus = DuplicateAnalysisPhase.Completed,
            FromIncompleteScan = fromIncompleteScan
        };

    private static void Report(
        IProgress<DuplicateAnalysisProgress>? progress,
        DuplicateAnalysisPhase phase,
        long analyzed,
        long candidates,
        long hashed,
        long skipped,
        string? currentPath)
    {
        progress?.Report(new DuplicateAnalysisProgress
        {
            Phase = phase,
            AnalyzedFiles = analyzed,
            CandidateFiles = candidates,
            HashedFiles = hashed,
            SkippedFiles = skipped,
            CurrentPath = currentPath
        });
    }

    private sealed class Candidate(FileSystemNode node)
    {
        public FileSystemNode Node { get; } = node;
    }
}

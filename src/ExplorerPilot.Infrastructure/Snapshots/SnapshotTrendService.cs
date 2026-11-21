using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Snapshots;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.Infrastructure.Snapshots;

public sealed class SnapshotTrendService : ISnapshotTrendService
{
    private readonly ISnapshotStore _store;
    private readonly ILogger<SnapshotTrendService> _logger;

    public SnapshotTrendService(ISnapshotStore store, ILogger<SnapshotTrendService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public Task<SnapshotTrendResult> AnalyzeAsync(
        IReadOnlyList<SnapshotInfo> snapshots,
        SnapshotTrendRequest request,
        IProgress<SnapshotTrendProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Analyze(snapshots, request ?? new SnapshotTrendRequest(), progress, cancellationToken),
            cancellationToken);

    private SnapshotTrendResult Analyze(
        IReadOnlyList<SnapshotInfo> snapshots,
        SnapshotTrendRequest request,
        IProgress<SnapshotTrendProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        progress?.Report(new SnapshotTrendProgress { Fraction = 0.05, Phase = "group-roots" });

        var maxChartPoints = Math.Clamp(request.MaxTimelinePoints, 2, 500);
        var maxSourceBuckets = Math.Clamp(request.MaxSourceBuckets, maxChartPoints, 10_000);
        var filtered = snapshots
            .Where(s => s is not null)
            .Where(s => !request.FromUtc.HasValue || s.CreatedAtUtc >= request.FromUtc.Value)
            .Where(s => !request.ToUtc.HasValue || s.CreatedAtUtc <= request.ToUtc.Value)
            .ToList();

        var byRoot = filtered
            .GroupBy(s => SnapshotPathNormalizer.NormalizeAbsolute(s.RootPath), SnapshotPathNormalizer.PathComparer)
            .OrderBy(g => g.Key, SnapshotPathNormalizer.PathComparer)
            .ToList();

        var availableRoots = byRoot
            .Select(g => g.First().RootPath)
            .Distinct(SnapshotPathNormalizer.PathComparer)
            .OrderBy(r => r, SnapshotPathNormalizer.PathComparer)
            .ToList();

        if (!string.IsNullOrWhiteSpace(request.RootPathFilter))
        {
            var wanted = SnapshotPathNormalizer.NormalizeAbsolute(request.RootPathFilter);
            byRoot = byRoot
                .Where(g => SnapshotPathNormalizer.RootsEquivalent(g.Key, wanted))
                .ToList();
        }

        progress?.Report(new SnapshotTrendProgress { Fraction = 0.2, Phase = "timeline" });

        var seriesList = new List<SnapshotTrendSeries>();
        var downsampled = false;
        var index = 0;
        foreach (var group in byRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            var series = BuildSeries(
                group.ToList(),
                request,
                maxChartPoints,
                maxSourceBuckets,
                cancellationToken);
            downsampled |= series.WasDownsampled;
            seriesList.Add(series);

            progress?.Report(new SnapshotTrendProgress
            {
                Fraction = 0.2 + (0.7 * index / Math.Max(1, byRoot.Count)),
                Phase = "series"
            });
        }

        SnapshotTrendSeries? selected = seriesList.Count == 1
            ? seriesList[0]
            : seriesList.FirstOrDefault(s =>
                  !string.IsNullOrWhiteSpace(request.RootPathFilter)
                  && SnapshotPathNormalizer.RootsEquivalent(s.NormalizedRootPath, request.RootPathFilter))
              ?? seriesList.FirstOrDefault();

        progress?.Report(new SnapshotTrendProgress { Fraction = 1.0, Phase = "done" });
        _logger.LogInformation(
            "Trend analysis: {Series} root series, {Snapshots} snapshots considered",
            seriesList.Count,
            filtered.Count);

        return new SnapshotTrendResult
        {
            Series = seriesList,
            SelectedSeries = selected,
            AvailableRoots = availableRoots,
            WasDownsampled = downsampled,
            SnapshotCountConsidered = filtered.Count
        };
    }

    private SnapshotTrendSeries BuildSeries(
        List<SnapshotInfo> snapshots,
        SnapshotTrendRequest request,
        int maxChartPoints,
        int maxSourceBuckets,
        CancellationToken cancellationToken)
    {
        // Chronological ascending.
        snapshots.Sort(static (a, b) =>
        {
            var cmp = a.CreatedAtUtc.CompareTo(b.CreatedAtUtc);
            return cmp != 0 ? cmp : a.Id.CompareTo(b.Id);
        });

        var buckets = new Dictionary<DateTimeOffset, List<SnapshotInfo>>();
        foreach (var snap in snapshots)
        {
            var key = BucketStart(snap.CreatedAtUtc, request.Granularity);
            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }

            list.Add(snap);
        }

        // Deterministic pick: latest CreatedAtUtc, then Id. No gap-filling for empty buckets.
        var bucketPoints = buckets
            .OrderBy(kv => kv.Key)
            .Select(kv =>
            {
                var chosen = kv.Value
                    .OrderByDescending(s => s.CreatedAtUtc)
                    .ThenByDescending(s => s.Id)
                    .First();
                return (Bucket: kv.Key, Snapshot: chosen, Multi: kv.Value.Count > 1);
            })
            .ToList();

        var wasSourceCapped = false;
        if (bucketPoints.Count > maxSourceBuckets)
        {
            // Safety only: keep newest buckets so detail loads stay bounded.
            wasSourceCapped = true;
            bucketPoints = bucketPoints.Skip(bucketPoints.Count - maxSourceBuckets).ToList();
        }

        var points = new List<SnapshotTrendPoint>(bucketPoints.Count);
        SnapshotTrendPoint? previous = null;
        foreach (var (bucket, snap, multi) in bucketPoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var delta = previous is null ? 0 : SnapshotMath.SafeDelta(snap.TotalBytes, previous.TotalBytes);
            var pct = previous is null
                ? null
                : SnapshotMath.SafePercentChange(snap.TotalBytes, previous.TotalBytes);

            var point = new SnapshotTrendPoint
            {
                BucketStartUtc = bucket,
                SnapshotCreatedAtUtc = snap.CreatedAtUtc,
                SnapshotId = snap.Id,
                DisplayName = snap.DisplayName,
                TotalBytes = snap.TotalBytes,
                TotalFiles = snap.TotalFiles,
                DeltaBytes = delta,
                DeltaPercent = pct,
                IsIncomplete = snap.IsIncomplete,
                BucketHadMultipleSnapshots = multi
            };
            points.Add(point);
            previous = point;
        }

        var chartPoints = TrendDownsampler.Downsample(points, maxChartPoints);
        var wasDownsampled = chartPoints.Count < points.Count;

        var first = points.FirstOrDefault();
        var last = points.LastOrDefault();
        var firstBytes = first?.TotalBytes ?? 0;
        var lastBytes = last?.TotalBytes ?? 0;
        var net = SnapshotMath.SafeDelta(lastBytes, firstBytes);
        var growth = SnapshotMath.SafePercentChange(lastBytes, firstBytes);

        IReadOnlyList<SnapshotCategoryTrend> categoryTrends = [];
        IReadOnlyList<SnapshotExtensionTrend> extensionTrends = [];
        IReadOnlyList<SnapshotFolderTrend> folderTrends = [];

        if (request.IncludeDetailBreakdowns && points.Count >= 1)
        {
            (categoryTrends, extensionTrends, folderTrends) = BuildDetails(
                points,
                request,
                cancellationToken);
        }

        var rootPath = snapshots[0].RootPath;
        return new SnapshotTrendSeries
        {
            RootPath = rootPath,
            NormalizedRootPath = SnapshotPathNormalizer.NormalizeAbsolute(rootPath),
            Points = points,
            ChartPoints = chartPoints,
            FirstTotalBytes = firstBytes,
            LastTotalBytes = lastBytes,
            NetDeltaBytes = net,
            GrowthPercent = growth,
            FirstSnapshotCreatedAtUtc = first?.SnapshotCreatedAtUtc,
            LastSnapshotCreatedAtUtc = last?.SnapshotCreatedAtUtc,
            ContainsIncompleteSnapshots = points.Any(p => p.IsIncomplete),
            WasDownsampled = wasDownsampled,
            WasSourceCapped = wasSourceCapped,
            CategoryTrends = categoryTrends,
            ExtensionTrends = extensionTrends,
            FastestGrowingFolders = folderTrends
        };
    }

    private (
        IReadOnlyList<SnapshotCategoryTrend> Categories,
        IReadOnlyList<SnapshotExtensionTrend> Extensions,
        IReadOnlyList<SnapshotFolderTrend> Folders) BuildDetails(
        IReadOnlyList<SnapshotTrendPoint> points,
        SnapshotTrendRequest request,
        CancellationToken cancellationToken)
    {
        var firstId = points[0].SnapshotId;
        var lastId = points[^1].SnapshotId;

        SnapshotTrendPayload firstPayload;
        SnapshotTrendPayload lastPayload;
        try
        {
            firstPayload = _store.LoadTrendPayloadAsync(firstId, cancellationToken: cancellationToken)
                .GetAwaiter().GetResult();
            lastPayload = firstId == lastId
                ? firstPayload
                : _store.LoadTrendPayloadAsync(lastId, cancellationToken: cancellationToken)
                    .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load trend payloads for detail breakdown");
            return ([], [], []);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var categories = MergeCategoryTrends(firstPayload.Categories, lastPayload.Categories, request.MaxCategorySeries);
        var extensions = MergeExtensionTrends(firstPayload.Extensions, lastPayload.Extensions, request.MaxExtensionSeries);
        var folders = MergeFolderTrends(firstPayload.DirectorySizes, lastPayload.DirectorySizes, request.MaxGrowingFolders);
        return (categories, extensions, folders);
    }

    private static List<SnapshotCategoryTrend> MergeCategoryTrends(
        IReadOnlyList<SnapshotCategoryStat> first,
        IReadOnlyList<SnapshotCategoryStat> last,
        int max)
    {
        var map = new Dictionary<string, (long First, long Last)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in first)
        {
            var code = string.IsNullOrWhiteSpace(item.CategoryCode) ? item.Category.ToString() : item.CategoryCode;
            map[code] = (item.TotalSizeBytes, 0);
        }

        foreach (var item in last)
        {
            var code = string.IsNullOrWhiteSpace(item.CategoryCode) ? item.Category.ToString() : item.CategoryCode;
            if (map.TryGetValue(code, out var existing))
            {
                map[code] = (existing.First, item.TotalSizeBytes);
            }
            else
            {
                map[code] = (0, item.TotalSizeBytes);
            }
        }

        return map
            .Select(kv =>
            {
                var delta = SnapshotMath.SafeDelta(kv.Value.Last, kv.Value.First);
                return new SnapshotCategoryTrend
                {
                    CategoryCode = kv.Key,
                    FirstBytes = kv.Value.First,
                    LastBytes = kv.Value.Last,
                    DeltaBytes = delta,
                    GrowthPercent = SnapshotMath.SafePercentChange(kv.Value.Last, kv.Value.First)
                };
            })
            .OrderByDescending(t => SnapshotMath.AbsClamped(t.DeltaBytes))
            .Take(Math.Clamp(max, 1, 50))
            .ToList();
    }

    private static List<SnapshotExtensionTrend> MergeExtensionTrends(
        IReadOnlyList<SnapshotExtensionStat> first,
        IReadOnlyList<SnapshotExtensionStat> last,
        int max)
    {
        var map = new Dictionary<string, (long First, long Last)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in first)
        {
            var key = item.Extension ?? string.Empty;
            map[key] = (item.TotalSizeBytes, 0);
        }

        foreach (var item in last)
        {
            var key = item.Extension ?? string.Empty;
            if (map.TryGetValue(key, out var existing))
            {
                map[key] = (existing.First, item.TotalSizeBytes);
            }
            else
            {
                map[key] = (0, item.TotalSizeBytes);
            }
        }

        return map
            .Select(kv =>
            {
                var delta = SnapshotMath.SafeDelta(kv.Value.Last, kv.Value.First);
                return new SnapshotExtensionTrend
                {
                    Extension = kv.Key,
                    FirstBytes = kv.Value.First,
                    LastBytes = kv.Value.Last,
                    DeltaBytes = delta,
                    GrowthPercent = SnapshotMath.SafePercentChange(kv.Value.Last, kv.Value.First)
                };
            })
            .OrderByDescending(t => SnapshotMath.AbsClamped(t.DeltaBytes))
            .Take(Math.Clamp(max, 1, 50))
            .ToList();
    }

    private static List<SnapshotFolderTrend> MergeFolderTrends(
        IReadOnlyDictionary<string, long> first,
        IReadOnlyDictionary<string, long> last,
        int max)
    {
        var keys = new HashSet<string>(first.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var key in last.Keys)
        {
            keys.Add(key);
        }

        var trends = new List<SnapshotFolderTrend>();
        foreach (var key in keys)
        {
            // Skip the scan root itself — its delta mirrors total growth and is not a folder tip.
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            first.TryGetValue(key, out var a);
            last.TryGetValue(key, out var b);
            var delta = SnapshotMath.SafeDelta(b, a);
            if (delta <= 0)
            {
                continue;
            }

            var name = key.Contains('\\')
                ? key[(key.LastIndexOf('\\') + 1)..]
                : key;

            trends.Add(new SnapshotFolderTrend
            {
                RelativePath = key,
                Name = name,
                FirstBytes = a,
                LastBytes = b,
                DeltaBytes = delta,
                GrowthPercent = SnapshotMath.SafePercentChange(b, a)
            });
        }

        return trends
            .OrderByDescending(t => t.DeltaBytes)
            .Take(Math.Clamp(max, 1, 200))
            .ToList();
    }

    public static DateTimeOffset BucketStart(DateTimeOffset timestamp, TrendGranularity granularity)
    {
        var utc = timestamp.ToUniversalTime();
        return granularity switch
        {
            TrendGranularity.Weekly => StartOfWeekUtc(utc),
            TrendGranularity.Monthly => new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero),
            _ => new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero)
        };
    }

    private static DateTimeOffset StartOfWeekUtc(DateTimeOffset utc)
    {
        // Monday-based week for deterministic grouping.
        var day = utc.Date;
        var diff = ((int)day.DayOfWeek + 6) % 7; // Monday=0
        var monday = day.AddDays(-diff);
        return new DateTimeOffset(monday.Year, monday.Month, monday.Day, 0, 0, 0, TimeSpan.Zero);
    }
}

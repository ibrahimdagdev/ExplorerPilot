using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;

namespace ExplorerPilot.Core.Scanning;

/// <summary>
/// Incrementally aggregates file-type statistics during a scan.
/// Memory usage is proportional to distinct extensions, not file count.
/// </summary>
public sealed class FileTypeStatisticsTracker
{
    private readonly Dictionary<string, Accumulator> _byExtension = new(StringComparer.Ordinal);
    private long _totalFiles;
    private long _totalBytes;

    public long TotalFiles => _totalFiles;

    public long TotalBytes => _totalBytes;

    public int UniqueExtensionCount => _byExtension.Count;

    public void Offer(string fileName, long sizeBytes)
    {
        var extension = FileCategoryMapper.NormalizeExtension(fileName);
        OfferNormalized(extension, sizeBytes);
    }

    public void OfferNormalized(string normalizedExtension, long sizeBytes)
    {
        if (sizeBytes < 0)
        {
            sizeBytes = 0;
        }

        if (!_byExtension.TryGetValue(normalizedExtension, out var acc))
        {
            acc = new Accumulator(normalizedExtension);
            _byExtension[normalizedExtension] = acc;
        }

        acc.FileCount++;
        acc.TotalSizeBytes += sizeBytes;
        _totalFiles++;
        _totalBytes += sizeBytes;
    }

    public IReadOnlyList<FileExtensionStatistics> BuildExtensionStatistics()
    {
        var totalBytes = _totalBytes;
        return _byExtension.Values
            .Select(acc =>
            {
                var average = acc.FileCount > 0
                    ? (double)acc.TotalSizeBytes / acc.FileCount
                    : 0d;
                var percentage = totalBytes > 0
                    ? (double)acc.TotalSizeBytes / totalBytes * 100d
                    : 0d;

                return new FileExtensionStatistics
                {
                    Extension = acc.Extension,
                    Category = FileCategoryMapper.GetCategory(acc.Extension),
                    FileCount = acc.FileCount,
                    TotalSizeBytes = acc.TotalSizeBytes,
                    PercentageOfTotalBytes = percentage,
                    AverageSizeBytes = average
                };
            })
            .OrderByDescending(s => s.TotalSizeBytes)
            .ThenBy(s => s.Extension, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<FileCategoryStatistics> BuildCategoryStatistics()
    {
        var totals = new Dictionary<FileCategory, (long Files, long Bytes)>();
        foreach (FileCategory category in Enum.GetValues<FileCategory>())
        {
            totals[category] = (0, 0);
        }

        foreach (var acc in _byExtension.Values)
        {
            var category = FileCategoryMapper.GetCategory(acc.Extension);
            var current = totals[category];
            totals[category] = (current.Files + acc.FileCount, current.Bytes + acc.TotalSizeBytes);
        }

        var totalBytes = _totalBytes;
        return totals
            .Select(pair =>
            {
                var percentage = totalBytes > 0
                    ? (double)pair.Value.Bytes / totalBytes * 100d
                    : 0d;

                return new FileCategoryStatistics
                {
                    Category = pair.Key,
                    FileCount = pair.Value.Files,
                    TotalSizeBytes = pair.Value.Bytes,
                    PercentageOfTotalBytes = percentage
                };
            })
            .OrderByDescending(s => s.TotalSizeBytes)
            .ThenBy(s => s.Category.ToString(), StringComparer.Ordinal)
            .ToList();
    }

    private sealed class Accumulator(string extension)
    {
        public string Extension { get; } = extension;

        public long FileCount { get; set; }

        public long TotalSizeBytes { get; set; }
    }
}

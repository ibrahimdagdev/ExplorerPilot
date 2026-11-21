using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Scanning;

namespace ExplorerPilot.Core.Treemap;

/// <summary>
/// Squarified treemap layout (Bruls, Huizing, van Wijk).
/// Operates on in-memory <see cref="FileSystemNode"/> data only — no disk I/O.
/// </summary>
public sealed class TreemapLayoutEngine
{
    public TreemapLayoutResult Layout(
        FileSystemNode viewRoot,
        double width,
        double height,
        TreemapOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(viewRoot);
        options ??= new TreemapOptions();

        width = Math.Max(0, width);
        height = Math.Max(0, height);

        if (width < 1 || height < 1 || viewRoot.SizeBytes <= 0)
        {
            return new TreemapLayoutResult
            {
                ViewRoot = viewRoot,
                Rectangles = [],
                Width = width,
                Height = height,
                TotalSizeBytes = Math.Max(0, viewRoot.SizeBytes)
            };
        }

        var items = PrepareItems(viewRoot, options);
        if (items.Count == 0)
        {
            return new TreemapLayoutResult
            {
                ViewRoot = viewRoot,
                Rectangles = [],
                Width = width,
                Height = height,
                TotalSizeBytes = viewRoot.SizeBytes
            };
        }

        var total = items.Sum(i => i.Size);
        var rectangles = new List<TreemapRectangle>(items.Count);
        Squarify(items, 0, items.Count, total, 0, 0, width, height, options.Padding, total, rectangles);

        return new TreemapLayoutResult
        {
            ViewRoot = viewRoot,
            Rectangles = rectangles,
            Width = width,
            Height = height,
            TotalSizeBytes = Math.Max(viewRoot.SizeBytes, total),
            IsAggregateView = TreemapAggregateView.IsAggregateView(viewRoot)
        };
    }

    public static TreemapRectangle? HitTest(IReadOnlyList<TreemapRectangle> rectangles, double x, double y) =>
        TreemapHitTesting.HitTest(rectangles, x, y);

    private static List<LayoutItem> PrepareItems(FileSystemNode viewRoot, TreemapOptions options)
    {
        var children = viewRoot.Children;
        if (children.Count == 0)
        {
            // Leaf file view: show itself as a single full rectangle.
            if (!viewRoot.IsDirectory && viewRoot.SizeBytes > 0)
            {
                return
                [
                    new LayoutItem(
                        viewRoot.SizeBytes,
                        viewRoot.Name,
                        viewRoot,
                        isDirectory: false,
                        isAggregate: false,
                        aggregatedCount: 0,
                        ResolveCategory(viewRoot),
                        [])
                ];
            }

            return [];
        }

        var positive = children
            .Where(c => c.SizeBytes > 0)
            .OrderByDescending(c => c.SizeBytes)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (positive.Count == 0)
        {
            return [];
        }

        var total = positive.Sum(c => c.SizeBytes);
        var maxRects = Math.Max(1, options.MaxRectangles);
        // Aggregate drill-down: show individuals up to the cap (no min-fraction cull).
        var minFraction = TreemapAggregateView.IsAggregateView(viewRoot)
            ? 0
            : Math.Clamp(options.MinAreaFraction, 0, 1);
        var minBytes = minFraction <= 0
            ? 0
            : Math.Max(1, (long)Math.Floor(total * minFraction));

        var kept = new List<FileSystemNode>();
        var tiny = new List<FileSystemNode>();

        foreach (var node in positive)
        {
            if (minBytes > 0 && node.SizeBytes < minBytes && kept.Count > 0)
            {
                tiny.Add(node);
            }
            else
            {
                kept.Add(node);
            }
        }

        // Cap rectangle count; spill the remainder into Other.
        if (kept.Count > maxRects)
        {
            var overflow = kept.Skip(maxRects - 1).ToList();
            kept = kept.Take(maxRects - 1).ToList();
            tiny.AddRange(overflow);
        }
        else if (kept.Count == maxRects && tiny.Count > 0)
        {
            var last = kept[^1];
            kept.RemoveAt(kept.Count - 1);
            tiny.Insert(0, last);
        }

        var items = kept
            .Select(n => new LayoutItem(
                n.SizeBytes,
                n.Name,
                n,
                n.IsDirectory,
                isAggregate: false,
                aggregatedCount: 0,
                ResolveCategory(n),
                []))
            .ToList();

        if (tiny.Count > 0)
        {
            var otherSize = tiny.Sum(t => t.SizeBytes);
            if (otherSize > 0)
            {
                items.Add(new LayoutItem(
                    otherSize,
                    options.OtherDisplayName,
                    source: null,
                    isDirectory: true,
                    isAggregate: true,
                    aggregatedCount: tiny.Count,
                    FileCategory.Other,
                    tiny));
            }
        }

        return items
            .OrderByDescending(i => i.Size)
            .ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static FileCategory ResolveCategory(FileSystemNode node)
    {
        if (node.IsDirectory)
        {
            return FileCategory.Other;
        }

        var ext = FileCategoryMapper.NormalizeExtension(node.Name);
        return FileCategoryMapper.GetCategory(ext);
    }

    private static void Squarify(
        List<LayoutItem> items,
        int start,
        int end,
        long remainingSize,
        double x,
        double y,
        double width,
        double height,
        double padding,
        long scopeTotal,
        List<TreemapRectangle> output)
    {
        if (start >= end || remainingSize <= 0 || width <= 0 || height <= 0)
        {
            return;
        }

        if (end - start == 1)
        {
            Place(items[start], x, y, width, height, padding, remainingSize, scopeTotal, output);
            return;
        }

        var horizontal = width >= height;
        var side = horizontal ? height : width;
        if (side <= 0)
        {
            return;
        }

        var row = new List<LayoutItem>();
        long rowSize = 0;
        var i = start;

        while (i < end)
        {
            var item = items[i];
            var trial = new List<LayoutItem>(row) { item };
            var trialSize = rowSize + item.Size;

            if (row.Count == 0 || WorstAspect(row, rowSize, remainingSize, side) >= WorstAspect(trial, trialSize, remainingSize, side))
            {
                row = trial;
                rowSize = trialSize;
                i++;
            }
            else
            {
                break;
            }
        }

        LayoutRow(row, rowSize, remainingSize, x, y, width, height, horizontal, padding, scopeTotal, output);

        var usedFraction = (double)rowSize / remainingSize;
        if (horizontal)
        {
            var usedWidth = width * usedFraction;
            Squarify(items, i, end, remainingSize - rowSize, x + usedWidth, y, width - usedWidth, height, padding, scopeTotal, output);
        }
        else
        {
            var usedHeight = height * usedFraction;
            Squarify(items, i, end, remainingSize - rowSize, x, y + usedHeight, width, height - usedHeight, padding, scopeTotal, output);
        }
    }

    private static void LayoutRow(
        List<LayoutItem> row,
        long rowSize,
        long remainingSize,
        double x,
        double y,
        double width,
        double height,
        bool horizontal,
        double padding,
        long scopeTotal,
        List<TreemapRectangle> output)
    {
        if (row.Count == 0 || rowSize <= 0 || remainingSize <= 0)
        {
            return;
        }

        var fraction = (double)rowSize / remainingSize;

        if (horizontal)
        {
            var rowWidth = width * fraction;
            var cursor = y;
            foreach (var item in row)
            {
                var h = height * ((double)item.Size / rowSize);
                Place(item, x, cursor, rowWidth, h, padding, item.Size, scopeTotal, output);
                cursor += h;
            }
        }
        else
        {
            var rowHeight = height * fraction;
            var cursor = x;
            foreach (var item in row)
            {
                var w = width * ((double)item.Size / rowSize);
                Place(item, cursor, y, w, rowHeight, padding, item.Size, scopeTotal, output);
                cursor += w;
            }
        }
    }

    private static void Place(
        LayoutItem item,
        double x,
        double y,
        double width,
        double height,
        double padding,
        long sizeBytes,
        long scopeTotal,
        List<TreemapRectangle> output)
    {
        var pad = Math.Max(0, padding);
        var innerW = Math.Max(0, width - pad);
        var innerH = Math.Max(0, height - pad);
        var innerX = x + pad * 0.5;
        var innerY = y + pad * 0.5;

        // Keep a 1×1 footprint for positive-size nodes so aggregates remain hittable.
        if (sizeBytes > 0)
        {
            innerW = Math.Max(1, innerW);
            innerH = Math.Max(1, innerH);
        }
        else if (innerW < 0.5 || innerH < 0.5)
        {
            return;
        }

        output.Add(new TreemapRectangle
        {
            Id = Guid.NewGuid(),
            X = innerX,
            Y = innerY,
            Width = innerW,
            Height = innerH,
            SizeBytes = sizeBytes,
            DisplayName = item.DisplayName,
            Category = item.Category,
            IsDirectory = item.IsDirectory,
            Source = item.Source,
            IsAggregate = item.IsAggregate,
            AggregatedCount = item.AggregatedCount,
            AggregatedNodes = item.AggregatedNodes,
            ScopeTotalBytes = scopeTotal
        });
    }

    private static double WorstAspect(List<LayoutItem> row, long rowSize, long totalSize, double side)
    {
        if (row.Count == 0 || rowSize <= 0 || totalSize <= 0 || side <= 0)
        {
            return double.MaxValue;
        }

        var rowArea = (double)rowSize / totalSize * side * side;
        // Normalize: use area units where total maps to side^2 for the remaining strip.
        // Classic worst: max(max^2 * s / rowArea, rowArea / (min^2 * s))
        var s = (double)rowSize;
        var max = row.Max(r => (double)r.Size);
        var min = row.Min(r => (double)r.Size);
        var worst = Math.Max((max * max * s) / (rowArea * rowArea), (rowArea * rowArea) / (min * min * s));
        return worst;
    }

    private readonly struct LayoutItem(
        long size,
        string displayName,
        FileSystemNode? source,
        bool isDirectory,
        bool isAggregate,
        int aggregatedCount,
        FileCategory category,
        IReadOnlyList<FileSystemNode> aggregatedNodes)
    {
        public long Size { get; } = size;
        public string DisplayName { get; } = displayName;
        public FileSystemNode? Source { get; } = source;
        public bool IsDirectory { get; } = isDirectory;
        public bool IsAggregate { get; } = isAggregate;
        public int AggregatedCount { get; } = aggregatedCount;
        public FileCategory Category { get; } = category;
        public IReadOnlyList<FileSystemNode> AggregatedNodes { get; } = aggregatedNodes;
    }
}

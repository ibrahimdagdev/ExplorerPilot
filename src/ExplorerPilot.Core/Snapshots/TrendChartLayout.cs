namespace ExplorerPilot.Core.Snapshots;

/// <summary>Pure layout math for trend charts (testable without WPF).</summary>
public static class TrendChartLayout
{
    public readonly record struct PlotMetrics(
        double PadLeft,
        double PadRight,
        double PadTop,
        double PadBottom,
        double PlotWidth,
        double PlotHeight,
        long MinBytes,
        long MaxBytes);

    public static PlotMetrics Compute(
        double width,
        double height,
        IReadOnlyList<SnapshotTrendPoint> points,
        double padLeft = 56,
        double padRight = 16,
        double padTop = 16,
        double padBottom = 28)
    {
        var plotW = Math.Max(1, width - padLeft - padRight);
        var plotH = Math.Max(1, height - padTop - padBottom);
        long minY = 0;
        long maxY = 1;
        if (points.Count > 0)
        {
            minY = points.Min(p => p.TotalBytes);
            maxY = points.Max(p => p.TotalBytes);
            if (minY == maxY)
            {
                minY = Math.Max(0, minY - 1);
                maxY += 1;
            }
        }

        return new PlotMetrics(padLeft, padRight, padTop, padBottom, plotW, plotH, minY, maxY);
    }

    public static (double X, double Y) MapPoint(
        PlotMetrics metrics,
        int index,
        int count,
        long totalBytes)
    {
        var x = metrics.PadLeft + (count <= 1
            ? metrics.PlotWidth / 2.0
            : metrics.PlotWidth * index / (count - 1.0));
        var range = (double)(metrics.MaxBytes - metrics.MinBytes);
        var y = metrics.PadTop + metrics.PlotHeight
            - ((totalBytes - metrics.MinBytes) / range * metrics.PlotHeight);
        return (x, y);
    }

    public static int FindNearestIndex(
        IReadOnlyList<(double X, double Y)> locations,
        double x,
        double y,
        double maxDistance)
    {
        var best = -1;
        var bestDist = maxDistance;
        for (var i = 0; i < locations.Count; i++)
        {
            var dx = locations[i].X - x;
            var dy = locations[i].Y - y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        return best;
    }

    public static int Navigate(int currentIndex, int count, int delta)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (currentIndex < 0)
        {
            return delta >= 0 ? 0 : count - 1;
        }

        return Math.Clamp(currentIndex + delta, 0, count - 1);
    }
}

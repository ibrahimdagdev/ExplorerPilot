namespace ExplorerPilot.Core.Snapshots;

/// <summary>
/// Visual downsampling for trend charts: preserves first/last points and major peaks
/// instead of discarding the older half of the timeline.
/// Never invents placeholder points for empty time gaps.
/// </summary>
public static class TrendDownsampler
{
    public static IReadOnlyList<SnapshotTrendPoint> Downsample(
        IReadOnlyList<SnapshotTrendPoint> source,
        int maxPoints)
    {
        ArgumentNullException.ThrowIfNull(source);
        var budget = Math.Clamp(maxPoints, 2, 500);
        if (source.Count <= budget)
        {
            return source;
        }

        var keep = new SortedSet<int> { 0, source.Count - 1 };

        // Major peaks / valleys by absolute step delta.
        var peakBudget = Math.Max(1, budget / 3);
        var peaksAdded = 0;
        foreach (var index in Enumerable.Range(1, source.Count - 1)
                     .OrderByDescending(i => SnapshotMath.AbsClamped(source[i].DeltaBytes))
                     .ThenByDescending(i => SnapshotMath.AbsClamped(source[i].TotalBytes))
                     .ThenBy(i => i))
        {
            if (peaksAdded >= peakBudget || keep.Count >= budget)
            {
                break;
            }

            if (keep.Add(index))
            {
                peaksAdded++;
            }
        }

        // Prefer retaining incomplete markers when room remains.
        for (var i = 0; i < source.Count && keep.Count < budget; i++)
        {
            if (source[i].IsIncomplete)
            {
                keep.Add(i);
            }
        }

        // Evenly spaced fillers across the full chronological range.
        var fillersNeeded = budget - keep.Count;
        if (fillersNeeded > 0)
        {
            for (var k = 1; k <= fillersNeeded; k++)
            {
                var idx = (int)Math.Round(k * (source.Count - 1.0) / (fillersNeeded + 1.0));
                keep.Add(idx);
                if (keep.Count >= budget)
                {
                    break;
                }
            }
        }

        // Resolve collisions by walking forward.
        for (var i = 0; i < source.Count && keep.Count < budget; i++)
        {
            keep.Add(i);
        }

        return keep.Select(i => source[i]).ToList();
    }
}

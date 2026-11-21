namespace ExplorerPilot.Core.Snapshots;

/// <summary>Checked arithmetic helpers for size deltas.</summary>
public static class SnapshotMath
{
    /// <summary>
    /// Returns <paramref name="right"/> - <paramref name="left"/> without throwing.
    /// On overflow, clamps to <see cref="long.MaxValue"/> / <see cref="long.MinValue"/>.
    /// </summary>
    public static long SafeDelta(long right, long left)
    {
        try
        {
            return checked(right - left);
        }
        catch (OverflowException)
        {
            return right >= left ? long.MaxValue : long.MinValue;
        }
    }

    public static long AbsClamped(long value) =>
        value == long.MinValue ? long.MaxValue : Math.Abs(value);

    /// <summary>
    /// Percent change from <paramref name="previous"/> to <paramref name="current"/>.
    /// Returns null when <paramref name="previous"/> is 0 (avoids divide-by-zero).
    /// </summary>
    public static double? SafePercentChange(long current, long previous)
    {
        if (previous == 0)
        {
            return null;
        }

        var delta = SafeDelta(current, previous);
        return 100.0 * delta / previous;
    }
}

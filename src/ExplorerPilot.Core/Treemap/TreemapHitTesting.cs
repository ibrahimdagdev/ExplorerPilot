namespace ExplorerPilot.Core.Treemap;

/// <summary>
/// Hit-testing and keyboard neighbor lookup for laid-out rectangles.
/// </summary>
public static class TreemapHitTesting
{
    public static TreemapRectangle? HitTest(IReadOnlyList<TreemapRectangle> rectangles, double x, double y)
    {
        for (var i = 0; i < rectangles.Count; i++)
        {
            if (rectangles[i].Contains(x, y))
            {
                return rectangles[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the nearest rectangle in the given direction (dx/dy in {-1,0,1}).
    /// </summary>
    public static TreemapRectangle? FindNeighbor(
        IReadOnlyList<TreemapRectangle> rectangles,
        TreemapRectangle current,
        int dx,
        int dy)
    {
        if (rectangles.Count == 0)
        {
            return null;
        }

        dx = Math.Sign(dx);
        dy = Math.Sign(dy);
        if (dx == 0 && dy == 0)
        {
            return current;
        }

        var cx = current.X + current.Width * 0.5;
        var cy = current.Y + current.Height * 0.5;
        TreemapRectangle? best = null;
        var bestScore = double.MaxValue;

        foreach (var candidate in rectangles)
        {
            if (ReferenceEquals(candidate, current) || candidate.Id == current.Id)
            {
                continue;
            }

            var ox = candidate.X + candidate.Width * 0.5;
            var oy = candidate.Y + candidate.Height * 0.5;
            var vx = ox - cx;
            var vy = oy - cy;

            // Must lie primarily in the requested direction.
            if (dx != 0 && Math.Sign(vx) != dx)
            {
                continue;
            }

            if (dy != 0 && Math.Sign(vy) != dy)
            {
                continue;
            }

            if (dx != 0 && Math.Abs(vx) < Math.Abs(vy) * 0.35)
            {
                continue;
            }

            if (dy != 0 && Math.Abs(vy) < Math.Abs(vx) * 0.35)
            {
                continue;
            }

            var primary = dx != 0 ? Math.Abs(vx) : Math.Abs(vy);
            var lateral = dx != 0 ? Math.Abs(vy) : Math.Abs(vx);
            var score = primary * 1000 + lateral;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }
}

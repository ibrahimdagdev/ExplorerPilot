namespace ExplorerPilot.Core.Treemap;

/// <summary>
/// Layout options for the squarified treemap engine (UI-agnostic).
/// </summary>
public sealed class TreemapOptions
{
    /// <summary>Inner padding between sibling rectangles, in layout units.</summary>
    public double Padding { get; init; } = 2.0;

    /// <summary>Maximum number of leaf rectangles rendered for the current view (including Other).</summary>
    public int MaxRectangles { get; init; } = 400;

    /// <summary>
    /// Nodes smaller than this fraction of the view total are candidates for aggregation.
    /// </summary>
    public double MinAreaFraction { get; init; } = 0.0005;

    /// <summary>Display name used for the aggregated "Other" rectangle.</summary>
    public string OtherDisplayName { get; init; } = "Other";
}

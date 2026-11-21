using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;

namespace ExplorerPilot.Core.Treemap;

/// <summary>
/// A laid-out rectangle in treemap coordinates (origin top-left, Y grows downward).
/// </summary>
public sealed class TreemapRectangle
{
    public required Guid Id { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public long SizeBytes { get; init; }

    public required string DisplayName { get; init; }

    public FileCategory Category { get; init; } = FileCategory.Other;

    public bool IsDirectory { get; init; }

    /// <summary>Source scan node; null when this rectangle is an aggregate "Other" bucket.</summary>
    public FileSystemNode? Source { get; init; }

    public bool IsAggregate { get; init; }

    public int AggregatedCount { get; init; }

    /// <summary>
    /// Original scan nodes represented by an aggregate rectangle (empty for normal cells).
    /// </summary>
    public IReadOnlyList<FileSystemNode> AggregatedNodes { get; init; } = [];

    /// <summary>Total size of the current layout scope (for percentage tooltips).</summary>
    public long ScopeTotalBytes { get; init; }

    public double Area => Width * Height;

    public double CenterX => X + Width * 0.5;

    public double CenterY => Y + Height * 0.5;

    public bool Contains(double x, double y) =>
        x >= X && y >= Y && x < X + Width && y < Y + Height;
}

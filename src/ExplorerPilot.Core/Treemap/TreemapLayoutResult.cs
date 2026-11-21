using ExplorerPilot.Core.Models;

namespace ExplorerPilot.Core.Treemap;

public sealed class TreemapLayoutResult
{
    public required FileSystemNode ViewRoot { get; init; }

    public required IReadOnlyList<TreemapRectangle> Rectangles { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public long TotalSizeBytes { get; init; }

    public Guid LayoutId { get; init; } = Guid.NewGuid();

    public bool IsAggregateView { get; init; }
}

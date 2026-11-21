using ExplorerPilot.Core.Models;

namespace ExplorerPilot.Core.Treemap;

/// <summary>
/// Builds a virtual directory node that hosts aggregated "Other" children for drill-down.
/// Does not alter Parent links on the original scan nodes and does not touch the filesystem.
/// </summary>
public static class TreemapAggregateView
{
    public const string PathMarker = "\u0001__treemap_other__\u0001";

    public static bool IsAggregateView(FileSystemNode? node) =>
        node is not null && node.FullPath.Contains(PathMarker, StringComparison.Ordinal);

    public static FileSystemNode Create(
        FileSystemNode scopeParent,
        string displayName,
        IReadOnlyList<FileSystemNode> members)
    {
        ArgumentNullException.ThrowIfNull(scopeParent);
        ArgumentNullException.ThrowIfNull(members);

        var size = members.Sum(m => m.SizeBytes);
        var view = new FileSystemNode
        {
            Name = displayName,
            FullPath = scopeParent.FullPath + PathMarker + members.Count.ToString("X") + ":" + size.ToString("X"),
            IsDirectory = true,
            SizeBytes = size,
            FileCount = members.Sum(m => m.IsDirectory ? m.FileCount : 1),
            Parent = scopeParent
        };

        foreach (var member in members)
        {
            view.Children.Add(member);
        }

        return view;
    }
}

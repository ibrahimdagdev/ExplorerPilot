using ExplorerPilot.Core.Models;

namespace ExplorerPilot.App.ViewModels;

public sealed class TreemapBreadcrumbItem
{
    public TreemapBreadcrumbItem(FileSystemNode node, string displayName)
    {
        Node = node;
        DisplayName = displayName;
    }

    public FileSystemNode Node { get; }

    public string DisplayName { get; }
}

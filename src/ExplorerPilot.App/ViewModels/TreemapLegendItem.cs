using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.App.ViewModels;

public sealed class TreemapLegendItem
{
    public TreemapLegendItem(string label, string brushKey, FileCategory? category = null, bool isFolder = false, bool isAggregate = false)
    {
        Label = label;
        BrushKey = brushKey;
        Category = category;
        IsFolder = isFolder;
        IsAggregate = isAggregate;
    }

    public string Label { get; private set; }

    public string BrushKey { get; }

    public FileCategory? Category { get; }

    public bool IsFolder { get; }

    public bool IsAggregate { get; }

    public void SetLabel(string label) => Label = label;
}

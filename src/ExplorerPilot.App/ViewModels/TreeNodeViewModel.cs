using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ExplorerPilot.App.Converters;
using ExplorerPilot.App.Localization;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;

namespace ExplorerPilot.App.ViewModels;

/// <summary>
/// UI-facing wrapper around <see cref="FileSystemNode"/> for the directory tree.
/// Children are materialized lazily on expand so ApplyResult does not allocate the full forest on the UI thread.
/// </summary>
public sealed partial class TreeNodeViewModel : ObservableObject
{
    private static readonly FileSystemNode PlaceholderSentinel = new()
    {
        Name = string.Empty,
        FullPath = string.Empty,
        IsDirectory = true
    };

    private readonly bool _hasChildNodes;
    private readonly bool _isPlaceholder;
    private bool _childrenMaterialized;

    public TreeNodeViewModel(FileSystemNode node, bool isRoot = false)
        : this(node, isRoot, isPlaceholder: false)
    {
    }

    private TreeNodeViewModel(FileSystemNode node, bool isRoot, bool isPlaceholder)
    {
        Node = node;
        Name = node.Name;
        FullPath = node.FullPath;
        IsDirectory = node.IsDirectory;
        SizeBytes = node.SizeBytes;
        PercentageOfParent = isRoot ? 100.0 : node.PercentageOfParent;
        ScanStatus = node.ScanStatus;
        FileCount = node.FileCount;
        DirectoryCount = node.DirectoryCount;
        Children = new ObservableCollection<TreeNodeViewModel>();
        _isPlaceholder = isPlaceholder;

        if (isPlaceholder)
        {
            _childrenMaterialized = true;
            return;
        }

        _hasChildNodes = node.IsDirectory && node.Children.Count > 0;
        if (_hasChildNodes)
        {
            // Keeps the TreeView expander visible before the first expand.
            Children.Add(new TreeNodeViewModel(PlaceholderSentinel, isRoot: false, isPlaceholder: true));
        }
        else
        {
            _childrenMaterialized = true;
        }
    }

    public FileSystemNode Node { get; }

    public string Name { get; }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    public long SizeBytes { get; }

    public double PercentageOfParent { get; }

    public NodeScanStatus ScanStatus { get; }

    public long FileCount { get; }

    public long DirectoryCount { get; }

    public ObservableCollection<TreeNodeViewModel> Children { get; }

    /// <summary>True after real child VMs have replaced the unload placeholder.</summary>
    public bool AreChildrenMaterialized => _childrenMaterialized;

    public string StatusHint
    {
        get
        {
            if (_isPlaceholder)
            {
                return string.Empty;
            }

            var loc = LocalizationAccessor.Current;
            return ScanStatus switch
            {
                NodeScanStatus.SkippedReparsePoint => loc?.GetString("Node_SkippedLink") ?? " (skipped link)",
                NodeScanStatus.Inaccessible => loc?.GetString("Node_Inaccessible") ?? " (inaccessible)",
                NodeScanStatus.Error => loc?.GetString("Node_Error") ?? " (error)",
                _ => string.Empty
            };
        }
    }

    public void NotifyLocalizationChanged()
    {
        if (_isPlaceholder || !_childrenMaterialized)
        {
            OnPropertyChanged(nameof(StatusHint));
            return;
        }

        OnPropertyChanged(nameof(StatusHint));
        foreach (var child in Children)
        {
            child.NotifyLocalizationChanged();
        }
    }

    /// <summary>Materializes direct child view-models once (safe to call repeatedly).</summary>
    public void EnsureChildrenMaterialized()
    {
        if (_childrenMaterialized || _isPlaceholder || !_hasChildNodes)
        {
            return;
        }

        _childrenMaterialized = true;
        Children.Clear();
        foreach (var child in Node.Children)
        {
            Children.Add(new TreeNodeViewModel(child));
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            EnsureChildrenMaterialized();
        }
    }

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;
}

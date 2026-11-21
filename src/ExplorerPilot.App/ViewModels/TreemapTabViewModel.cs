using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplorerPilot.App.Converters;
using ExplorerPilot.App.Localization;
using ExplorerPilot.App.Services;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Scanning;
using ExplorerPilot.Core.Treemap;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.App.ViewModels;

public partial class TreemapTabViewModel : ObservableObject
{
    private const int MaxAggregateDepth = 32;

    private readonly IFileActionService _fileActions;
    private readonly ILocalizationService _localization;
    private readonly ILogger<TreemapTabViewModel> _logger;
    private readonly TreemapLayoutEngine _engine = new();

    private FileSystemNode? _scanRoot;
    private Guid _scanSessionId = Guid.Empty;
    private bool _fromIncompleteScan;
    private CancellationTokenSource? _layoutCts;
    private int _layoutVersion;
    private double _viewportWidth = 800;
    private double _viewportHeight = 500;
    private readonly List<FileSystemNode> _navigationStack = [];
    private int _aggregateDepth;
    private bool _suppressSelectionEvents;
    private string? _cachedTooltip;
    private Guid? _cachedTooltipId;

    public TreemapTabViewModel(
        IFileActionService fileActions,
        ILocalizationService localization,
        ILogger<TreemapTabViewModel> logger)
    {
        _fileActions = fileActions;
        _localization = localization;
        _logger = logger;
        RebuildLegend();
    }

    public ObservableCollection<TreemapBreadcrumbItem> Breadcrumbs { get; } = [];

    public ObservableCollection<TreemapLegendItem> LegendItems { get; } = [];

    [ObservableProperty]
    private bool _hasScanRoot;

    [ObservableProperty]
    private bool _fromIncompleteScanVisible;

    [ObservableProperty]
    private bool _isLayoutBusy;

    [ObservableProperty]
    private bool _showLegend = true;

    [ObservableProperty]
    private TreemapLayoutResult? _layout;

    [ObservableProperty]
    private Guid? _selectedRectangleId;

    [ObservableProperty]
    private Guid? _focusedRectangleId;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _selectedName = string.Empty;

    [ObservableProperty]
    private string _selectedPath = string.Empty;

    [ObservableProperty]
    private string _selectedSizeText = string.Empty;

    [ObservableProperty]
    private string _selectedTypeText = string.Empty;

    [ObservableProperty]
    private string _selectedPercentText = string.Empty;

    [ObservableProperty]
    private string _hoverStatusText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenContainingFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    private FileSystemNode? _selectedNode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private bool _canNavigateBack;

    public Guid ScanSessionId => _scanSessionId;

    /// <summary>Raised when Treemap selection changes a real scan node (not aggregate chrome).</summary>
    public event EventHandler<FileSystemNode?>? NodeSelected;

    public void AttachScan(FileSystemNode? root, Guid scanSessionId, bool incomplete)
    {
        CancelLayout();
        _scanRoot = root;
        _scanSessionId = scanSessionId;
        _fromIncompleteScan = incomplete;
        HasScanRoot = root is not null;
        FromIncompleteScanVisible = incomplete && root is not null;
        _navigationStack.Clear();
        _aggregateDepth = 0;
        Layout = null;
        ClearSelection();
        _cachedTooltip = null;
        _cachedTooltipId = null;

        if (root is null)
        {
            RebuildBreadcrumbs();
            StatusText = string.Empty;
            return;
        }

        _navigationStack.Add(root);
        RebuildBreadcrumbs();
        StatusText = incomplete
            ? _localization.GetString("Treemap_PartialResults")
            : _localization.GetString("Treemap_Ready");
        RequestLayout();
    }

    public void OnScanStarting()
    {
        CancelLayout();
        _scanRoot = null;
        _scanSessionId = Guid.Empty;
        _fromIncompleteScan = false;
        HasScanRoot = false;
        FromIncompleteScanVisible = false;
        _navigationStack.Clear();
        _aggregateDepth = 0;
        Layout = null;
        ClearSelection();
        RebuildBreadcrumbs();
        StatusText = _localization.GetString("Treemap_NoScanResults");
    }

    public void RefreshLocalization()
    {
        RebuildLegend();
        if (!HasScanRoot)
        {
            StatusText = _localization.GetString("Treemap_NoScanResults");
        }
        else if (_fromIncompleteScan)
        {
            StatusText = _localization.GetString("Treemap_PartialResults");
        }

        RebuildBreadcrumbs();
        RefreshSelectedTexts();
        _cachedTooltip = null;
        _cachedTooltipId = null;
        RequestLayout();
    }

    public void NotifyViewportSize(double width, double height)
    {
        if (width < 32 || height < 32)
        {
            return;
        }

        if (Math.Abs(width - _viewportWidth) < 1 && Math.Abs(height - _viewportHeight) < 1)
        {
            return;
        }

        _viewportWidth = width;
        _viewportHeight = height;
        RequestLayout();
    }

    /// <summary>Directory Explorer → Treemap selection (session-aware, loop-safe).</summary>
    public void SelectFromExplorer(FileSystemNode? node, Guid sessionId)
    {
        if (sessionId != _scanSessionId || _scanRoot is null || node is null)
        {
            return;
        }

        if (_suppressSelectionEvents)
        {
            return;
        }

        _suppressSelectionEvents = true;
        try
        {
            EnsureViewContains(node);
        }
        finally
        {
            _suppressSelectionEvents = false;
        }
    }

    public void SelectRectangle(TreemapRectangle? rect)
    {
        if (rect is null)
        {
            ClearSelection();
            RaiseNodeSelected(null);
            return;
        }

        FocusedRectangleId = rect.Id;
        SelectedRectangleId = rect.Id;

        if (rect.IsAggregate)
        {
            SelectedNode = null;
            SelectedName = rect.DisplayName;
            SelectedPath = _localization.GetString("Treemap_OtherAggregateFormat", rect.AggregatedCount);
            SelectedSizeText = ByteSizeConverter.Format(rect.SizeBytes, _localization.CurrentCulture);
            SelectedTypeText = _localization.GetString("Treemap_Type_OtherAggregate");
            SelectedPercentText = FormatPercent(rect.SizeBytes, rect.ScopeTotalBytes);
            // Sync tree to nearest real parent scope rather than an unrelated node.
            var parent = _navigationStack.Count > 0 ? _navigationStack[^1] : null;
            if (parent is not null && !TreemapAggregateView.IsAggregateView(parent))
            {
                RaiseNodeSelected(parent);
            }
            else if (parent?.Parent is { } realParent)
            {
                RaiseNodeSelected(realParent);
            }
            else
            {
                RaiseNodeSelected(null);
            }

            return;
        }

        SelectedNode = rect.Source;
        SelectedName = rect.DisplayName;
        SelectedPath = rect.Source?.FullPath ?? string.Empty;
        SelectedSizeText = ByteSizeConverter.Format(rect.SizeBytes, _localization.CurrentCulture);
        SelectedTypeText = FormatType(rect);
        SelectedPercentText = FormatPercent(rect.SizeBytes, rect.ScopeTotalBytes);
        RaiseNodeSelected(rect.Source);
    }

    public void HoverRectangle(TreemapRectangle? rect)
    {
        if (rect is null)
        {
            HoverStatusText = string.Empty;
            return;
        }

        HoverStatusText = FormatTooltip(rect).Replace("\n", " — ", StringComparison.Ordinal);
    }

    public string FormatTooltip(TreemapRectangle rect)
    {
        if (_cachedTooltipId == rect.Id && _cachedTooltip is not null)
        {
            return _cachedTooltip;
        }

        var culture = _localization.CurrentCulture;
        var sb = new StringBuilder(256);
        sb.Append(rect.DisplayName);

        if (rect.IsAggregate)
        {
            sb.AppendLine();
            sb.Append(_localization.GetString("Treemap_OtherAggregateFormat", rect.AggregatedCount));
            sb.AppendLine();
            sb.Append(_localization.GetString("Treemap_LogicalSize")).Append(": ")
                .Append(ByteSizeConverter.Format(rect.SizeBytes, culture));
            sb.AppendLine();
            sb.Append(_localization.GetString("Treemap_Percentage")).Append(": ")
                .Append(FormatPercent(rect.SizeBytes, rect.ScopeTotalBytes));
        }
        else
        {
            if (rect.Source is not null)
            {
                sb.AppendLine();
                sb.Append(rect.Source.FullPath);
            }

            sb.AppendLine();
            sb.Append(_localization.GetString("Treemap_LogicalSize")).Append(": ")
                .Append(ByteSizeConverter.Format(rect.SizeBytes, culture));
            sb.AppendLine();
            sb.Append(_localization.GetString("Treemap_Percentage")).Append(": ")
                .Append(FormatPercent(rect.SizeBytes, rect.ScopeTotalBytes));
            sb.AppendLine();
            sb.Append(_localization.GetString("Treemap_Category")).Append(": ")
                .Append(FormatType(rect));

            if (rect.Source?.LastModified is { } modified)
            {
                sb.AppendLine();
                sb.Append(_localization.GetString("Treemap_Modified")).Append(": ")
                    .Append(modified.ToLocalTime().ToString("g", culture));
            }
        }

        _cachedTooltipId = rect.Id;
        _cachedTooltip = sb.ToString();
        return _cachedTooltip;
    }

    public void ActivateRectangle(TreemapRectangle? rect)
    {
        if (rect is null)
        {
            return;
        }

        if (rect.IsAggregate)
        {
            DrillIntoAggregate(rect);
            return;
        }

        if (rect.Source is { IsDirectory: true } dir)
        {
            NavigateToDirectory(dir);
        }
    }

    public void MoveFocus(int dx, int dy)
    {
        if (Layout is null || Layout.Rectangles.Count == 0)
        {
            return;
        }

        var current = FindById(FocusedRectangleId) ?? FindById(SelectedRectangleId) ?? Layout.Rectangles[0];
        var next = TreemapHitTesting.FindNeighbor(Layout.Rectangles, current, dx, dy) ?? current;
        FocusedRectangleId = next.Id;
    }

    public void ActivateFocused()
    {
        var rect = FindById(FocusedRectangleId) ?? FindById(SelectedRectangleId);
        if (rect is null)
        {
            return;
        }

        SelectRectangle(rect);
        if (rect.IsAggregate || rect.IsDirectory)
        {
            ActivateRectangle(rect);
        }
    }

    public void ClearFocus()
    {
        FocusedRectangleId = null;
    }

    [RelayCommand(CanExecute = nameof(CanNavigateBack))]
    private void Back()
    {
        if (_navigationStack.Count <= 1)
        {
            return;
        }

        var leaving = _navigationStack[^1];
        _navigationStack.RemoveAt(_navigationStack.Count - 1);
        if (TreemapAggregateView.IsAggregateView(leaving))
        {
            _aggregateDepth = Math.Max(0, _aggregateDepth - 1);
        }

        RebuildBreadcrumbs();
        ClearSelection();
        RequestLayout();
    }

    [RelayCommand(CanExecute = nameof(CanGoHome))]
    private void Home()
    {
        if (_scanRoot is null)
        {
            return;
        }

        _navigationStack.Clear();
        _navigationStack.Add(_scanRoot);
        _aggregateDepth = 0;
        RebuildBreadcrumbs();
        ClearSelection();
        RequestLayout();
    }

    [RelayCommand]
    private void NavigateBreadcrumb(TreemapBreadcrumbItem? item)
    {
        if (item is null)
        {
            return;
        }

        var index = _navigationStack.FindIndex(n => ReferenceEquals(n, item.Node));
        if (index < 0 || index == _navigationStack.Count - 1)
        {
            return;
        }

        while (_navigationStack.Count > index + 1)
        {
            var removed = _navigationStack[^1];
            _navigationStack.RemoveAt(_navigationStack.Count - 1);
            if (TreemapAggregateView.IsAggregateView(removed))
            {
                _aggregateDepth = Math.Max(0, _aggregateDepth - 1);
            }
        }

        RebuildBreadcrumbs();
        ClearSelection();
        RequestLayout();
    }

    [RelayCommand(CanExecute = nameof(CanFileAction))]
    private void OpenContainingFolder()
    {
        if (SelectedNode is null)
        {
            return;
        }

        if (_fileActions.TryOpenContainingFolder(SelectedNode.FullPath, out _))
        {
            return;
        }

        MessageBox.Show(
            _localization.GetString("Treemap_OpenFolderFailed"),
            _localization.GetString("Treemap_Title"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    [RelayCommand(CanExecute = nameof(CanFileAction))]
    private void CopyPath()
    {
        if (SelectedNode is null)
        {
            return;
        }

        if (_fileActions.TryCopyPath(SelectedNode.FullPath, out _))
        {
            return;
        }

        MessageBox.Show(
            _localization.GetString("Treemap_CopyPathFailed"),
            _localization.GetString("Treemap_Title"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private bool CanGoHome() => HasScanRoot && _navigationStack.Count > 1;

    private bool CanFileAction() => SelectedNode is not null;

    private void DrillIntoAggregate(TreemapRectangle rect)
    {
        if (!rect.IsAggregate || rect.AggregatedNodes.Count == 0)
        {
            return;
        }

        if (_aggregateDepth >= MaxAggregateDepth)
        {
            StatusText = _localization.GetString("Treemap_AggregateDepthLimit");
            return;
        }

        if (rect.AggregatedNodes.Count == 1)
        {
            var only = rect.AggregatedNodes[0];
            if (only.IsDirectory)
            {
                NavigateToDirectory(only);
            }
            else
            {
                HighlightNode(only);
            }

            return;
        }

        var scope = _navigationStack.Count > 0 ? _navigationStack[^1] : _scanRoot;
        if (scope is null)
        {
            return;
        }

        // Prevent no-op drills that would recreate an identical aggregate view.
        if (TreemapAggregateView.IsAggregateView(scope)
            && scope.Children.Count == rect.AggregatedNodes.Count
            && scope.SizeBytes == rect.SizeBytes)
        {
            StatusText = _localization.GetString("Treemap_AggregateCannotExpand");
            return;
        }

        var view = TreemapAggregateView.Create(scope, rect.DisplayName, rect.AggregatedNodes);
        _navigationStack.Add(view);
        _aggregateDepth++;
        RebuildBreadcrumbs();
        ClearSelection();
        RequestLayout();
    }

    private void NavigateToDirectory(FileSystemNode node)
    {
        if (!node.IsDirectory || TreemapAggregateView.IsAggregateView(node))
        {
            return;
        }

        // Avoid pushing the same directory twice.
        if (_navigationStack.Count > 0 && ReferenceEquals(_navigationStack[^1], node))
        {
            return;
        }

        _navigationStack.Add(node);
        RebuildBreadcrumbs();
        ClearSelection();
        RequestLayout();
    }

    private void EnsureViewContains(FileSystemNode node)
    {
        if (_scanRoot is null || !IsUnderScanRoot(node))
        {
            return;
        }

        // Selection is highlighted in the parent scope (where the node appears as a rectangle).
        var targetView = ReferenceEquals(node, _scanRoot)
            ? _scanRoot
            : node.Parent ?? _scanRoot;

        while (TreemapAggregateView.IsAggregateView(targetView) && targetView.Parent is not null)
        {
            targetView = targetView.Parent;
        }

        if (_navigationStack.Count > 0
            && ReferenceEquals(_navigationStack[^1], targetView)
            && Layout is not null)
        {
            HighlightNode(node);
            return;
        }

        _navigationStack.Clear();
        _aggregateDepth = 0;
        foreach (var step in BuildDirectoryChain(targetView))
        {
            _navigationStack.Add(step);
        }

        if (_navigationStack.Count == 0)
        {
            _navigationStack.Add(_scanRoot);
        }

        RebuildBreadcrumbs();
        RequestLayout(selectAfter: node);
    }

    private bool IsUnderScanRoot(FileSystemNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, _scanRoot))
            {
                return true;
            }
        }

        return false;
    }

    private List<FileSystemNode> BuildDirectoryChain(FileSystemNode targetView)
    {
        var chain = new List<FileSystemNode>();
        for (var current = targetView; current is not null; current = current.Parent)
        {
            if (TreemapAggregateView.IsAggregateView(current))
            {
                continue;
            }

            chain.Add(current);
            if (ReferenceEquals(current, _scanRoot))
            {
                break;
            }
        }

        chain.Reverse();
        if (chain.Count == 0 || !ReferenceEquals(chain[0], _scanRoot))
        {
            chain.Insert(0, _scanRoot!);
        }

        return chain;
    }

    private void HighlightNode(FileSystemNode node)
    {
        SelectedNode = node;
        SelectedName = node.Name;
        SelectedPath = node.FullPath;
        SelectedSizeText = ByteSizeConverter.Format(node.SizeBytes, _localization.CurrentCulture);
        SelectedTypeText = node.IsDirectory
            ? _localization.GetString("Treemap_Type_Folder")
            : CategoryLabel(FileCategoryMapper.GetCategory(FileCategoryMapper.NormalizeExtension(node.Name)));
        SelectedPercentText = FormatPercent(node.SizeBytes, CurrentScopeTotal());

        if (Layout is not null)
        {
            var match = Layout.Rectangles.FirstOrDefault(r =>
                !r.IsAggregate && ReferenceEquals(r.Source, node));
            if (match is null)
            {
                // Node may be inside Other — select the aggregate that contains it.
                match = Layout.Rectangles.FirstOrDefault(r =>
                    r.IsAggregate && r.AggregatedNodes.Any(n => ReferenceEquals(n, node)));
            }

            SelectedRectangleId = match?.Id;
            FocusedRectangleId = match?.Id;
        }
    }

    private void RequestLayout(FileSystemNode? selectAfter = null)
    {
        if (_navigationStack.Count == 0)
        {
            Layout = null;
            return;
        }

        var viewRoot = _navigationStack[^1];
        var session = _scanSessionId;
        var width = _viewportWidth;
        var height = _viewportHeight;

        // Cancel prior work without bumping the version we are about to claim.
        CancelLayout(bumpVersion: false);
        var version = Interlocked.Increment(ref _layoutVersion);
        _layoutCts = new CancellationTokenSource();
        var token = _layoutCts.Token;
        IsLayoutBusy = true;

        var options = new TreemapOptions
        {
            Padding = 2,
            MaxRectangles = 400,
            MinAreaFraction = TreemapAggregateView.IsAggregateView(viewRoot) ? 0 : 0.0005,
            OtherDisplayName = _localization.GetString("Treemap_Other")
        };

        _ = Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            return _engine.Layout(viewRoot, width, height, options);
        }, token).ContinueWith(t =>
        {
            void Apply()
            {
                if (version != _layoutVersion || session != _scanSessionId)
                {
                    return;
                }

                IsLayoutBusy = false;

                if (t.IsCanceled)
                {
                    return;
                }

                if (t.IsFaulted)
                {
                    _logger.LogError(t.Exception, "Treemap layout failed");
                    StatusText = _localization.GetString("Treemap_LayoutFailed");
                    return;
                }

                Layout = t.Result;
                _cachedTooltip = null;
                _cachedTooltipId = null;

                if (selectAfter is not null)
                {
                    HighlightNode(selectAfter);
                }

                if (_fromIncompleteScan)
                {
                    StatusText = _localization.GetString("Treemap_PartialResults");
                }
            }

            PostToUi(Apply);
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private static void PostToUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        // Prefer the UI dispatcher, but never block forever — unit tests may have a
        // dormant Application.Current without a message pump.
        var applied = 0;
        void Guarded()
        {
            if (Interlocked.Exchange(ref applied, 1) != 0)
            {
                return;
            }

            action();
        }

        try
        {
            var op = dispatcher.BeginInvoke(Guarded, DispatcherPriority.DataBind);
            if (op.Wait(TimeSpan.FromMilliseconds(500)) == DispatcherOperationStatus.Completed)
            {
                return;
            }
        }
        catch
        {
            // Fall through to inline apply.
        }

        Guarded();
    }

    private void CancelLayout(bool bumpVersion = true)
    {
        try
        {
            _layoutCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        _layoutCts?.Dispose();
        _layoutCts = null;
        if (bumpVersion)
        {
            Interlocked.Increment(ref _layoutVersion);
        }

        IsLayoutBusy = false;
    }

    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        CanNavigateBack = _navigationStack.Count > 1;
        foreach (var node in _navigationStack)
        {
            var label = TreemapAggregateView.IsAggregateView(node)
                ? _localization.GetString("Treemap_Other")
                : node.Name;
            Breadcrumbs.Add(new TreemapBreadcrumbItem(node, label));
        }

        HomeCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
    }

    private void RebuildLegend()
    {
        LegendItems.Clear();
        LegendItems.Add(new TreemapLegendItem(_localization.GetString("Treemap_Legend_Folder"), "TreemapFolderBrush", isFolder: true));
        LegendItems.Add(new TreemapLegendItem(_localization.GetString("FileTypes_Category_Videos"), "TreemapVideosBrush", FileCategory.Videos));
        LegendItems.Add(new TreemapLegendItem(_localization.GetString("FileTypes_Category_Images"), "TreemapImagesBrush", FileCategory.Images));
        LegendItems.Add(new TreemapLegendItem(_localization.GetString("FileTypes_Category_Audio"), "TreemapAudioBrush", FileCategory.Audio));
        LegendItems.Add(new TreemapLegendItem(_localization.GetString("FileTypes_Category_Documents"), "TreemapDocumentsBrush", FileCategory.Documents));
        LegendItems.Add(new TreemapLegendItem(_localization.GetString("FileTypes_Category_Archives"), "TreemapArchivesBrush", FileCategory.Archives));
        LegendItems.Add(new TreemapLegendItem(_localization.GetString("FileTypes_Category_Applications"), "TreemapApplicationsBrush", FileCategory.Applications));
        LegendItems.Add(new TreemapLegendItem(_localization.GetString("FileTypes_Category_Other"), "TreemapOtherBrush", FileCategory.Other));
        LegendItems.Add(new TreemapLegendItem(_localization.GetString("Treemap_Other"), "TreemapOtherBrush", isAggregate: true));
    }

    private void ClearSelection()
    {
        SelectedRectangleId = null;
        FocusedRectangleId = null;
        SelectedNode = null;
        SelectedName = string.Empty;
        SelectedPath = string.Empty;
        SelectedSizeText = string.Empty;
        SelectedTypeText = string.Empty;
        SelectedPercentText = string.Empty;
        HoverStatusText = string.Empty;
        _cachedTooltip = null;
        _cachedTooltipId = null;
    }

    private void RefreshSelectedTexts()
    {
        if (SelectedNode is null)
        {
            return;
        }

        SelectedSizeText = ByteSizeConverter.Format(SelectedNode.SizeBytes, _localization.CurrentCulture);
        SelectedTypeText = SelectedNode.IsDirectory
            ? _localization.GetString("Treemap_Type_Folder")
            : CategoryLabel(FileCategoryMapper.GetCategory(FileCategoryMapper.NormalizeExtension(SelectedNode.Name)));
        SelectedPercentText = FormatPercent(SelectedNode.SizeBytes, CurrentScopeTotal());
    }

    private void RaiseNodeSelected(FileSystemNode? node)
    {
        if (_suppressSelectionEvents)
        {
            return;
        }

        NodeSelected?.Invoke(this, node);
    }

    private TreemapRectangle? FindById(Guid? id)
    {
        if (id is null || Layout is null)
        {
            return null;
        }

        return Layout.Rectangles.FirstOrDefault(r => r.Id == id.Value);
    }

    private long CurrentScopeTotal()
    {
        if (Layout is { TotalSizeBytes: > 0 } layout)
        {
            return layout.TotalSizeBytes;
        }

        return _navigationStack.Count > 0 ? Math.Max(0, _navigationStack[^1].SizeBytes) : 0;
    }

    private string FormatPercent(long sizeBytes, long scopeTotal)
    {
        if (scopeTotal <= 0)
        {
            return _localization.GetString("EmDash");
        }

        var pct = (double)sizeBytes / scopeTotal * 100.0;
        return pct.ToString("0.##", _localization.CurrentCulture) + "%";
    }

    private string FormatType(TreemapRectangle rect)
    {
        if (rect.IsAggregate)
        {
            return _localization.GetString("Treemap_Type_OtherAggregate");
        }

        if (rect.IsDirectory)
        {
            return _localization.GetString("Treemap_Type_Folder");
        }

        return CategoryLabel(rect.Category);
    }

    private string CategoryLabel(FileCategory category) =>
        category switch
        {
            FileCategory.Videos => _localization.GetString("FileTypes_Category_Videos"),
            FileCategory.Images => _localization.GetString("FileTypes_Category_Images"),
            FileCategory.Audio => _localization.GetString("FileTypes_Category_Audio"),
            FileCategory.Documents => _localization.GetString("FileTypes_Category_Documents"),
            FileCategory.Archives => _localization.GetString("FileTypes_Category_Archives"),
            FileCategory.Applications => _localization.GetString("FileTypes_Category_Applications"),
            _ => _localization.GetString("FileTypes_Category_Other")
        };
}

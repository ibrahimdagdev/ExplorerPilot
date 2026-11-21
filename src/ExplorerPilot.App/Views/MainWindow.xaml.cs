using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ExplorerPilot.App.ViewModels;
using ExplorerPilot.Core.Models;

namespace ExplorerPilot.App.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private int _bringIntoViewAttempts;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        viewModel.ExplorerSelectionBringIntoViewRequested += OnBringExplorerSelectionIntoView;
        Closed += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.ExplorerSelectionBringIntoViewRequested -= OnBringExplorerSelectionIntoView;
            }
        };
    }

    private void ExplorerTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.OnExplorerSelectionChanged(e.NewValue as TreeNodeViewModel);
        }
    }

    private void OnBringExplorerSelectionIntoView(object? sender, EventArgs e)
    {
        _bringIntoViewAttempts = 0;
        ScheduleBringIntoView(DispatcherPriority.Background);
    }

    private void ScheduleBringIntoView(DispatcherPriority priority) =>
        Dispatcher.BeginInvoke(TryBringExplorerSelectionIntoView, priority);

    private void TryBringExplorerSelectionIntoView()
    {
        if (_viewModel?.SelectedExplorerNode is not { } node)
        {
            return;
        }

        ExplorerTree.UpdateLayout();

        if (TryBringPathIntoView(node))
        {
            return;
        }

        // Virtualized trees often need several generator passes after IsExpanded flips.
        if (_bringIntoViewAttempts++ < 8)
        {
            ScheduleBringIntoView(DispatcherPriority.Loaded);
        }
    }

    private bool TryBringPathIntoView(TreeNodeViewModel target)
    {
        var path = BuildTreePath(target);
        if (path.Count == 0)
        {
            return false;
        }

        ItemsControl parent = ExplorerTree;
        TreeViewItem? leaf = null;

        foreach (var step in path)
        {
            parent.UpdateLayout();
            var tvi = parent.ItemContainerGenerator.ContainerFromItem(step) as TreeViewItem
                      ?? FindDirectChildContainer(parent, step);
            if (tvi is null)
            {
                return false;
            }

            if (!ReferenceEquals(step, target) && !tvi.IsExpanded)
            {
                tvi.IsExpanded = true;
                tvi.UpdateLayout();
            }

            leaf = tvi;
            parent = tvi;
        }

        if (leaf is null)
        {
            return false;
        }

        leaf.IsSelected = true;
        leaf.BringIntoView();
        return true;
    }

    private List<TreeNodeViewModel> BuildTreePath(TreeNodeViewModel target)
    {
        var fsChain = new List<FileSystemNode>();
        for (var current = target.Node; current is not null; current = current.Parent)
        {
            fsChain.Add(current);
        }

        fsChain.Reverse();

        var path = new List<TreeNodeViewModel>(fsChain.Count);
        IReadOnlyList<TreeNodeViewModel> level = _viewModel is null
            ? Array.Empty<TreeNodeViewModel>()
            : _viewModel.TreeRoots;

        foreach (var fs in fsChain)
        {
            TreeNodeViewModel? match = null;
            foreach (var candidate in level)
            {
                if (ReferenceEquals(candidate.Node, fs))
                {
                    match = candidate;
                    break;
                }
            }

            if (match is null)
            {
                // Fall back to the already-selected VM for the leaf.
                if (ReferenceEquals(fs, target.Node))
                {
                    path.Add(target);
                }

                break;
            }

            path.Add(match);
            match.EnsureChildrenMaterialized();
            level = match.Children;
        }

        return path;
    }

    private static TreeViewItem? FindDirectChildContainer(ItemsControl parent, object data)
    {
        var count = parent.Items.Count;
        for (var i = 0; i < count; i++)
        {
            if (!ReferenceEquals(parent.Items[i], data))
            {
                continue;
            }

            return parent.ItemContainerGenerator.ContainerFromIndex(i) as TreeViewItem;
        }

        return null;
    }
}

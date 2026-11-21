using System.Windows;
using System.Windows.Controls;
using ExplorerPilot.App.ViewModels;
using ExplorerPilot.Core.Treemap;

namespace ExplorerPilot.App.Controls;

public partial class TreemapView : UserControl
{
    public TreemapView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;

        Canvas.TooltipFormatter = rect => ViewModel?.FormatTooltip(rect) ?? rect.DisplayName;
        Canvas.RectangleClicked += (_, rect) => ViewModel?.SelectRectangle(rect);
        Canvas.RectangleDoubleClicked += (_, rect) => ViewModel?.ActivateRectangle(rect);
        Canvas.RectangleHovered += (_, rect) => ViewModel?.HoverRectangle(rect);
        Canvas.BackRequested += (_, _) => ViewModel?.BackCommand.Execute(null);
        Canvas.HomeRequested += (_, _) => ViewModel?.HomeCommand.Execute(null);
        Canvas.ActivateFocusedRequested += (_, _) => ViewModel?.ActivateFocused();
        Canvas.MoveFocusRequested += (_, dir) => ViewModel?.MoveFocus(dir.Dx, dir.Dy);
        Canvas.ClearFocusRequested += (_, _) =>
        {
            ViewModel?.ClearFocus();
            Canvas.FocusedId = null;
        };
    }

    private TreemapTabViewModel? ViewModel => DataContext as TreemapTabViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e) => NotifySize();

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => NotifySize();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Canvas.TooltipFormatter = rect => ViewModel?.FormatTooltip(rect) ?? rect.DisplayName;
        NotifySize();
    }

    private void NotifySize()
    {
        if (ActualWidth > 0 && ActualHeight > 0)
        {
            ViewModel?.NotifyViewportSize(ActualWidth, ActualHeight);
        }
    }
}

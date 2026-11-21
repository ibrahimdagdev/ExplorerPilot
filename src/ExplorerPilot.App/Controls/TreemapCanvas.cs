using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Treemap;

namespace ExplorerPilot.App.Controls;

/// <summary>
/// High-performance treemap renderer with keyboard navigation and cached brushes.
/// </summary>
public sealed class TreemapCanvas : FrameworkElement
{
    public static readonly DependencyProperty LayoutProperty =
        DependencyProperty.Register(
            nameof(Layout),
            typeof(TreemapLayoutResult),
            typeof(TreemapCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutChanged));

    public static readonly DependencyProperty SelectedIdProperty =
        DependencyProperty.Register(
            nameof(SelectedId),
            typeof(Guid?),
            typeof(TreemapCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty HoveredIdProperty =
        DependencyProperty.Register(
            nameof(HoveredId),
            typeof(Guid?),
            typeof(TreemapCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FocusedIdProperty =
        DependencyProperty.Register(
            nameof(FocusedId),
            typeof(Guid?),
            typeof(TreemapCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private Pen? _selectedPen;
    private Pen? _hoverPen;
    private Pen? _focusPen;
    private string? _penThemeEpoch;

    public TreemapCanvas()
    {
        Focusable = true;
        SnapsToDevicePixels = true;
    }

    public TreemapLayoutResult? Layout
    {
        get => (TreemapLayoutResult?)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public Guid? SelectedId
    {
        get => (Guid?)GetValue(SelectedIdProperty);
        set => SetValue(SelectedIdProperty, value);
    }

    public Guid? HoveredId
    {
        get => (Guid?)GetValue(HoveredIdProperty);
        set => SetValue(HoveredIdProperty, value);
    }

    public Guid? FocusedId
    {
        get => (Guid?)GetValue(FocusedIdProperty);
        set => SetValue(FocusedIdProperty, value);
    }

    public Func<TreemapRectangle, string>? TooltipFormatter { get; set; }

    public event EventHandler<TreemapRectangle?>? RectangleClicked;

    public event EventHandler<TreemapRectangle?>? RectangleDoubleClicked;

    public event EventHandler<TreemapRectangle?>? RectangleHovered;

    public event EventHandler? BackRequested;

    public event EventHandler? HomeRequested;

    public event EventHandler? ActivateFocusedRequested;

    public event EventHandler<DirectionRequest>? MoveFocusRequested;

    public event EventHandler? ClearFocusRequested;

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var layout = Layout;
        if (layout is null || layout.Rectangles.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        EnsurePens();
        var scaleX = layout.Width > 0 ? ActualWidth / layout.Width : 1;
        var scaleY = layout.Height > 0 ? ActualHeight / layout.Height : 1;
        var selected = SelectedId;
        var hovered = HoveredId;
        var focused = FocusedId;

        foreach (var rect in layout.Rectangles)
        {
            var x = rect.X * scaleX;
            var y = rect.Y * scaleY;
            var w = Math.Max(0.5, rect.Width * scaleX);
            var h = Math.Max(0.5, rect.Height * scaleY);
            var area = new Rect(x, y, w, h);

            drawingContext.DrawRectangle(GetFillBrush(rect), null, area);

            if (rect.Id == selected)
            {
                drawingContext.DrawRectangle(null, _selectedPen, area);
            }
            else if (rect.Id == hovered)
            {
                drawingContext.DrawRectangle(null, _hoverPen, area);
            }

            if (rect.Id == focused && IsKeyboardFocusWithin)
            {
                drawingContext.DrawRectangle(null, _focusPen, Rect.Inflate(area, -1, -1));
            }

            if (w >= 48 && h >= 22)
            {
                DrawLabel(drawingContext, rect, x, y, w, h);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTestCore(e.GetPosition(this));
        var id = hit?.Id;
        if (id != HoveredId)
        {
            HoveredId = id;
            RectangleHovered?.Invoke(this, hit);
            ToolTip = hit is null ? null : (TooltipFormatter?.Invoke(hit) ?? hit.DisplayName);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (HoveredId is not null)
        {
            HoveredId = null;
            RectangleHovered?.Invoke(this, null);
            ToolTip = null;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var hit = HitTestCore(e.GetPosition(this));
        if (e.ClickCount >= 2)
        {
            RectangleDoubleClicked?.Invoke(this, hit);
        }
        else
        {
            SelectedId = hit?.Id;
            FocusedId = hit?.Id;
            RectangleClicked?.Invoke(this, hit);
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Left:
                MoveFocusRequested?.Invoke(this, new DirectionRequest(-1, 0));
                e.Handled = true;
                break;
            case Key.Right:
                MoveFocusRequested?.Invoke(this, new DirectionRequest(1, 0));
                e.Handled = true;
                break;
            case Key.Up:
                MoveFocusRequested?.Invoke(this, new DirectionRequest(0, -1));
                e.Handled = true;
                break;
            case Key.Down:
                MoveFocusRequested?.Invoke(this, new DirectionRequest(0, 1));
                e.Handled = true;
                break;
            case Key.Enter:
                ActivateFocusedRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.Back:
                BackRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.Home:
                HomeRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.Escape:
                ClearFocusRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new TreemapCanvasAutomationPeer(this);

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreemapCanvas canvas)
        {
            canvas.InvalidateVisual();
        }
    }

    private TreemapRectangle? HitTestCore(Point point)
    {
        var layout = Layout;
        if (layout is null || layout.Width <= 0 || layout.Height <= 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return null;
        }

        var x = point.X * (layout.Width / ActualWidth);
        var y = point.Y * (layout.Height / ActualHeight);
        return TreemapHitTesting.HitTest(layout.Rectangles, x, y);
    }

    private void DrawLabel(DrawingContext dc, TreemapRectangle rect, double x, double y, double w, double h)
    {
        var textBrush = ResolveBrush("AccentForegroundBrush")
                        ?? ResolveBrush("TextPrimaryBrush")
                        ?? Brushes.White;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var label = rect.IsAggregate
            ? $"{rect.DisplayName} ({rect.AggregatedCount})"
            : rect.DisplayName;
        var ft = new FormattedText(
            label,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            11,
            textBrush,
            dpi)
        {
            MaxTextWidth = Math.Max(8, w - 8),
            MaxTextHeight = Math.Max(8, h - 6),
            Trimming = TextTrimming.CharacterEllipsis
        };
        dc.DrawText(ft, new Point(x + 4, y + 3));
    }

    private void EnsurePens()
    {
        // Rebuild pens when theme brushes change (Light/Dark swap).
        var epoch = (TryFindResource("TextPrimaryBrush") as SolidColorBrush)?.Color.ToString()
                    ?? string.Empty;
        if (_penThemeEpoch == epoch && _selectedPen is not null)
        {
            return;
        }

        _penThemeEpoch = epoch;
        _selectedPen = new Pen(ResolveBrush("TextPrimaryBrush") ?? Brushes.White, 2.0);
        _selectedPen.Freeze();
        _hoverPen = new Pen(ResolveBrush("FocusBrush") ?? Brushes.LightGray, 1.25);
        _hoverPen.Freeze();
        _focusPen = new Pen(ResolveBrush("AccentBrush") ?? Brushes.DodgerBlue, 1.5) { DashStyle = DashStyles.Dash };
        _focusPen.Freeze();
    }

    private Brush GetFillBrush(TreemapRectangle rect)
    {
        if (rect.IsAggregate)
        {
            return ResolveBrush("TreemapOtherBrush") ?? ResolveBrush("BgControlBrush") ?? Brushes.Gray;
        }

        if (rect.IsDirectory)
        {
            return ResolveBrush("TreemapFolderBrush") ?? ResolveBrush("BgSelectedBrush") ?? Brushes.SlateGray;
        }

        var key = rect.Category switch
        {
            FileCategory.Videos => "TreemapVideosBrush",
            FileCategory.Images => "TreemapImagesBrush",
            FileCategory.Audio => "TreemapAudioBrush",
            FileCategory.Documents => "TreemapDocumentsBrush",
            FileCategory.Archives => "TreemapArchivesBrush",
            FileCategory.Applications => "TreemapApplicationsBrush",
            _ => "TreemapOtherBrush"
        };

        return ResolveBrush(key) ?? ResolveBrush("AccentBrush") ?? Brushes.SteelBlue;
    }

    private Brush? ResolveBrush(string key) => TryFindResource(key) as Brush;

    public readonly record struct DirectionRequest(int Dx, int Dy);

    private sealed class TreemapCanvasAutomationPeer(TreemapCanvas owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore()
        {
            var layout = owner.Layout;
            if (layout is null)
            {
                return "Treemap";
            }

            return $"Treemap, {layout.Rectangles.Count} regions";
        }

        protected override AutomationControlType GetAutomationControlTypeCore() =>
            AutomationControlType.Custom;
    }
}

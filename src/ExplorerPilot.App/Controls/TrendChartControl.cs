using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using ExplorerPilot.Core.Snapshots;

namespace ExplorerPilot.App.Controls;

/// <summary>Lightweight WPF timeline chart for snapshot total-size trends (no third-party chart lib).</summary>
public sealed class TrendChartControl : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(
            nameof(Points),
            typeof(IReadOnlyList<SnapshotTrendPoint>),
            typeof(TrendChartControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPointsChanged));

    public static readonly DependencyProperty LineBrushProperty =
        DependencyProperty.Register(
            nameof(LineBrush),
            typeof(Brush),
            typeof(TrendChartControl),
            new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IncompleteBrushProperty =
        DependencyProperty.Register(
            nameof(IncompleteBrush),
            typeof(Brush),
            typeof(TrendChartControl),
            new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AxisBrushProperty =
        DependencyProperty.Register(
            nameof(AxisBrush),
            typeof(Brush),
            typeof(TrendChartControl),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectionBrushProperty =
        DependencyProperty.Register(
            nameof(SelectionBrush),
            typeof(Brush),
            typeof(TrendChartControl),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TooltipTextProperty =
        DependencyProperty.Register(
            nameof(TooltipText),
            typeof(string),
            typeof(TrendChartControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(
            nameof(SelectedIndex),
            typeof(int),
            typeof(TrendChartControl),
            new FrameworkPropertyMetadata(
                -1,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender,
                OnSelectedIndexChanged));

    public static readonly DependencyProperty AccessibilitySummaryProperty =
        DependencyProperty.Register(
            nameof(AccessibilitySummary),
            typeof(string),
            typeof(TrendChartControl),
            new PropertyMetadata(string.Empty, OnAccessibilitySummaryChanged));

    private readonly List<PointHit> _hits = [];
    private CultureInfo _culture = CultureInfo.CurrentCulture;

    public TrendChartControl()
    {
        Focusable = true;
        KeyboardNavigation.SetIsTabStop(this, true);
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        FocusVisualStyle = null;
        Loaded += (_, _) => InvalidateVisual();
        SizeChanged += (_, _) =>
        {
            RebuildHits();
            InvalidateVisual();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                InvalidateVisual();
            }
        };

        // DynamicResource theme swaps update brush DPs; force a redraw/hit rebuild.
        DependencyPropertyDescriptor.FromProperty(LineBrushProperty, typeof(TrendChartControl))
            ?.AddValueChanged(this, (_, _) =>
            {
                RebuildHits();
                InvalidateVisual();
            });
        DependencyPropertyDescriptor.FromProperty(AxisBrushProperty, typeof(TrendChartControl))
            ?.AddValueChanged(this, (_, _) => InvalidateVisual());
        DependencyPropertyDescriptor.FromProperty(IncompleteBrushProperty, typeof(TrendChartControl))
            ?.AddValueChanged(this, (_, _) => InvalidateVisual());
        DependencyPropertyDescriptor.FromProperty(SelectionBrushProperty, typeof(TrendChartControl))
            ?.AddValueChanged(this, (_, _) => InvalidateVisual());
    }

    public IReadOnlyList<SnapshotTrendPoint>? Points
    {
        get => (IReadOnlyList<SnapshotTrendPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public Brush IncompleteBrush
    {
        get => (Brush)GetValue(IncompleteBrushProperty);
        set => SetValue(IncompleteBrushProperty, value);
    }

    public Brush AxisBrush
    {
        get => (Brush)GetValue(AxisBrushProperty);
        set => SetValue(AxisBrushProperty, value);
    }

    public Brush SelectionBrush
    {
        get => (Brush)GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    public string TooltipText
    {
        get => (string)GetValue(TooltipTextProperty);
        set => SetValue(TooltipTextProperty, value);
    }

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public string AccessibilitySummary
    {
        get => (string)GetValue(AccessibilitySummaryProperty);
        set => SetValue(AccessibilitySummaryProperty, value);
    }

    public void SetCulture(CultureInfo culture)
    {
        _culture = culture ?? CultureInfo.CurrentCulture;
        InvalidateVisual();
        UpdateSelectedTooltip();
    }

    protected override void OnRender(DrawingContext dc)
    {
        RebuildHits();
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 1 || height <= 1)
        {
            return;
        }

        var points = Points;
        var metrics = TrendChartLayout.Compute(width, height, points ?? Array.Empty<SnapshotTrendPoint>());
        var axisPen = new Pen(AxisBrush, 1);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        dc.DrawLine(
            axisPen,
            new Point(metrics.PadLeft, metrics.PadTop),
            new Point(metrics.PadLeft, metrics.PadTop + metrics.PlotHeight));
        dc.DrawLine(
            axisPen,
            new Point(metrics.PadLeft, metrics.PadTop + metrics.PlotHeight),
            new Point(metrics.PadLeft + metrics.PlotWidth, metrics.PadTop + metrics.PlotHeight));

        if (points is null || points.Count == 0)
        {
            DrawLabel(dc, "—", metrics.PadLeft + 8, metrics.PadTop + 8, AxisBrush);
            return;
        }

        Point? prev = null;
        var linePen = new Pen(LineBrush, 2);
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var (x, y) = TrendChartLayout.MapPoint(metrics, i, points.Count, p.TotalBytes);
            var pt = new Point(x, y);

            if (prev is not null)
            {
                // Gaps are not zero-filled: consecutive chart points are connected only as displayed samples.
                dc.DrawLine(linePen, prev.Value, pt);
            }

            var brush = p.IsIncomplete ? IncompleteBrush : LineBrush;
            var radius = i == SelectedIndex ? 6.0 : 4.0;
            dc.DrawEllipse(brush, null, pt, radius, radius);
            if (i == SelectedIndex)
            {
                dc.DrawEllipse(null, new Pen(SelectionBrush, 1.5), pt, radius + 2, radius + 2);
            }

            // Incomplete markers get a second ring so they stay obvious on both themes.
            if (p.IsIncomplete)
            {
                dc.DrawEllipse(null, new Pen(IncompleteBrush, 1.25), pt, radius + 3, radius + 3);
            }

            prev = pt;
        }

        DrawLabel(dc, FormatBytes(metrics.MaxBytes), 4, metrics.PadTop, AxisBrush);
        DrawLabel(dc, FormatBytes(metrics.MinBytes), 4, metrics.PadTop + metrics.PlotHeight - 12, AxisBrush);

        var first = points[0].SnapshotCreatedAtUtc.ToLocalTime().ToString("d", _culture);
        var last = points[^1].SnapshotCreatedAtUtc.ToLocalTime().ToString("d", _culture);
        DrawLabel(dc, first, metrics.PadLeft, metrics.PadTop + metrics.PlotHeight + 6, AxisBrush);
        var lastWidth = MeasureText(last);
        DrawLabel(
            dc,
            last,
            metrics.PadLeft + metrics.PlotWidth - lastWidth,
            metrics.PadTop + metrics.PlotHeight + 6,
            AxisBrush);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_hits.Count == 0)
        {
            TooltipText = string.Empty;
            ToolTip = null;
            return;
        }

        var pos = e.GetPosition(this);
        var locations = _hits.Select(h => (h.Location.X, h.Location.Y)).ToList();
        var index = TrendChartLayout.FindNearestIndex(locations, pos.X, pos.Y, 18);
        if (index < 0)
        {
            TooltipText = string.Empty;
            ToolTip = null;
            return;
        }

        ApplyHoverOrSelectionTooltip(index, setSelection: false);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (_hits.Count == 0)
        {
            return;
        }

        var pos = e.GetPosition(this);
        var locations = _hits.Select(h => (h.Location.X, h.Location.Y)).ToList();
        var index = TrendChartLayout.FindNearestIndex(locations, pos.X, pos.Y, 22);
        if (index >= 0)
        {
            SelectedIndex = index;
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var count = Points?.Count ?? 0;
        if (count <= 0)
        {
            return;
        }

        if (e.Key is Key.Left or Key.Up)
        {
            SelectedIndex = TrendChartLayout.Navigate(SelectedIndex, count, -1);
            e.Handled = true;
        }
        else if (e.Key is Key.Right or Key.Down)
        {
            SelectedIndex = TrendChartLayout.Navigate(SelectedIndex, count, 1);
            e.Handled = true;
        }
        else if (e.Key is Key.Home)
        {
            SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.Key is Key.End)
        {
            SelectedIndex = count - 1;
            e.Handled = true;
        }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (SelectedIndex < 0 && Points is { Count: > 0 })
        {
            SelectedIndex = Points.Count - 1;
        }
    }

    private void RebuildHits()
    {
        _hits.Clear();
        var points = Points;
        if (points is null || points.Count == 0 || ActualWidth <= 1 || ActualHeight <= 1)
        {
            return;
        }

        var metrics = TrendChartLayout.Compute(ActualWidth, ActualHeight, points);
        for (var i = 0; i < points.Count; i++)
        {
            var (x, y) = TrendChartLayout.MapPoint(metrics, i, points.Count, points[i].TotalBytes);
            _hits.Add(new PointHit(new Point(x, y), points[i], i));
        }
    }

    private void ApplyHoverOrSelectionTooltip(int index, bool setSelection)
    {
        var points = Points;
        if (points is null || index < 0 || index >= points.Count)
        {
            TooltipText = string.Empty;
            ToolTip = null;
            return;
        }

        if (setSelection)
        {
            SelectedIndex = index;
        }

        TooltipText = FormatPointTooltip(points[index]);
        ToolTip = TooltipText;
    }

    private void UpdateSelectedTooltip()
    {
        var points = Points;
        if (points is null || SelectedIndex < 0 || SelectedIndex >= points.Count)
        {
            return;
        }

        TooltipText = FormatPointTooltip(points[SelectedIndex]);
        ToolTip = TooltipText;
    }

    private string FormatPointTooltip(SnapshotTrendPoint p)
    {
        var delta = p.DeltaPercent is null
            ? FormatBytesSigned(p.DeltaBytes)
            : $"{FormatBytesSigned(p.DeltaBytes)} ({p.DeltaPercent.Value.ToString("0.##", _culture)}%)";
        var incomplete = p.IsIncomplete ? " ⚠" : string.Empty;
        return
            $"{p.DisplayName}{incomplete}\n" +
            $"{p.SnapshotCreatedAtUtc.ToLocalTime().ToString("g", _culture)}\n" +
            $"{FormatBytes(p.TotalBytes)}\nΔ {delta}";
    }

    private void DrawLabel(DrawingContext dc, string text, double x, double y, Brush brush)
    {
        var ft = new FormattedText(
            text,
            _culture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            11,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(x, y));
    }

    private double MeasureText(string text)
    {
        var ft = new FormattedText(
            text,
            _culture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            11,
            AxisBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return ft.Width;
    }

    private string FormatBytes(long bytes)
    {
        double value = Math.Abs(bytes);
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var u = 0;
        while (value >= 1024 && u < units.Length - 1)
        {
            value /= 1024;
            u++;
        }

        return value.ToString(u == 0 ? "0" : "0.##", _culture) + " " + units[u];
    }

    private string FormatBytesSigned(long bytes)
    {
        var sign = bytes > 0 ? "+" : bytes < 0 ? "-" : string.Empty;
        return sign + FormatBytes(SnapshotMath.AbsClamped(bytes));
    }

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TrendChartControl chart)
        {
            return;
        }

        var count = chart.Points?.Count ?? 0;
        if (count == 0)
        {
            chart.SelectedIndex = -1;
        }
        else if (chart.SelectedIndex < 0 || chart.SelectedIndex >= count)
        {
            chart.SelectedIndex = count - 1;
        }

        chart.RebuildHits();
        chart.UpdateSelectedTooltip();
    }

    private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TrendChartControl chart)
        {
            chart.UpdateSelectedTooltip();
        }
    }

    private static void OnAccessibilitySummaryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TrendChartControl chart)
        {
            AutomationProperties.SetName(chart, e.NewValue as string ?? string.Empty);
            AutomationProperties.SetHelpText(chart, e.NewValue as string ?? string.Empty);
        }
    }

    private sealed record PointHit(Point Location, SnapshotTrendPoint Point, int Index);
}

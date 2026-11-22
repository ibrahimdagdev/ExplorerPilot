using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplorerPilot.App.Converters;
using ExplorerPilot.App.Localization;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Snapshots;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.App.ViewModels;

public partial class TrendsTabViewModel : ObservableObject, IDisposable
{
    private readonly ISnapshotStore _snapshotStore;
    private readonly ISnapshotTrendService _trendService;
    private readonly ISnapshotCatalogNotifier _catalogNotifier;
    private readonly ILocalizationService _localization;
    private readonly ILogger<TrendsTabViewModel> _logger;

    private CancellationTokenSource? _workCts;
    private int _workVersion;
    private IReadOnlyList<SnapshotInfo> _catalog = [];
    private SnapshotTrendResult? _lastResult;

    public TrendsTabViewModel(
        ISnapshotStore snapshotStore,
        ISnapshotTrendService trendService,
        ISnapshotCatalogNotifier catalogNotifier,
        ILocalizationService localization,
        ILogger<TrendsTabViewModel> logger)
    {
        _snapshotStore = snapshotStore;
        _trendService = trendService;
        _catalogNotifier = catalogNotifier;
        _localization = localization;
        _logger = logger;

        GranularityOptions =
        [
            new TrendOptionItem<TrendGranularity>(TrendGranularity.Daily, string.Empty),
            new TrendOptionItem<TrendGranularity>(TrendGranularity.Weekly, string.Empty),
            new TrendOptionItem<TrendGranularity>(TrendGranularity.Monthly, string.Empty)
        ];
        SelectedGranularity = GranularityOptions[0];
        RootOptions = [];
        SelectedRoot = null;
        RefreshLocalizedChrome();

        _catalogNotifier.CatalogChanged += OnCatalogChanged;
    }

    public ObservableCollection<SnapshotTrendPointRowViewModel> TimelineRows { get; } = [];

    public ObservableCollection<SnapshotCategoryTrendRowViewModel> CategoryRows { get; } = [];

    public ObservableCollection<SnapshotExtensionTrendRowViewModel> ExtensionRows { get; } = [];

    public ObservableCollection<SnapshotFolderTrendRowViewModel> GrowingFolderRows { get; } = [];

    public IReadOnlyList<TrendOptionItem<TrendGranularity>> GranularityOptions { get; }

    public ObservableCollection<TrendRootOption> RootOptions { get; }

    public IReadOnlyList<SnapshotTrendPoint>? ChartPoints { get; private set; }

    [ObservableProperty]
    private TrendOptionItem<TrendGranularity>? _selectedGranularity;

    [ObservableProperty]
    private TrendRootOption? _selectedRoot;

    [ObservableProperty]
    private DateTime? _fromDate;

    [ObservableProperty]
    private DateTime? _toDate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCatalogCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelWorkCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasSeries;

    [ObservableProperty]
    private bool _incompleteWarningVisible;

    [ObservableProperty]
    private bool _downsampledWarningVisible;

    [ObservableProperty]
    private bool _folderCaveatVisible;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _totalSizeText = "—";

    [ObservableProperty]
    private string _netDeltaText = "—";

    [ObservableProperty]
    private string _growthRateText = "—";

    [ObservableProperty]
    private string _rootLabelText = string.Empty;

    [ObservableProperty]
    private string _comparisonRangeText = string.Empty;

    [ObservableProperty]
    private string _chartAccessibilitySummary = string.Empty;

    [ObservableProperty]
    private string _selectedPointSummary = string.Empty;

    [ObservableProperty]
    private int _selectedChartIndex = -1;

    [ObservableProperty]
    private bool _netDeltaPositive;

    [ObservableProperty]
    private bool _netDeltaNegative;

    public async Task InitializeAsync()
    {
        await RefreshCatalogAsync().ConfigureAwait(true);
    }

    public void OnScanStarting()
    {
        // Trends are snapshot-based; no live scan attachment required.
    }

    public void RefreshLocalization()
    {
        RefreshLocalizedChrome();
        if (_lastResult?.SelectedSeries is not null)
        {
            ApplySeries(_lastResult.SelectedSeries);
            StatusText = BuildStatus(_lastResult);
        }
        else if (!IsBusy)
        {
            StatusText = _localization.GetString("Trends_StatusReady");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshCatalogAsync()
    {
        await RunWorkAsync(async (version, token) =>
        {
            ProgressText = _localization.GetString("Trends_ProgressListing");
            var list = await _snapshotStore.ListAsync(token).ConfigureAwait(true);
            if (version != _workVersion)
            {
                return;
            }

            _catalog = list;
            RebuildRootOptions(list);
            StatusText = _localization.GetString("Trends_SnapshotCountFormat", list.Count);

            if (list.Count > 0 && SelectedRoot?.NormalizedRoot is not null)
            {
                await AnalyzeCoreAsync(version, token).ConfigureAwait(true);
            }
            else if (list.Count == 0)
            {
                ClearSeries();
            }
            else
            {
                ClearSeries();
                StatusText = _localization.GetString("Trends_SelectRoot");
            }
        }).ConfigureAwait(true);
    }

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        await RunWorkAsync(async (version, token) =>
        {
            await AnalyzeCoreAsync(version, token).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private bool CanAnalyze() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelWork()
    {
        CancelWorkCore();
        IsBusy = false;
        ProgressText = string.Empty;
        StatusText = _localization.GetString("Trends_Cancelled");
    }

    private bool CanCancel() => IsBusy;

    private async Task AnalyzeCoreAsync(int version, CancellationToken token)
    {
        if (SelectedRoot?.NormalizedRoot is null)
        {
            ClearSeries();
            StatusText = _localization.GetString("Trends_SelectRoot");
            return;
        }

        ProgressText = _localization.GetString("Trends_ProgressAnalyzing");
        var request = new SnapshotTrendRequest
        {
            Granularity = SelectedGranularity?.Value ?? TrendGranularity.Daily,
            RootPathFilter = SelectedRoot.NormalizedRoot,
            FromUtc = FromDate is null ? null : new DateTimeOffset(DateTime.SpecifyKind(FromDate.Value.Date, DateTimeKind.Local)).ToUniversalTime(),
            ToUtc = ToDate is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local)).ToUniversalTime(),
            MaxTimelinePoints = 60,
            IncludeDetailBreakdowns = true
        };

        var result = await _trendService.AnalyzeAsync(
                _catalog,
                request,
                new Progress<SnapshotTrendProgress>(p =>
                {
                    if (version != _workVersion)
                    {
                        return;
                    }

                    ProgressText = _localization.GetString(
                        "Trends_ProgressPercentFormat",
                        (p.Fraction * 100).ToString("0", _localization.CurrentCulture));
                }),
                token)
            .ConfigureAwait(true);

        if (version != _workVersion)
        {
            return;
        }

        _lastResult = result;
        RebuildRootOptions(_catalog, result.AvailableRoots);
        if (result.SelectedSeries is null || result.SelectedSeries.Points.Count == 0)
        {
            ClearSeries();
            StatusText = _localization.GetString("Trends_NoData");
            return;
        }

        ApplySeries(result.SelectedSeries);
        DownsampledWarningVisible = result.WasDownsampled || result.SelectedSeries.WasDownsampled;
        StatusText = BuildStatus(result);
    }

    private void ApplySeries(SnapshotTrendSeries series)
    {
        var culture = _localization.CurrentCulture;
        ChartPoints = series.ChartPoints;
        OnPropertyChanged(nameof(ChartPoints));

        TimelineRows.Clear();
        foreach (var point in series.Points.Reverse())
        {
            TimelineRows.Add(new SnapshotTrendPointRowViewModel(
                point,
                ByteSizeConverter.Format(point.TotalBytes, culture),
                FormatSigned(point.DeltaBytes, culture),
                point.DeltaPercent is null ? "—" : point.DeltaPercent.Value.ToString("0.##", culture) + "%",
                point.SnapshotCreatedAtUtc.ToLocalTime().ToString("g", culture),
                point.IsIncomplete
                    ? _localization.GetString("Trends_Incomplete")
                    : _localization.GetString("Trends_Complete")));
        }

        CategoryRows.Clear();
        foreach (var row in series.CategoryTrends)
        {
            CategoryRows.Add(new SnapshotCategoryTrendRowViewModel(
                CategoryLabel(row.CategoryCode),
                ByteSizeConverter.Format(row.FirstBytes, culture),
                ByteSizeConverter.Format(row.LastBytes, culture),
                FormatSigned(row.DeltaBytes, culture),
                FormatPercent(row.GrowthPercent, culture)));
        }

        ExtensionRows.Clear();
        foreach (var row in series.ExtensionTrends)
        {
            var label = string.IsNullOrEmpty(row.Extension)
                ? _localization.GetString("Trends_NoExtension")
                : row.Extension;
            ExtensionRows.Add(new SnapshotExtensionTrendRowViewModel(
                label,
                ByteSizeConverter.Format(row.FirstBytes, culture),
                ByteSizeConverter.Format(row.LastBytes, culture),
                FormatSigned(row.DeltaBytes, culture),
                FormatPercent(row.GrowthPercent, culture)));
        }

        GrowingFolderRows.Clear();
        foreach (var row in series.FastestGrowingFolders)
        {
            GrowingFolderRows.Add(new SnapshotFolderTrendRowViewModel(
                string.IsNullOrEmpty(row.RelativePath) ? row.Name : row.RelativePath,
                ByteSizeConverter.Format(row.FirstBytes, culture),
                ByteSizeConverter.Format(row.LastBytes, culture),
                FormatSigned(row.DeltaBytes, culture),
                FormatPercent(row.GrowthPercent, culture)));
        }

        TotalSizeText = ByteSizeConverter.Format(series.LastTotalBytes, culture);
        NetDeltaText = FormatSigned(series.NetDeltaBytes, culture);
        GrowthRateText = FormatPercent(series.GrowthPercent, culture);
        NetDeltaPositive = series.NetDeltaBytes > 0;
        NetDeltaNegative = series.NetDeltaBytes < 0;
        RootLabelText = series.RootPath;
        IncompleteWarningVisible = series.ContainsIncompleteSnapshots;
        FolderCaveatVisible = series.FastestGrowingFolders.Count > 0;
        DownsampledWarningVisible = series.WasDownsampled;

        var firstAt = series.FirstSnapshotCreatedAtUtc?.ToLocalTime().ToString("g", culture) ?? "—";
        var lastAt = series.LastSnapshotCreatedAtUtc?.ToLocalTime().ToString("g", culture) ?? "—";
        ComparisonRangeText = _localization.GetString("Trends_ComparisonRangeFormat", firstAt, lastAt);

        ChartAccessibilitySummary = _localization.GetString(
            "Trends_ChartAccessibilityFormat",
            series.ChartPoints.Count.ToString("N0", culture),
            series.Points.Count.ToString("N0", culture),
            ByteSizeConverter.Format(series.FirstTotalBytes, culture),
            ByteSizeConverter.Format(series.LastTotalBytes, culture),
            FormatSigned(series.NetDeltaBytes, culture));

        SelectedChartIndex = series.ChartPoints.Count > 0 ? series.ChartPoints.Count - 1 : -1;
        UpdateSelectedPointSummary();
        HasSeries = series.Points.Count > 0;
    }

    private void ClearSeries()
    {
        ChartPoints = null;
        OnPropertyChanged(nameof(ChartPoints));
        TimelineRows.Clear();
        CategoryRows.Clear();
        ExtensionRows.Clear();
        GrowingFolderRows.Clear();
        HasSeries = false;
        IncompleteWarningVisible = false;
        DownsampledWarningVisible = false;
        FolderCaveatVisible = false;
        TotalSizeText = "—";
        NetDeltaText = "—";
        GrowthRateText = "—";
        RootLabelText = string.Empty;
        ComparisonRangeText = string.Empty;
        ChartAccessibilitySummary = string.Empty;
        SelectedPointSummary = string.Empty;
        SelectedChartIndex = -1;
        NetDeltaPositive = false;
        NetDeltaNegative = false;
        _lastResult = null;
    }

    private void RebuildRootOptions(IReadOnlyList<SnapshotInfo> catalog, IReadOnlyList<string>? preferredRoots = null)
    {
        var previous = SelectedRoot?.NormalizedRoot;
        RootOptions.Clear();
        // Placeholder only — multi-root merge is not offered by default.
        RootOptions.Add(new TrendRootOption(null, _localization.GetString("Trends_Root_Select")));

        var roots = preferredRoots is { Count: > 0 }
            ? preferredRoots
            : catalog
                .Select(s => s.RootPath)
                .Distinct(SnapshotPathNormalizer.PathComparer)
                .OrderBy(r => r, SnapshotPathNormalizer.PathComparer)
                .ToList();

        foreach (var root in roots)
        {
            RootOptions.Add(new TrendRootOption(
                SnapshotPathNormalizer.NormalizeAbsolute(root),
                root));
        }

        SelectedRoot = RootOptions.FirstOrDefault(r =>
                            previous is not null
                            && r.NormalizedRoot is not null
                            && SnapshotPathNormalizer.RootsEquivalent(r.NormalizedRoot, previous))
                        ?? RootOptions.FirstOrDefault(r => r.NormalizedRoot is not null)
                        ?? RootOptions[0];
    }

    private async Task RunWorkAsync(Func<int, CancellationToken, Task> work)
    {
        CancelWorkCore();
        _workCts?.Dispose();
        _workCts = new CancellationTokenSource();
        var version = Interlocked.Increment(ref _workVersion);
        var token = _workCts.Token;
        IsBusy = true;
        ProgressText = string.Empty;

        try
        {
            await work(version, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            if (version == _workVersion)
            {
                StatusText = _localization.GetString("Trends_Cancelled");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Trends work failed");
            if (version == _workVersion)
            {
                StatusText = _localization.GetString("Trends_FailedFormat", ex.Message);
                MessageBox.Show(
                    StatusText,
                    _localization.GetString("Trends_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            if (version == _workVersion)
            {
                IsBusy = false;
                ProgressText = string.Empty;
            }
        }
    }

    private void CancelWorkCore()
    {
        try
        {
            _workCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        Interlocked.Increment(ref _workVersion);
    }

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        // Marshal to UI thread if needed.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            _ = RefreshCatalogAsync();
            return;
        }

        _ = dispatcher.BeginInvoke(new Action(() => _ = RefreshCatalogAsync()));
    }

    private string BuildStatus(SnapshotTrendResult result)
    {
        var series = result.SelectedSeries;
        if (series is null)
        {
            return _localization.GetString("Trends_NoData");
        }

        var baseStatus = _localization.GetString(
            "Trends_CompletedFormat",
            series.Points.Count.ToString("N0", _localization.CurrentCulture),
            result.SnapshotCountConsidered.ToString("N0", _localization.CurrentCulture),
            series.ChartPoints.Count.ToString("N0", _localization.CurrentCulture));

        if (series.ContainsIncompleteSnapshots)
        {
            return baseStatus + " " + _localization.GetString("Trends_LimitedReliabilityNote");
        }

        return baseStatus;
    }

    private void UpdateSelectedPointSummary()
    {
        var points = ChartPoints;
        if (points is null || SelectedChartIndex < 0 || SelectedChartIndex >= points.Count)
        {
            SelectedPointSummary = string.Empty;
            return;
        }

        var culture = _localization.CurrentCulture;
        var point = points[SelectedChartIndex];
        var delta = FormatSigned(point.DeltaBytes, culture);
        var pct = point.DeltaPercent is null
            ? "—"
            : point.DeltaPercent.Value.ToString("0.##", culture) + "%";
        var incomplete = point.IsIncomplete
            ? " · " + _localization.GetString("Trends_Incomplete")
            : string.Empty;
        SelectedPointSummary = _localization.GetString(
            "Trends_SelectedPointFormat",
            point.SnapshotCreatedAtUtc.ToLocalTime().ToString("g", culture),
            ByteSizeConverter.Format(point.TotalBytes, culture),
            delta,
            pct) + incomplete;
    }

    private string CategoryLabel(string code) =>
        code switch
        {
            "Videos" => _localization.GetString("FileTypes_Category_Videos"),
            "Images" => _localization.GetString("FileTypes_Category_Images"),
            "Audio" => _localization.GetString("FileTypes_Category_Audio"),
            "Documents" => _localization.GetString("FileTypes_Category_Documents"),
            "Archives" => _localization.GetString("FileTypes_Category_Archives"),
            "Applications" => _localization.GetString("FileTypes_Category_Applications"),
            _ => _localization.GetString("FileTypes_Category_Other")
        };

    private static string FormatSigned(long delta, CultureInfo culture)
    {
        var abs = ByteSizeConverter.Format(SnapshotMath.AbsClamped(delta), culture);
        if (delta > 0)
        {
            return "+" + abs;
        }

        if (delta < 0)
        {
            return "-" + abs;
        }

        return abs;
    }

    private static string FormatPercent(double? value, CultureInfo culture) =>
        value is null ? "—" : value.Value.ToString("0.##", culture) + "%";

    private void RefreshLocalizedChrome()
    {
        GranularityOptions[0].DisplayName = _localization.GetString("Trends_Granularity_Daily");
        GranularityOptions[1].DisplayName = _localization.GetString("Trends_Granularity_Weekly");
        GranularityOptions[2].DisplayName = _localization.GetString("Trends_Granularity_Monthly");

        if (RootOptions.Count > 0 && RootOptions[0].NormalizedRoot is null)
        {
            RootOptions[0].DisplayName = _localization.GetString("Trends_Root_Select");
        }

        if (!IsBusy && !HasSeries)
        {
            StatusText = _localization.GetString("Trends_StatusReady");
        }
    }

    partial void OnSelectedGranularityChanged(TrendOptionItem<TrendGranularity>? value)
    {
        if (!IsBusy && _catalog.Count > 0 && SelectedRoot?.NormalizedRoot is not null)
        {
            _ = AnalyzeAsync();
        }
    }

    partial void OnSelectedRootChanged(TrendRootOption? value)
    {
        if (!IsBusy && _catalog.Count > 0 && value?.NormalizedRoot is not null)
        {
            _ = AnalyzeAsync();
        }
        else if (value?.NormalizedRoot is null && !IsBusy)
        {
            ClearSeries();
            StatusText = _localization.GetString("Trends_SelectRoot");
        }
    }

    partial void OnFromDateChanged(DateTime? value)
    {
        if (!IsBusy && _catalog.Count > 0 && SelectedRoot?.NormalizedRoot is not null)
        {
            _ = AnalyzeAsync();
        }
    }

    partial void OnToDateChanged(DateTime? value)
    {
        if (!IsBusy && _catalog.Count > 0 && SelectedRoot?.NormalizedRoot is not null)
        {
            _ = AnalyzeAsync();
        }
    }

    partial void OnSelectedChartIndexChanged(int value) => UpdateSelectedPointSummary();

    public void Dispose()
    {
        _catalogNotifier.CatalogChanged -= OnCatalogChanged;
        CancelWorkCore();
        _workCts?.Dispose();
        _workCts = null;
    }
}

public sealed class TrendOptionItem<T>
{
    public TrendOptionItem(T value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public T Value { get; }

    public string DisplayName { get; set; }
}

public sealed class TrendRootOption
{
    public TrendRootOption(string? normalizedRoot, string displayName)
    {
        NormalizedRoot = normalizedRoot;
        DisplayName = displayName;
    }

    public string? NormalizedRoot { get; }

    public string DisplayName { get; set; }
}

public sealed class SnapshotTrendPointRowViewModel
{
    public SnapshotTrendPointRowViewModel(
        SnapshotTrendPoint point,
        string sizeText,
        string deltaText,
        string percentText,
        string dateText,
        string statusText)
    {
        Point = point;
        SizeText = sizeText;
        DeltaText = deltaText;
        PercentText = percentText;
        DateText = dateText;
        StatusText = statusText;
    }

    public SnapshotTrendPoint Point { get; }

    public string DisplayName => Point.DisplayName;

    public string SizeText { get; }

    public string DeltaText { get; }

    public string PercentText { get; }

    public string DateText { get; }

    public string StatusText { get; }
}

public sealed class SnapshotCategoryTrendRowViewModel
{
    public SnapshotCategoryTrendRowViewModel(
        string label,
        string firstText,
        string lastText,
        string deltaText,
        string percentText)
    {
        Label = label;
        FirstText = firstText;
        LastText = lastText;
        DeltaText = deltaText;
        PercentText = percentText;
    }

    public string Label { get; }

    public string FirstText { get; }

    public string LastText { get; }

    public string DeltaText { get; }

    public string PercentText { get; }
}

public sealed class SnapshotExtensionTrendRowViewModel
{
    public SnapshotExtensionTrendRowViewModel(
        string label,
        string firstText,
        string lastText,
        string deltaText,
        string percentText)
    {
        Label = label;
        FirstText = firstText;
        LastText = lastText;
        DeltaText = deltaText;
        PercentText = percentText;
    }

    public string Label { get; }

    public string FirstText { get; }

    public string LastText { get; }

    public string DeltaText { get; }

    public string PercentText { get; }
}

public sealed class SnapshotFolderTrendRowViewModel
{
    public SnapshotFolderTrendRowViewModel(
        string path,
        string firstText,
        string lastText,
        string deltaText,
        string percentText)
    {
        Path = path;
        FirstText = firstText;
        LastText = lastText;
        DeltaText = deltaText;
        PercentText = percentText;
    }

    public string Path { get; }

    public string FirstText { get; }

    public string LastText { get; }

    public string DeltaText { get; }

    public string PercentText { get; }
}

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplorerPilot.App.Converters;
using ExplorerPilot.App.Localization;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Snapshots;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.App.ViewModels;

/// <summary>
/// Professional dashboard over drives + last scan + optional snapshot/cleanup previews.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly IDashboardSummaryService _summaryService;
    private readonly ISnapshotStore _snapshotStore;
    private readonly ISnapshotTrendService _trendService;
    private readonly ICleanupRecommendationService _cleanupService;
    private readonly ISnapshotCatalogNotifier _catalogNotifier;
    private readonly ILocalizationService _localization;
    private readonly ILogger<DashboardViewModel> _logger;

    private CancellationTokenSource? _refreshCts;
    private int _refreshVersion;
    private Guid _boundSessionId = Guid.Empty;
    private ScanResult? _boundScan;
    private bool _boundIncomplete;
    private DateTimeOffset? _boundCompletedAt;
    private IReadOnlyList<DriveInformation> _boundDrives = [];
    private bool _isVisible;

    public DashboardViewModel(
        IDashboardSummaryService summaryService,
        ISnapshotStore snapshotStore,
        ISnapshotTrendService trendService,
        ICleanupRecommendationService cleanupService,
        ISnapshotCatalogNotifier catalogNotifier,
        ILocalizationService localization,
        ILogger<DashboardViewModel> logger)
    {
        _summaryService = summaryService;
        _snapshotStore = snapshotStore;
        _trendService = trendService;
        _cleanupService = cleanupService;
        _catalogNotifier = catalogNotifier;
        _localization = localization;
        _logger = logger;
        _catalogNotifier.CatalogChanged += OnCatalogChanged;
    }

    public void Dispose()
    {
        _catalogNotifier.CatalogChanged -= OnCatalogChanged;
        try
        {
            _refreshCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        _refreshCts?.Dispose();
    }

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        if (!_isVisible)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            _ = RefreshAsync();
            return;
        }

        _ = dispatcher.BeginInvoke(new Action(() => _ = RefreshAsync()));
    }

    public ObservableCollection<DashboardDriveRowViewModel> DriveRows { get; } = [];

    public ObservableCollection<DashboardPathRowViewModel> LargestFiles { get; } = [];

    public ObservableCollection<DashboardPathRowViewModel> LargestFolders { get; } = [];

    public ObservableCollection<DashboardCategoryRowViewModel> Categories { get; } = [];

    public event EventHandler<int>? NavigateToTabRequested;

    public event EventHandler<string>? RevealPathRequested;

    public event EventHandler<string>? ScanRootRequested;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _hasScanSummary;

    [ObservableProperty]
    private bool _hasSnapshotSummary;

    [ObservableProperty]
    private bool _hasCleanupSummary;

    [ObservableProperty]
    private bool _showIncompleteWarning;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _driveCountText = string.Empty;

    [ObservableProperty]
    private string _totalCapacityText = string.Empty;

    [ObservableProperty]
    private string _usedSpaceText = string.Empty;

    [ObservableProperty]
    private string _freeSpaceText = string.Empty;

    [ObservableProperty]
    private string _usageText = string.Empty;

    [ObservableProperty]
    private double _usageRatio;

    [ObservableProperty]
    private string _highestUsageDriveText = string.Empty;

    [ObservableProperty]
    private string _scanRootText = string.Empty;

    [ObservableProperty]
    private string _scanTimeText = string.Empty;

    [ObservableProperty]
    private string _scanSizeText = string.Empty;

    [ObservableProperty]
    private string _scanCountsText = string.Empty;

    [ObservableProperty]
    private string _scanDurationText = string.Empty;

    [ObservableProperty]
    private string _snapshotLastText = string.Empty;

    [ObservableProperty]
    private string _snapshotDeltaText = string.Empty;

    [ObservableProperty]
    private string _snapshotEmptyText = string.Empty;

    [ObservableProperty]
    private string _cleanupSavingsText = string.Empty;

    [ObservableProperty]
    private string _cleanupCountText = string.Empty;

    [ObservableProperty]
    private string _cleanupRiskText = string.Empty;

    [ObservableProperty]
    private string _cleanupPreviewNote = string.Empty;

    /// <summary>Called when the Dashboard tab becomes selected/deselected.</summary>
    public void SetVisible(bool visible)
    {
        _isVisible = visible;
        if (visible)
        {
            _ = RefreshAsync();
        }
    }

    public void BindInputs(
        IReadOnlyList<DriveInformation> drives,
        ScanResult? scan,
        Guid scanSessionId,
        bool incomplete,
        DateTimeOffset? completedAtUtc)
    {
        _boundDrives = drives;
        _boundScan = scan;
        _boundSessionId = scanSessionId;
        _boundIncomplete = incomplete;
        _boundCompletedAt = completedAtUtc;

        if (_isVisible)
        {
            _ = RefreshAsync();
        }
    }

    public void OnScanStarting()
    {
        _boundScan = null;
        _boundSessionId = Guid.Empty;
        _boundIncomplete = false;
        _boundCompletedAt = null;
        _lastSummary = null;
        HasScanSummary = false;
        HasCleanupSummary = false;
        ShowIncompleteWarning = false;
        LargestFiles.Clear();
        LargestFolders.Clear();
        Categories.Clear();
        StatusText = _localization.GetString("Dashboard_Scanning");
    }

    public void RefreshLocalization()
    {
        CleanupPreviewNote = _localization.GetString("Dashboard_CleanupPreviewOnly");
        SnapshotEmptyText = _localization.GetString("Dashboard_SnapshotEmpty");
        ApplySummaryTexts(_lastSummary);
    }

    private DashboardSummary? _lastSummary;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        var token = _refreshCts.Token;

        IsRefreshing = true;
        StatusText = _localization.GetString("Dashboard_Refreshing");
        RefreshCommand.NotifyCanExecuteChanged();

        try
        {
            var sessionAtStart = _boundSessionId;
            var snapshot = await BuildSnapshotSummaryAsync(_boundScan?.RootNode.FullPath, token).ConfigureAwait(true);
            if (version != _refreshVersion || sessionAtStart != _boundSessionId)
            {
                return;
            }

            var cleanup = await BuildCleanupSummaryAsync(sessionAtStart, token).ConfigureAwait(true);
            if (version != _refreshVersion || sessionAtStart != _boundSessionId)
            {
                return;
            }

            var request = new DashboardBuildRequest
            {
                Drives = _boundDrives,
                Scan = _boundScan,
                ScanSessionId = sessionAtStart,
                ScanIncomplete = _boundIncomplete,
                ScanCompletedAtUtc = _boundCompletedAt,
                Snapshot = snapshot,
                Cleanup = cleanup,
                TopFiles = 10,
                TopFolders = 10
            };

            var summary = await _summaryService.BuildAsync(request, token).ConfigureAwait(true);
            if (version != _refreshVersion || summary.ScanSessionId != _boundSessionId)
            {
                return;
            }

            ApplySummary(summary);
            StatusText = _localization.GetString("Dashboard_Ready");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            StatusText = _localization.GetString("Dashboard_Cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard refresh failed");
            StatusText = _localization.GetString("Dashboard_RefreshFailed");
        }
        finally
        {
            if (version == _refreshVersion)
            {
                IsRefreshing = false;
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool CanRefresh() => !IsRefreshing;

    private async Task<DashboardSnapshotSummary?> BuildSnapshotSummaryAsync(
        string? scanRoot,
        CancellationToken cancellationToken)
    {
        try
        {
            var list = await _snapshotStore.ListAsync(cancellationToken).ConfigureAwait(false);
            if (list.Count == 0)
            {
                return new DashboardSnapshotSummary { HasMatchingSnapshots = false };
            }

            if (string.IsNullOrWhiteSpace(scanRoot))
            {
                var latest = list.OrderByDescending(s => s.CreatedAtUtc).First();
                return new DashboardSnapshotSummary
                {
                    HasMatchingSnapshots = false,
                    LastSnapshotAtUtc = latest.CreatedAtUtc,
                    SnapshotCount = list.Count,
                    ContainsIncompleteSnapshots = latest.IsIncomplete
                };
            }

            var trend = await _trendService.AnalyzeAsync(
                list,
                new SnapshotTrendRequest
                {
                    RootPathFilter = scanRoot,
                    IncludeDetailBreakdowns = false,
                    MaxTimelinePoints = 30
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var series = trend.SelectedSeries;
            if (series is null || series.Points.Count == 0)
            {
                return new DashboardSnapshotSummary
                {
                    HasMatchingSnapshots = false,
                    SnapshotCount = trend.SnapshotCountConsidered,
                    MatchedRootPath = scanRoot
                };
            }

            return new DashboardSnapshotSummary
            {
                HasMatchingSnapshots = true,
                LastSnapshotAtUtc = series.LastSnapshotCreatedAtUtc,
                DeltaBytesVsPrevious = series.NetDeltaBytes,
                GrowthPercent = series.GrowthPercent,
                MatchedRootPath = series.RootPath,
                ContainsIncompleteSnapshots = series.ContainsIncompleteSnapshots,
                SnapshotCount = series.Points.Count
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Dashboard snapshot summary skipped");
            return new DashboardSnapshotSummary { HasMatchingSnapshots = false };
        }
    }

    private async Task<DashboardCleanupSummary?> BuildCleanupSummaryAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (_boundScan?.RootNode is null || sessionId == Guid.Empty)
        {
            return null;
        }

        try
        {
            var result = await _cleanupService.AnalyzeAsync(
                _boundScan.RootNode,
                new CleanupRecommendationCriteria
                {
                    ScanSessionId = sessionId,
                    MaxRecommendations = 200
                },
                verifiedDuplicateGroups: null,
                fromIncompleteScan: _boundIncomplete,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result.ScanSessionId != sessionId)
            {
                return null;
            }

            return new DashboardCleanupSummary
            {
                UniquePotentialSavingsBytes = result.UniquePotentialSavingsBytes,
                RecommendationCount = result.Candidates.Count,
                LowRiskCount = result.Candidates.Count(c => c.Risk == CleanupRiskLevel.Low),
                MediumRiskCount = result.Candidates.Count(c => c.Risk == CleanupRiskLevel.Medium),
                HighRiskCount = result.Candidates.Count(c => c.Risk == CleanupRiskLevel.High),
                FromIncompleteScan = result.FromIncompleteScan,
                IsPreviewOnly = true
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Dashboard cleanup summary skipped");
            return null;
        }
    }

    private void ApplySummary(DashboardSummary summary)
    {
        _lastSummary = summary;
        ApplySummaryTexts(summary);

        DriveRows.Clear();
        foreach (var d in summary.Capacity.Drives)
        {
            DriveRows.Add(new DashboardDriveRowViewModel(d, _localization));
        }

        LargestFiles.Clear();
        foreach (var f in summary.LargestFiles)
        {
            LargestFiles.Add(new DashboardPathRowViewModel(f));
        }

        LargestFolders.Clear();
        foreach (var f in summary.LargestFolders)
        {
            LargestFolders.Add(new DashboardPathRowViewModel(f));
        }

        Categories.Clear();
        foreach (var c in summary.Categories)
        {
            Categories.Add(new DashboardCategoryRowViewModel(c, _localization));
        }
    }

    private void ApplySummaryTexts(DashboardSummary? summary)
    {
        var culture = _localization.CurrentCulture;
        CleanupPreviewNote = _localization.GetString("Dashboard_CleanupPreviewOnly");

        if (summary is null)
        {
            DriveCountText = "—";
            TotalCapacityText = "—";
            UsedSpaceText = "—";
            FreeSpaceText = "—";
            UsageText = "—";
            UsageRatio = 0;
            HighestUsageDriveText = "—";
            HasScanSummary = false;
            HasSnapshotSummary = false;
            HasCleanupSummary = false;
            SnapshotEmptyText = _localization.GetString("Dashboard_SnapshotEmpty");
            return;
        }

        var cap = summary.Capacity;
        DriveCountText = _localization.GetString(
            "Dashboard_DriveCountFormat",
            cap.ReadyDriveCount,
            cap.DriveCount);
        TotalCapacityText = ByteSizeConverter.Format(cap.TotalCapacityBytes, culture);
        UsedSpaceText = ByteSizeConverter.Format(cap.UsedSpaceBytes, culture);
        FreeSpaceText = ByteSizeConverter.Format(cap.FreeSpaceBytes, culture);
        UsageText = PercentageConverter.Format(cap.UsagePercentage, culture);
        UsageRatio = DriveCapacityMath.SafeUsageRatio(cap.TotalCapacityBytes, cap.UsedSpaceBytes);
        HighestUsageDriveText = cap.HighestUsageDrive is { } hi
            ? _localization.GetString(
                "Dashboard_HighestUsageFormat",
                hi.Name.TrimEnd('\\'),
                PercentageConverter.Format(hi.UsagePercentage, culture))
            : _localization.GetString("Dashboard_NoReadyDrives");

        HasScanSummary = summary.LastScan is not null;
        ShowIncompleteWarning = summary.LastScan?.IsIncomplete == true
                                || (summary.LastScan?.ErrorCount ?? 0) > 0;
        if (summary.LastScan is { } scan)
        {
            ScanRootText = scan.RootPath;
            ScanTimeText = scan.CompletedAtUtc?.ToLocalTime().ToString("g", culture)
                           ?? _localization.GetString("Dashboard_ScanTimeUnknown");
            ScanSizeText = ByteSizeConverter.Format(scan.TotalBytes, culture);
            ScanCountsText = _localization.GetString(
                "Dashboard_ScanCountsFormat",
                scan.TotalFiles.ToString("N0", culture),
                scan.TotalDirectories.ToString("N0", culture));
            ScanDurationText = scan.Duration.ToString(@"hh\:mm\:ss", culture);
        }

        HasSnapshotSummary = summary.Snapshot?.HasMatchingSnapshots == true;
        if (HasSnapshotSummary && summary.Snapshot is { } snap)
        {
            SnapshotLastText = snap.LastSnapshotAtUtc?.ToLocalTime().ToString("g", culture)
                               ?? _localization.GetString("Drives_NotAvailable");
            var delta = snap.DeltaBytesVsPrevious ?? 0;
            var sign = delta > 0 ? "+" : string.Empty;
            SnapshotDeltaText = _localization.GetString(
                "Dashboard_SnapshotDeltaFormat",
                sign + ByteSizeConverter.Format(delta, culture),
                snap.GrowthPercent is { } g
                    ? PercentageConverter.Format(g, culture)
                    : _localization.GetString("Drives_NotAvailable"));
            SnapshotEmptyText = string.Empty;
        }
        else
        {
            SnapshotEmptyText = _localization.GetString("Dashboard_SnapshotEmpty");
            SnapshotLastText = string.Empty;
            SnapshotDeltaText = string.Empty;
        }

        HasCleanupSummary = summary.Cleanup is not null;
        if (summary.Cleanup is { } clean)
        {
            CleanupSavingsText = ByteSizeConverter.Format(clean.UniquePotentialSavingsBytes, culture);
            CleanupCountText = clean.RecommendationCount.ToString("N0", culture);
            CleanupRiskText = _localization.GetString(
                "Dashboard_CleanupRiskFormat",
                clean.LowRiskCount,
                clean.MediumRiskCount,
                clean.HighRiskCount);
        }
    }

    [RelayCommand]
    private void OpenTreemap() => NavigateToTabRequested?.Invoke(this, DashboardTabIndex.Treemap);

    [RelayCommand]
    private void OpenCleanup() => NavigateToTabRequested?.Invoke(this, DashboardTabIndex.Cleanup);

    [RelayCommand]
    private void OpenTrends() => NavigateToTabRequested?.Invoke(this, DashboardTabIndex.Trends);

    [RelayCommand]
    private void OpenExplorer() => NavigateToTabRequested?.Invoke(this, DashboardTabIndex.Explorer);

    [RelayCommand]
    private void RevealPath(DashboardPathRowViewModel? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.FullPath))
        {
            return;
        }

        RevealPathRequested?.Invoke(this, row.FullPath);
    }

    [RelayCommand]
    private void ScanDrive(DashboardDriveRowViewModel? row)
    {
        if (row is null || !row.CanScan)
        {
            return;
        }

        ScanRootRequested?.Invoke(this, row.RootPath);
    }
}

public static class DashboardTabIndex
{
    // 0 Dashboard, 1 Drives, 2 Explorer, ... 7 Treemap, 9 Cleanup, 10 Trends
    public const int Drives = 1;
    public const int Explorer = 2;
    public const int Treemap = 7;
    public const int Cleanup = 9;
    public const int Trends = 10;
}

public sealed class DashboardDriveRowViewModel
{
    public DashboardDriveRowViewModel(DashboardDriveSummary drive, ILocalizationService localization)
    {
        Name = drive.Name;
        RootPath = drive.RootPath;
        CanScan = drive.CanScan;
        IsReady = drive.IsReady;
        UsageRatio = DriveCapacityMath.SafeUsageRatio(drive.TotalSizeBytes, drive.UsedSpaceBytes);
        TitleText = string.IsNullOrWhiteSpace(drive.VolumeLabel)
            ? drive.Name.TrimEnd('\\')
            : $"{drive.Name.TrimEnd('\\')} — {drive.VolumeLabel}";
        MetaText = $"{drive.Kind} · {drive.Status}";
        TotalText = drive.IsReady
            ? ByteSizeConverter.Format(drive.TotalSizeBytes, localization.CurrentCulture)
            : localization.GetString("Drives_NotAvailable");
        UsedText = drive.IsReady
            ? ByteSizeConverter.Format(drive.UsedSpaceBytes, localization.CurrentCulture)
            : localization.GetString("Drives_NotAvailable");
        FreeText = drive.IsReady
            ? ByteSizeConverter.Format(drive.AvailableFreeSpaceBytes, localization.CurrentCulture)
            : localization.GetString("Drives_NotAvailable");
        UsageText = drive.IsReady
            ? PercentageConverter.Format(drive.UsagePercentage, localization.CurrentCulture)
            : localization.GetString("Drives_NotAvailable");
    }

    public string Name { get; }
    public string RootPath { get; }
    public bool CanScan { get; }
    public bool IsReady { get; }
    public double UsageRatio { get; }
    public string TitleText { get; }
    public string MetaText { get; }
    public string TotalText { get; }
    public string UsedText { get; }
    public string FreeText { get; }
    public string UsageText { get; }
}

public sealed class DashboardPathRowViewModel
{
    public DashboardPathRowViewModel(DashboardPathEntry entry)
    {
        Name = entry.Name;
        FullPath = entry.FullPath;
        SizeBytes = entry.SizeBytes;
        IsDirectory = entry.IsDirectory;
        SizeText = ByteSizeConverter.Format(entry.SizeBytes, CultureInfo.CurrentCulture);
    }

    public string Name { get; }
    public string FullPath { get; }
    public long SizeBytes { get; }
    public bool IsDirectory { get; }
    public string SizeText { get; }
}

public sealed class DashboardCategoryRowViewModel
{
    public DashboardCategoryRowViewModel(DashboardCategorySummary summary, ILocalizationService localization)
    {
        Category = summary.Category;
        Name = localization.GetString($"FileTypes_Category_{summary.Category}");
        SizeText = ByteSizeConverter.Format(summary.TotalSizeBytes, localization.CurrentCulture);
        PercentageText = PercentageConverter.Format(summary.PercentageOfTotalBytes, localization.CurrentCulture);
        UsageRatio = Math.Clamp(summary.PercentageOfTotalBytes / 100d, 0d, 1d);
        FileCountText = summary.FileCount.ToString("N0", localization.CurrentCulture);
    }

    public FileCategory Category { get; }
    public string Name { get; }
    public string SizeText { get; }
    public string PercentageText { get; }
    public double UsageRatio { get; }
    public string FileCountText { get; }
}

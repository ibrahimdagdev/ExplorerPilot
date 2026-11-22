using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplorerPilot.App.Converters;
using ExplorerPilot.App.Localization;
using ExplorerPilot.App.Services;
using ExplorerPilot.App.Settings;
using ExplorerPilot.App.Theming;
using ExplorerPilot.Core.Diagnostics;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Reporting;
using ExplorerPilot.Core.Scanning;
using ExplorerPilot.Core.Scheduling;
using ExplorerPilot.Core.Treemap;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ExplorerPilot.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable, IScheduledScanExecutor
{
    private readonly IDiskScanner _diskScanner;
    private readonly IDriveDiscoveryService _driveDiscovery;
    private readonly IScheduledScanService _scheduledScans;
    private readonly ISnapshotStore _snapshotStore;
    private readonly IThemeService _themeService;
    private readonly ILocalizationService _localization;
    private readonly IApplicationInfoService _appInfo;
    private readonly UserSettingsStore _settingsStore;
    private readonly IAboutDialogService _aboutDialog;
    private readonly ISystemInformationDialogService _systemInformationDialog;
    private readonly IFileActionService _fileActions;
    private readonly IReportExportService _reportExport;
    private readonly IReportExportOptionsDialogService _reportOptionsDialog;
    private readonly ILogger<MainViewModel> _logger;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _exportCts;
    private CancellationTokenSource? _driveDiscoveryCts;
    private bool _suppressThemePreferenceWrite;
    private bool _suppressLanguagePreferenceWrite;
    private int _driveDiscoveryVersion;
    private UiStatusKind _statusKind = UiStatusKind.Ready;
    private CompletenessKind _completenessKind = CompletenessKind.None;
    private string? _statusDetail;
    private int _statusCount;
    private ScanResult? _lastResultForLocalization;
    private IReadOnlyList<LargestFileInfo> _largestFilesSource = [];
    private IReadOnlyList<FileExtensionRowViewModel> _extensionRowsSource = [];
    private Guid _scanSessionId = Guid.Empty;
    private DateTimeOffset? _lastScanCompletedAtUtc;
    private IReadOnlyList<DriveInformation> _discoveredDrives = [];
    private readonly Dictionary<FileSystemNode, TreeNodeViewModel> _treeByNode = new(NodeReferenceComparer.Instance);
    private bool _suppressExplorerToTreemap;
    private bool _suppressTreemapToExplorer;
    private TreeNodeViewModel? _selectedExplorerNode;

    public MainViewModel(
        IDiskScanner diskScanner,
        IDriveDiscoveryService driveDiscovery,
        IThemeService themeService,
        ILocalizationService localization,
        IApplicationInfoService appInfo,
        IAboutDialogService aboutDialog,
        ISystemInformationDialogService systemInformationDialog,
        IFileActionService fileActions,
        IReportExportService reportExport,
        IReportExportOptionsDialogService reportOptionsDialog,
        SearchTabViewModel search,
        DuplicatesTabViewModel duplicates,
        TreemapTabViewModel treemap,
        ComparisonTabViewModel comparison,
        CleanupTabViewModel cleanup,
        TrendsTabViewModel trends,
        DashboardViewModel dashboard,
        ScheduledScansViewModel scheduledScansUi,
        IScheduledScanService scheduledScans,
        ISnapshotStore snapshotStore,
        UserSettingsStore settingsStore,
        ILogger<MainViewModel> logger)
    {
        _diskScanner = diskScanner;
        _driveDiscovery = driveDiscovery;
        _scheduledScans = scheduledScans;
        _snapshotStore = snapshotStore;
        _settingsStore = settingsStore;
        _themeService = themeService;
        _localization = localization;
        _appInfo = appInfo;
        _aboutDialog = aboutDialog;
        _systemInformationDialog = systemInformationDialog;
        _fileActions = fileActions;
        _reportExport = reportExport;
        _reportOptionsDialog = reportOptionsDialog;
        Search = search;
        Duplicates = duplicates;
        Treemap = treemap;
        Comparison = comparison;
        Cleanup = cleanup;
        Trends = trends;
        Dashboard = dashboard;
        ScheduledScans = scheduledScansUi;
        _logger = logger;
        _scheduledScans.Executor = this;

        ThemeOptions = themeService.AvailableThemes;
        LanguageOptions = localization.AvailableLanguages;
        SettingsThemeOptions = ThemeOptions.Where(t => t.Mode is AppThemeMode.Light or AppThemeMode.Dark).ToList();
        SettingsLanguageOptions = LanguageOptions
            .Where(l => l.Mode is AppLanguageMode.English or AppLanguageMode.Turkish)
            .ToList();

        LargestFilesLimitOptions = LargestFilesOptions.DisplayLimits
            .Select(limit => new LargestFilesLimitOption(limit))
            .ToList();

        _suppressThemePreferenceWrite = true;
        SelectedTheme = ThemeOptions.First(t => t.Mode == themeService.Preference);
        _suppressThemePreferenceWrite = false;

        _suppressLanguagePreferenceWrite = true;
        SelectedLanguage = LanguageOptions.First(t => t.Mode == localization.Preference);
        _suppressLanguagePreferenceWrite = false;

        SelectedLargestFilesLimit = LargestFilesLimitOptions.First(o => o.Limit == LargestFilesOptions.DefaultDisplayLimit);

        BuildSidebarNavigation();
        SyncSettingsSelectorsFromPreferences();
        _isSidebarExpanded = _settingsStore.Load().SidebarExpanded;
        SidebarWidth = _isSidebarExpanded ? ExpandedSidebarWidth : CollapsedSidebarWidth;

        _localization.LanguageChanged += OnLanguageChanged;
        _themeService.ThemeChanged += OnThemeChanged;
        Treemap.NodeSelected += OnTreemapNodeSelected;
        Dashboard.NavigateToTabRequested += OnDashboardNavigateToTab;
        Dashboard.RevealPathRequested += OnDashboardRevealPath;
        Dashboard.ScanRootRequested += OnDashboardScanRoot;
        RefreshLocalizedChrome();
        ApplyStatus(_statusKind);
        SelectedMainTabIndex = 0;
        SyncSidebarSelection(0);
        Dashboard.SetVisible(true);
    }

    partial void OnSelectedMainTabIndexChanged(int value)
    {
        Dashboard.SetVisible(value == 0);
        SyncSidebarSelection(value);
    }

    partial void OnIsSidebarExpandedChanged(bool value)
    {
        SidebarWidth = value ? ExpandedSidebarWidth : CollapsedSidebarWidth;
        try
        {
            _settingsStore.Update(s => s.SidebarExpanded = value);
        }
        catch
        {
            // preference persistence is best-effort
        }
    }

    /// <summary>Currently selected Directory Explorer node (shared with Treemap).</summary>
    public TreeNodeViewModel? SelectedExplorerNode
    {
        get => _selectedExplorerNode;
        private set
        {
            if (ReferenceEquals(_selectedExplorerNode, value))
            {
                return;
            }

            if (_selectedExplorerNode is not null)
            {
                _selectedExplorerNode.IsSelected = false;
            }

            _selectedExplorerNode = value;
            if (_selectedExplorerNode is not null)
            {
                _selectedExplorerNode.IsSelected = true;
            }

            OnPropertyChanged();
        }
    }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    /// <summary>Settings page: Light/Dark only (System preference still supported via stored settings).</summary>
    public IReadOnlyList<ThemeOption> SettingsThemeOptions { get; }

    /// <summary>Settings page: English/Turkish only.</summary>
    public IReadOnlyList<LanguageOption> SettingsLanguageOptions { get; }

    public IReadOnlyList<LargestFilesLimitOption> LargestFilesLimitOptions { get; }

    public IApplicationInfoService AppInfo => _appInfo;

    [ObservableProperty]
    private ThemeOption? _selectedSettingsTheme;

    [ObservableProperty]
    private LanguageOption? _selectedSettingsLanguage;

    public SearchTabViewModel Search { get; }

    public DuplicatesTabViewModel Duplicates { get; }

    public TreemapTabViewModel Treemap { get; }

    public ComparisonTabViewModel Comparison { get; }

    public CleanupTabViewModel Cleanup { get; }

    public TrendsTabViewModel Trends { get; }

    public DashboardViewModel Dashboard { get; }

    public ScheduledScansViewModel ScheduledScans { get; }

    public ObservableCollection<LargestFileRowViewModel> LargestFiles { get; } = [];

    public ObservableCollection<FileCategoryRowViewModel> FileCategories { get; } = [];

    public ObservableCollection<FileExtensionRowViewModel> FileExtensions { get; } = [];

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    [ObservableProperty]
    private LargestFilesLimitOption? _selectedLargestFilesLimit;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenContainingFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    private LargestFileRowViewModel? _selectedLargestFile;

    [ObservableProperty]
    private bool _hasLargestFiles;

    [ObservableProperty]
    private bool _largestFilesIncomplete;

    [ObservableProperty]
    private string _largestFilesStatusText = string.Empty;

    [ObservableProperty]
    private string _largestFilesNameHeader = string.Empty;

    [ObservableProperty]
    private string _largestFilesSizeHeader = string.Empty;

    [ObservableProperty]
    private string _largestFilesExtensionHeader = string.Empty;

    [ObservableProperty]
    private string _largestFilesModifiedHeader = string.Empty;

    [ObservableProperty]
    private string _largestFilesParentHeader = string.Empty;

    [ObservableProperty]
    private string _largestFilesPathHeader = string.Empty;

    [ObservableProperty]
    private bool _hasFileTypeStatistics;

    [ObservableProperty]
    private bool _fileTypesIncomplete;

    [ObservableProperty]
    private string _fileTypesFilter = string.Empty;

    [ObservableProperty]
    private string _fileTypesAnalyzedFilesText = string.Empty;

    [ObservableProperty]
    private string _fileTypesTotalBytesText = string.Empty;

    [ObservableProperty]
    private string _fileTypesUniqueExtensionsText = string.Empty;

    [ObservableProperty]
    private string _fileTypesExtensionHeader = string.Empty;

    [ObservableProperty]
    private string _fileTypesCategoryHeader = string.Empty;

    [ObservableProperty]
    private string _fileTypesFileCountHeader = string.Empty;

    [ObservableProperty]
    private string _fileTypesTotalSizeHeader = string.Empty;

    [ObservableProperty]
    private string _fileTypesAverageSizeHeader = string.Empty;

    [ObservableProperty]
    private string _fileTypesPercentageHeader = string.Empty;

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (_suppressThemePreferenceWrite || value is null)
        {
            return;
        }

        _themeService.SetPreference(value.Mode);
        SyncSettingsSelectorsFromPreferences();
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (_suppressLanguagePreferenceWrite || value is null)
        {
            return;
        }

        _localization.SetPreference(value.Mode);
        SyncSettingsSelectorsFromPreferences();
    }

    partial void OnSelectedSettingsThemeChanged(ThemeOption? value)
    {
        if (_suppressThemePreferenceWrite || value is null)
        {
            return;
        }

        SelectedTheme = ThemeOptions.First(t => t.Mode == value.Mode);
    }

    partial void OnSelectedSettingsLanguageChanged(LanguageOption? value)
    {
        if (_suppressLanguagePreferenceWrite || value is null)
        {
            return;
        }

        SelectedLanguage = LanguageOptions.First(l => l.Mode == value.Mode);
    }

    partial void OnSelectedLargestFilesLimitChanged(LargestFilesLimitOption? value)
    {
        if (value is null)
        {
            return;
        }

        RebuildLargestFilesView();
    }

    partial void OnFileTypesFilterChanged(string value) => ApplyFileTypesFilter();

    public ObservableCollection<DriveInfoModel> AvailableDrives { get; } = [];

    /// <summary>Multi-drive overview cards (v2.0 Phase 1).</summary>
    public ObservableCollection<DriveCardViewModel> DriveCards { get; } = [];

    public ObservableCollection<TreeNodeViewModel> TreeRoots { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanDriveCommand))]
    private DriveInfoModel? _selectedDrive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanDriveCommand))]
    private string? _selectedFolder;

    [ObservableProperty]
    private TreeNodeViewModel? _rootNode;

    /// <summary>Workspace page index (0 Dashboard … 11 Scheduled, 12 Settings) — kept in sync with TabControl.</summary>
    [ObservableProperty]
    private int _selectedMainTabIndex;

    public const int SettingsTabIndex = 12;

    [ObservableProperty]
    private bool _isSidebarExpanded = true;

    [ObservableProperty]
    private double _sidebarWidth = ExpandedSidebarWidth;

    [ObservableProperty]
    private SidebarNavItemViewModel? _selectedNavItem;

    public const double ExpandedSidebarWidth = 232;
    public const double CollapsedSidebarWidth = 64;

    public ObservableCollection<SidebarNavSectionViewModel> NavigationSections { get; } = [];

    public SidebarNavItemViewModel SettingsNavItem { get; private set; } = null!;

    public IEnumerable<SidebarNavItemViewModel> AllNavItems =>
        NavigationSections.SelectMany(s => s.Items).Append(SettingsNavItem);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanDriveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshDrivesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelExportCommand))]
    private bool _isExporting;

    [ObservableProperty]
    private string _exportProgressText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanDriveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshDrivesCommand))]
    private bool _isDiscoveringDrives;

    [ObservableProperty]
    private string _driveSelectorStatus = string.Empty;

    [ObservableProperty]
    private bool _hasDriveCards;

    [ObservableProperty]
    private ScanProgress? _scanProgress;

    [ObservableProperty]
    private string _scanStatus = string.Empty;

    [ObservableProperty]
    private ScanResult? _lastResult;

    [ObservableProperty]
    private string _driveCapacityText = "—";

    [ObservableProperty]
    private string _driveUsedText = "—";

    [ObservableProperty]
    private string _driveFreeText = "—";

    [ObservableProperty]
    private string _summaryFilesText = "—";

    [ObservableProperty]
    private string _summaryFoldersText = "—";

    [ObservableProperty]
    private string _summarySizeText = "—";

    [ObservableProperty]
    private string _summaryDurationText = "—";

    [ObservableProperty]
    private string _summaryErrorsText = "—";

    [ObservableProperty]
    private string _summaryCompletenessText = string.Empty;

    [ObservableProperty]
    private string _currentPathText = string.Empty;

    [ObservableProperty]
    private string _progressFilesText = "0";

    [ObservableProperty]
    private string _progressFoldersText = "0";

    [ObservableProperty]
    private string _progressBytesText = "0 B";

    [ObservableProperty]
    private string _progressElapsedText = "00:00:00";

    [ObservableProperty]
    private string _targetPathText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
    private bool _hasScanResults;

    [ObservableProperty]
    private double _driveUsageRatio;

    [ObservableProperty]
    private string _driveUsagePercentText = "—";

    partial void OnSelectedDriveChanged(DriveInfoModel? value)
    {
        UpdateDriveSummary(value);
        SelectedFolder = null;
        UpdateTargetPathText();
        RefreshDriveDisplayBindings();
    }

    partial void OnSelectedFolderChanged(string? value) => UpdateTargetPathText();

    partial void OnRootNodeChanged(TreeNodeViewModel? value) =>
        HasScanResults = value is not null || TreeRoots.Count > 0;

    public async Task DiscoverDrivesAsync(
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        _ = Comparison.InitializeAsync();
        _ = Trends.InitializeAsync();

        var version = Interlocked.Increment(ref _driveDiscoveryVersion);
        _driveDiscoveryCts?.Cancel();
        _driveDiscoveryCts?.Dispose();
        _driveDiscoveryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _driveDiscoveryCts.Token;

        IsDiscoveringDrives = true;
        DriveSelectorStatus = _localization.GetString("Drive_Discovering");
        status?.Report(_localization.GetString("Splash_Status_DiscoveringDrives"));

        try
        {
            var discovered = await _driveDiscovery.DiscoverAsync(token).ConfigureAwait(true);

            if (version != _driveDiscoveryVersion)
            {
                return;
            }

            AvailableDrives.Clear();
            DriveCards.Clear();
            _discoveredDrives = discovered;
            foreach (var drive in discovered)
            {
                AvailableDrives.Add(ToDriveInfoModel(drive));
                var card = new DriveCardViewModel(drive);
                card.RefreshFormatting(_localization);
                DriveCards.Add(card);
            }

            SelectedDrive = AvailableDrives.FirstOrDefault(d => d.IsReady)
                            ?? AvailableDrives.FirstOrDefault();
            HasDriveCards = DriveCards.Count > 0;
            SyncDashboardInputs();

            if (AvailableDrives.Count == 0)
            {
                DriveSelectorStatus = _localization.GetString("Drive_NoneFound");
                status?.Report(_localization.GetString("Drive_NoneFoundShort"));
                ApplyStatus(UiStatusKind.NoDrives);
            }
            else
            {
                DriveSelectorStatus = _localization.GetString("Drive_CountFormat", AvailableDrives.Count);
                status?.Report(_localization.GetString("Drive_FoundFormat", AvailableDrives.Count));
                if (_statusKind is UiStatusKind.Ready or UiStatusKind.NoDrives or UiStatusKind.DriveDiscoveryFailed)
                {
                    ApplyStatus(UiStatusKind.Ready);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            DriveSelectorStatus = _localization.GetString("Drive_DiscoveryCancelled");
            status?.Report(_localization.GetString("Drive_DiscoveryCancelled"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Drive discovery failed");
            AvailableDrives.Clear();
            DriveCards.Clear();
            _discoveredDrives = [];
            HasDriveCards = false;
            SelectedDrive = null;
            SyncDashboardInputs();
            DriveSelectorStatus = _localization.GetString("Drive_DiscoveryFailed");
            status?.Report(_localization.GetString("Drive_DiscoveryFailedShort"));
            ApplyStatus(UiStatusKind.DriveDiscoveryFailed);
        }
        finally
        {
            if (version == _driveDiscoveryVersion)
            {
                IsDiscoveringDrives = false;
                ScanCommand.NotifyCanExecuteChanged();
                ScanDriveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshDrives))]
    private async Task RefreshDrivesAsync()
    {
        await DiscoverDrivesAsync().ConfigureAwait(true);
    }

    private bool CanRefreshDrives() => !IsScanning && !IsDiscoveringDrives;

    /// <summary>
    /// Starts a single-root scan of the selected volume via the existing DiskScanner pipeline.
    /// Concurrent multi-drive scans are intentionally unsupported in Phase 1.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanScanDrive))]
    private async Task ScanDriveAsync(DriveCardViewModel? card)
    {
        if (card is null || !card.CanScan)
        {
            return;
        }

        SelectedFolder = card.RootPath;
        SelectedDrive = AvailableDrives.FirstOrDefault(d =>
                            string.Equals(d.RootPath, card.RootPath, StringComparison.OrdinalIgnoreCase))
                        ?? AvailableDrives.FirstOrDefault(d =>
                            string.Equals(d.Name, card.Name, StringComparison.OrdinalIgnoreCase));

        // Switch to Explorer so results are visible after ApplyResult.
        SelectedMainTabIndex = DashboardTabIndex.Explorer;
        await ScanAsync().ConfigureAwait(true);
    }

    private void SyncDashboardInputs()
    {
        var incomplete = LastResult is not null && !LastResult.IsComplete;
        Dashboard.BindInputs(
            _discoveredDrives,
            LastResult,
            _scanSessionId,
            incomplete,
            _lastScanCompletedAtUtc);
    }

    private void OnDashboardNavigateToTab(object? sender, int tabIndex) =>
        SelectedMainTabIndex = tabIndex;

    private void OnDashboardRevealPath(object? sender, string path) =>
        _fileActions.TryOpenContainingFolder(path, out _);

    private async void OnDashboardScanRoot(object? sender, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || IsScanning)
        {
            return;
        }

        SelectedFolder = rootPath;
        SelectedDrive = AvailableDrives.FirstOrDefault(d =>
                            string.Equals(d.RootPath, rootPath, StringComparison.OrdinalIgnoreCase))
                        ?? AvailableDrives.FirstOrDefault(d =>
                            string.Equals(d.Name, rootPath, StringComparison.OrdinalIgnoreCase));
        SelectedMainTabIndex = DashboardTabIndex.Explorer;
        await ScanAsync().ConfigureAwait(true);
    }

    private bool CanScanDrive(DriveCardViewModel? card) =>
        !IsScanning && !IsDiscoveringDrives && card is { CanScan: true };

    /// <summary>
    /// Shared exclusive scan entry used by the in-process scheduler (and concurrency-aligned with manual scans).
    /// </summary>
    public async Task<ScheduledScanRunResult> ExecuteAsync(
        ScheduledScanDefinition plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return await Application.Current.Dispatcher.InvokeAsync(
                    () => ExecuteAsync(plan, cancellationToken))
                .Task.Unwrap()
                .ConfigureAwait(true);
        }

        if (IsScanning)
        {
            return new ScheduledScanRunResult
            {
                PlanId = plan.Id,
                Outcome = ScheduledScanRunOutcome.SkippedBusy,
                ErrorMessage = "Another scan is already running."
            };
        }

        if (!ScheduledScanPathValidator.TryNormalize(plan.TargetPath, out var path, out var pathError))
        {
            return new ScheduledScanRunResult
            {
                PlanId = plan.Id,
                Outcome = ScheduledScanRunOutcome.Failed,
                ErrorMessage = pathError
            };
        }

        SelectedFolder = path;
        SelectedDrive = AvailableDrives.FirstOrDefault(d =>
            string.Equals(d.RootPath, path, StringComparison.OrdinalIgnoreCase));

        _exportCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsScanning = true;
        LastResult = null;
        _lastResultForLocalization = null;
        RootNode = null;
        TreeRoots.Clear();
        _treeByNode.Clear();
        SelectedExplorerNode = null;
        HasScanResults = false;
        _scanSessionId = Guid.Empty;
        Search.OnScanStarting();
        Duplicates.OnScanStarting();
        Treemap.OnScanStarting();
        Comparison.OnScanStarting();
        Cleanup.OnScanStarting();
        Trends.OnScanStarting();
        Dashboard.OnScanStarting();
        _lastScanCompletedAtUtc = null;
        ClearLargestFiles();
        ClearFileTypeStatistics();
        ClearSummary();
        ApplyStatus(UiStatusKind.Scanning);
        ApplyCompleteness(CompletenessKind.InProgress);

        var progress = new Progress<ScanProgress>(OnScanProgress);
        try
        {
            _logger.LogInformation(
                "Starting scheduled scan {PlanId} of {Path}",
                plan.Id,
                PathRedaction.ForLog(path));

            // ConfigureAwait(false) so StopAsync/OnExit waiting off the UI thread can complete
            // after cancel without deadlocking on a UI-bound continuation.
            var result = await _diskScanner.ScanAsync(path, progress, _scanCts.Token).ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                LastResult = result;
                _lastResultForLocalization = result;
                ApplyResult(result);
            }).ConfigureAwait(false);

            if (result.WasCancelled)
            {
                return new ScheduledScanRunResult
                {
                    PlanId = plan.Id,
                    Outcome = ScheduledScanRunOutcome.Cancelled
                };
            }

            if (result.FinalStatus == ScanPhase.Failed || !result.IsComplete)
            {
                return new ScheduledScanRunResult
                {
                    PlanId = plan.Id,
                    Outcome = result.FinalStatus == ScanPhase.Failed
                        ? ScheduledScanRunOutcome.Failed
                        : ScheduledScanRunOutcome.Incomplete,
                    ErrorMessage = result.Errors.FirstOrDefault()?.Message
                };
            }

            try
            {
                var snapshotName = string.IsNullOrWhiteSpace(plan.Name)
                    ? $"Scheduled {DateTime.Now:yyyy-MM-dd HH:mm}"
                    : $"{plan.Name} ({DateTime.Now:yyyy-MM-dd HH:mm})";
                var info = await _snapshotStore
                    .SaveAsync(result, snapshotName, cancellationToken: _scanCts?.Token ?? cancellationToken)
                    .ConfigureAwait(false);
                return new ScheduledScanRunResult
                {
                    PlanId = plan.Id,
                    Outcome = ScheduledScanRunOutcome.Succeeded,
                    SnapshotId = info.Id
                };
            }
            catch (OperationCanceledException)
            {
                return new ScheduledScanRunResult
                {
                    PlanId = plan.Id,
                    Outcome = ScheduledScanRunOutcome.Cancelled
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automatic snapshot failed for scheduled plan {PlanId}", plan.Id);
                return new ScheduledScanRunResult
                {
                    PlanId = plan.Id,
                    Outcome = ScheduledScanRunOutcome.SnapshotFailed,
                    ErrorMessage = ex.Message
                };
            }
        }
        catch (OperationCanceledException)
        {
            await RunOnUiAsync(() =>
            {
                ApplyStatus(UiStatusKind.ScanCancelled);
                ApplyCompleteness(CompletenessKind.Cancelled);
            }).ConfigureAwait(false);
            return new ScheduledScanRunResult
            {
                PlanId = plan.Id,
                Outcome = ScheduledScanRunOutcome.Cancelled
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled scan failed for {Path}", PathRedaction.ForLog(path));
            await RunOnUiAsync(() =>
            {
                ApplyStatus(UiStatusKind.ScanFailed, _localization.GetString("Error_Generic"));
                ApplyCompleteness(CompletenessKind.Failed);
            }).ConfigureAwait(false);
            return new ScheduledScanRunResult
            {
                PlanId = plan.Id,
                Outcome = ScheduledScanRunOutcome.Failed,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                IsScanning = false;
                _scanCts?.Dispose();
                _scanCts = null;
            }).ConfigureAwait(false);
        }
    }

    private static Task RunOnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
            return Task.CompletedTask;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    private static DriveInfoModel ToDriveInfoModel(DriveInformation drive) =>
        new()
        {
            Name = drive.Name,
            RootPath = drive.RootPath,
            VolumeLabel = drive.VolumeLabel,
            DriveType = drive.Kind.ToString(),
            DriveFormat = drive.FileSystem,
            TotalSizeBytes = drive.TotalSizeBytes,
            TotalFreeSpaceBytes = drive.TotalFreeSpaceBytes,
            AvailableFreeSpaceBytes = drive.AvailableFreeSpaceBytes,
            IsReady = drive.IsReady
        };

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = _localization.GetString("Dialog_BrowseFolderTitle")
        };

        if (SelectedDrive is { IsReady: true })
        {
            dialog.InitialDirectory = SelectedDrive.RootPath;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        SelectedFolder = dialog.FolderName;
        ApplyStatus(UiStatusKind.FolderSelected, SelectedFolder);
        UpdateTargetPathText();
    }

    private bool CanBrowse() => !IsScanning;

    [RelayCommand]
    private void ShowAbout()
    {
        var owner = Application.Current?.MainWindow;
        _aboutDialog.Show(owner);
    }

    [RelayCommand]
    private void ShowSystemInformation()
    {
        var owner = Application.Current?.MainWindow;
        _systemInformationDialog.Show(owner);
    }

    [RelayCommand(CanExecute = nameof(CanLargestFileAction))]
    private void OpenContainingFolder()
    {
        if (SelectedLargestFile is null)
        {
            return;
        }

        if (_fileActions.TryOpenContainingFolder(SelectedLargestFile.FullPath, out _))
        {
            return;
        }

        MessageBox.Show(
            _localization.GetString("LargestFiles_OpenFolderFailed"),
            _localization.GetString("LargestFiles_Title"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    [RelayCommand(CanExecute = nameof(CanLargestFileAction))]
    private void CopyPath()
    {
        if (SelectedLargestFile is null)
        {
            return;
        }

        if (_fileActions.TryCopyPath(SelectedLargestFile.FullPath, out _))
        {
            return;
        }

        MessageBox.Show(
            _localization.GetString("LargestFiles_CopyPathFailed"),
            _localization.GetString("LargestFiles_Title"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private bool CanLargestFileAction() => SelectedLargestFile is not null;

    [RelayCommand(CanExecute = nameof(CanExportReport))]
    private async Task ExportReportAsync()
    {
        if (LastResult is null)
        {
            return;
        }

        var owner = Application.Current?.MainWindow;
        var choices = _reportOptionsDialog.Show(owner);
        if (choices is null)
        {
            return;
        }

        var destinationPath = PromptSavePath(choices.Format);
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return;
        }

        _exportCts?.Dispose();
        _exportCts = new CancellationTokenSource();
        var sessionId = _scanSessionId;
        IsExporting = true;
        ExportProgressText = _localization.GetString("Report_ProgressPreparing");
        ApplyStatus(UiStatusKind.Exporting);

        var labels = BuildReportLabels();
        var options = new ReportExportOptions
        {
            ScanResult = LastResult,
            Format = choices.Format,
            DestinationPath = destinationPath,
            IncludeDirectoryTree = choices.IncludeDirectoryTree,
            IncludeCategories = choices.IncludeCategories,
            IncludeExtensions = choices.IncludeExtensions,
            IncludeErrors = choices.IncludeErrors,
            MaxLargestFiles = choices.MaxLargestFiles,
            MaxLargestFolders = choices.MaxLargestFolders,
            ScanSessionId = sessionId,
            Labels = labels
        };

        var progress = new Progress<ReportExportProgress>(p =>
        {
            var stage = LocalizeExportStage(p.Stage);
            ExportProgressText = _localization.GetString(
                "Report_ProgressStageFormat",
                p.Percent.ToString("0", _localization.CurrentCulture),
                stage);
            ApplyStatus(UiStatusKind.Exporting);
        });

        try
        {
            var result = await _reportExport.ExportAsync(
                options,
                progress,
                sid => sid == _scanSessionId && LastResult is not null,
                _exportCts.Token).ConfigureAwait(true);

            if (result.WasCancelled)
            {
                ApplyStatus(UiStatusKind.ExportCancelled);
                return;
            }

            if (result.WasStaleSession)
            {
                ApplyStatus(UiStatusKind.ExportStale);
                MessageBox.Show(
                    _localization.GetString("Report_StaleSession"),
                    _localization.GetString("Report_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!result.Succeeded)
            {
                ApplyStatus(UiStatusKind.ExportFailed, result.ErrorMessage);
                MessageBox.Show(
                    _localization.GetString(
                        "Report_FailedFormat",
                        Environment.NewLine,
                        result.ErrorMessage ?? _localization.GetString("Error_Generic")),
                    _localization.GetString("Report_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            ApplyStatus(UiStatusKind.ExportSucceeded, result.DestinationPath);
            MessageBox.Show(
                _localization.GetString(
                    "Report_SucceededFormat",
                    Environment.NewLine,
                    result.DestinationPath ?? destinationPath),
                _localization.GetString("Report_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        finally
        {
            IsExporting = false;
            ExportProgressText = string.Empty;
            _exportCts?.Dispose();
            _exportCts = null;
        }
    }

    private bool CanExportReport() =>
        HasScanResults && LastResult is not null && !IsScanning && !IsExporting;

    [RelayCommand(CanExecute = nameof(CanCancelExport))]
    private void CancelExport()
    {
        _exportCts?.Cancel();
        ApplyStatus(UiStatusKind.ExportCancelling);
    }

    private bool CanCancelExport() => IsExporting;

    private string? PromptSavePath(ReportFormat format)
    {
        var (filter, extension) = format switch
        {
            ReportFormat.Csv => (_localization.GetString("Report_FilterCsv"), ".csv"),
            ReportFormat.Json => (_localization.GetString("Report_FilterJson"), ".json"),
            _ => (_localization.GetString("Report_FilterHtml"), ".html")
        };

        var dialog = new SaveFileDialog
        {
            Title = _localization.GetString("Report_SaveDialogTitle"),
            AddExtension = true,
            DefaultExt = extension,
            OverwritePrompt = true,
            Filter = filter,
            FileName = "ExplorerPilot-Report" + extension
        };

        return dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName)
            ? dialog.FileName
            : null;
    }

    private string LocalizeExportStage(string stage) =>
        stage switch
        {
            "build" => _localization.GetString("Report_Stage_Build"),
            "write" => _localization.GetString("Report_Stage_Write"),
            "html-head" => _localization.GetString("Report_Stage_HtmlHead"),
            "summary" => _localization.GetString("Report_Stage_Summary"),
            "categories" => _localization.GetString("Report_Stage_Categories"),
            "largest-files" => _localization.GetString("Report_Stage_LargestFiles"),
            "largest-folders" => _localization.GetString("Report_Stage_LargestFolders"),
            "extensions" => _localization.GetString("Report_Stage_Extensions"),
            "errors" => _localization.GetString("Report_Stage_Errors"),
            "tree" => _localization.GetString("Report_Stage_Tree"),
            "serialize" => _localization.GetString("Report_Stage_Serialize"),
            "done" => _localization.GetString("Report_Stage_Done"),
            _ => stage
        };

    private ReportDisplayLabels BuildReportLabels() =>
        new()
        {
            ReportTitle = _localization.GetString("Report_Title"),
            IncompleteBanner = _localization.GetString("Report_IncompleteBanner"),
            SummarySection = _localization.GetString("Report_Section_Summary"),
            LargestFilesSection = _localization.GetString("Report_Section_LargestFiles"),
            LargestFoldersSection = _localization.GetString("Report_Section_LargestFolders"),
            CategoriesSection = _localization.GetString("Report_Section_Categories"),
            ExtensionsSection = _localization.GetString("Report_Section_Extensions"),
            ErrorsSection = _localization.GetString("Report_Section_Errors"),
            DirectoryTreeSection = _localization.GetString("Report_Section_DirectoryTree"),
            RootPath = _localization.GetString("Report_Field_RootPath"),
            GeneratedAt = _localization.GetString("Report_Field_GeneratedAt"),
            TotalSize = _localization.GetString("Report_Field_TotalSize"),
            TotalFiles = _localization.GetString("Report_Field_TotalFiles"),
            TotalFolders = _localization.GetString("Report_Field_TotalFolders"),
            Duration = _localization.GetString("Report_Field_Duration"),
            Completeness = _localization.GetString("Report_Field_Completeness"),
            ErrorCount = _localization.GetString("Report_Field_ErrorCount"),
            Name = _localization.GetString("Report_Field_Name"),
            Path = _localization.GetString("Report_Field_Path"),
            SizeBytes = _localization.GetString("Report_Field_SizeBytes"),
            Extension = _localization.GetString("Report_Field_Extension"),
            Category = _localization.GetString("Report_Field_Category"),
            FileCount = _localization.GetString("Report_Field_FileCount"),
            DirectoryCount = _localization.GetString("Report_Field_DirectoryCount"),
            Percentage = _localization.GetString("Report_Field_Percentage"),
            LastModifiedUtc = _localization.GetString("Report_Field_LastModifiedUtc"),
            ErrorType = _localization.GetString("Report_Field_ErrorType"),
            Message = _localization.GetString("Report_Field_Message"),
            ScanStatus = _localization.GetString("Report_Field_ScanStatus"),
            CompletenessComplete = _localization.GetString("Completeness_Complete"),
            CompletenessCancelled = _localization.GetString("Completeness_Cancelled"),
            CompletenessFailed = _localization.GetString("Completeness_Failed"),
            CompletenessWarnings = _localization.GetString("Completeness_Warnings"),
            CategoryNames = new Dictionary<FileCategory, string>
            {
                [FileCategory.Videos] = _localization.GetString("FileTypes_Category_Videos"),
                [FileCategory.Images] = _localization.GetString("FileTypes_Category_Images"),
                [FileCategory.Audio] = _localization.GetString("FileTypes_Category_Audio"),
                [FileCategory.Documents] = _localization.GetString("FileTypes_Category_Documents"),
                [FileCategory.Archives] = _localization.GetString("FileTypes_Category_Archives"),
                [FileCategory.Applications] = _localization.GetString("FileTypes_Category_Applications"),
                [FileCategory.Other] = _localization.GetString("FileTypes_Category_Other")
            }
        };

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        var path = ResolveScanPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            ApplyStatus(UiStatusKind.SelectTarget);
            return;
        }

        _exportCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        LastResult = null;
        _lastResultForLocalization = null;
        RootNode = null;
        TreeRoots.Clear();
        _treeByNode.Clear();
        SelectedExplorerNode = null;
        HasScanResults = false;
        _scanSessionId = Guid.Empty;
        Search.OnScanStarting();
        Duplicates.OnScanStarting();
        Treemap.OnScanStarting();
        Comparison.OnScanStarting();
        Cleanup.OnScanStarting();
        Trends.OnScanStarting();
        Dashboard.OnScanStarting();
        _lastScanCompletedAtUtc = null;
        ClearLargestFiles();
        ClearFileTypeStatistics();
        ClearSummary();
        ApplyStatus(UiStatusKind.Scanning);
        ApplyCompleteness(CompletenessKind.InProgress);

        var progress = new Progress<ScanProgress>(OnScanProgress);

        try
        {
            _logger.LogInformation("Starting scan of {Path}", PathRedaction.ForLog(path));
            var result = await _diskScanner.ScanAsync(path, progress, _scanCts.Token).ConfigureAwait(true);

            LastResult = result;
            _lastResultForLocalization = result;
            var applyWatch = Stopwatch.StartNew();
            ApplyResult(result);
            applyWatch.Stop();
            _logger.LogInformation(
                "Scan timing: ScanCore={ScanCoreMs}ms ApplyResult={ApplyMs}ms files={Files} dirs={Dirs} bytes={Bytes}",
                (long)result.Duration.TotalMilliseconds,
                applyWatch.ElapsedMilliseconds,
                result.TotalFiles,
                result.TotalDirectories,
                result.TotalBytes);
        }
        catch (OperationCanceledException)
        {
            ApplyStatus(UiStatusKind.ScanCancelled);
            ApplyCompleteness(CompletenessKind.Cancelled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan failed for {Path}", PathRedaction.ForLog(path));
            ApplyStatus(UiStatusKind.ScanFailed, _localization.GetString("Error_Generic"));
            ApplyCompleteness(CompletenessKind.Failed);
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private bool CanScan() =>
        !IsScanning
        && (!string.IsNullOrWhiteSpace(SelectedFolder)
            || SelectedDrive is { IsReady: true });

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelScan()
    {
        _ = _scheduledScans.CancelActiveAsync();
        _scanCts?.Cancel();
        ApplyStatus(UiStatusKind.Cancelling);
    }

    private bool CanCancel() => IsScanning;

    private string? ResolveScanPath()
    {
        if (!string.IsNullOrWhiteSpace(SelectedFolder))
        {
            return SelectedFolder;
        }

        return SelectedDrive is { IsReady: true } drive ? drive.RootPath : null;
    }

    private void OnScanProgress(ScanProgress progress)
    {
        ScanProgress = progress;
        CurrentPathText = progress.CurrentPath;
        var culture = _localization.CurrentCulture;
        ProgressFilesText = progress.ScannedFileCount.ToString("N0", culture);
        ProgressFoldersText = progress.ScannedDirectoryCount.ToString("N0", culture);
        ProgressBytesText = ByteSizeConverter.Format(progress.TotalBytesScanned, culture);
        ProgressElapsedText = progress.ElapsedTime.ToString(@"hh\:mm\:ss", culture);
        ApplyStatus(UiStatusKind.Scanning);
    }

    private void ApplyResult(ScanResult result)
    {
        var tree = new TreeNodeViewModel(result.RootNode, isRoot: true)
        {
            IsExpanded = true
        };

        RootNode = tree;
        TreeRoots.Clear();
        TreeRoots.Add(tree);
        RebuildTreeNodeIndex(tree);
        SelectedExplorerNode = null;
        HasScanResults = true;

        _scanSessionId = Guid.NewGuid();
        var incomplete = !result.IsComplete;
        Search.AttachScan(result.RootNode, _scanSessionId, incomplete);
        Duplicates.AttachScan(result.RootNode, _scanSessionId, incomplete);
        Treemap.AttachScan(result.RootNode, _scanSessionId, incomplete);
        Comparison.AttachScan(result, _scanSessionId, incomplete);
        Cleanup.AttachScan(result.RootNode, _scanSessionId, incomplete);

        _largestFilesSource = result.LargestFiles;
        LargestFilesIncomplete = result.WasCancelled || result.FinalStatus == ScanPhase.Failed;
        RebuildLargestFilesView();
        ApplyFileTypeStatistics(result);
        RefreshSummaryNumbers(result);

        _lastScanCompletedAtUtc = DateTimeOffset.UtcNow;
        SyncDashboardInputs();

        if (result.WasCancelled)
        {
            ApplyStatus(UiStatusKind.CancelledIncomplete);
            ApplyCompleteness(CompletenessKind.Cancelled);
        }
        else if (result.FinalStatus == ScanPhase.Failed)
        {
            ApplyStatus(UiStatusKind.Failed);
            ApplyCompleteness(CompletenessKind.Failed);
        }
        else if (result.Errors.Count > 0)
        {
            ApplyStatus(UiStatusKind.CompletedWithWarnings, count: result.Errors.Count);
            ApplyCompleteness(CompletenessKind.Warnings);
        }
        else
        {
            ApplyStatus(UiStatusKind.Complete);
            ApplyCompleteness(CompletenessKind.Complete);
        }
    }

    private void ClearLargestFiles()
    {
        _largestFilesSource = [];
        LargestFiles.Clear();
        SelectedLargestFile = null;
        HasLargestFiles = false;
        LargestFilesIncomplete = false;
        LargestFilesStatusText = string.Empty;
    }

    private void ClearFileTypeStatistics()
    {
        _extensionRowsSource = [];
        FileCategories.Clear();
        FileExtensions.Clear();
        HasFileTypeStatistics = false;
        FileTypesIncomplete = false;
        FileTypesFilter = string.Empty;
        FileTypesAnalyzedFilesText = string.Empty;
        FileTypesTotalBytesText = string.Empty;
        FileTypesUniqueExtensionsText = string.Empty;
    }

    private void ApplyFileTypeStatistics(ScanResult result)
    {
        var culture = _localization.CurrentCulture;
        var noExtension = _localization.GetString("FileTypes_UnknownExtension");
        FileTypesIncomplete = result.WasCancelled || result.FinalStatus == ScanPhase.Failed;

        FileCategories.Clear();
        foreach (var stats in result.CategoryStatistics)
        {
            var row = new FileCategoryRowViewModel(stats);
            row.RefreshFormatting(culture, ResolveCategoryName);
            FileCategories.Add(row);
        }

        _extensionRowsSource = result.ExtensionStatistics
            .Select(stats =>
            {
                var row = new FileExtensionRowViewModel(stats);
                row.RefreshFormatting(culture, noExtension, ResolveCategoryName);
                return row;
            })
            .ToList();

        ApplyFileTypesFilter();

        var analyzedFiles = result.ExtensionStatistics.Sum(s => s.FileCount);
        var analyzedBytes = result.ExtensionStatistics.Sum(s => s.TotalSizeBytes);
        FileTypesAnalyzedFilesText = analyzedFiles.ToString("N0", culture);
        FileTypesTotalBytesText = ByteSizeConverter.Format(analyzedBytes, culture);
        FileTypesUniqueExtensionsText = result.ExtensionStatistics.Count.ToString("N0", culture);
        HasFileTypeStatistics = analyzedFiles > 0 || result.ExtensionStatistics.Count > 0;
    }

    private void ApplyFileTypesFilter()
    {
        FileExtensions.Clear();
        foreach (var row in _extensionRowsSource)
        {
            if (row.MatchesFilter(FileTypesFilter))
            {
                FileExtensions.Add(row);
            }
        }
    }

    private string ResolveCategoryName(FileCategory category) =>
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

    private void RefreshFileTypeFormatting()
    {
        var culture = _localization.CurrentCulture;
        var noExtension = _localization.GetString("FileTypes_UnknownExtension");

        foreach (var row in FileCategories)
        {
            row.RefreshFormatting(culture, ResolveCategoryName);
        }

        foreach (var row in _extensionRowsSource)
        {
            row.RefreshFormatting(culture, noExtension, ResolveCategoryName);
        }

        ApplyFileTypesFilter();

        if (_lastResultForLocalization is not null)
        {
            var analyzedFiles = _lastResultForLocalization.ExtensionStatistics.Sum(s => s.FileCount);
            var analyzedBytes = _lastResultForLocalization.ExtensionStatistics.Sum(s => s.TotalSizeBytes);
            FileTypesAnalyzedFilesText = analyzedFiles.ToString("N0", culture);
            FileTypesTotalBytesText = ByteSizeConverter.Format(analyzedBytes, culture);
            FileTypesUniqueExtensionsText = _lastResultForLocalization.ExtensionStatistics.Count.ToString("N0", culture);
        }
    }

    private void RebuildLargestFilesView()
    {
        var limit = SelectedLargestFilesLimit?.Limit ?? LargestFilesOptions.DefaultDisplayLimit;
        var culture = _localization.CurrentCulture;
        var dash = _localization.GetString("EmDash");
        var previousPath = SelectedLargestFile?.FullPath;

        LargestFiles.Clear();
        foreach (var file in _largestFilesSource.Take(limit))
        {
            var row = new LargestFileRowViewModel(file);
            row.RefreshFormatting(culture, dash);
            LargestFiles.Add(row);
        }

        HasLargestFiles = LargestFiles.Count > 0;
        SelectedLargestFile = previousPath is null
            ? null
            : LargestFiles.FirstOrDefault(r => string.Equals(r.FullPath, previousPath, StringComparison.OrdinalIgnoreCase));

        RefreshLargestFilesStatusText();
    }

    private void RefreshLargestFilesStatusText()
    {
        if (!HasScanResults)
        {
            LargestFilesStatusText = string.Empty;
            return;
        }

        if (!HasLargestFiles)
        {
            LargestFilesStatusText = _localization.GetString("LargestFiles_NoFilesFound");
            return;
        }

        var shown = LargestFiles.Count;
        var tracked = _largestFilesSource.Count;
        LargestFilesStatusText = _localization.GetString("LargestFiles_ShowingFormat", shown, tracked);
    }

    private void RefreshLargestFilesFormatting()
    {
        var culture = _localization.CurrentCulture;
        var dash = _localization.GetString("EmDash");
        foreach (var row in LargestFiles)
        {
            row.RefreshFormatting(culture, dash);
        }

        foreach (var option in LargestFilesLimitOptions)
        {
            option.RefreshDisplayName(_localization);
        }

        RefreshLargestFilesStatusText();
    }

    private void RefreshSummaryNumbers(ScanResult result)
    {
        var culture = _localization.CurrentCulture;
        SummaryFilesText = result.TotalFiles.ToString("N0", culture);
        SummaryFoldersText = result.TotalDirectories.ToString("N0", culture);
        SummarySizeText = ByteSizeConverter.Format(result.TotalBytes, culture);
        SummaryDurationText = result.Duration.ToString(@"hh\:mm\:ss\.fff", culture);
        SummaryErrorsText = result.Errors.Count.ToString("N0", culture);
    }

    private void ClearSummary()
    {
        var dash = _localization.GetString("EmDash");
        SummaryFilesText = dash;
        SummaryFoldersText = dash;
        SummarySizeText = dash;
        SummaryDurationText = dash;
        SummaryErrorsText = dash;
        CurrentPathText = string.Empty;
        ProgressFilesText = 0.ToString("N0", _localization.CurrentCulture);
        ProgressFoldersText = 0.ToString("N0", _localization.CurrentCulture);
        ProgressBytesText = ByteSizeConverter.Format(0, _localization.CurrentCulture);
        ProgressElapsedText = TimeSpan.Zero.ToString(@"hh\:mm\:ss", _localization.CurrentCulture);
    }

    private void UpdateDriveSummary(DriveInfoModel? drive)
    {
        var culture = _localization.CurrentCulture;
        var dash = _localization.GetString("EmDash");

        if (drive is not { IsReady: true } || drive.TotalSizeBytes <= 0)
        {
            DriveCapacityText = dash;
            DriveUsedText = dash;
            DriveFreeText = dash;
            DriveUsageRatio = 0;
            DriveUsagePercentText = dash;
            return;
        }

        DriveCapacityText = ByteSizeConverter.Format(drive.TotalSizeBytes, culture);
        DriveUsedText = ByteSizeConverter.Format(drive.UsedSpaceBytes, culture);
        DriveFreeText = ByteSizeConverter.Format(drive.TotalFreeSpaceBytes, culture);
        DriveUsageRatio = Math.Clamp((double)drive.UsedSpaceBytes / drive.TotalSizeBytes, 0, 1);
        DriveUsagePercentText = _localization.GetString(
            "Summary_UsedPercentFormat",
            DriveUsageRatio.ToString("0.0", culture));
    }

    private void UpdateTargetPathText()
    {
        if (!string.IsNullOrWhiteSpace(SelectedFolder))
        {
            TargetPathText = SelectedFolder;
            return;
        }

        if (SelectedDrive is { IsReady: true } drive)
        {
            TargetPathText = drive.RootPath;
            return;
        }

        TargetPathText = SelectedDrive?.Name ?? _localization.GetString("Status_SelectTarget");
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshLocalizedChrome();
        ApplyStatus(_statusKind, _statusDetail, _statusCount);
        ApplyCompleteness(_completenessKind);

        if (_lastResultForLocalization is not null)
        {
            RefreshSummaryNumbers(_lastResultForLocalization);
        }

        if (ScanProgress is not null && IsScanning)
        {
            OnScanProgress(ScanProgress);
        }

        UpdateDriveSummary(SelectedDrive);
        UpdateTargetPathText();

        if (IsDiscoveringDrives)
        {
            DriveSelectorStatus = _localization.GetString("Drive_Discovering");
        }
        else if (AvailableDrives.Count == 0)
        {
            DriveSelectorStatus = _localization.GetString("Drive_NoneFound");
        }
        else
        {
            DriveSelectorStatus = _localization.GetString("Drive_CountFormat", AvailableDrives.Count);
        }

        RootNode?.NotifyLocalizationChanged();
        RefreshDriveDisplayBindings();
        foreach (var card in DriveCards)
        {
            card.RefreshFormatting(_localization);
        }

        RefreshLargestFilesFormatting();
        RefreshFileTypeFormatting();
        Search.RefreshLocalization();
        Duplicates.RefreshLocalization();
        Treemap.RefreshLocalization();
        Comparison.RefreshLocalization();
        Cleanup.RefreshLocalization();
        Trends.RefreshLocalization();
        Dashboard.RefreshLocalization();
        ScheduledScans.RefreshLocalization();
    }

    private void BuildSidebarNavigation()
    {
        NavigationSections.Clear();
        NavigationSections.Add(new SidebarNavSectionViewModel("Nav_Section_Overview",
        [
            new SidebarNavItemViewModel(0, "Dashboard_Title", SidebarNavIcons.Dashboard),
            new SidebarNavItemViewModel(1, "Drives_Title", SidebarNavIcons.Drives)
        ]));
        NavigationSections.Add(new SidebarNavSectionViewModel("Nav_Section_DiskAnalysis",
        [
            new SidebarNavItemViewModel(2, "Explorer_Title", SidebarNavIcons.Explorer),
            new SidebarNavItemViewModel(3, "LargestFiles_Title", SidebarNavIcons.LargestFiles),
            new SidebarNavItemViewModel(4, "FileTypes_Title", SidebarNavIcons.FileTypes),
            new SidebarNavItemViewModel(5, "Search_Title", SidebarNavIcons.Search),
            new SidebarNavItemViewModel(6, "Duplicates_Title", SidebarNavIcons.Duplicates),
            new SidebarNavItemViewModel(7, "Treemap_Title", SidebarNavIcons.Treemap)
        ]));
        NavigationSections.Add(new SidebarNavSectionViewModel("Nav_Section_Monitoring",
        [
            new SidebarNavItemViewModel(8, "Comparison_Title", SidebarNavIcons.Comparison),
            new SidebarNavItemViewModel(10, "Trends_Title", SidebarNavIcons.Trends),
            new SidebarNavItemViewModel(11, "Scheduled_Title", SidebarNavIcons.Scheduled)
        ]));
        NavigationSections.Add(new SidebarNavSectionViewModel("Nav_Section_Tools",
        [
            new SidebarNavItemViewModel(9, "Cleanup_Title", SidebarNavIcons.Cleanup)
        ]));
        SettingsNavItem = new SidebarNavItemViewModel(SettingsTabIndex, "Settings_Title", SidebarNavIcons.Settings);
    }

    private void SyncSettingsSelectorsFromPreferences()
    {
        _suppressThemePreferenceWrite = true;
        _suppressLanguagePreferenceWrite = true;
        try
        {
            var themeMode = _themeService.Preference is AppThemeMode.Light or AppThemeMode.Dark
                ? _themeService.Preference
                : _themeService.EffectiveTheme;
            if (themeMode is not (AppThemeMode.Light or AppThemeMode.Dark))
            {
                themeMode = AppThemeMode.Light;
            }

            SelectedSettingsTheme = SettingsThemeOptions.First(t => t.Mode == themeMode);

            var languageMode = _localization.Preference is AppLanguageMode.English or AppLanguageMode.Turkish
                ? _localization.Preference
                : _localization.EffectiveLanguage;
            if (languageMode is not (AppLanguageMode.English or AppLanguageMode.Turkish))
            {
                languageMode = AppLanguageMode.English;
            }

            SelectedSettingsLanguage = SettingsLanguageOptions.First(l => l.Mode == languageMode);
        }
        finally
        {
            _suppressThemePreferenceWrite = false;
            _suppressLanguagePreferenceWrite = false;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => SyncSettingsSelectorsFromPreferences();

    private void SyncSidebarSelection(int pageIndex)
    {
        SidebarNavItemViewModel? match = null;
        foreach (var item in AllNavItems)
        {
            var selected = item.PageIndex == pageIndex;
            item.IsSelected = selected;
            if (selected)
            {
                match = item;
            }
        }

        if (!ReferenceEquals(SelectedNavItem, match))
        {
            _suppressNavSelectionWrite = true;
            try
            {
                SelectedNavItem = match;
            }
            finally
            {
                _suppressNavSelectionWrite = false;
            }
        }
    }

    partial void OnSelectedNavItemChanged(SidebarNavItemViewModel? value)
    {
        if (_suppressNavSelectionWrite || value is null)
        {
            return;
        }

        if (SelectedMainTabIndex != value.PageIndex)
        {
            SelectedMainTabIndex = value.PageIndex;
        }
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

    [RelayCommand]
    private void NavigateToPage(int pageIndex)
    {
        if (pageIndex is >= 0 and <= SettingsTabIndex)
        {
            SelectedMainTabIndex = pageIndex;
        }
    }

    [RelayCommand]
    private void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _appInfo.RepositoryUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to open repository URL");
            MessageBox.Show(
                _localization.GetString("About_WebsiteOpenFailed"),
                _localization.GetString("Settings_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private bool _suppressNavSelectionWrite;

    private void RefreshLocalizedChrome()
    {
        // Updating DisplayName can make WPF ComboBoxes re-evaluate SelectedItem and fire
        // TwoWay writes. Suppress preference persistence for the duration of the refresh.
        _suppressThemePreferenceWrite = true;
        _suppressLanguagePreferenceWrite = true;
        try
        {
            _themeService.RefreshOptionDisplayNames(_localization.GetString);
            var theme = SelectedTheme;
            if (theme is not null)
            {
                SelectedTheme = theme;
            }

            var language = SelectedLanguage;
            if (language is not null)
            {
                SelectedLanguage = language;
            }
        }
        finally
        {
            _suppressThemePreferenceWrite = false;
            _suppressLanguagePreferenceWrite = false;
        }

        foreach (var section in NavigationSections)
        {
            section.Title = _localization.GetString(section.TitleKey);
            foreach (var item in section.Items)
            {
                item.Title = _localization.GetString(item.TitleKey);
            }
        }

        if (SettingsNavItem is not null)
        {
            SettingsNavItem.Title = _localization.GetString(SettingsNavItem.TitleKey);
        }

        SyncSettingsSelectorsFromPreferences();

        foreach (var option in LargestFilesLimitOptions)
        {
            option.RefreshDisplayName(_localization);
        }

        LargestFilesNameHeader = _localization.GetString("LargestFiles_FileName");
        LargestFilesSizeHeader = _localization.GetString("LargestFiles_FileSize");
        LargestFilesExtensionHeader = _localization.GetString("LargestFiles_Extension");
        LargestFilesModifiedHeader = _localization.GetString("LargestFiles_Modified");
        LargestFilesParentHeader = _localization.GetString("LargestFiles_ParentDirectory");
        LargestFilesPathHeader = _localization.GetString("LargestFiles_FullPath");

        FileTypesExtensionHeader = _localization.GetString("FileTypes_Extension");
        FileTypesCategoryHeader = _localization.GetString("FileTypes_Category");
        FileTypesFileCountHeader = _localization.GetString("FileTypes_FileCount");
        FileTypesTotalSizeHeader = _localization.GetString("FileTypes_TotalSize");
        FileTypesAverageSizeHeader = _localization.GetString("FileTypes_AverageSize");
        FileTypesPercentageHeader = _localization.GetString("FileTypes_Percentage");
        // Language option names are refreshed inside LocalizationService.
    }

    private void RefreshDriveDisplayBindings()
    {
        // Force ComboBox to re-query DisplayMemberPath/ItemTemplate by briefly clearing selection text path.
        // Replacing the collection reference isn't needed; converter uses LocalizationAccessor.
        var selected = SelectedDrive;
        if (selected is null)
        {
            return;
        }

        // Nudge binding refresh for selected item tooltip/text.
        OnPropertyChanged(nameof(SelectedDrive));
    }

    private void ApplyStatus(UiStatusKind kind, string? detail = null, int count = 0)
    {
        _statusKind = kind;
        _statusDetail = detail;
        _statusCount = count;

        ScanStatus = kind switch
        {
            UiStatusKind.Ready => _localization.GetString("Status_Ready"),
            UiStatusKind.Scanning => _localization.GetString("Status_Scanning"),
            UiStatusKind.Cancelling => _localization.GetString("Status_Cancelling"),
            UiStatusKind.SelectTarget => _localization.GetString("Status_SelectTarget"),
            UiStatusKind.FolderSelected => _localization.GetString("Status_FolderSelectedFormat", detail ?? string.Empty),
            UiStatusKind.ScanCancelled => _localization.GetString("Status_ScanCancelled"),
            UiStatusKind.ScanFailed => _localization.GetString("Status_ScanFailedFormat", detail ?? _localization.GetString("Error_Generic")),
            UiStatusKind.CancelledIncomplete => _localization.GetString("Status_CancelledIncomplete"),
            UiStatusKind.Failed => _localization.GetString("Status_Failed"),
            UiStatusKind.CompletedWithWarnings => _localization.GetString("Status_CompletedWithWarningsFormat", count),
            UiStatusKind.Complete => _localization.GetString("Status_Complete"),
            UiStatusKind.NoDrives => _localization.GetString("Status_NoDrivesBrowse"),
            UiStatusKind.DriveDiscoveryFailed => _localization.GetString("Status_DriveDiscoveryFailedBrowse"),
            UiStatusKind.Exporting => string.IsNullOrWhiteSpace(ExportProgressText)
                ? _localization.GetString("Status_Exporting")
                : ExportProgressText,
            UiStatusKind.ExportCancelling => _localization.GetString("Status_ExportCancelling"),
            UiStatusKind.ExportCancelled => _localization.GetString("Status_ExportCancelled"),
            UiStatusKind.ExportFailed => _localization.GetString("Status_ExportFailedFormat", detail ?? _localization.GetString("Error_Generic")),
            UiStatusKind.ExportSucceeded => _localization.GetString("Status_ExportSucceeded"),
            UiStatusKind.ExportStale => _localization.GetString("Status_ExportStale"),
            _ => _localization.GetString("Status_Ready")
        };
    }

    private void ApplyCompleteness(CompletenessKind kind)
    {
        _completenessKind = kind;
        SummaryCompletenessText = kind switch
        {
            CompletenessKind.InProgress => _localization.GetString("Completeness_InProgress"),
            CompletenessKind.Cancelled => _localization.GetString("Completeness_Cancelled"),
            CompletenessKind.Failed => _localization.GetString("Completeness_Failed"),
            CompletenessKind.Warnings => _localization.GetString("Completeness_Warnings"),
            CompletenessKind.Complete => _localization.GetString("Completeness_Complete"),
            _ => string.Empty
        };
    }

    /// <summary>Called when Directory Explorer selection changes.</summary>
    public void OnExplorerSelectionChanged(TreeNodeViewModel? node)
    {
        if (_suppressExplorerToTreemap)
        {
            return;
        }

        SelectedExplorerNode = node;
        if (node is null)
        {
            return;
        }

        ExpandAncestors(node);
        _suppressTreemapToExplorer = true;
        try
        {
            Treemap.SelectFromExplorer(node.Node, _scanSessionId);
        }
        finally
        {
            _suppressTreemapToExplorer = false;
        }
    }

    /// <summary>Requests BringIntoView for the current explorer selection (handled by the view).</summary>
    public event EventHandler? ExplorerSelectionBringIntoViewRequested;

    private void OnTreemapNodeSelected(object? sender, FileSystemNode? node)
    {
        if (_suppressTreemapToExplorer || node is null)
        {
            return;
        }

        // Ignore synthetic aggregate view roots; map real nodes (including Other members) via index.
        if (TreemapAggregateView.IsAggregateView(node))
        {
            node = node.Parent;
            if (node is null)
            {
                return;
            }
        }

        var treeNode = MaterializeTreeNode(node);
        if (treeNode is null)
        {
            return;
        }

        // Avoid redundant expand/BringIntoView storms when selection is unchanged.
        if (ReferenceEquals(SelectedExplorerNode, treeNode))
        {
            ExplorerSelectionBringIntoViewRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        _suppressExplorerToTreemap = true;
        try
        {
            ExpandAncestors(treeNode);
            SelectedExplorerNode = treeNode;
            ExplorerSelectionBringIntoViewRequested?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _suppressExplorerToTreemap = false;
        }
    }

    private void RebuildTreeNodeIndex(TreeNodeViewModel root)
    {
        _treeByNode.Clear();
        IndexMaterializedSubtree(root);
    }

    private void IndexMaterializedSubtree(TreeNodeViewModel root)
    {
        var stack = new Stack<TreeNodeViewModel>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            _treeByNode[current.Node] = current;
            if (!current.AreChildrenMaterialized)
            {
                continue;
            }

            foreach (var child in current.Children)
            {
                stack.Push(child);
            }
        }
    }

    /// <summary>
    /// Ensures TreeNodeViewModels exist along the path to <paramref name="target"/> (lazy tree).
    /// </summary>
    private TreeNodeViewModel? MaterializeTreeNode(FileSystemNode target)
    {
        if (RootNode is null)
        {
            return null;
        }

        if (TreemapAggregateView.IsAggregateView(target))
        {
            var aggregateParent = target.Parent;
            if (aggregateParent is null)
            {
                return null;
            }

            target = aggregateParent;
        }

        var chain = new List<FileSystemNode>();
        for (var current = target; current is not null; current = current.Parent)
        {
            if (TreemapAggregateView.IsAggregateView(current))
            {
                continue;
            }

            chain.Add(current);
        }

        if (chain.Count == 0)
        {
            return null;
        }

        chain.Reverse();
        if (!ReferenceEquals(RootNode.Node, chain[0]))
        {
            return null;
        }

        var vm = RootNode;
        _treeByNode[vm.Node] = vm;

        for (var i = 1; i < chain.Count; i++)
        {
            vm.EnsureChildrenMaterialized();
            foreach (var child in vm.Children)
            {
                _treeByNode[child.Node] = child;
            }

            var next = vm.Children.FirstOrDefault(c => ReferenceEquals(c.Node, chain[i]));
            if (next is null)
            {
                return null;
            }

            vm.IsExpanded = true;
            vm = next;
        }

        _treeByNode[vm.Node] = vm;
        return vm;
    }

    private void ExpandAncestors(TreeNodeViewModel selected)
    {
        MaterializeTreeNode(selected.Node);

        var chain = new List<TreeNodeViewModel>();
        for (var fs = selected.Node.Parent; fs is not null; fs = fs.Parent)
        {
            if (TreemapAggregateView.IsAggregateView(fs))
            {
                continue;
            }

            if (_treeByNode.TryGetValue(fs, out var ancestor))
            {
                chain.Add(ancestor);
            }
        }

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            chain[i].EnsureChildrenMaterialized();
            foreach (var child in chain[i].Children)
            {
                _treeByNode[child.Node] = child;
            }

            chain[i].IsExpanded = true;
        }
    }

    private sealed class NodeReferenceComparer : IEqualityComparer<FileSystemNode>
    {
        public static readonly NodeReferenceComparer Instance = new();

        public bool Equals(FileSystemNode? x, FileSystemNode? y) => ReferenceEquals(x, y);

        public int GetHashCode(FileSystemNode obj) => RuntimeHelpers.GetHashCode(obj);
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        _themeService.ThemeChanged -= OnThemeChanged;
        Treemap.NodeSelected -= OnTreemapNodeSelected;
        Dashboard.NavigateToTabRequested -= OnDashboardNavigateToTab;
        Dashboard.RevealPathRequested -= OnDashboardRevealPath;
        Dashboard.ScanRootRequested -= OnDashboardScanRoot;
        _scheduledScans.Executor = null;
        Trends.Dispose();
        Comparison.Dispose();
        Dashboard.Dispose();

        try { _scanCts?.Cancel(); } catch { /* ignore */ }
        try { _exportCts?.Cancel(); } catch { /* ignore */ }
        try { _driveDiscoveryCts?.Cancel(); } catch { /* ignore */ }
        _scanCts?.Dispose();
        _exportCts?.Dispose();
        _driveDiscoveryCts?.Dispose();
        _scanCts = null;
        _exportCts = null;
        _driveDiscoveryCts = null;
    }

    private enum UiStatusKind
    {
        Ready,
        Scanning,
        Cancelling,
        SelectTarget,
        FolderSelected,
        ScanCancelled,
        ScanFailed,
        CancelledIncomplete,
        Failed,
        CompletedWithWarnings,
        Complete,
        NoDrives,
        DriveDiscoveryFailed,
        Exporting,
        ExportCancelling,
        ExportCancelled,
        ExportFailed,
        ExportSucceeded,
        ExportStale
    }

    private enum CompletenessKind
    {
        None,
        InProgress,
        Cancelled,
        Failed,
        Warnings,
        Complete
    }
}

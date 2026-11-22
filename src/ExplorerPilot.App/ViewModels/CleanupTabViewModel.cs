using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplorerPilot.App.Converters;
using ExplorerPilot.App.Localization;
using ExplorerPilot.App.Services;
using ExplorerPilot.Core.Cleanup;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Reporting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ExplorerPilot.App.ViewModels;

public partial class CleanupTabViewModel : ObservableObject
{
    private readonly ICleanupRecommendationService _cleanupService;
    private readonly ICleanupExportService _exportService;
    private readonly IFileActionService _fileActions;
    private readonly ILocalizationService _localization;
    private readonly ILogger<CleanupTabViewModel> _logger;
    private readonly DuplicatesTabViewModel _duplicates;

    private FileSystemNode? _scanRoot;
    private Guid _scanSessionId = Guid.Empty;
    private bool _fromIncompleteScan;
    private CancellationTokenSource? _analyzeCts;
    private CancellationTokenSource? _exportCts;
    private int _analyzeVersion;
    private int _exportVersion;
    private CleanupRecommendationResult? _lastResult;
    private IReadOnlyList<CleanupCandidateRowViewModel> _allRows = [];
    private bool _suppressSelectionEvents;

    public CleanupTabViewModel(
        ICleanupRecommendationService cleanupService,
        ICleanupExportService exportService,
        IFileActionService fileActions,
        ILocalizationService localization,
        DuplicatesTabViewModel duplicates,
        ILogger<CleanupTabViewModel> logger)
    {
        _cleanupService = cleanupService;
        _exportService = exportService;
        _fileActions = fileActions;
        _localization = localization;
        _duplicates = duplicates;
        _logger = logger;

        CategoryFilterOptions =
        [
            new CleanupFilterOption<CleanupCategoryFilter>(CleanupCategoryFilter.All, string.Empty),
            new CleanupFilterOption<CleanupCategoryFilter>(CleanupCategoryFilter.LargeFiles, string.Empty),
            new CleanupFilterOption<CleanupCategoryFilter>(CleanupCategoryFilter.OldFiles, string.Empty),
            new CleanupFilterOption<CleanupCategoryFilter>(CleanupCategoryFilter.TemporaryFiles, string.Empty),
            new CleanupFilterOption<CleanupCategoryFilter>(CleanupCategoryFilter.OldLogFiles, string.Empty),
            new CleanupFilterOption<CleanupCategoryFilter>(CleanupCategoryFilter.EmptyFolders, string.Empty),
            new CleanupFilterOption<CleanupCategoryFilter>(CleanupCategoryFilter.VerifiedDuplicates, string.Empty)
        ];

        RiskFilterOptions =
        [
            new CleanupFilterOption<CleanupRiskFilter>(CleanupRiskFilter.All, string.Empty),
            new CleanupFilterOption<CleanupRiskFilter>(CleanupRiskFilter.Low, string.Empty),
            new CleanupFilterOption<CleanupRiskFilter>(CleanupRiskFilter.Medium, string.Empty),
            new CleanupFilterOption<CleanupRiskFilter>(CleanupRiskFilter.High, string.Empty)
        ];

        SortOptions =
        [
            new CleanupFilterOption<CleanupSortMode>(CleanupSortMode.PotentialDesc, string.Empty),
            new CleanupFilterOption<CleanupSortMode>(CleanupSortMode.SizeDesc, string.Empty),
            new CleanupFilterOption<CleanupSortMode>(CleanupSortMode.ModifiedDesc, string.Empty),
            new CleanupFilterOption<CleanupSortMode>(CleanupSortMode.RiskDesc, string.Empty),
            new CleanupFilterOption<CleanupSortMode>(CleanupSortMode.NameAsc, string.Empty)
        ];

        SelectedCategoryFilter = CategoryFilterOptions[0];
        SelectedRiskFilter = RiskFilterOptions[0];
        SelectedSort = SortOptions[0];
        LargeFileMinMbText = "100";
        OldFileDaysText = "365";
        RefreshLocalizedChrome();
    }

    public ObservableCollection<CleanupCandidateRowViewModel> VisibleCandidates { get; } = [];

    public IReadOnlyList<CleanupFilterOption<CleanupCategoryFilter>> CategoryFilterOptions { get; }

    public IReadOnlyList<CleanupFilterOption<CleanupRiskFilter>> RiskFilterOptions { get; }

    public IReadOnlyList<CleanupFilterOption<CleanupSortMode>> SortOptions { get; }

    [ObservableProperty]
    private CleanupFilterOption<CleanupCategoryFilter>? _selectedCategoryFilter;

    [ObservableProperty]
    private CleanupFilterOption<CleanupRiskFilter>? _selectedRiskFilter;

    [ObservableProperty]
    private CleanupFilterOption<CleanupSortMode>? _selectedSort;

    [ObservableProperty]
    private string _largeFileMinMbText = "100";

    [ObservableProperty]
    private string _oldFileDaysText = "365";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelAnalyzeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool _isAnalyzing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelExportCommand))]
    private bool _isExporting;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    private bool _hasScanRoot;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool _hasResults;

    [ObservableProperty]
    private bool _resultsStale;

    [ObservableProperty]
    private bool _fromIncompleteScanVisible;

    [ObservableProperty]
    private bool _resultsTruncatedVisible;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _potentialSavingsText = "—";

    [ObservableProperty]
    private string _uniquePathsText = "—";

    [ObservableProperty]
    private string _candidateCountText = "—";

    [ObservableProperty]
    private string _selectedSavingsText = "—";

    [ObservableProperty]
    private string _selectedSizeText = "—";

    [ObservableProperty]
    private string _selectedCountText = "—";

    [ObservableProperty]
    private string _selectedRiskSummaryText = "—";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenContainingFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    private CleanupCandidateRowViewModel? _selectedCandidate;

    public void AttachScan(FileSystemNode? root, Guid scanSessionId, bool incomplete)
    {
        _scanRoot = root;
        _scanSessionId = scanSessionId;
        _fromIncompleteScan = incomplete;
        HasScanRoot = root is not null;
        FromIncompleteScanVisible = incomplete && root is not null;

        if (root is null)
        {
            CancelActiveAnalysis();
            CancelActiveExport();
            ClearResults(markStale: false);
            ResultsStale = false;
            StatusText = _localization.GetString("Cleanup_NoScan");
            return;
        }

        if (_lastResult is not null && _lastResult.ScanSessionId != scanSessionId)
        {
            ResultsStale = true;
            StatusText = _localization.GetString("Cleanup_ResultsStale");
            return;
        }

        ResultsStale = false;
        StatusText = _localization.GetString("Cleanup_StatusReady");
    }

    public void OnScanStarting()
    {
        CancelActiveAnalysis();
        CancelActiveExport();
        _scanRoot = null;
        _scanSessionId = Guid.Empty;
        _fromIncompleteScan = false;
        HasScanRoot = false;
        FromIncompleteScanVisible = false;
        ResultsStale = VisibleCandidates.Count > 0 || _allRows.Count > 0;
        if (ResultsStale)
        {
            StatusText = _localization.GetString("Cleanup_ResultsStale");
        }
    }

    public void RefreshLocalization()
    {
        RefreshLocalizedChrome();
        if (_lastResult is not null && !ResultsStale)
        {
            RebuildRows(_lastResult);
            ApplyFiltersAndSort();
            StatusText = BuildCompletionStatus(_lastResult);
        }
        else if (ResultsStale)
        {
            StatusText = _localization.GetString("Cleanup_ResultsStale");
        }
        else if (!HasScanRoot)
        {
            StatusText = _localization.GetString("Cleanup_NoScan");
        }
    }

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        if (_scanRoot is null || _scanSessionId == Guid.Empty)
        {
            StatusText = _localization.GetString("Cleanup_NoScan");
            return;
        }

        if (!TryBuildCriteria(out var criteria, out var errorKey))
        {
            StatusText = _localization.GetString(errorKey);
            return;
        }

        CancelActiveAnalysis();
        _analyzeCts = new CancellationTokenSource();
        var version = Interlocked.Increment(ref _analyzeVersion);
        var token = _analyzeCts.Token;
        var session = _scanSessionId;
        var root = _scanRoot;
        var incomplete = _fromIncompleteScan;

        IsAnalyzing = true;
        ProgressText = string.Empty;
        StatusText = _localization.GetString("Cleanup_ProgressAnalyzing");

        IReadOnlyList<DuplicateGroup>? duplicateGroups = null;
        if (_duplicates.TryGetVerifiedGroups(session, out var groups))
        {
            duplicateGroups = groups;
        }

        try
        {
            var result = await _cleanupService.AnalyzeAsync(
                    root,
                    criteria,
                    duplicateGroups,
                    incomplete,
                    new Progress<CleanupRecommendationProgress>(p =>
                    {
                        if (version != _analyzeVersion)
                        {
                            return;
                        }

                        ProgressText = _localization.GetString(
                            "Cleanup_ProgressPercentFormat",
                            (p.Fraction * 100).ToString("0", _localization.CurrentCulture),
                            p.CandidatesFound);
                    }),
                    token)
                .ConfigureAwait(true);

            if (version != _analyzeVersion || session != _scanSessionId)
            {
                return;
            }

            _lastResult = result;
            ResultsStale = false;
            RebuildRows(result);
            ApplyFiltersAndSort();
            StatusText = BuildCompletionStatus(result);
            if (result.FromIncompleteScan)
            {
                StatusText += " " + _localization.GetString("Cleanup_IncompleteWarning");
            }

            if (duplicateGroups is null)
            {
                StatusText += " " + _localization.GetString("Cleanup_DuplicatesNotAvailable");
            }
        }
        catch (OperationCanceledException)
        {
            if (version == _analyzeVersion)
            {
                StatusText = _localization.GetString("Cleanup_Cancelled");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cleanup analysis failed");
            if (version == _analyzeVersion)
            {
                StatusText = _localization.GetString("Cleanup_FailedFormat", ex.Message);
                MessageBox.Show(
                    StatusText,
                    _localization.GetString("Cleanup_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            if (version == _analyzeVersion)
            {
                IsAnalyzing = false;
                ProgressText = string.Empty;
            }
        }
    }

    private bool CanAnalyze() => HasScanRoot && !IsAnalyzing && _scanRoot is not null;

    [RelayCommand(CanExecute = nameof(CanCancelAnalyze))]
    private void CancelAnalyze() => CancelActiveAnalysis();

    private bool CanCancelAnalyze() => IsAnalyzing;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (_lastResult is null || ResultsStale)
        {
            StatusText = _localization.GetString("Cleanup_ResultsStale");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = _localization.GetString("Cleanup_ExportDialogTitle"),
            Filter = _localization.GetString("Cleanup_ExportFileFilter"),
            AddExtension = true,
            OverwritePrompt = true,
            FileName = "cleanup-preview"
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        var format = ResolveFormat(dialog.FileName, dialog.FilterIndex);
        var destination = dialog.FileName;
        var session = _scanSessionId;
        var resultSnapshot = _lastResult;
        var rows = GetExportCandidates();

        CancelActiveExport();
        _exportCts = new CancellationTokenSource();
        var version = Interlocked.Increment(ref _exportVersion);
        var token = _exportCts.Token;
        IsExporting = true;
        StatusText = _localization.GetString("Cleanup_ProgressExporting");

        try
        {
            var exportResult = await _exportService.ExportAsync(
                    new CleanupExportOptions
                    {
                        Candidates = rows,
                        Format = format,
                        DestinationPath = destination,
                        ScanSessionId = session,
                        FromIncompleteScan = resultSnapshot.FromIncompleteScan || _fromIncompleteScan,
                        ResultsTruncated = resultSnapshot.ResultsTruncated,
                        UniquePotentialSavingsBytes = CleanupSavingsAggregator.SumUniquePotential(rows),
                        UniquePathCount = CleanupSavingsAggregator.CountUniquePaths(rows),
                        Labels = BuildExportLabels()
                    },
                    new Progress<ReportExportProgress>(p =>
                    {
                        if (version != _exportVersion)
                        {
                            return;
                        }

                        ProgressText = _localization.GetString(
                            "Cleanup_ExportProgressFormat",
                            p.Percent.ToString("0", _localization.CurrentCulture));
                    }),
                    id => id == _scanSessionId && !ResultsStale,
                    token)
                .ConfigureAwait(true);

            if (version != _exportVersion)
            {
                return;
            }

            if (exportResult.WasCancelled)
            {
                StatusText = _localization.GetString("Cleanup_ExportCancelled");
            }
            else if (exportResult.WasStaleSession)
            {
                StatusText = _localization.GetString("Cleanup_ResultsStale");
            }
            else if (!exportResult.Succeeded)
            {
                StatusText = _localization.GetString("Cleanup_ExportFailedFormat", exportResult.ErrorMessage ?? string.Empty);
            }
            else
            {
                StatusText = _localization.GetString("Cleanup_ExportedFormat", exportResult.DestinationPath ?? destination);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cleanup export failed");
            if (version == _exportVersion)
            {
                StatusText = _localization.GetString("Cleanup_ExportFailedFormat", ex.Message);
            }
        }
        finally
        {
            if (version == _exportVersion)
            {
                IsExporting = false;
                ProgressText = string.Empty;
            }
        }
    }

    private bool CanExport() =>
        HasResults && _lastResult is not null && !ResultsStale && !IsAnalyzing && !IsExporting;

    [RelayCommand(CanExecute = nameof(CanCancelExport))]
    private void CancelExport() => CancelActiveExport();

    private bool CanCancelExport() => IsExporting;

    [RelayCommand]
    private void SelectAllVisible()
    {
        _suppressSelectionEvents = true;
        try
        {
            foreach (var row in VisibleCandidates)
            {
                row.IsSelected = true;
            }
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        RecalculateSelectionTotals();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        _suppressSelectionEvents = true;
        try
        {
            foreach (var row in _allRows)
            {
                row.IsSelected = false;
            }
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        RecalculateSelectionTotals();
    }

    [RelayCommand(CanExecute = nameof(CanOpenOrCopy))]
    private void OpenContainingFolder()
    {
        if (SelectedCandidate is null)
        {
            return;
        }

        if (!_fileActions.TryOpenContainingFolder(SelectedCandidate.FullPath, out _))
        {
            StatusText = _localization.GetString("Cleanup_OpenFolderFailed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenOrCopy))]
    private void CopyPath()
    {
        if (SelectedCandidate is null)
        {
            return;
        }

        if (_fileActions.TryCopyPath(SelectedCandidate.FullPath, out _))
        {
            StatusText = _localization.GetString("Cleanup_PathCopied");
        }
        else
        {
            StatusText = _localization.GetString("Cleanup_CopyPathFailed");
        }
    }

    private bool CanOpenOrCopy() => SelectedCandidate is not null;

    private bool TryBuildCriteria(out CleanupRecommendationCriteria criteria, out string errorKey)
    {
        criteria = null!;
        errorKey = string.Empty;

        if (!long.TryParse(LargeFileMinMbText.Trim(), NumberStyles.Integer, _localization.CurrentCulture, out var mb)
            && !long.TryParse(LargeFileMinMbText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out mb))
        {
            errorKey = "Cleanup_InvalidLargeFileThreshold";
            return false;
        }

        if (mb < 1 || mb > 1024 * 1024)
        {
            errorKey = "Cleanup_InvalidLargeFileThreshold";
            return false;
        }

        if (!int.TryParse(OldFileDaysText.Trim(), NumberStyles.Integer, _localization.CurrentCulture, out var days)
            && !int.TryParse(OldFileDaysText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out days))
        {
            errorKey = "Cleanup_InvalidOldFileDays";
            return false;
        }

        if (days < 1 || days > 36500)
        {
            errorKey = "Cleanup_InvalidOldFileDays";
            return false;
        }

        criteria = new CleanupRecommendationCriteria
        {
            ScanSessionId = _scanSessionId,
            LargeFileMinBytes = mb * 1024L * 1024L,
            OldFileMinAge = TimeSpan.FromDays(days),
            OldLogMinAge = TimeSpan.FromDays(Math.Min(days, 90)),
            MaxRecommendations = 5_000,
            IncludeEmptyFolders = true,
            IncludeVerifiedDuplicates = true,
            UtcNow = DateTimeOffset.UtcNow
        };
        return true;
    }

    private void RebuildRows(CleanupRecommendationResult result)
    {
        foreach (var row in _allRows)
        {
            row.SelectionChanged -= OnRowSelectionChanged;
        }

        var culture = _localization.CurrentCulture;
        var rows = new List<CleanupCandidateRowViewModel>(result.Candidates.Count);
        foreach (var candidate in result.Candidates)
        {
            var row = new CleanupCandidateRowViewModel(
                candidate,
                CategoryLabel(candidate.Category),
                RiskLabel(candidate.Risk),
                ByteSizeConverter.Format(candidate.SizeBytes, culture),
                ByteSizeConverter.Format(candidate.PotentialSavingsBytes, culture),
                candidate.LastModified?.ToLocalTime().ToString("g", culture) ?? "—",
                _localization.GetString(candidate.ReasonKey));
            row.SelectionChanged += OnRowSelectionChanged;
            rows.Add(row);
        }

        _allRows = rows;
        PotentialSavingsText = ByteSizeConverter.Format(result.UniquePotentialSavingsBytes, culture);
        UniquePathsText = result.UniquePathCount.ToString("N0", culture);
        CandidateCountText = result.Candidates.Count.ToString("N0", culture);
        ResultsTruncatedVisible = result.ResultsTruncated;
        FromIncompleteScanVisible = result.FromIncompleteScan || _fromIncompleteScan;
        RecalculateSelectionTotals();
    }

    private void ApplyFiltersAndSort()
    {
        var category = SelectedCategoryFilter?.Value ?? CleanupCategoryFilter.All;
        var risk = SelectedRiskFilter?.Value ?? CleanupRiskFilter.All;
        var sort = SelectedSort?.Value ?? CleanupSortMode.PotentialDesc;

        IEnumerable<CleanupCandidateRowViewModel> query = _allRows;
        if (category != CleanupCategoryFilter.All)
        {
            query = query.Where(r => r.Category == (CleanupCategory)category);
        }

        if (risk != CleanupRiskFilter.All)
        {
            query = query.Where(r => r.Risk == (CleanupRiskLevel)risk);
        }

        query = sort switch
        {
            CleanupSortMode.SizeDesc => query
                .OrderByDescending(r => r.SizeBytes)
                .ThenBy(r => r.FullPath, StringComparer.OrdinalIgnoreCase),
            CleanupSortMode.ModifiedDesc => query
                .OrderByDescending(r => r.Candidate.LastModified ?? DateTimeOffset.MinValue)
                .ThenByDescending(r => r.PotentialSavingsBytes),
            CleanupSortMode.RiskDesc => query
                .OrderByDescending(r => r.Risk)
                .ThenByDescending(r => r.PotentialSavingsBytes),
            CleanupSortMode.NameAsc => query
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.FullPath, StringComparer.OrdinalIgnoreCase),
            _ => query
                .OrderByDescending(r => r.PotentialSavingsBytes)
                .ThenByDescending(r => r.SizeBytes)
                .ThenBy(r => r.FullPath, StringComparer.OrdinalIgnoreCase)
        };

        VisibleCandidates.Clear();
        foreach (var row in query)
        {
            VisibleCandidates.Add(row);
        }

        HasResults = VisibleCandidates.Count > 0 || _allRows.Count > 0;
        RecalculateSelectionTotals();
    }

    private void OnRowSelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressSelectionEvents)
        {
            return;
        }

        RecalculateSelectionTotals();
    }

    private void RecalculateSelectionTotals()
    {
        var selected = _allRows.Where(r => r.IsSelected).Select(r => r.Candidate).ToList();
        var culture = _localization.CurrentCulture;
        SelectedSavingsText = ByteSizeConverter.Format(CleanupSavingsAggregator.SumUniquePotential(selected), culture);
        SelectedSizeText = ByteSizeConverter.Format(CleanupSavingsAggregator.SumUniqueSize(selected), culture);
        SelectedCountText = CleanupSavingsAggregator.CountUniquePaths(selected).ToString("N0", culture);
        var (low, medium, high) = CleanupSavingsAggregator.CountUniquePathsByHighestRisk(selected);
        SelectedRiskSummaryText = selected.Count == 0
            ? "—"
            : _localization.GetString(
                "Cleanup_SelectedRiskFormat",
                low.ToString("N0", culture),
                medium.ToString("N0", culture),
                high.ToString("N0", culture));
    }

    private IReadOnlyList<CleanupCandidate> GetExportCandidates()
    {
        var selected = _allRows.Where(r => r.IsSelected).Select(r => r.Candidate).ToList();
        if (selected.Count > 0)
        {
            return selected;
        }

        return VisibleCandidates.Select(r => r.Candidate).ToList();
    }

    private CleanupExportLabels BuildExportLabels() =>
        new()
        {
            Title = _localization.GetString("Cleanup_ExportTitle"),
            Disclaimer = _localization.GetString("Cleanup_ExportDisclaimer"),
            IncompleteBanner = _localization.GetString("Cleanup_IncompleteWarning"),
            TruncatedBanner = _localization.GetString("Cleanup_TruncatedWarning"),
            GeneratedAt = _localization.GetString("Cleanup_ExportGeneratedAt"),
            UniquePotential = _localization.GetString("Cleanup_Card_Potential"),
            UniquePaths = _localization.GetString("Cleanup_Card_UniquePaths"),
            CandidateCount = _localization.GetString("Cleanup_Card_Candidates"),
            Name = _localization.GetString("Cleanup_ColumnName"),
            Path = _localization.GetString("Cleanup_ColumnPath"),
            SizeBytes = _localization.GetString("Cleanup_ColumnSize"),
            PotentialSavingsBytes = _localization.GetString("Cleanup_ColumnSavings"),
            LastModifiedUtc = _localization.GetString("Cleanup_ColumnModified"),
            Category = _localization.GetString("Cleanup_ColumnCategory"),
            Risk = _localization.GetString("Cleanup_ColumnRisk"),
            Reason = _localization.GetString("Cleanup_ColumnReason"),
            IsDirectory = _localization.GetString("Cleanup_ColumnIsDirectory"),
            CategoryFormatter = CategoryLabel,
            RiskFormatter = RiskLabel,
            ReasonFormatter = c => _localization.GetString(c.ReasonKey)
        };

    private static ReportFormat ResolveFormat(string path, int filterIndex)
    {
        var ext = System.IO.Path.GetExtension(path);
        if (ext.Equals(".html", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".htm", StringComparison.OrdinalIgnoreCase))
        {
            return ReportFormat.Html;
        }

        if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return ReportFormat.Json;
        }

        if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return ReportFormat.Csv;
        }

        return filterIndex switch
        {
            2 => ReportFormat.Html,
            3 => ReportFormat.Json,
            _ => ReportFormat.Csv
        };
    }

    private void ClearResults(bool markStale)
    {
        foreach (var row in _allRows)
        {
            row.SelectionChanged -= OnRowSelectionChanged;
        }

        _allRows = [];
        VisibleCandidates.Clear();
        _lastResult = null;
        HasResults = false;
        ResultsTruncatedVisible = false;
        PotentialSavingsText = "—";
        UniquePathsText = "—";
        CandidateCountText = "—";
        SelectedSavingsText = "—";
        SelectedSizeText = "—";
        SelectedCountText = "—";
        SelectedRiskSummaryText = "—";
        SelectedCandidate = null;
        if (markStale)
        {
            ResultsStale = true;
        }
    }

    private void CancelActiveAnalysis()
    {
        try
        {
            _analyzeCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        Interlocked.Increment(ref _analyzeVersion);
    }

    private void CancelActiveExport()
    {
        try
        {
            _exportCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        Interlocked.Increment(ref _exportVersion);
    }

    private string BuildCompletionStatus(CleanupRecommendationResult result)
    {
        var culture = _localization.CurrentCulture;
        return _localization.GetString(
            "Cleanup_CompletedFormat",
            result.Candidates.Count.ToString("N0", culture),
            result.UniquePathCount.ToString("N0", culture),
            ByteSizeConverter.Format(result.UniquePotentialSavingsBytes, culture));
    }

    private string CategoryLabel(CleanupCategory category) =>
        category switch
        {
            CleanupCategory.LargeFiles => _localization.GetString("Cleanup_Category_LargeFiles"),
            CleanupCategory.OldFiles => _localization.GetString("Cleanup_Category_OldFiles"),
            CleanupCategory.TemporaryFiles => _localization.GetString("Cleanup_Category_Temporary"),
            CleanupCategory.OldLogFiles => _localization.GetString("Cleanup_Category_OldLogs"),
            CleanupCategory.EmptyFolders => _localization.GetString("Cleanup_Category_EmptyFolders"),
            CleanupCategory.VerifiedDuplicates => _localization.GetString("Cleanup_Category_Duplicates"),
            _ => category.ToString()
        };

    private string RiskLabel(CleanupRiskLevel risk) =>
        risk switch
        {
            CleanupRiskLevel.Low => _localization.GetString("Cleanup_Risk_Low"),
            CleanupRiskLevel.Medium => _localization.GetString("Cleanup_Risk_Medium"),
            CleanupRiskLevel.High => _localization.GetString("Cleanup_Risk_High"),
            _ => risk.ToString()
        };

    private void RefreshLocalizedChrome()
    {
        CategoryFilterOptions[0].DisplayName = _localization.GetString("Cleanup_Filter_AllCategories");
        CategoryFilterOptions[1].DisplayName = _localization.GetString("Cleanup_Category_LargeFiles");
        CategoryFilterOptions[2].DisplayName = _localization.GetString("Cleanup_Category_OldFiles");
        CategoryFilterOptions[3].DisplayName = _localization.GetString("Cleanup_Category_Temporary");
        CategoryFilterOptions[4].DisplayName = _localization.GetString("Cleanup_Category_OldLogs");
        CategoryFilterOptions[5].DisplayName = _localization.GetString("Cleanup_Category_EmptyFolders");
        CategoryFilterOptions[6].DisplayName = _localization.GetString("Cleanup_Category_Duplicates");

        RiskFilterOptions[0].DisplayName = _localization.GetString("Cleanup_Filter_AllRisks");
        RiskFilterOptions[1].DisplayName = _localization.GetString("Cleanup_Risk_Low");
        RiskFilterOptions[2].DisplayName = _localization.GetString("Cleanup_Risk_Medium");
        RiskFilterOptions[3].DisplayName = _localization.GetString("Cleanup_Risk_High");

        SortOptions[0].DisplayName = _localization.GetString("Cleanup_Sort_Potential");
        SortOptions[1].DisplayName = _localization.GetString("Cleanup_Sort_Size");
        SortOptions[2].DisplayName = _localization.GetString("Cleanup_Sort_Modified");
        SortOptions[3].DisplayName = _localization.GetString("Cleanup_Sort_Risk");
        SortOptions[4].DisplayName = _localization.GetString("Cleanup_Sort_Name");

        if (!IsAnalyzing && !HasResults && !ResultsStale)
        {
            StatusText = HasScanRoot
                ? _localization.GetString("Cleanup_StatusReady")
                : _localization.GetString("Cleanup_NoScan");
        }
    }

    partial void OnSelectedCategoryFilterChanged(CleanupFilterOption<CleanupCategoryFilter>? value)
    {
        if (_allRows.Count > 0)
        {
            ApplyFiltersAndSort();
        }
    }

    partial void OnSelectedRiskFilterChanged(CleanupFilterOption<CleanupRiskFilter>? value)
    {
        if (_allRows.Count > 0)
        {
            ApplyFiltersAndSort();
        }
    }

    partial void OnSelectedSortChanged(CleanupFilterOption<CleanupSortMode>? value)
    {
        if (_allRows.Count > 0)
        {
            ApplyFiltersAndSort();
        }
    }
}

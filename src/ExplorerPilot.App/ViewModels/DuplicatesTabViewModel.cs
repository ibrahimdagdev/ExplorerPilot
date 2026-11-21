using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplorerPilot.App.Converters;
using ExplorerPilot.App.Localization;
using ExplorerPilot.App.Services;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Scanning;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.App.ViewModels;

public partial class DuplicatesTabViewModel : ObservableObject
{
    private readonly IDuplicateFileFinderService _finder;
    private readonly IFileActionService _fileActions;
    private readonly ILocalizationService _localization;
    private readonly ILogger<DuplicatesTabViewModel> _logger;

    private FileSystemNode? _scanRoot;
    private Guid _scanSessionId = Guid.Empty;
    private bool _fromIncompleteScan;
    private CancellationTokenSource? _analysisCts;
    private int _analysisVersion;
    private DuplicateAnalysisResult? _lastResult;
    private IReadOnlyList<DuplicateGroup> _sourceGroups = [];

    public DuplicatesTabViewModel(
        IDuplicateFileFinderService finder,
        IFileActionService fileActions,
        ILocalizationService localization,
        ILogger<DuplicatesTabViewModel> logger)
    {
        _finder = finder;
        _fileActions = fileActions;
        _localization = localization;
        _logger = logger;

        SizeUnitOptions =
        [
            new SearchOptionItem<FileSizeUnit>(FileSizeUnit.KB),
            new SearchOptionItem<FileSizeUnit>(FileSizeUnit.MB),
            new SearchOptionItem<FileSizeUnit>(FileSizeUnit.GB),
            new SearchOptionItem<FileSizeUnit>(FileSizeUnit.TB)
        ];

        SortOptions =
        [
            new SearchOptionItem<DuplicateSortField>(DuplicateSortField.PotentialSavings),
            new SearchOptionItem<DuplicateSortField>(DuplicateSortField.FileSize),
            new SearchOptionItem<DuplicateSortField>(DuplicateSortField.DuplicateCount),
            new SearchOptionItem<DuplicateSortField>(DuplicateSortField.FileName)
        ];

        SelectedSizeUnit = SizeUnitOptions[1];
        SelectedSort = SortOptions[0];
        RefreshLocalizedChrome();
    }

    public ObservableCollection<DuplicateGroupRowViewModel> Groups { get; } = [];

    public IReadOnlyList<SearchOptionItem<FileSizeUnit>> SizeUnitOptions { get; }

    public IReadOnlyList<SearchOptionItem<DuplicateSortField>> SortOptions { get; }

    [ObservableProperty]
    private string _nameFilter = string.Empty;

    [ObservableProperty]
    private string _extensionFilter = string.Empty;

    [ObservableProperty]
    private string _minSizeText = string.Empty;

    [ObservableProperty]
    private string _minCopyCountText = "2";

    [ObservableProperty]
    private SearchOptionItem<FileSizeUnit>? _selectedSizeUnit;

    [ObservableProperty]
    private SearchOptionItem<DuplicateSortField>? _selectedSort;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelAnalyzeCommand))]
    private bool _isAnalyzing;

    [ObservableProperty]
    private bool _hasScanRoot;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private bool _resultsStale;

    [ObservableProperty]
    private bool _fromIncompleteScanVisible;

    [ObservableProperty]
    private bool _analysisIncompleteVisible;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _groupsCountText = string.Empty;

    [ObservableProperty]
    private string _filesCountText = string.Empty;

    [ObservableProperty]
    private string _savingsText = string.Empty;

    [ObservableProperty]
    private string _analyzedFilesText = string.Empty;

    [ObservableProperty]
    private string _skippedFailedText = string.Empty;

    [ObservableProperty]
    private DuplicateGroupRowViewModel? _selectedGroup;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenContainingFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    private DuplicateFileRowViewModel? _selectedFile;

    /// <summary>
    /// Exposes SHA-256–verified duplicate groups for the current scan session.
    /// Returns false when results are missing, incomplete, or stale.
    /// </summary>
    public bool TryGetVerifiedGroups(Guid scanSessionId, out IReadOnlyList<DuplicateGroup> groups)
    {
        groups = [];
        if (_lastResult is null
            || ResultsStale
            || !_lastResult.IsComplete
            || _lastResult.ScanSessionId != scanSessionId
            || _sourceGroups.Count == 0)
        {
            return false;
        }

        groups = _sourceGroups;
        return true;
    }

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
            ClearResults(markStale: false);
            ResultsStale = false;
            StatusText = string.Empty;
            return;
        }

        if (_lastResult is not null && _lastResult.ScanSessionId != scanSessionId)
        {
            ResultsStale = true;
            StatusText = _localization.GetString("Duplicates_ResultsStale");
        }
    }

    public void OnScanStarting()
    {
        CancelActiveAnalysis();
        _scanRoot = null;
        _scanSessionId = Guid.Empty;
        _fromIncompleteScan = false;
        HasScanRoot = false;
        FromIncompleteScanVisible = false;
        ResultsStale = Groups.Count > 0;
        if (ResultsStale)
        {
            StatusText = _localization.GetString("Duplicates_ResultsStale");
        }
    }

    public void RefreshLocalization()
    {
        RefreshLocalizedChrome();
        RefreshSummaryTexts();
        foreach (var group in Groups)
        {
            group.RefreshFormatting(_localization.CurrentCulture, _localization.GetString);
        }

        if (_lastResult is not null && !ResultsStale)
        {
            StatusText = BuildCompletionStatus(_lastResult);
        }
        else if (ResultsStale)
        {
            StatusText = _localization.GetString("Duplicates_ResultsStale");
        }
        else if (!HasScanRoot)
        {
            StatusText = _localization.GetString("Duplicates_NoScan");
        }
    }

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        if (_scanRoot is null || _scanSessionId == Guid.Empty)
        {
            StatusText = _localization.GetString("Duplicates_NoScan");
            return;
        }

        if (!TryBuildCriteria(out var criteria, out var errorKey))
        {
            StatusText = _localization.GetString(errorKey);
            return;
        }

        CancelActiveAnalysis();
        _analysisCts = new CancellationTokenSource();
        var version = Interlocked.Increment(ref _analysisVersion);
        var sessionAtStart = _scanSessionId;
        IsAnalyzing = true;
        AnalysisIncompleteVisible = false;
        ProgressText = string.Empty;
        StatusText = _localization.GetString("Duplicates_Analyzing");

        var progress = new Progress<DuplicateAnalysisProgress>(OnProgress);

        try
        {
            var result = await _finder.AnalyzeAsync(
                    _scanRoot,
                    criteria,
                    _fromIncompleteScan,
                    progress,
                    _analysisCts.Token)
                .ConfigureAwait(true);

            if (version != _analysisVersion)
            {
                return;
            }

            if (sessionAtStart != _scanSessionId || result.ScanSessionId != _scanSessionId)
            {
                ResultsStale = true;
                StatusText = _localization.GetString("Duplicates_ResultsStale");
                return;
            }

            ApplyResult(result);
        }
        catch (OperationCanceledException)
        {
            if (version == _analysisVersion)
            {
                StatusText = _localization.GetString("Duplicates_Cancelled");
                AnalysisIncompleteVisible = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Duplicate analysis failed");
            if (version == _analysisVersion)
            {
                StatusText = _localization.GetString("Duplicates_Failed");
                AnalysisIncompleteVisible = true;
            }
        }
        finally
        {
            if (version == _analysisVersion)
            {
                IsAnalyzing = false;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelAnalyze))]
    private void CancelAnalyze()
    {
        CancelActiveAnalysis();
        IsAnalyzing = false;
        StatusText = _localization.GetString("Duplicates_Cancelled");
        AnalysisIncompleteVisible = true;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        NameFilter = string.Empty;
        ExtensionFilter = string.Empty;
        MinSizeText = string.Empty;
        MinCopyCountText = "2";
        SelectedSizeUnit = SizeUnitOptions[1];
        SelectedSort = SortOptions[0];

        if (_sourceGroups.Count > 0 && _lastResult is { IsComplete: true } && !ResultsStale)
        {
            ApplyDisplayFilters();
        }
    }

    [RelayCommand]
    private void ApplyDisplayFilters()
    {
        if (_lastResult is null || ResultsStale)
        {
            return;
        }

        if (!TryBuildCriteria(out var criteria, out var errorKey))
        {
            StatusText = _localization.GetString(errorKey);
            return;
        }

        var filtered = FilterAndSort(_sourceGroups, criteria);
        PopulateGroups(filtered);
        RefreshSummaryFromGroups(filtered, _lastResult);
        StatusText = BuildCompletionStatus(_lastResult);
    }

    [RelayCommand(CanExecute = nameof(CanFileAction))]
    private void OpenContainingFolder()
    {
        var file = ResolveSelectedFile();
        if (file is null)
        {
            return;
        }

        if (_fileActions.TryOpenContainingFolder(file.FullPath, out _))
        {
            return;
        }

        MessageBox.Show(
            _localization.GetString("Duplicates_OpenFolderFailed"),
            _localization.GetString("Duplicates_Title"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    [RelayCommand(CanExecute = nameof(CanFileAction))]
    private void CopyPath()
    {
        var file = ResolveSelectedFile();
        if (file is null)
        {
            return;
        }

        if (_fileActions.TryCopyPath(file.FullPath, out _))
        {
            return;
        }

        MessageBox.Show(
            _localization.GetString("Duplicates_CopyPathFailed"),
            _localization.GetString("Duplicates_Title"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private bool CanAnalyze() => HasScanRoot && !IsAnalyzing;

    private bool CanCancelAnalyze() => IsAnalyzing;

    private bool CanFileAction() => ResolveSelectedFile() is not null;

    private DuplicateFileRowViewModel? ResolveSelectedFile()
    {
        if (SelectedFile is not null)
        {
            return SelectedFile;
        }

        if (SelectedGroup?.SelectedFile is not null)
        {
            return SelectedGroup.SelectedFile;
        }

        foreach (var group in Groups)
        {
            if (group.SelectedFile is not null)
            {
                return group.SelectedFile;
            }
        }

        return null;
    }

    private void ApplyResult(DuplicateAnalysisResult result)
    {
        _lastResult = result;
        _sourceGroups = result.Groups;
        ResultsStale = false;

        if (!result.IsComplete)
        {
            ClearGroupCollection();
            HasResults = false;
            AnalysisIncompleteVisible = true;
            StatusText = result.WasCancelled
                ? _localization.GetString("Duplicates_Cancelled")
                : _localization.GetString("Duplicates_Failed");
            RefreshSummaryTexts();
            return;
        }

        AnalysisIncompleteVisible = false;
        PopulateGroups(result.Groups);
        RefreshSummaryFromGroups(result.Groups, result);
        StatusText = BuildCompletionStatus(result);
        FromIncompleteScanVisible = result.FromIncompleteScan;
    }

    private void PopulateGroups(IReadOnlyList<DuplicateGroup> groups)
    {
        Groups.Clear();
        SelectedGroup = null;
        SelectedFile = null;
        var culture = _localization.CurrentCulture;
        foreach (var group in groups)
        {
            var row = new DuplicateGroupRowViewModel(group);
            row.RefreshFormatting(culture, _localization.GetString);
            Groups.Add(row);
        }

        HasResults = Groups.Count > 0;
    }

    private void RefreshSummaryFromGroups(IReadOnlyList<DuplicateGroup> groups, DuplicateAnalysisResult result)
    {
        var culture = _localization.CurrentCulture;
        GroupsCountText = groups.Count.ToString("N0", culture);
        FilesCountText = groups.Sum(g => g.DuplicateCount).ToString("N0", culture);
        SavingsText = ByteSizeConverter.Format(DuplicateSavingsCalculator.SumGroupSavings(groups), culture);
        AnalyzedFilesText = result.AnalyzedFiles.ToString("N0", culture);
        SkippedFailedText = _localization.GetString(
            "Duplicates_SkippedFailedFormat",
            result.SkippedFiles.ToString("N0", culture),
            result.FailedFiles.ToString("N0", culture));
    }

    private void RefreshSummaryTexts()
    {
        if (_lastResult is null || ResultsStale)
        {
            if (!HasResults)
            {
                GroupsCountText = "0";
                FilesCountText = "0";
                SavingsText = ByteSizeConverter.Format(0, _localization.CurrentCulture);
                AnalyzedFilesText = "0";
                SkippedFailedText = string.Empty;
            }

            return;
        }

        RefreshSummaryFromGroups(Groups.Select(g => g.Group).ToList(), _lastResult);
    }

    private string BuildCompletionStatus(DuplicateAnalysisResult result)
    {
        var culture = _localization.CurrentCulture;
        var baseText = _localization.GetString(
            "Duplicates_CompletedFormat",
            result.DuplicateGroupCount.ToString("N0", culture),
            result.DuplicateFileCount.ToString("N0", culture));

        if (result.SkippedFiles > 0 || result.FailedFiles > 0)
        {
            return baseText + " " + _localization.GetString(
                "Duplicates_SkippedFailedFormat",
                result.SkippedFiles.ToString("N0", culture),
                result.FailedFiles.ToString("N0", culture));
        }

        return baseText;
    }

    private void OnProgress(DuplicateAnalysisProgress progress)
    {
        var culture = _localization.CurrentCulture;
        ProgressText = progress.Phase switch
        {
            DuplicateAnalysisPhase.GroupingBySize => _localization.GetString("Duplicates_Phase_Grouping"),
            DuplicateAnalysisPhase.PartialHashing => _localization.GetString(
                "Duplicates_Phase_PartialFormat",
                progress.HashedFiles.ToString("N0", culture),
                progress.CandidateFiles.ToString("N0", culture)),
            DuplicateAnalysisPhase.FullHashing => _localization.GetString(
                "Duplicates_Phase_FullFormat",
                progress.HashedFiles.ToString("N0", culture),
                progress.CandidateFiles.ToString("N0", culture)),
            DuplicateAnalysisPhase.Finalizing => _localization.GetString("Duplicates_Phase_Finalizing"),
            _ => progress.CurrentPath ?? string.Empty
        };

        if (!string.IsNullOrWhiteSpace(progress.CurrentPath))
        {
            ProgressText = ProgressText + " — " + progress.CurrentPath;
        }
    }

    private bool TryBuildCriteria(out DuplicateAnalysisCriteria criteria, out string errorKey)
    {
        criteria = null!;
        errorKey = string.Empty;

        long? minBytes = null;
        if (!string.IsNullOrWhiteSpace(MinSizeText))
        {
            if (!double.TryParse(MinSizeText, NumberStyles.Number, _localization.CurrentCulture, out var amount)
                || amount < 0)
            {
                errorKey = "Duplicates_InvalidSize";
                return false;
            }

            var unit = SelectedSizeUnit?.Value ?? FileSizeUnit.MB;
            minBytes = FileSizeUnitConverter.ToBytes(amount, unit);
        }

        var minCopies = 2;
        if (!string.IsNullOrWhiteSpace(MinCopyCountText))
        {
            if (!int.TryParse(MinCopyCountText, NumberStyles.Integer, _localization.CurrentCulture, out minCopies)
                || minCopies < 2)
            {
                errorKey = "Duplicates_InvalidCopyCount";
                return false;
            }
        }

        criteria = new DuplicateAnalysisCriteria
        {
            ScanSessionId = _scanSessionId,
            NameFilter = string.IsNullOrWhiteSpace(NameFilter) ? null : NameFilter.Trim(),
            ExtensionFilter = string.IsNullOrWhiteSpace(ExtensionFilter) ? null : ExtensionFilter.Trim(),
            MinimumSizeBytes = minBytes,
            MinimumDuplicateCount = minCopies,
            SortBy = SelectedSort?.Value ?? DuplicateSortField.PotentialSavings,
            SortDescending = true,
            MaxDegreeOfParallelism = 2
        };
        return true;
    }

    private static List<DuplicateGroup> FilterAndSort(
        IReadOnlyList<DuplicateGroup> source,
        DuplicateAnalysisCriteria criteria)
    {
        // Reuse service-side filter semantics by constructing a lightweight pass.
        IEnumerable<DuplicateGroup> query = source;

        query = query.Where(g => g.DuplicateCount >= Math.Max(2, criteria.MinimumDuplicateCount));

        if (criteria.MinimumSizeBytes is { } min)
        {
            query = query.Where(g => g.SizeBytes >= min);
        }

        if (!string.IsNullOrWhiteSpace(criteria.NameFilter))
        {
            var name = criteria.NameFilter;
            query = query.Where(g =>
                g.Files.Any(f => f.Name.Contains(name, StringComparison.OrdinalIgnoreCase)
                                 || f.FullPath.Contains(name, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(criteria.ExtensionFilter))
        {
            var ext = criteria.ExtensionFilter.Trim();
            if (!ext.StartsWith('.'))
            {
                ext = "." + ext;
            }

            query = query.Where(g =>
                g.Files.Any(f => Path.GetExtension(f.Name).Equals(ext, StringComparison.OrdinalIgnoreCase)));
        }

        query = criteria.SortBy switch
        {
            DuplicateSortField.FileSize => query.OrderByDescending(g => g.SizeBytes),
            DuplicateSortField.DuplicateCount => query.OrderByDescending(g => g.DuplicateCount),
            DuplicateSortField.FileName => query.OrderBy(g => g.Files[0].Name, StringComparer.OrdinalIgnoreCase),
            _ => query.OrderByDescending(g => g.PotentialSavingsBytes).ThenByDescending(g => g.SizeBytes)
        };

        return query.ToList();
    }

    private void ClearResults(bool markStale)
    {
        ClearGroupCollection();
        _lastResult = null;
        _sourceGroups = [];
        HasResults = false;
        AnalysisIncompleteVisible = false;
        ProgressText = string.Empty;
        GroupsCountText = "0";
        FilesCountText = "0";
        SavingsText = ByteSizeConverter.Format(0, _localization.CurrentCulture);
        AnalyzedFilesText = "0";
        SkippedFailedText = string.Empty;
        if (markStale)
        {
            ResultsStale = true;
        }
    }

    private void ClearGroupCollection()
    {
        Groups.Clear();
        SelectedGroup = null;
        SelectedFile = null;
    }

    private void CancelActiveAnalysis()
    {
        try
        {
            _analysisCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        _analysisCts?.Dispose();
        _analysisCts = null;
        Interlocked.Increment(ref _analysisVersion);
    }

    private void RefreshLocalizedChrome()
    {
        foreach (var option in SizeUnitOptions)
        {
            option.DisplayName = option.Value.ToString();
        }

        foreach (var option in SortOptions)
        {
            option.DisplayName = option.Value switch
            {
                DuplicateSortField.PotentialSavings => _localization.GetString("Duplicates_Sort_Savings"),
                DuplicateSortField.FileSize => _localization.GetString("Duplicates_Sort_Size"),
                DuplicateSortField.DuplicateCount => _localization.GetString("Duplicates_Sort_Count"),
                DuplicateSortField.FileName => _localization.GetString("Duplicates_Sort_Name"),
                _ => option.Value.ToString()
            };
        }
    }

    partial void OnSelectedGroupChanged(DuplicateGroupRowViewModel? value)
    {
        if (value?.SelectedFile is not null)
        {
            SelectedFile = value.SelectedFile;
        }

        OpenContainingFolderCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
    }
}

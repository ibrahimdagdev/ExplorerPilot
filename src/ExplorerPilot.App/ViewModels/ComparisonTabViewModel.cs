using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplorerPilot.App.Converters;
using ExplorerPilot.App.Localization;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Snapshots;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ExplorerPilot.App.ViewModels;

public partial class ComparisonTabViewModel : ObservableObject, IDisposable
{
    private readonly ISnapshotStore _snapshotStore;
    private readonly ISnapshotComparisonService _comparisonService;
    private readonly ISnapshotCatalogNotifier _catalogNotifier;
    private readonly ILocalizationService _localization;
    private readonly ILogger<ComparisonTabViewModel> _logger;

    private ScanResult? _currentScan;
    private Guid _scanSessionId = Guid.Empty;
    private CancellationTokenSource? _workCts;
    private int _workVersion;
    private SnapshotComparisonResult? _lastComparison;

    public ComparisonTabViewModel(
        ISnapshotStore snapshotStore,
        ISnapshotComparisonService comparisonService,
        ISnapshotCatalogNotifier catalogNotifier,
        ILocalizationService localization,
        ILogger<ComparisonTabViewModel> logger)
    {
        _snapshotStore = snapshotStore;
        _comparisonService = comparisonService;
        _catalogNotifier = catalogNotifier;
        _localization = localization;
        _logger = logger;
        _catalogNotifier.CatalogChanged += OnCatalogChanged;

        FilterOptions =
        [
            new ComparisonFilterOption(ComparisonResultFilter.All, string.Empty),
            new ComparisonFilterOption(ComparisonResultFilter.Added, string.Empty),
            new ComparisonFilterOption(ComparisonResultFilter.Removed, string.Empty),
            new ComparisonFilterOption(ComparisonResultFilter.Changed, string.Empty),
            new ComparisonFilterOption(ComparisonResultFilter.Grown, string.Empty),
            new ComparisonFilterOption(ComparisonResultFilter.Shrunk, string.Empty)
        ];
        SortOptions =
        [
            new ComparisonSortOption(ComparisonSortMode.AbsDeltaDesc, string.Empty),
            new ComparisonSortOption(ComparisonSortMode.AbsDeltaAsc, string.Empty),
            new ComparisonSortOption(ComparisonSortMode.PathAsc, string.Empty),
            new ComparisonSortOption(ComparisonSortMode.NameAsc, string.Empty)
        ];
        SelectedFilter = FilterOptions[0];
        SelectedSort = SortOptions[0];
        RefreshLocalizedChrome();
    }

    public ObservableCollection<SnapshotListItemViewModel> Snapshots { get; } = [];

    public ObservableCollection<FolderChangeRowViewModel> GrowingFolders { get; } = [];

    public ObservableCollection<FolderChangeRowViewModel> ShrinkingFolders { get; } = [];

    public ObservableCollection<FileChangeRowViewModel> AddedFiles { get; } = [];

    public ObservableCollection<FileChangeRowViewModel> RemovedFiles { get; } = [];

    public ObservableCollection<FileChangeRowViewModel> ChangedFiles { get; } = [];

    public ObservableCollection<StatChangeRowViewModel> CategoryChanges { get; } = [];

    public ObservableCollection<StatChangeRowViewModel> ExtensionChanges { get; } = [];

    public ObservableCollection<FileChangeRowViewModel> FilteredFileRows { get; } = [];

    public ObservableCollection<FolderChangeRowViewModel> FilteredFolderRows { get; } = [];

    public IReadOnlyList<ComparisonFilterOption> FilterOptions { get; }

    public IReadOnlyList<ComparisonSortOption> SortOptions { get; }

    [ObservableProperty]
    private SnapshotListItemViewModel? _selectedLeft;

    [ObservableProperty]
    private SnapshotListItemViewModel? _selectedRight;

    [ObservableProperty]
    private ComparisonFilterOption? _selectedFilter;

    [ObservableProperty]
    private ComparisonSortOption? _selectedSort;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSnapshotCommand))]
    private bool _hasCurrentScan;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSnapshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshSnapshotsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSnapshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelWorkCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportSnapshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportSnapshotCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasComparison;

    [ObservableProperty]
    private bool _showFilteredFileGrid;

    [ObservableProperty]
    private bool _showFilteredFolderGrid;

    [ObservableProperty]
    private bool _showDefaultResultTabs = true;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _snapshotName = string.Empty;

    [ObservableProperty]
    private string _totalDeltaText = "—";

    [ObservableProperty]
    private string _leftTotalText = "—";

    [ObservableProperty]
    private string _rightTotalText = "—";

    [ObservableProperty]
    private string _addedCountText = "—";

    [ObservableProperty]
    private string _removedCountText = "—";

    [ObservableProperty]
    private string _changedCountText = "—";

    [ObservableProperty]
    private string _rootsNote = string.Empty;

    [ObservableProperty]
    private string _incompleteNote = string.Empty;

    [ObservableProperty]
    private string _leftMetaText = string.Empty;

    [ObservableProperty]
    private string _rightMetaText = string.Empty;

    [ObservableProperty]
    private bool _totalDeltaPositive;

    [ObservableProperty]
    private bool _totalDeltaNegative;

    public void AttachScan(ScanResult? result, Guid scanSessionId, bool incomplete)
    {
        _ = incomplete;
        _currentScan = result;
        _scanSessionId = scanSessionId;
        HasCurrentScan = result is not null;
        if (result is not null && string.IsNullOrWhiteSpace(SnapshotName))
        {
            SnapshotName = $"{result.RootNode.Name} — {DateTime.Now:yyyy-MM-dd HH:mm}";
        }
    }

    public void OnScanStarting()
    {
        CancelWorkCore();
        _currentScan = null;
        _scanSessionId = Guid.Empty;
        HasCurrentScan = false;
    }

    public void RefreshLocalization()
    {
        RefreshLocalizedChrome();
        RebuildSnapshotListFormatting();
        if (HasComparison)
        {
            // Re-format summary numbers with new culture when possible via stored texts refresh.
            StatusText = _localization.GetString("Comparison_StatusReady");
        }
    }

    public async Task InitializeAsync()
    {
        await RefreshSnapshotsAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshSnapshotsAsync()
    {
        await RunWorkAsync(async (version, token) =>
        {
            ProgressText = _localization.GetString("Comparison_ProgressListing");
            var list = await _snapshotStore.ListAsync(token).ConfigureAwait(true);
            if (version != _workVersion)
            {
                return;
            }

            Snapshots.Clear();
            foreach (var info in list)
            {
                Snapshots.Add(ToListItem(info));
            }

            StatusText = _localization.GetString("Comparison_SnapshotCountFormat", Snapshots.Count);
        }).ConfigureAwait(true);
    }

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveSnapshotAsync()
    {
        if (_currentScan is null)
        {
            return;
        }

        var session = _scanSessionId;
        var scan = _currentScan;
        var name = SnapshotName;

        await RunWorkAsync(async (version, token) =>
        {
            ProgressText = _localization.GetString("Comparison_ProgressSaving");
            var info = await _snapshotStore.SaveAsync(
                scan,
                name,
                new Progress<double>(p =>
                {
                    ProgressText = _localization.GetString(
                        "Comparison_ProgressPercentFormat",
                        (p * 100).ToString("0", _localization.CurrentCulture));
                }),
                token).ConfigureAwait(true);

            if (version != _workVersion || session != _scanSessionId)
            {
                return;
            }

            Snapshots.Insert(0, ToListItem(info));
            SelectedRight ??= Snapshots.FirstOrDefault(s => s.Id == info.Id);
            StatusText = _localization.GetString("Comparison_SavedFormat", info.DisplayName);
        }).ConfigureAwait(true);
    }

    private bool CanSave() => HasCurrentScan && _currentScan is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportSnapshotAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = _localization.GetString("Comparison_ImportDialogTitle"),
            Filter = _localization.GetString("Comparison_SnapshotFileFilter"),
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        var policy = SnapshotIdCollisionPolicy.AssignNewId;
        if (Snapshots.Count > 0)
        {
            var choice = MessageBox.Show(
                _localization.GetString("Comparison_ImportCollisionPrompt"),
                _localization.GetString("Comparison_Title"),
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel)
            {
                return;
            }

            // Yes = replace existing same id, No = assign new id
            policy = choice == MessageBoxResult.Yes
                ? SnapshotIdCollisionPolicy.ReplaceExisting
                : SnapshotIdCollisionPolicy.AssignNewId;
        }

        var source = dialog.FileName;
        await RunWorkAsync(async (version, token) =>
        {
            ProgressText = _localization.GetString("Comparison_ProgressImporting");
            try
            {
                var imported = await _snapshotStore.ImportAsync(source, policy, cancellationToken: token)
                    .ConfigureAwait(true);
                if (version != _workVersion)
                {
                    return;
                }

                var item = ToListItem(imported.Info);
                var existing = Snapshots.FirstOrDefault(s => s.Id == item.Id);
                if (existing is not null)
                {
                    var index = Snapshots.IndexOf(existing);
                    Snapshots[index] = item;
                }
                else
                {
                    Snapshots.Insert(0, item);
                }

                SelectedRight = Snapshots.FirstOrDefault(s => s.Id == item.Id);
                StatusText = imported.IdWasReassigned
                    ? _localization.GetString("Comparison_ImportedNewIdFormat", item.DisplayName)
                    : _localization.GetString("Comparison_ImportedFormat", item.DisplayName);
            }
            catch (SnapshotValidationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                StatusText = _localization.GetString("Comparison_ImportKeptExisting");
            }
        }).ConfigureAwait(true);
    }

    private bool CanImport() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportSnapshotAsync()
    {
        var item = SelectedRight ?? SelectedLeft;
        if (item is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = _localization.GetString("Comparison_ExportDialogTitle"),
            Filter = _localization.GetString("Comparison_SnapshotFileFilter"),
            FileName = SanitizeFileName(item.DisplayName) + SnapshotSchema.FileExtension,
            AddExtension = true,
            DefaultExt = SnapshotSchema.FileExtension,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        var id = item.Id;
        var destination = dialog.FileName;
        await RunWorkAsync(async (version, token) =>
        {
            ProgressText = _localization.GetString("Comparison_ProgressExporting");
            var exported = await _snapshotStore.ExportAsync(id, destination, cancellationToken: token)
                .ConfigureAwait(true);
            if (version != _workVersion)
            {
                return;
            }

            StatusText = _localization.GetString("Comparison_ExportedFormat", exported.FilePath);
        }).ConfigureAwait(true);
    }

    private bool CanExport() => !IsBusy && (SelectedLeft is not null || SelectedRight is not null);

    [RelayCommand(CanExecute = nameof(CanCompare))]
    private async Task CompareAsync()
    {
        if (SelectedLeft is null || SelectedRight is null)
        {
            return;
        }

        if (SelectedLeft.Id == SelectedRight.Id)
        {
            MessageBox.Show(
                _localization.GetString("Comparison_SameSnapshot"),
                _localization.GetString("Comparison_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var leftId = SelectedLeft.Id;
        var rightId = SelectedRight.Id;

        await RunWorkAsync(async (version, token) =>
        {
            ProgressText = _localization.GetString("Comparison_ProgressLoading");
            var left = await _snapshotStore.LoadAsync(leftId, token).ConfigureAwait(true);
            var right = await _snapshotStore.LoadAsync(rightId, token).ConfigureAwait(true);
            if (version != _workVersion)
            {
                return;
            }

            ProgressText = _localization.GetString("Comparison_ProgressComparing");
            var result = await _comparisonService.CompareAsync(
                left,
                right,
                new SnapshotCompareOptions(),
                new Progress<double>(p =>
                {
                    ProgressText = _localization.GetString(
                        "Comparison_ProgressPercentFormat",
                        (p * 100).ToString("0", _localization.CurrentCulture));
                }),
                token).ConfigureAwait(true);

            if (version != _workVersion)
            {
                return;
            }

            ApplyComparison(result);
            StatusText = _localization.GetString("Comparison_Complete");
        }).ConfigureAwait(true);
    }

    private bool CanCompare() =>
        !IsBusy && SelectedLeft is not null && SelectedRight is not null;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteSnapshotAsync(SnapshotListItemViewModel? item)
    {
        item ??= SelectedLeft ?? SelectedRight;
        if (item is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            _localization.GetString("Comparison_DeleteConfirmFormat", item.DisplayName),
            _localization.GetString("Comparison_Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var id = item.Id;
        await RunWorkAsync(async (version, token) =>
        {
            ProgressText = _localization.GetString("Comparison_ProgressDeleting");
            var deleted = await _snapshotStore.DeleteAsync(id, token).ConfigureAwait(true);
            if (version != _workVersion)
            {
                return;
            }

            if (!deleted)
            {
                StatusText = _localization.GetString("Comparison_DeleteFailed");
                return;
            }

            var match = Snapshots.FirstOrDefault(s => s.Id == id);
            if (match is not null)
            {
                Snapshots.Remove(match);
            }

            if (SelectedLeft?.Id == id)
            {
                SelectedLeft = null;
            }

            if (SelectedRight?.Id == id)
            {
                SelectedRight = null;
            }

            StatusText = _localization.GetString("Comparison_Deleted");
        }).ConfigureAwait(true);
    }

    private bool CanDelete() => !IsBusy && (SelectedLeft is not null || SelectedRight is not null);

    [RelayCommand(CanExecute = nameof(CanCancelWork))]
    private void CancelWork() => CancelWorkCore();

    private bool CanCancelWork() => IsBusy;

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
                StatusText = _localization.GetString("Comparison_Cancelled");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot/comparison work failed");
            if (version == _workVersion)
            {
                StatusText = _localization.GetString("Comparison_FailedFormat", ex.Message);
                MessageBox.Show(
                    StatusText,
                    _localization.GetString("Comparison_Title"),
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

    private void ApplyComparison(SnapshotComparisonResult result)
    {
        _lastComparison = result;
        var culture = _localization.CurrentCulture;
        LeftTotalText = ByteSizeConverter.Format(result.Summary.LeftTotalBytes, culture);
        RightTotalText = ByteSizeConverter.Format(result.Summary.RightTotalBytes, culture);
        TotalDeltaText = SnapshotDeltaFormatter.FormatSignedBytes(result.Summary.TotalBytesDelta, culture);
        TotalDeltaPositive = result.Summary.TotalBytesDelta > 0;
        TotalDeltaNegative = result.Summary.TotalBytesDelta < 0;
        AddedCountText = result.Summary.AddedFileCount.ToString("N0", culture);
        RemovedCountText = result.Summary.RemovedFileCount.ToString("N0", culture);
        ChangedCountText = result.Summary.ChangedFileCount.ToString("N0", culture);
        RootsNote = result.Summary.RootsDiffer
            ? _localization.GetString(
                "Comparison_RootsDifferFormat",
                result.Summary.LeftRootPath,
                result.Summary.RightRootPath)
            : string.Empty;
        IncompleteNote = result.Summary.LeftIncomplete || result.Summary.RightIncomplete
            ? _localization.GetString("Comparison_IncompleteWarning")
            : string.Empty;
        LeftMetaText = _localization.GetString(
            "Comparison_SnapshotMetaFormat",
            result.Summary.LeftDisplayName,
            result.Summary.LeftCreatedAtUtc.ToLocalTime().ToString("g", culture),
            result.Summary.LeftRootPath);
        RightMetaText = _localization.GetString(
            "Comparison_SnapshotMetaFormat",
            result.Summary.RightDisplayName,
            result.Summary.RightCreatedAtUtc.ToLocalTime().ToString("g", culture),
            result.Summary.RightRootPath);

        RebuildResultCollections(result);
        HasComparison = true;
        ApplyFilterAndSort();
    }

    private void RebuildResultCollections(SnapshotComparisonResult result)
    {
        var culture = _localization.CurrentCulture;

        GrowingFolders.Clear();
        foreach (var folder in result.GrowingFolders)
        {
            GrowingFolders.Add(new FolderChangeRowViewModel(
                folder,
                ByteSizeConverter.Format(folder.LeftSizeBytes, culture),
                ByteSizeConverter.Format(folder.RightSizeBytes, culture),
                SnapshotDeltaFormatter.FormatSignedBytes(folder.DeltaBytes, culture),
                isGrowth: true));
        }

        ShrinkingFolders.Clear();
        foreach (var folder in result.ShrinkingFolders)
        {
            ShrinkingFolders.Add(new FolderChangeRowViewModel(
                folder,
                ByteSizeConverter.Format(folder.LeftSizeBytes, culture),
                ByteSizeConverter.Format(folder.RightSizeBytes, culture),
                SnapshotDeltaFormatter.FormatSignedBytes(folder.DeltaBytes, culture),
                isGrowth: false));
        }

        AddedFiles.Clear();
        foreach (var file in result.AddedFiles)
        {
            AddedFiles.Add(new FileChangeRowViewModel(
                file.RelativePath,
                file.Name,
                ByteSizeConverter.Format(file.SizeBytes, culture),
                SnapshotDeltaFormatter.FormatSignedBytes(file.SizeBytes, culture),
                file.SizeBytes,
                _localization.GetString("Comparison_KindAdded")));
        }

        RemovedFiles.Clear();
        foreach (var file in result.RemovedFiles)
        {
            RemovedFiles.Add(new FileChangeRowViewModel(
                file.RelativePath,
                file.Name,
                ByteSizeConverter.Format(file.SizeBytes, culture),
                SnapshotDeltaFormatter.FormatSignedBytes(SnapshotMath.SafeDelta(0, file.SizeBytes), culture),
                SnapshotMath.SafeDelta(0, file.SizeBytes),
                _localization.GetString("Comparison_KindRemoved")));
        }

        ChangedFiles.Clear();
        foreach (var file in result.ChangedFiles)
        {
            ChangedFiles.Add(new FileChangeRowViewModel(
                file.RelativePath,
                file.Name,
                ByteSizeConverter.Format(file.RightSizeBytes, culture),
                SnapshotDeltaFormatter.FormatSignedBytes(file.DeltaBytes, culture),
                file.DeltaBytes,
                _localization.GetString("Comparison_KindChanged")));
        }

        CategoryChanges.Clear();
        foreach (var change in result.CategoryChanges)
        {
            CategoryChanges.Add(new StatChangeRowViewModel(
                CategoryLabel(change.CategoryCode),
                ByteSizeConverter.Format(change.LeftSizeBytes, culture),
                ByteSizeConverter.Format(change.RightSizeBytes, culture),
                SnapshotDeltaFormatter.FormatSignedBytes(change.DeltaBytes, culture),
                change.DeltaBytes));
        }

        ExtensionChanges.Clear();
        foreach (var change in result.ExtensionChanges.Take(50))
        {
            var label = string.IsNullOrEmpty(change.Extension)
                ? _localization.GetString("Comparison_NoExtension")
                : change.Extension;
            ExtensionChanges.Add(new StatChangeRowViewModel(
                label,
                ByteSizeConverter.Format(change.LeftSizeBytes, culture),
                ByteSizeConverter.Format(change.RightSizeBytes, culture),
                SnapshotDeltaFormatter.FormatSignedBytes(change.DeltaBytes, culture),
                change.DeltaBytes));
        }
    }

    private void ApplyFilterAndSort()
    {
        var filter = SelectedFilter?.Filter ?? ComparisonResultFilter.All;
        var sort = SelectedSort?.Mode ?? ComparisonSortMode.AbsDeltaDesc;

        ShowFilteredFileGrid = filter is ComparisonResultFilter.Added or ComparisonResultFilter.Removed or ComparisonResultFilter.Changed;
        ShowFilteredFolderGrid = filter is ComparisonResultFilter.Grown or ComparisonResultFilter.Shrunk;
        ShowDefaultResultTabs = !ShowFilteredFileGrid && !ShowFilteredFolderGrid;

        FilteredFileRows.Clear();
        FilteredFolderRows.Clear();

        IEnumerable<FileChangeRowViewModel> files = filter switch
        {
            ComparisonResultFilter.Added => AddedFiles,
            ComparisonResultFilter.Removed => RemovedFiles,
            ComparisonResultFilter.Changed => ChangedFiles,
            _ => []
        };

        IEnumerable<FolderChangeRowViewModel> folders = filter switch
        {
            ComparisonResultFilter.Grown => GrowingFolders,
            ComparisonResultFilter.Shrunk => ShrinkingFolders,
            _ => []
        };

        foreach (var row in SortFiles(files, sort))
        {
            FilteredFileRows.Add(row);
        }

        foreach (var row in SortFolders(folders, sort))
        {
            FilteredFolderRows.Add(row);
        }

        if (filter == ComparisonResultFilter.All && _lastComparison is not null)
        {
            // Keep tab grids sorted consistently.
            ResortInPlace(AddedFiles, SortFiles(AddedFiles, sort));
            ResortInPlace(RemovedFiles, SortFiles(RemovedFiles, sort));
            ResortInPlace(ChangedFiles, SortFiles(ChangedFiles, sort));
            ResortInPlace(GrowingFolders, SortFolders(GrowingFolders, sort));
            ResortInPlace(ShrinkingFolders, SortFolders(ShrinkingFolders, sort));
        }
    }

    private static IEnumerable<FileChangeRowViewModel> SortFiles(
        IEnumerable<FileChangeRowViewModel> source,
        ComparisonSortMode sort) =>
        sort switch
        {
            ComparisonSortMode.AbsDeltaAsc => source.OrderBy(r => SnapshotMath.AbsClamped(r.DeltaBytes)),
            ComparisonSortMode.PathAsc => source.OrderBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase),
            ComparisonSortMode.NameAsc => source.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            _ => source.OrderByDescending(r => SnapshotMath.AbsClamped(r.DeltaBytes))
        };

    private static IEnumerable<FolderChangeRowViewModel> SortFolders(
        IEnumerable<FolderChangeRowViewModel> source,
        ComparisonSortMode sort) =>
        sort switch
        {
            ComparisonSortMode.AbsDeltaAsc => source.OrderBy(r => SnapshotMath.AbsClamped(r.DeltaBytes)),
            ComparisonSortMode.PathAsc => source.OrderBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase),
            ComparisonSortMode.NameAsc => source.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            _ => source.OrderByDescending(r => SnapshotMath.AbsClamped(r.DeltaBytes))
        };

    private static void ResortInPlace<T>(ObservableCollection<T> target, IEnumerable<T> ordered)
    {
        var list = ordered.ToList();
        target.Clear();
        foreach (var item in list)
        {
            target.Add(item);
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "snapshot" : cleaned;
    }

    private SnapshotListItemViewModel ToListItem(SnapshotInfo info)
    {
        var culture = _localization.CurrentCulture;
        var status = info.IsIncomplete
            ? _localization.GetString("Comparison_Incomplete")
            : _localization.GetString("Comparison_CompleteScan");
        return new SnapshotListItemViewModel(
            info,
            ByteSizeConverter.Format(info.TotalBytes, culture),
            info.CreatedAtUtc.ToLocalTime().ToString("g", culture),
            status);
    }

    private void RebuildSnapshotListFormatting()
    {
        var items = Snapshots.Select(s => ToListItem(s.Info)).ToList();
        var leftId = SelectedLeft?.Id;
        var rightId = SelectedRight?.Id;
        Snapshots.Clear();
        foreach (var item in items)
        {
            Snapshots.Add(item);
        }

        SelectedLeft = Snapshots.FirstOrDefault(s => s.Id == leftId);
        SelectedRight = Snapshots.FirstOrDefault(s => s.Id == rightId);
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

    private void RefreshLocalizedChrome()
    {
        FilterOptions[0].DisplayName = _localization.GetString("Comparison_Filter_All");
        FilterOptions[1].DisplayName = _localization.GetString("Comparison_Filter_Added");
        FilterOptions[2].DisplayName = _localization.GetString("Comparison_Filter_Removed");
        FilterOptions[3].DisplayName = _localization.GetString("Comparison_Filter_Changed");
        FilterOptions[4].DisplayName = _localization.GetString("Comparison_Filter_Grown");
        FilterOptions[5].DisplayName = _localization.GetString("Comparison_Filter_Shrunk");
        SortOptions[0].DisplayName = _localization.GetString("Comparison_Sort_AbsDeltaDesc");
        SortOptions[1].DisplayName = _localization.GetString("Comparison_Sort_AbsDeltaAsc");
        SortOptions[2].DisplayName = _localization.GetString("Comparison_Sort_Path");
        SortOptions[3].DisplayName = _localization.GetString("Comparison_Sort_Name");

        if (!IsBusy && !HasComparison)
        {
            StatusText = _localization.GetString("Comparison_StatusReady");
        }
    }

    partial void OnSelectedLeftChanged(SnapshotListItemViewModel? value)
    {
        CompareCommand.NotifyCanExecuteChanged();
        ExportSnapshotCommand.NotifyCanExecuteChanged();
        DeleteSnapshotCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedRightChanged(SnapshotListItemViewModel? value)
    {
        CompareCommand.NotifyCanExecuteChanged();
        ExportSnapshotCommand.NotifyCanExecuteChanged();
        DeleteSnapshotCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedFilterChanged(ComparisonFilterOption? value)
    {
        if (HasComparison)
        {
            ApplyFilterAndSort();
        }
    }

    partial void OnSelectedSortChanged(ComparisonSortOption? value)
    {
        if (HasComparison)
        {
            ApplyFilterAndSort();
        }
    }

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        if (IsBusy)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            _ = RefreshSnapshotsAsync();
            return;
        }

        _ = dispatcher.BeginInvoke(new Action(() => _ = RefreshSnapshotsAsync()));
    }

    public void Dispose()
    {
        _catalogNotifier.CatalogChanged -= OnCatalogChanged;
        CancelWorkCore();
        _workCts?.Dispose();
        _workCts = null;
    }
}

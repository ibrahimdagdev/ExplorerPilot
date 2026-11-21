using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplorerPilot.App.Localization;
using ExplorerPilot.App.Services;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Scanning;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.App.ViewModels;

public partial class SearchTabViewModel : ObservableObject
{
    private readonly IFileSearchService _searchService;
    private readonly IFileActionService _fileActions;
    private readonly ILocalizationService _localization;
    private readonly ILogger<SearchTabViewModel> _logger;

    private FileSystemNode? _searchRoot;
    private Guid _scanSessionId = Guid.Empty;
    private bool _fromIncompleteScan;
    private CancellationTokenSource? _searchCts;
    private int _searchVersion;
    private FileSearchPage? _lastPage;

    public SearchTabViewModel(
        IFileSearchService searchService,
        IFileActionService fileActions,
        ILocalizationService localization,
        ILogger<SearchTabViewModel> logger)
    {
        _searchService = searchService;
        _fileActions = fileActions;
        _localization = localization;
        _logger = logger;

        ScopeOptions =
        [
            new SearchOptionItem<SearchScope>(SearchScope.NameOrPath),
            new SearchOptionItem<SearchScope>(SearchScope.Name),
            new SearchOptionItem<SearchScope>(SearchScope.FolderName),
            new SearchOptionItem<SearchScope>(SearchScope.FullPath)
        ];

        ItemKindOptions =
        [
            new SearchOptionItem<SearchItemKind>(SearchItemKind.All),
            new SearchOptionItem<SearchItemKind>(SearchItemKind.FilesOnly),
            new SearchOptionItem<SearchItemKind>(SearchItemKind.FoldersOnly)
        ];

        SizeUnitOptions =
        [
            new SearchOptionItem<FileSizeUnit>(FileSizeUnit.KB),
            new SearchOptionItem<FileSizeUnit>(FileSizeUnit.MB),
            new SearchOptionItem<FileSizeUnit>(FileSizeUnit.GB),
            new SearchOptionItem<FileSizeUnit>(FileSizeUnit.TB)
        ];

        SortOptions =
        [
            new SearchOptionItem<SearchSortField>(SearchSortField.Size),
            new SearchOptionItem<SearchSortField>(SearchSortField.Name),
            new SearchOptionItem<SearchSortField>(SearchSortField.Modified)
        ];

        SelectedScope = ScopeOptions[0];
        SelectedItemKind = ItemKindOptions[0];
        SelectedSizeUnit = SizeUnitOptions[1]; // MB
        SelectedSort = SortOptions[0];

        RefreshLocalizedChrome();
    }

    public ObservableCollection<SearchResultRowViewModel> Results { get; } = [];

    public IReadOnlyList<SearchOptionItem<SearchScope>> ScopeOptions { get; }

    public IReadOnlyList<SearchOptionItem<SearchItemKind>> ItemKindOptions { get; }

    public IReadOnlyList<SearchOptionItem<FileSizeUnit>> SizeUnitOptions { get; }

    public IReadOnlyList<SearchOptionItem<SearchSortField>> SortOptions { get; }

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private SearchOptionItem<SearchScope>? _selectedScope;

    [ObservableProperty]
    private SearchOptionItem<SearchItemKind>? _selectedItemKind;

    [ObservableProperty]
    private string _extensionFilter = string.Empty;

    [ObservableProperty]
    private string _minSizeText = string.Empty;

    [ObservableProperty]
    private string _maxSizeText = string.Empty;

    [ObservableProperty]
    private SearchOptionItem<FileSizeUnit>? _selectedSizeUnit;

    [ObservableProperty]
    private DateTime? _modifiedFrom;

    [ObservableProperty]
    private DateTime? _modifiedTo;

    [ObservableProperty]
    private SearchOptionItem<SearchSortField>? _selectedSort;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelSearchCommand))]
    private bool _isSearching;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private bool _hasSearchRoot;

    [ObservableProperty]
    private bool _resultsStale;

    [ObservableProperty]
    private bool _fromIncompleteScanVisible;

    [ObservableProperty]
    private bool _resultsTruncatedVisible;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _pageInfoText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenContainingFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    private SearchResultRowViewModel? _selectedResult;

    [ObservableProperty]
    private string _nameHeader = string.Empty;

    [ObservableProperty]
    private string _typeHeader = string.Empty;

    [ObservableProperty]
    private string _sizeHeader = string.Empty;

    [ObservableProperty]
    private string _modifiedHeader = string.Empty;

    [ObservableProperty]
    private string _parentHeader = string.Empty;

    [ObservableProperty]
    private string _pathHeader = string.Empty;

    public void AttachScan(FileSystemNode? root, Guid scanSessionId, bool incomplete)
    {
        _searchRoot = root;
        _scanSessionId = scanSessionId;
        _fromIncompleteScan = incomplete;
        HasSearchRoot = root is not null;
        FromIncompleteScanVisible = incomplete && root is not null;

        if (root is null)
        {
            CancelActiveSearch();
            ClearResults(markStale: false);
            ResultsStale = false;
            StatusText = string.Empty;
            PageInfoText = string.Empty;
            return;
        }

        // New scan available — previous page no longer represents this session.
        if (_lastPage is not null && _lastPage.ScanSessionId != scanSessionId)
        {
            ResultsStale = true;
            StatusText = _localization.GetString("Search_ResultsStale");
        }
    }

    public void OnScanStarting()
    {
        CancelActiveSearch();
        _searchRoot = null;
        _scanSessionId = Guid.Empty;
        _fromIncompleteScan = false;
        HasSearchRoot = false;
        FromIncompleteScanVisible = false;
        ResultsStale = Results.Count > 0;
        if (ResultsStale)
        {
            StatusText = _localization.GetString("Search_ResultsStale");
        }
    }

    public void RefreshLocalization()
    {
        RefreshLocalizedChrome();
        RefreshResultFormatting();
        RefreshStatusFromLastPage();
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync() => await RunSearchAsync(pageIndex: 0).ConfigureAwait(true);

    [RelayCommand]
    private void ClearFilters()
    {
        Query = string.Empty;
        ExtensionFilter = string.Empty;
        MinSizeText = string.Empty;
        MaxSizeText = string.Empty;
        ModifiedFrom = null;
        ModifiedTo = null;
        SelectedScope = ScopeOptions[0];
        SelectedItemKind = ItemKindOptions[0];
        SelectedSizeUnit = SizeUnitOptions[1];
        SelectedSort = SortOptions[0];
        CancelActiveSearch();
        ClearResults(markStale: false);
        ResultsStale = false;
        ResultsTruncatedVisible = false;
        StatusText = HasSearchRoot
            ? _localization.GetString("Search_Ready")
            : _localization.GetString("Search_NoScan");
        PageInfoText = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanCancelSearch))]
    private void CancelSearch()
    {
        CancelActiveSearch();
        IsSearching = false;
        StatusText = _localization.GetString("Search_Cancelled");
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextPageAsync()
    {
        if (_lastPage is null)
        {
            return;
        }

        await RunSearchAsync(_lastPage.PageIndex + 1).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private async Task PreviousPageAsync()
    {
        if (_lastPage is null)
        {
            return;
        }

        await RunSearchAsync(Math.Max(0, _lastPage.PageIndex - 1)).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanResultAction))]
    private void OpenContainingFolder()
    {
        if (SelectedResult is null)
        {
            return;
        }

        if (_fileActions.TryOpenContainingFolder(SelectedResult.FullPath, out _))
        {
            return;
        }

        MessageBox.Show(
            _localization.GetString("Search_OpenFolderFailed"),
            _localization.GetString("Search_Title"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    [RelayCommand(CanExecute = nameof(CanResultAction))]
    private void CopyPath()
    {
        if (SelectedResult is null)
        {
            return;
        }

        if (_fileActions.TryCopyPath(SelectedResult.FullPath, out _))
        {
            return;
        }

        MessageBox.Show(
            _localization.GetString("Search_CopyPathFailed"),
            _localization.GetString("Search_Title"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private bool CanSearch() => HasSearchRoot && !IsSearching;

    private bool CanCancelSearch() => IsSearching;

    private bool CanGoNext() =>
        !IsSearching && _lastPage is { HasNextPage: true } && !ResultsStale
        && _lastPage.ScanSessionId == _scanSessionId;

    private bool CanGoPrevious() =>
        !IsSearching && _lastPage is { HasPreviousPage: true } && !ResultsStale
        && _lastPage.ScanSessionId == _scanSessionId;

    private bool CanResultAction() => SelectedResult is not null;

    private async Task RunSearchAsync(int pageIndex)
    {
        if (_searchRoot is null || _scanSessionId == Guid.Empty)
        {
            StatusText = _localization.GetString("Search_NoScan");
            return;
        }

        if (!FileSizeUnitConverter.TryParseSize(MinSizeText, SelectedSizeUnit?.Value ?? FileSizeUnit.MB, out var minBytes)
            || !FileSizeUnitConverter.TryParseSize(MaxSizeText, SelectedSizeUnit?.Value ?? FileSizeUnit.MB, out var maxBytes))
        {
            StatusText = _localization.GetString("Search_InvalidSize");
            return;
        }

        if (minBytes is not null && maxBytes is not null && minBytes > maxBytes)
        {
            StatusText = _localization.GetString("Search_InvalidSizeRange");
            return;
        }

        var version = Interlocked.Increment(ref _searchVersion);
        CancelActiveSearch();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsSearching = true;
        ResultsStale = false;
        StatusText = _localization.GetString("Search_Searching");

        var sortBy = SelectedSort?.Value ?? SearchSortField.Size;
        var criteria = new FileSearchCriteria
        {
            ScanSessionId = _scanSessionId,
            Query = Query,
            Scope = SelectedScope?.Value ?? SearchScope.NameOrPath,
            ExtensionFilter = ExtensionFilter,
            MinSizeBytes = minBytes,
            MaxSizeBytes = maxBytes,
            ModifiedFromUtc = ToStartUtc(ModifiedFrom),
            ModifiedToUtc = ToEndUtc(ModifiedTo),
            ItemKind = SelectedItemKind?.Value ?? SearchItemKind.All,
            SortBy = sortBy,
            SortDirection = sortBy == SearchSortField.Name
                ? SearchSortDirection.Ascending
                : SearchSortDirection.Descending,
            PageIndex = pageIndex,
            PageSize = FileSearchCriteria.DefaultPageSize
        };

        try
        {
            var page = await _searchService
                .SearchAsync(_searchRoot, criteria, _fromIncompleteScan, token)
                .ConfigureAwait(true);

            if (version != _searchVersion)
            {
                return;
            }

            ApplyPage(page);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (version == _searchVersion)
            {
                StatusText = _localization.GetString("Search_Cancelled");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed");
            if (version == _searchVersion)
            {
                StatusText = _localization.GetString("Search_Failed");
                ClearResults(markStale: false);
            }
        }
        finally
        {
            if (version == _searchVersion)
            {
                IsSearching = false;
                NotifyPagingCommands();
            }
        }
    }

    private void ApplyPage(FileSearchPage page)
    {
        _lastPage = page;
        Results.Clear();
        SelectedResult = null;

        var culture = _localization.CurrentCulture;
        var fileLabel = _localization.GetString("Search_TypeFile");
        var folderLabel = _localization.GetString("Search_TypeFolder");
        var dash = _localization.GetString("EmDash");

        foreach (var hit in page.Items)
        {
            var row = new SearchResultRowViewModel(hit);
            row.RefreshFormatting(culture, fileLabel, folderLabel, dash);
            Results.Add(row);
        }

        HasResults = Results.Count > 0;
        ResultsTruncatedVisible = page.ResultsTruncated;
        FromIncompleteScanVisible = page.FromIncompleteScan;
        ResultsStale = false;

        if (!HasResults)
        {
            StatusText = _localization.GetString("Search_NoResults");
            PageInfoText = string.Empty;
        }
        else
        {
            StatusText = page.ResultsTruncated
                ? _localization.GetString("Search_ResultsTruncatedFormat", page.CollectedMatchCount, page.MaxCollectibleMatches)
                : _localization.GetString("Search_ResultsFormat", page.CollectedMatchCount);

            PageInfoText = _localization.GetString(
                "Search_PageFormat",
                page.PageIndex + 1,
                Math.Max(1, page.TotalPages));
        }

        NotifyPagingCommands();
    }

    private void ClearResults(bool markStale)
    {
        Results.Clear();
        SelectedResult = null;
        HasResults = false;
        _lastPage = null;
        ResultsTruncatedVisible = false;
        if (markStale)
        {
            ResultsStale = true;
        }

        NotifyPagingCommands();
    }

    private void CancelActiveSearch()
    {
        Interlocked.Increment(ref _searchVersion);
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
    }

    private void RefreshLocalizedChrome()
    {
        NameHeader = _localization.GetString("Search_ColumnName");
        TypeHeader = _localization.GetString("Search_ColumnType");
        SizeHeader = _localization.GetString("Search_ColumnSize");
        ModifiedHeader = _localization.GetString("Search_ColumnModified");
        ParentHeader = _localization.GetString("Search_ColumnParent");
        PathHeader = _localization.GetString("Search_ColumnPath");

        SetOption(ScopeOptions[0], "Search_Scope_NameOrPath");
        SetOption(ScopeOptions[1], "Search_Scope_Name");
        SetOption(ScopeOptions[2], "Search_Scope_FolderName");
        SetOption(ScopeOptions[3], "Search_Scope_FullPath");

        SetOption(ItemKindOptions[0], "Search_Kind_All");
        SetOption(ItemKindOptions[1], "Search_Kind_Files");
        SetOption(ItemKindOptions[2], "Search_Kind_Folders");

        SizeUnitOptions[0].DisplayName = "KB";
        SizeUnitOptions[1].DisplayName = "MB";
        SizeUnitOptions[2].DisplayName = "GB";
        SizeUnitOptions[3].DisplayName = "TB";

        SetOption(SortOptions[0], "Search_Sort_Size");
        SetOption(SortOptions[1], "Search_Sort_Name");
        SetOption(SortOptions[2], "Search_Sort_Modified");
    }

    private void SetOption<T>(SearchOptionItem<T> option, string key) =>
        option.DisplayName = _localization.GetString(key);

    private void RefreshResultFormatting()
    {
        var culture = _localization.CurrentCulture;
        var fileLabel = _localization.GetString("Search_TypeFile");
        var folderLabel = _localization.GetString("Search_TypeFolder");
        var dash = _localization.GetString("EmDash");
        foreach (var row in Results)
        {
            row.RefreshFormatting(culture, fileLabel, folderLabel, dash);
        }
    }

    private void RefreshStatusFromLastPage()
    {
        if (ResultsStale)
        {
            StatusText = _localization.GetString("Search_ResultsStale");
            return;
        }

        if (_lastPage is null)
        {
            StatusText = HasSearchRoot
                ? _localization.GetString("Search_Ready")
                : _localization.GetString("Search_NoScan");
            PageInfoText = string.Empty;
            return;
        }

        ApplyPage(_lastPage);
    }

    private void NotifyPagingCommands()
    {
        NextPageCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        SearchCommand.NotifyCanExecuteChanged();
        CancelSearchCommand.NotifyCanExecuteChanged();
    }

    private static DateTimeOffset? ToStartUtc(DateTime? localDate)
    {
        if (localDate is null)
        {
            return null;
        }

        var local = DateTime.SpecifyKind(localDate.Value.Date, DateTimeKind.Local);
        return new DateTimeOffset(local).ToUniversalTime();
    }

    private static DateTimeOffset? ToEndUtc(DateTime? localDate)
    {
        if (localDate is null)
        {
            return null;
        }

        var localEnd = DateTime.SpecifyKind(localDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local);
        return new DateTimeOffset(localEnd).ToUniversalTime();
    }
}

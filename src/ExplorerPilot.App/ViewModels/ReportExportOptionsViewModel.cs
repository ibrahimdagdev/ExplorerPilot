using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplorerPilot.App.Localization;
using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.App.ViewModels;

public partial class ReportExportOptionsViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;
    private Action<bool?>? _close;

    public ReportExportOptionsViewModel(ILocalizationService localization)
    {
        _localization = localization;
        RefreshLocalizedText();
    }

    public void AttachClose(Action<bool?> close) => _close = close;

    public bool Confirmed { get; private set; }

    [ObservableProperty]
    private string _windowTitle = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _formatLabel = string.Empty;

    [ObservableProperty]
    private string _formatCsvLabel = string.Empty;

    [ObservableProperty]
    private string _formatHtmlLabel = string.Empty;

    [ObservableProperty]
    private string _formatJsonLabel = string.Empty;

    [ObservableProperty]
    private string _sectionsLabel = string.Empty;

    [ObservableProperty]
    private string _includeTreeLabel = string.Empty;

    [ObservableProperty]
    private string _includeCategoriesLabel = string.Empty;

    [ObservableProperty]
    private string _includeExtensionsLabel = string.Empty;

    [ObservableProperty]
    private string _includeErrorsLabel = string.Empty;

    [ObservableProperty]
    private string _limitsLabel = string.Empty;

    [ObservableProperty]
    private string _maxFilesLabel = string.Empty;

    [ObservableProperty]
    private string _maxFoldersLabel = string.Empty;

    [ObservableProperty]
    private string _exportButtonText = string.Empty;

    [ObservableProperty]
    private string _cancelButtonText = string.Empty;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private ReportFormat _selectedFormat = ReportFormat.Html;

    [ObservableProperty]
    private bool _includeDirectoryTree;

    [ObservableProperty]
    private bool _includeCategories = true;

    [ObservableProperty]
    private bool _includeExtensions = true;

    [ObservableProperty]
    private bool _includeErrors = true;

    [ObservableProperty]
    private int _maxLargestFiles = 100;

    [ObservableProperty]
    private int _maxLargestFolders = 100;

    public bool IsCsvSelected
    {
        get => SelectedFormat == ReportFormat.Csv;
        set
        {
            if (value)
            {
                SelectedFormat = ReportFormat.Csv;
            }
        }
    }

    public bool IsHtmlSelected
    {
        get => SelectedFormat == ReportFormat.Html;
        set
        {
            if (value)
            {
                SelectedFormat = ReportFormat.Html;
            }
        }
    }

    public bool IsJsonSelected
    {
        get => SelectedFormat == ReportFormat.Json;
        set
        {
            if (value)
            {
                SelectedFormat = ReportFormat.Json;
            }
        }
    }

    partial void OnSelectedFormatChanged(ReportFormat value)
    {
        OnPropertyChanged(nameof(IsCsvSelected));
        OnPropertyChanged(nameof(IsHtmlSelected));
        OnPropertyChanged(nameof(IsJsonSelected));
    }

    [RelayCommand]
    private void Confirm()
    {
        if (!TryValidate(out var message))
        {
            ValidationMessage = message;
            return;
        }

        ValidationMessage = string.Empty;
        Confirmed = true;
        _close?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        Confirmed = false;
        _close?.Invoke(false);
    }

    public bool TryValidate(out string message)
    {
        if (MaxLargestFiles is < 0 or > 10_000)
        {
            message = _localization.GetString("ReportOptions_InvalidMaxFiles");
            return false;
        }

        if (MaxLargestFolders is < 0 or > 10_000)
        {
            message = _localization.GetString("ReportOptions_InvalidMaxFolders");
            return false;
        }

        message = string.Empty;
        return true;
    }

    private void RefreshLocalizedText()
    {
        WindowTitle = _localization.GetString("ReportOptions_Title");
        Subtitle = _localization.GetString("ReportOptions_Subtitle");
        FormatLabel = _localization.GetString("ReportOptions_Format");
        FormatCsvLabel = _localization.GetString("ReportOptions_FormatCsv");
        FormatHtmlLabel = _localization.GetString("ReportOptions_FormatHtml");
        FormatJsonLabel = _localization.GetString("ReportOptions_FormatJson");
        SectionsLabel = _localization.GetString("ReportOptions_Sections");
        IncludeTreeLabel = _localization.GetString("ReportOptions_IncludeTree");
        IncludeCategoriesLabel = _localization.GetString("ReportOptions_IncludeCategories");
        IncludeExtensionsLabel = _localization.GetString("ReportOptions_IncludeExtensions");
        IncludeErrorsLabel = _localization.GetString("ReportOptions_IncludeErrors");
        LimitsLabel = _localization.GetString("ReportOptions_Limits");
        MaxFilesLabel = _localization.GetString("ReportOptions_MaxFiles");
        MaxFoldersLabel = _localization.GetString("ReportOptions_MaxFolders");
        ExportButtonText = _localization.GetString("ReportOptions_Export");
        CancelButtonText = _localization.GetString("ReportOptions_Cancel");
    }
}

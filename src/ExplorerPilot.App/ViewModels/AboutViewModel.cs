using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplorerPilot.App.Localization;
using ExplorerPilot.App.Services;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.App.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly IApplicationInfoService _appInfo;
    private readonly ILocalizationService _localization;
    private readonly ILogger<AboutViewModel> _logger;

    public AboutViewModel(
        IApplicationInfoService appInfo,
        ILocalizationService localization,
        ILogger<AboutViewModel> logger)
    {
        _appInfo = appInfo;
        _localization = localization;
        _logger = logger;

        ProductName = appInfo.ProductName;
        VersionDisplay = appInfo.BrandedVersionLabel;
        Developer = appInfo.Author;
        Publisher = appInfo.Company;
        WebsiteDisplay = appInfo.WebsiteDisplay;
        WebsiteUrl = appInfo.WebsiteUrl;
        Platform = appInfo.Platform;
        Copyright = appInfo.Copyright;

        _localization.LanguageChanged += OnLanguageChanged;
        RefreshLocalizedText();
    }

    public string ProductName { get; }

    public string VersionDisplay { get; }

    public string Developer { get; }

    public string Publisher { get; }

    public string WebsiteDisplay { get; }

    public string WebsiteUrl { get; }

    public string Platform { get; }

    public string Copyright { get; }

    [ObservableProperty]
    private string _windowTitle = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _developerLabel = string.Empty;

    [ObservableProperty]
    private string _publisherLabel = string.Empty;

    [ObservableProperty]
    private string _websiteLabel = string.Empty;

    [ObservableProperty]
    private string _platformLabel = string.Empty;

    [ObservableProperty]
    private string _licenseLabel = string.Empty;

    [ObservableProperty]
    private string _licenseValue = string.Empty;

    [ObservableProperty]
    private string _closeButtonText = string.Empty;

    [RelayCommand]
    private void OpenWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = WebsiteUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open website {Url}", WebsiteUrl);
            MessageBox.Show(
                _localization.GetString("About_WebsiteOpenFailed"),
                _localization.GetString("About_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void Close(Window? window) => window?.Close();

    public void Detach()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshLocalizedText();

    private void RefreshLocalizedText()
    {
        WindowTitle = _localization.GetString("About_Title");
        Description = _localization.GetString("About_Description");
        DeveloperLabel = _localization.GetString("About_Developer");
        PublisherLabel = _localization.GetString("About_Publisher");
        WebsiteLabel = _localization.GetString("About_Website");
        PlatformLabel = _localization.GetString("About_Platform");
        LicenseLabel = _localization.GetString("About_License");
        LicenseValue = _appInfo.HasLicenseFile && !string.IsNullOrWhiteSpace(_appInfo.LicenseName)
            ? _appInfo.LicenseName
            : _localization.GetString("About_LicenseNotSpecified");
        CloseButtonText = _localization.GetString("About_Close");
    }
}

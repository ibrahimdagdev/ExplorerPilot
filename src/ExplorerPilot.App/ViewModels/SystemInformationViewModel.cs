using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplorerPilot.App.Converters;
using ExplorerPilot.App.Localization;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.App.ViewModels;

public partial class SystemInformationViewModel : ObservableObject
{
    private readonly ISystemInformationProvider _provider;
    private readonly ILocalizationService _localization;
    private readonly ILogger<SystemInformationViewModel> _logger;
    private CancellationTokenSource? _loadCts;
    private SystemInformationModel? _model;
    private int _loadVersion;

    public SystemInformationViewModel(
        ISystemInformationProvider provider,
        ILocalizationService localization,
        ILogger<SystemInformationViewModel> logger)
    {
        _provider = provider;
        _localization = localization;
        _logger = logger;
        _localization.LanguageChanged += OnLanguageChanged;
        RefreshChrome();
    }

    public ObservableCollection<SystemInfoSectionViewModel> Sections { get; } = [];

    [ObservableProperty]
    private string _windowTitle = string.Empty;

    [ObservableProperty]
    private string _refreshButtonText = string.Empty;

    [ObservableProperty]
    private string _closeButtonText = string.Empty;

    [ObservableProperty]
    private string _loadingText = string.Empty;

    [ObservableProperty]
    private string _privacyNote = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _warningMessage = string.Empty;

    [ObservableProperty]
    private bool _hasWarning;

    public async Task LoadAsync()
    {
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        var version = Interlocked.Increment(ref _loadVersion);
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;
        HasWarning = false;
        WarningMessage = string.Empty;

        try
        {
            var model = await _provider.GetSystemInformationAsync(token).ConfigureAwait(true);
            if (version != _loadVersion)
            {
                return;
            }

            _model = model;
            HasData = true;
            RebuildSections();
            ApplyWarnings(model);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Superseded or closed.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load system information");
            if (version != _loadVersion)
            {
                return;
            }

            HasError = true;
            ErrorMessage = _localization.GetString("SystemInfo_LoadFailed");
            if (_model is null)
            {
                HasData = false;
                Sections.Clear();
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private bool CanRefresh() => !IsLoading;

    [RelayCommand]
    private void Close(Window? window) => window?.Close();

    public void Detach()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshChrome();
        if (_model is not null)
        {
            RebuildSections();
            ApplyWarnings(_model);
        }

        if (HasError)
        {
            ErrorMessage = _localization.GetString("SystemInfo_LoadFailed");
        }
    }

    private void RefreshChrome()
    {
        WindowTitle = _localization.GetString("SystemInfo_Title");
        RefreshButtonText = _localization.GetString("SystemInfo_Refresh");
        CloseButtonText = _localization.GetString("SystemInfo_Close");
        LoadingText = _localization.GetString("SystemInfo_Loading");
        PrivacyNote = _localization.GetString("SystemInfo_PrivacyNote");
    }

    private void ApplyWarnings(SystemInformationModel model)
    {
        if (model.Warnings.Count == 0)
        {
            HasWarning = false;
            WarningMessage = string.Empty;
            return;
        }

        HasWarning = true;
        WarningMessage = _localization.GetString("SystemInfo_PartialWarning");
    }

    private void RebuildSections()
    {
        if (_model is null)
        {
            return;
        }

        var culture = _localization.CurrentCulture;
        var na = _localization.GetString("SystemInfo_NotAvailable");
        var model = _model;

        Sections.Clear();

        Sections.Add(BuildSection(
            _localization.GetString("SystemInfo_Section_OperatingSystem"),
            [
                (_localization.GetString("SystemInfo_OsName"), Display(model.OperatingSystem.Name, na)),
                (_localization.GetString("SystemInfo_OsVersion"), Display(model.OperatingSystem.Version, na)),
                (_localization.GetString("SystemInfo_OsBuild"), Display(model.OperatingSystem.BuildNumber, na)),
                (_localization.GetString("SystemInfo_OsArchitecture"), Display(model.OperatingSystem.OsArchitecture, na)),
                (_localization.GetString("SystemInfo_ProcessArchitecture"), Display(model.OperatingSystem.ProcessArchitecture, na))
            ]));

        Sections.Add(BuildSection(
            _localization.GetString("SystemInfo_Section_Processor"),
            [
                (_localization.GetString("SystemInfo_CpuManufacturer"), Display(model.Processor.Manufacturer, na)),
                (_localization.GetString("SystemInfo_CpuModel"), Display(model.Processor.Model, na)),
                (_localization.GetString("SystemInfo_PhysicalCores"), DisplayCount(model.Processor.PhysicalCoreCount, na, culture)),
                (_localization.GetString("SystemInfo_LogicalProcessors"), DisplayCount(model.Processor.LogicalProcessorCount, na, culture))
            ]));

        Sections.Add(BuildSection(
            _localization.GetString("SystemInfo_Section_Memory"),
            [
                (_localization.GetString("SystemInfo_TotalRam"), DisplayBytes(model.Memory.TotalPhysicalBytes, na, culture)),
                (_localization.GetString("SystemInfo_AvailableRam"), DisplayBytes(model.Memory.AvailablePhysicalBytes, na, culture))
            ]));

        Sections.Add(BuildSection(
            _localization.GetString("SystemInfo_Section_Motherboard"),
            [
                (_localization.GetString("SystemInfo_BoardManufacturer"), Display(model.Motherboard.Manufacturer, na)),
                (_localization.GetString("SystemInfo_BoardModel"), Display(model.Motherboard.Model, na))
            ]));

        Sections.Add(BuildSection(
            _localization.GetString("SystemInfo_Section_Bios"),
            [
                (_localization.GetString("SystemInfo_BiosManufacturer"), Display(model.Bios.Manufacturer, na)),
                (_localization.GetString("SystemInfo_BiosVersion"), Display(model.Bios.Version, na)),
                (_localization.GetString("SystemInfo_BiosReleaseDate"), Display(model.Bios.ReleaseDate, na))
            ]));

        if (model.GraphicsAdapters.Count == 0)
        {
            Sections.Add(BuildSection(
                _localization.GetString("SystemInfo_Section_Graphics"),
                [(_localization.GetString("SystemInfo_GpuName"), na)]));
        }
        else
        {
            for (var i = 0; i < model.GraphicsAdapters.Count; i++)
            {
                var gpu = model.GraphicsAdapters[i];
                var title = model.GraphicsAdapters.Count == 1
                    ? _localization.GetString("SystemInfo_Section_Graphics")
                    : _localization.GetString("SystemInfo_Section_GraphicsIndexed", i + 1);

                Sections.Add(BuildSection(
                    title,
                    [
                        (_localization.GetString("SystemInfo_GpuName"), Display(gpu.Name, na)),
                        (_localization.GetString("SystemInfo_GpuManufacturer"), Display(gpu.Manufacturer, na)),
                        (_localization.GetString("SystemInfo_GpuMemory"), DisplayBytes(gpu.AdapterRamBytes, na, culture))
                    ]));
            }
        }

        if (model.StorageDevices.Count == 0)
        {
            Sections.Add(BuildSection(
                _localization.GetString("SystemInfo_Section_Storage"),
                [(_localization.GetString("SystemInfo_DiskModel"), na)]));
        }
        else
        {
            for (var i = 0; i < model.StorageDevices.Count; i++)
            {
                var disk = model.StorageDevices[i];
                var title = model.StorageDevices.Count == 1
                    ? _localization.GetString("SystemInfo_Section_Storage")
                    : _localization.GetString("SystemInfo_Section_StorageIndexed", i + 1);

                Sections.Add(BuildSection(
                    title,
                    [
                        (_localization.GetString("SystemInfo_DiskModel"), Display(disk.Model, na)),
                        (_localization.GetString("SystemInfo_DiskCapacity"), DisplayBytes(disk.SizeBytes, na, culture)),
                        (_localization.GetString("SystemInfo_DiskMediaType"), Display(disk.MediaType, na)),
                        (_localization.GetString("SystemInfo_DiskInterface"), Display(disk.InterfaceType, na))
                    ]));
            }
        }

        Sections.Add(BuildSection(
            _localization.GetString("SystemInfo_Section_System"),
            [
                (_localization.GetString("SystemInfo_ComputerManufacturer"), Display(model.Computer.Manufacturer, na)),
                (_localization.GetString("SystemInfo_ComputerModel"), Display(model.Computer.Model, na)),
                (_localization.GetString("SystemInfo_SystemArchitecture"), Display(model.Computer.SystemArchitecture, na))
            ]));
    }

    private static SystemInfoSectionViewModel BuildSection(
        string title,
        IReadOnlyList<(string Label, string Value)> rows)
    {
        var section = new SystemInfoSectionViewModel(title);
        foreach (var (label, value) in rows)
        {
            section.Rows.Add(new SystemInfoRowViewModel(label, value));
        }

        return section;
    }

    private static string Display(string? value, string notAvailable) =>
        string.IsNullOrWhiteSpace(value) ? notAvailable : value.Trim();

    private static string DisplayCount(int? value, string notAvailable, CultureInfo culture) =>
        value is null ? notAvailable : value.Value.ToString("N0", culture);

    private static string DisplayBytes(ulong? bytes, string notAvailable, CultureInfo culture)
    {
        if (bytes is null)
        {
            return notAvailable;
        }

        var capped = bytes.Value > long.MaxValue ? long.MaxValue : (long)bytes.Value;
        return ByteSizeConverter.Format(capped, culture);
    }
}

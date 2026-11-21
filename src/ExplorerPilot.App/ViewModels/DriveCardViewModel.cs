using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ExplorerPilot.App.Converters;
using ExplorerPilot.App.Localization;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;

namespace ExplorerPilot.App.ViewModels;

/// <summary>
/// UI row for the multi-drive overview. Capacity is volume metadata, not scan totals.
/// </summary>
public sealed partial class DriveCardViewModel : ObservableObject
{
    public DriveCardViewModel(DriveInformation drive)
    {
        Drive = drive;
        Name = drive.Name;
        RootPath = drive.RootPath;
        Kind = drive.Kind;
        Status = drive.Status;
        IsReady = drive.IsReady;
        CanScan = drive.CanScan;
        UsageRatio = drive.UsageRatio;
        TotalSizeBytes = drive.TotalSizeBytes;
        UsedSpaceBytes = drive.UsedSpaceBytes;
        AvailableFreeSpaceBytes = drive.AvailableFreeSpaceBytes;
        FileSystem = drive.FileSystem;
        VolumeLabel = drive.VolumeLabel;
    }

    public DriveInformation Drive { get; }

    public string Name { get; }

    public string RootPath { get; }

    public VolumeDriveKind Kind { get; }

    public DriveReadyStatus Status { get; }

    public bool IsReady { get; }

    public bool CanScan { get; }

    public double UsageRatio { get; }

    public long TotalSizeBytes { get; }

    public long UsedSpaceBytes { get; }

    public long AvailableFreeSpaceBytes { get; }

    public string FileSystem { get; }

    public string VolumeLabel { get; }

    [ObservableProperty]
    private string _titleText = string.Empty;

    [ObservableProperty]
    private string _kindText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _fileSystemText = string.Empty;

    [ObservableProperty]
    private string _totalText = string.Empty;

    [ObservableProperty]
    private string _usedText = string.Empty;

    [ObservableProperty]
    private string _freeText = string.Empty;

    [ObservableProperty]
    private string _usageText = string.Empty;

    public void RefreshFormatting(ILocalizationService localization)
    {
        var culture = localization.CurrentCulture;
        var label = string.IsNullOrWhiteSpace(VolumeLabel)
            ? localization.GetString("Drives_DefaultLabel")
            : VolumeLabel;

        TitleText = $"{Name.TrimEnd('\\')} — {label}";
        KindText = ResolveKind(localization, Kind);
        StatusText = ResolveStatus(localization, Status);
        FileSystemText = string.IsNullOrWhiteSpace(FileSystem)
            ? localization.GetString("Drives_FileSystemUnknown")
            : FileSystem;

        if (!IsReady)
        {
            TotalText = localization.GetString("Drives_NotAvailable");
            UsedText = localization.GetString("Drives_NotAvailable");
            FreeText = localization.GetString("Drives_NotAvailable");
            UsageText = localization.GetString("Drives_NotAvailable");
            return;
        }

        TotalText = ByteSizeConverter.Format(TotalSizeBytes, culture);
        UsedText = ByteSizeConverter.Format(UsedSpaceBytes, culture);
        FreeText = ByteSizeConverter.Format(AvailableFreeSpaceBytes, culture);
        UsageText = PercentageConverter.Format(Drive.UsagePercentage, culture);
    }

    private static string ResolveKind(ILocalizationService localization, VolumeDriveKind kind) =>
        kind switch
        {
            VolumeDriveKind.Fixed => localization.GetString("Drives_Kind_Fixed"),
            VolumeDriveKind.Removable => localization.GetString("Drives_Kind_Removable"),
            VolumeDriveKind.Network => localization.GetString("Drives_Kind_Network"),
            VolumeDriveKind.Optical => localization.GetString("Drives_Kind_Optical"),
            VolumeDriveKind.Ram => localization.GetString("Drives_Kind_Ram"),
            _ => localization.GetString("Drives_Kind_Unknown")
        };

    private static string ResolveStatus(ILocalizationService localization, DriveReadyStatus status) =>
        status switch
        {
            DriveReadyStatus.Ready => localization.GetString("Drives_Status_Ready"),
            DriveReadyStatus.NotReady => localization.GetString("Drives_Status_NotReady"),
            DriveReadyStatus.TimedOut => localization.GetString("Drives_Status_TimedOut"),
            _ => localization.GetString("Drives_Status_Unavailable")
        };
}

using System.Globalization;
using System.Windows.Data;
using ExplorerPilot.App.Localization;
using ExplorerPilot.Core.Models;

namespace ExplorerPilot.App.Converters;

/// <summary>
/// Formats <see cref="DriveInfoModel"/> using the active localization culture and strings.
/// </summary>
public sealed class DriveDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DriveInfoModel drive)
        {
            return string.Empty;
        }

        return Format(drive, LocalizationAccessor.Current);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public static string Format(DriveInfoModel drive, ILocalizationService? localization)
    {
        var cultureInfo = localization?.CurrentCulture ?? CultureInfo.CurrentCulture;
        var free = ByteSizeConverter.Format(drive.TotalFreeSpaceBytes, cultureInfo);
        var total = ByteSizeConverter.Format(drive.TotalSizeBytes, cultureInfo);

        if (localization is null)
        {
            return drive.IsReady
                ? $"{drive.Name} ({(string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel)}) — {free} free of {total}"
                : $"{drive.Name} ({drive.DriveType}) — Not ready";
        }

        if (!drive.IsReady)
        {
            var type = string.IsNullOrWhiteSpace(drive.DriveType)
                ? localization.GetString("Drive_Unavailable")
                : drive.DriveType;
            return localization.GetString(
                "Drive_NotReadyFormat",
                drive.Name,
                type,
                localization.GetString("Drive_NotReady"));
        }

        var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
            ? localization.GetString("Drive_LocalDisk")
            : drive.VolumeLabel;

        return localization.GetString("Drive_FreeOfFormat", drive.Name, label, free, total);
    }
}

/// <summary>
/// Set by <see cref="Localization.LocalizationService"/> after initialization.
/// </summary>
internal static class LocalizationAccessor
{
    public static ILocalizationService? Current { get; set; }
}

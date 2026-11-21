using System.Globalization;
using System.Windows.Data;

namespace ExplorerPilot.App.Converters;

public sealed class ByteSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes)
        {
            return "—";
        }

        return Format(bytes, culture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public static string Format(long bytes) => Format(bytes, CultureInfo.CurrentCulture);

    public static string Format(long bytes, CultureInfo culture)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return string.Format(culture, "{0:0.##} {1}", size, units[unit]);
    }
}

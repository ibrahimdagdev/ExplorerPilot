using System.Globalization;
using System.Windows.Data;

namespace ExplorerPilot.App.Converters;

public sealed class PercentageConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double percentage)
        {
            return string.Empty;
        }

        return Format(percentage, culture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public static string Format(double percentage, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var number = percentage.ToString("0.0", culture);
        if (culture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase))
        {
            return "%" + number;
        }

        return number + "%";
    }
}

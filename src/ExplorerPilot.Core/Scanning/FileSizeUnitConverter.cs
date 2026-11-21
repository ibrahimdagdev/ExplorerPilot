using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Scanning;

public static class FileSizeUnitConverter
{
    public static long ToBytes(double value, FileSizeUnit unit) =>
        unit switch
        {
            FileSizeUnit.KB => (long)Math.Round(value * 1024d),
            FileSizeUnit.MB => (long)Math.Round(value * 1024d * 1024d),
            FileSizeUnit.GB => (long)Math.Round(value * 1024d * 1024d * 1024d),
            FileSizeUnit.TB => (long)Math.Round(value * 1024d * 1024d * 1024d * 1024d),
            _ => (long)Math.Round(value * 1024d)
        };

    public static bool TryParseSize(string? text, FileSizeUnit unit, out long? bytes)
    {
        bytes = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture, out var value)
            && !double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        if (value < 0)
        {
            return false;
        }

        bytes = ToBytes(value, unit);
        return true;
    }
}

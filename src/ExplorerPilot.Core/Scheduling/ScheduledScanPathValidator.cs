namespace ExplorerPilot.Core.Scheduling;

public static class ScheduledScanPathValidator
{
    public static bool TryNormalize(string? path, out string normalized, out string? error)
    {
        normalized = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Path is required.";
            return false;
        }

        var trimmed = path.Trim();
        if (trimmed.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            error = "Path contains invalid characters.";
            return false;
        }

        try
        {
            if (!Path.IsPathRooted(trimmed))
            {
                error = "Path must be absolute (drive root or folder).";
                return false;
            }

            normalized = Path.GetFullPath(trimmed);
            // Drive roots often end with separator; keep a stable form.
            if (normalized.Length == 3 && normalized[1] == ':' &&
                (normalized[2] == '\\' || normalized[2] == '/'))
            {
                return true;
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "Path could not be normalized.";
            return false;
        }
    }
}

namespace ExplorerPilot.Infrastructure.FileSystem;

/// <summary>
/// Path utilities for long-path-friendly scanning on Windows.
/// </summary>
internal static class PathHelper
{
    private const string ExtendedPrefix = @"\\?\";
    private const string UncExtendedPrefix = @"\\?\UNC\";

    /// <summary>
    /// Returns a path suitable for Win32 long-path APIs when needed.
    /// .NET enumeration APIs accept these forms when long paths are enabled.
    /// </summary>
    public static string EnsureExtendedLengthPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (path.StartsWith(ExtendedPrefix, StringComparison.Ordinal))
        {
            return path;
        }

        var fullPath = Path.GetFullPath(path);

        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // \\server\share\... -> \\?\UNC\server\share\...
            return UncExtendedPrefix + fullPath[2..];
        }

        return ExtendedPrefix + fullPath;
    }

    public static string GetDisplayPath(string path)
    {
        if (path.StartsWith(UncExtendedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[UncExtendedPrefix.Length..];
        }

        if (path.StartsWith(ExtendedPrefix, StringComparison.Ordinal))
        {
            return path[ExtendedPrefix.Length..];
        }

        return path;
    }

    public static string GetNodeName(string path)
    {
        var display = GetDisplayPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (display.Length >= 2 && display[1] == ':' && display.Length <= 3)
        {
            return display.Length == 2 ? display + Path.DirectorySeparatorChar : display;
        }

        return Path.GetFileName(display) is { Length: > 0 } name ? name : display;
    }
}

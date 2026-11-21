namespace ExplorerPilot.Core.Diagnostics;

/// <summary>
/// Redacts filesystem paths for logs and diagnostic reports to reduce personal-data exposure.
/// Keeps enough shape for troubleshooting (drive letter / last segment) without full paths.
/// </summary>
public static class PathRedaction
{
    /// <summary>Returns a privacy-safe path token for logging.</summary>
    public static string ForLog(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "(empty)";
        }

        try
        {
            var trimmed = path.Trim();
            var root = Path.GetPathRoot(trimmed);
            var name = Path.GetFileName(trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            if (string.IsNullOrEmpty(root))
            {
                return string.IsNullOrEmpty(name) ? "(path)" : "***\\" + name;
            }

            if (string.IsNullOrEmpty(name) || string.Equals(name, root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                return root.TrimEnd('\\', '/') + "\\***";
            }

            return root.TrimEnd('\\', '/') + "\\***\\" + name;
        }
        catch
        {
            return "(path)";
        }
    }
}

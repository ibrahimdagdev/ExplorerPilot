namespace ExplorerPilot.Core.Cleanup;

/// <summary>
/// Default exclusion rules for Windows system and protected locations,
/// plus high-risk location heuristics for advisory cleanup ranking.
/// </summary>
public static class CleanupProtectedPaths
{
    private static readonly string[] SegmentBlocks =
    [
        "Windows",
        "Program Files",
        "Program Files (x86)",
        "ProgramData",
        "System Volume Information",
        "$Recycle.Bin",
        "Recovery",
        "PerfLogs",
        "Boot",
        "EFI",
        "Windows.old"
    ];

    private static readonly string[] HighRiskMarkers =
    [
        @"\Users\Default\",
        @"\AppData\Local\Microsoft\Windows\",
        @"\AppData\Roaming\Microsoft\Windows\",
        @"\Application Data\",
        @"\Local Settings\"
    ];

    /// <summary>Well-known user profile folders that should never be treated as low-risk cleanup.</summary>
    private static readonly string[] ImportantUserFolders =
    [
        "Desktop",
        "Documents",
        "Pictures",
        "Videos",
        "Music",
        "Downloads",
        "OneDrive"
    ];

    private static readonly HashSet<string> HighRiskExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys", ".drv", ".ocx", ".cpl", ".msi", ".msp",
        ".scr", ".com", ".efi"
    };

    public static bool IsExcluded(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return true;
        }

        var normalized = Normalize(fullPath);
        if (normalized.Length <= 3)
        {
            return true;
        }

        var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 1; i < parts.Length; i++)
        {
            foreach (var block in SegmentBlocks)
            {
                if (parts[i].Equals(block, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsHighRiskLocation(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return true;
        }

        var normalized = Normalize(fullPath);
        foreach (var marker in HighRiskMarkers)
        {
            if (normalized.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return IsImportantUserProfilePath(normalized);
    }

    /// <summary>
    /// True when the path is under a user profile important folder
    /// (Desktop, Documents, Pictures, Videos, Music, Downloads, OneDrive).
    /// </summary>
    public static bool IsImportantUserProfilePath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        var parts = Normalize(fullPath).Split('\\', StringSplitOptions.RemoveEmptyEntries);
        // Expect: C:, Users, <name>, Desktop|Documents|..., ...
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!parts[i].Equals("Users", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 2 >= parts.Length)
            {
                return false;
            }

            var folder = parts[i + 2];
            foreach (var important in ImportantUserFolders)
            {
                if (folder.Equals(important, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        return false;
    }

    public static bool IsHighRiskExtension(string? nameOrExtension)
    {
        if (string.IsNullOrWhiteSpace(nameOrExtension))
        {
            return false;
        }

        var ext = nameOrExtension.Contains('.')
            ? Path.GetExtension(nameOrExtension)
            : nameOrExtension;
        return HighRiskExtensions.Contains(ext);
    }

    /// <summary>
    /// Case-insensitive identity key for Windows paths: drive letter uppercased,
    /// separators normalized, trailing slash removed, \\?\ prefix stripped.
    /// </summary>
    public static string NormalizeKey(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return string.Empty;
        }

        return Normalize(fullPath);
    }

    public static bool PathKeysEqual(string? left, string? right) =>
        string.Equals(NormalizeKey(left), NormalizeKey(right), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path)
    {
        var trimmed = path.Trim();
        if (trimmed.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            trimmed = trimmed[4..];
        }
        else if (trimmed.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            trimmed = trimmed[4..];
        }

        trimmed = trimmed.Replace('/', '\\');
        trimmed = CollapseSeparators(trimmed);

        if (trimmed.Length >= 2 && trimmed[1] == ':')
        {
            trimmed = char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
        }

        if (trimmed.Length > 3 && trimmed.EndsWith('\\'))
        {
            trimmed = trimmed.TrimEnd('\\');
        }

        return trimmed;
    }

    private static string CollapseSeparators(string path)
    {
        if (path.Length < 2)
        {
            return path;
        }

        var chars = new char[path.Length];
        var w = 0;
        var i = 0;

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            chars[w++] = '\\';
            chars[w++] = '\\';
            i = 2;
        }

        var lastWasSep = false;
        for (; i < path.Length; i++)
        {
            var c = path[i];
            if (c == '\\')
            {
                if (lastWasSep)
                {
                    continue;
                }

                lastWasSep = true;
                chars[w++] = c;
            }
            else
            {
                lastWasSep = false;
                chars[w++] = c;
            }
        }

        return new string(chars, 0, w);
    }
}

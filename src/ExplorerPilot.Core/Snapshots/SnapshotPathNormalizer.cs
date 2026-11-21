namespace ExplorerPilot.Core.Snapshots;

/// <summary>Windows-oriented path normalization for snapshot identity and comparison.</summary>
public static class SnapshotPathNormalizer
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static StringComparer PathComparer => Comparer;

    public static string NormalizeAbsolute(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim();
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

    /// <summary>
    /// Normalizes a relative path for dictionary keys: separators, no traversal, no drive roots.
    /// Preserves original segment casing (comparison is case-insensitive via <see cref="PathComparer"/>).
    /// </summary>
    public static string NormalizeRelative(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        var trimmed = relativePath.Trim().Replace('/', '\\');
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal)
            || (trimmed.Length >= 2 && trimmed[1] == ':'))
        {
            // Absolute slipped into relative field — treat as opaque full path key.
            return NormalizeAbsolute(trimmed);
        }

        trimmed = trimmed.TrimStart('\\');
        var parts = trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(static p => p is "." or ".."))
        {
            throw new SnapshotValidationException("Snapshot relative path must not contain '.' or '..' segments.");
        }

        return string.Join('\\', parts);
    }

    public static bool TryNormalizeRelative(string? relativePath, out string normalized)
    {
        try
        {
            normalized = NormalizeRelative(relativePath);
            return true;
        }
        catch (SnapshotValidationException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    public static string MakeRelative(string rootAbsolute, string fullAbsolute)
    {
        var root = NormalizeAbsolute(rootAbsolute);
        var full = NormalizeAbsolute(fullAbsolute);

        if (root.Length == 0)
        {
            return NormalizeRelative(full);
        }

        if (Comparer.Equals(root, full))
        {
            return string.Empty;
        }

        var prefix = root.EndsWith('\\') ? root : root + "\\";
        if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeRelative(full[prefix.Length..]);
        }

        return NormalizeAbsolute(full);
    }

    public static bool RootsEquivalent(string leftRoot, string rightRoot) =>
        Comparer.Equals(NormalizeAbsolute(leftRoot), NormalizeAbsolute(rightRoot));

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

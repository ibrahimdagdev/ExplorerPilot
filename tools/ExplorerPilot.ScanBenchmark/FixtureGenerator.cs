namespace ExplorerPilot.ScanBenchmark;

/// <summary>
/// Creates controlled temporary directory trees for DiskScanner benchmarks.
/// Only writes under the provided root (expected to be a TEMP path).
/// </summary>
internal static class FixtureGenerator
{
    public static FixtureInfo EnsureFixture(string root, int fileCount, int filesPerDirectory = 200)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (fileCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileCount));
        }

        Directory.CreateDirectory(root);
        var marker = Path.Combine(root, $".fixture-{fileCount}.ok");
        if (File.Exists(marker))
        {
            var existingDirs = Directory.GetDirectories(root).Length;
            var existingFiles = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count();
            return new FixtureInfo(root, fileCount, existingDirs, existingFiles, Created: false);
        }

        // Wipe incomplete previous attempts for this size.
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            var name = Path.GetFileName(entry);
            if (name.StartsWith(".fixture-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }

        var directoryCount = Math.Max(1, (int)Math.Ceiling(fileCount / (double)filesPerDirectory));
        var payload = "EP-BENCH"u8.ToArray();
        var created = 0;
        for (var d = 0; d < directoryCount && created < fileCount; d++)
        {
            var dir = Path.Combine(root, $"d{d:D5}");
            Directory.CreateDirectory(dir);
            var remaining = fileCount - created;
            var batch = Math.Min(filesPerDirectory, remaining);
            for (var f = 0; f < batch; f++)
            {
                var path = Path.Combine(dir, $"f{f:D5}.bin");
                // Tiny deterministic payload — metadata cost dominates, not content I/O.
                File.WriteAllBytes(path, payload);
                created++;
            }
        }

        File.WriteAllText(marker, $"files={fileCount};dirs={directoryCount};utc={DateTime.UtcNow:O}");
        return new FixtureInfo(root, fileCount, directoryCount, created, Created: true);
    }

    public static void TryDeleteTree(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // best effort — benchmarks must not touch user data elsewhere
        }
    }
}

internal sealed record FixtureInfo(
    string Root,
    int RequestedFiles,
    int DirectoryCount,
    int FileCount,
    bool Created);

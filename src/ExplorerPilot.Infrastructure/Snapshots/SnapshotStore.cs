using System.Text.Json;
using ExplorerPilot.Core.Diagnostics;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Snapshots;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.Infrastructure.Snapshots;

public sealed class SnapshotStore : ISnapshotStore
{
    public const long MaxImportFileBytes = 512L * 1024 * 1024;
    public const int MaxEntryCount = 5_000_000;

    private readonly ILogger<SnapshotStore> _logger;
    private readonly ISnapshotCatalogNotifier? _catalogNotifier;
    private readonly string _directory;

    public SnapshotStore(ILogger<SnapshotStore> logger, ISnapshotCatalogNotifier catalogNotifier)
        : this(logger, DefaultDirectory(), catalogNotifier)
    {
    }

    public SnapshotStore(ILogger<SnapshotStore> logger)
        : this(logger, DefaultDirectory(), catalogNotifier: null)
    {
    }

    public SnapshotStore(ILogger<SnapshotStore> logger, string storageDirectory)
        : this(logger, storageDirectory, catalogNotifier: null)
    {
    }

    public SnapshotStore(
        ILogger<SnapshotStore> logger,
        string storageDirectory,
        ISnapshotCatalogNotifier? catalogNotifier)
    {
        _logger = logger;
        _catalogNotifier = catalogNotifier;
        _directory = storageDirectory;
        Directory.CreateDirectory(_directory);
    }

    public string StorageDirectory => _directory;

    public static string DefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExplorerPilot",
            "Snapshots");

    public Task<IReadOnlyList<SnapshotInfo>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var list = new List<SnapshotInfo>();
            foreach (var file in Directory.EnumerateFiles(_directory, "*" + SnapshotSchema.FileExtension))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var stream = File.OpenRead(file);
                    using var doc = JsonDocument.Parse(stream);
                    if (!TryReadInfo(doc.RootElement, file, out var info))
                    {
                        continue;
                    }

                    list.Add(info);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipping unreadable snapshot {File}", PathRedaction.ForLog(file));
                }
            }

            list.Sort(static (a, b) => b.CreatedAtUtc.CompareTo(a.CreatedAtUtc));
            return (IReadOnlyList<SnapshotInfo>)list;
        }, cancellationToken);

    public async Task<SnapshotInfo> SaveAsync(
        ScanResult scanResult,
        string? displayName = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scanResult);
        progress?.Report(0.05);

        var snapshot = await Task.Run(
                () => DiskSnapshotBuilder.Build(scanResult, displayName, cancellationToken: cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(0.4);
        var destination = GetSnapshotPath(snapshot.Id);
        await WriteSnapshotFileAsync(snapshot, destination, progress, cancellationToken).ConfigureAwait(false);
        progress?.Report(1.0);
        _logger.LogInformation("Saved snapshot {Id} to {Path}", snapshot.Id, PathRedaction.ForLog(destination));
        _catalogNotifier?.NotifyChanged();
        return SnapshotJson.ToInfo(snapshot, destination);
    }

    public Task<DiskSnapshot> LoadAsync(Guid snapshotId, CancellationToken cancellationToken = default)
    {
        var path = GetSnapshotPath(snapshotId);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Snapshot was not found.", path);
        }

        return LoadFromPathAsync(path, cancellationToken);
    }

    public Task<SnapshotTrendPayload> LoadTrendPayloadAsync(
        Guid snapshotId,
        int maxDirectoryEntries = 2_000,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = GetSnapshotPath(snapshotId);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Snapshot was not found.", path);
            }

            EnsurePathInSandbox(path);
            return ReadTrendPayload(path, Math.Clamp(maxDirectoryEntries, 1, 50_000), cancellationToken);
        }, cancellationToken);

    public Task<DiskSnapshot> LoadFromPathAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        EnsurePathInSandbox(filePath);
        return LoadValidatedAsync(filePath, enforceSandboxExtension: true, cancellationToken);
    }

    public Task<DiskSnapshot> LoadExternalAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return LoadValidatedAsync(filePath, enforceSandboxExtension: false, cancellationToken);
    }

    public async Task<SnapshotInfo> ExportAsync(
        Guid snapshotId,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var source = GetSnapshotPath(snapshotId);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Snapshot was not found.", source);
        }

        EnsurePathInSandbox(source);
        progress?.Report(0.1);

        var fullDestination = Path.GetFullPath(destinationPath);
        var destDir = Path.GetDirectoryName(fullDestination);
        if (string.IsNullOrWhiteSpace(destDir))
        {
            throw new SnapshotValidationException("Export destination directory is invalid.");
        }

        Directory.CreateDirectory(destDir);

        // Copy only — never delete/modify the caller's existing destination first in a destructive way
        // beyond overwrite of the chosen export target file.
        var temp = fullDestination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                await input.CopyToAsync(output, 64 * 1024, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(0.9);
            File.Move(temp, fullDestination, overwrite: true);
            progress?.Report(1.0);

            var snapshot = await LoadFromPathAsync(source, cancellationToken).ConfigureAwait(false);
            return SnapshotJson.ToInfo(snapshot, fullDestination);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public async Task<SnapshotImportResult> ImportAsync(
        string sourcePath,
        SnapshotIdCollisionPolicy collisionPolicy = SnapshotIdCollisionPolicy.AssignNewId,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        progress?.Report(0.05);

        var loaded = await LoadExternalAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        progress?.Report(0.4);

        var originalId = loaded.Id;
        var destinationId = loaded.Id;
        var replaced = false;
        var reassigned = false;
        var existingPath = GetSnapshotPath(originalId);

        if (File.Exists(existingPath))
        {
            switch (collisionPolicy)
            {
                case SnapshotIdCollisionPolicy.KeepExisting:
                    throw new SnapshotValidationException(
                        $"A snapshot with id '{originalId:N}' already exists. Import cancelled.");
                case SnapshotIdCollisionPolicy.ReplaceExisting:
                    destinationId = originalId;
                    replaced = true;
                    break;
                default:
                    destinationId = Guid.NewGuid();
                    reassigned = true;
                    break;
            }
        }

        if (reassigned)
        {
            loaded = CloneWithNewId(loaded, destinationId);
        }

        var destination = GetSnapshotPath(destinationId);
        await WriteSnapshotFileAsync(loaded, destination, progress, cancellationToken).ConfigureAwait(false);
        progress?.Report(1.0);

        _logger.LogInformation(
            "Imported snapshot from {Source} as {Id} (replaced={Replaced}, reassigned={Reassigned})",
            PathRedaction.ForLog(sourcePath),
            destinationId,
            replaced,
            reassigned);

        _catalogNotifier?.NotifyChanged();
        return new SnapshotImportResult
        {
            Info = SnapshotJson.ToInfo(loaded, destination),
            IdWasReassigned = reassigned,
            ReplacedExisting = replaced,
            OriginalId = originalId
        };
    }

    public Task<bool> DeleteAsync(Guid snapshotId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = GetSnapshotPath(snapshotId);
            if (!File.Exists(path))
            {
                return false;
            }

            EnsurePathInSandbox(path);
            File.Delete(path);
            _logger.LogInformation("Deleted snapshot {Id}", snapshotId);
            _catalogNotifier?.NotifyChanged();
            return true;
        }, cancellationToken);

    private async Task<DiskSnapshot> LoadValidatedAsync(
        string filePath,
        bool enforceSandboxExtension,
        CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(filePath);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException("Snapshot file was not found.", full);
        }

        if (!full.EndsWith(SnapshotSchema.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new SnapshotValidationException(
                $"Snapshot file must use the '{SnapshotSchema.FileExtension}' extension.");
        }

        var length = new FileInfo(full).Length;
        if (length <= 0)
        {
            throw new SnapshotValidationException("Snapshot file is empty.");
        }

        if (length > MaxImportFileBytes)
        {
            throw new SnapshotValidationException(
                $"Snapshot file exceeds the maximum allowed size ({MaxImportFileBytes} bytes).");
        }

        if (enforceSandboxExtension)
        {
            EnsurePathInSandbox(full);
        }

        await using var stream = new FileStream(
            full,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        DiskSnapshot? snapshot;
        try
        {
            snapshot = await JsonSerializer.DeserializeAsync<DiskSnapshot>(stream, SnapshotJson.Options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new SnapshotValidationException("Snapshot file is corrupt or incomplete.", ex);
        }

        if (snapshot is null)
        {
            throw new SnapshotValidationException("Snapshot file is empty.");
        }

        ValidateSnapshotContent(snapshot);
        return snapshot;
    }

    private async Task WriteSnapshotFileAsync(
        DiskSnapshot snapshot,
        string destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var tempPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, SnapshotJson.Options, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(0.9);
            File.Move(tempPath, destination, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private string GetSnapshotPath(Guid id) =>
        Path.Combine(_directory, id.ToString("N") + SnapshotSchema.FileExtension);

    private void EnsurePathInSandbox(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        var root = Path.GetFullPath(_directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(_directory), StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Snapshot path is outside the application snapshot directory.");
        }

        if (!full.EndsWith(SnapshotSchema.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Only application snapshot files can be managed.");
        }
    }

    private static void ValidateSnapshotContent(DiskSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.SchemaVersion))
        {
            throw new SnapshotValidationException("Snapshot schema version is missing.");
        }

        var currentMajor = SnapshotSchema.CurrentVersion.Split('.')[0];
        var fileMajor = snapshot.SchemaVersion.Split('.')[0];
        if (!string.Equals(currentMajor, fileMajor, StringComparison.Ordinal))
        {
            throw new SnapshotValidationException(
                $"Unsupported snapshot schema version '{snapshot.SchemaVersion}'. Expected major {currentMajor}.");
        }

        if (snapshot.Id == Guid.Empty)
        {
            throw new SnapshotValidationException("Snapshot id is missing.");
        }

        if (snapshot.Entries.Count > MaxEntryCount)
        {
            throw new SnapshotValidationException(
                $"Snapshot entry count ({snapshot.Entries.Count}) exceeds the allowed maximum ({MaxEntryCount}).");
        }

        var visited = 0;
        foreach (var entry in snapshot.Entries)
        {
            if ((++visited & 0x3FF) == 0)
            {
                // Cooperative validation pacing for huge imports.
            }

            if (!SnapshotPathNormalizer.TryNormalizeRelative(entry.RelativePath, out _))
            {
                throw new SnapshotValidationException(
                    $"Snapshot contains an unsafe relative path: '{entry.RelativePath}'.");
            }
        }
    }

    private static SnapshotTrendPayload ReadTrendPayload(
        string filePath,
        int maxDirectoryEntries,
        CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        using var doc = JsonDocument.Parse(stream);
        cancellationToken.ThrowIfCancellationRequested();
        var root = doc.RootElement;

        if (!root.TryGetProperty("id", out var idProp)
            || !Guid.TryParse(idProp.GetString(), out var id))
        {
            throw new SnapshotValidationException("Snapshot id is missing.");
        }

        var rootPath = root.TryGetProperty("rootPath", out var rootProp)
            ? rootProp.GetString() ?? string.Empty
            : string.Empty;
        var normalized = root.TryGetProperty("normalizedRootPath", out var normProp)
            ? normProp.GetString() ?? SnapshotPathNormalizer.NormalizeAbsolute(rootPath)
            : SnapshotPathNormalizer.NormalizeAbsolute(rootPath);

        var created = root.TryGetProperty("createdAtUtc", out var createdProp)
            && createdProp.TryGetDateTimeOffset(out var dto)
            ? dto
            : DateTimeOffset.MinValue;

        var categories = new List<SnapshotCategoryStat>();
        if (root.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in cats.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                categories.Add(new SnapshotCategoryStat
                {
                    Category = ParseCategory(item),
                    CategoryCode = item.TryGetProperty("categoryCode", out var code)
                        ? code.GetString() ?? "Other"
                        : "Other",
                    FileCount = item.TryGetProperty("fileCount", out var fc) ? fc.GetInt64() : 0,
                    TotalSizeBytes = item.TryGetProperty("totalSizeBytes", out var tb) ? tb.GetInt64() : 0,
                    PercentageOfTotalBytes = item.TryGetProperty("percentageOfTotalBytes", out var pct)
                        ? pct.GetDouble()
                        : 0
                });
            }
        }

        var extensions = new List<SnapshotExtensionStat>();
        if (root.TryGetProperty("extensions", out var exts) && exts.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in exts.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                extensions.Add(new SnapshotExtensionStat
                {
                    Extension = item.TryGetProperty("extension", out var ext)
                        ? ext.GetString() ?? string.Empty
                        : string.Empty,
                    Category = ParseCategory(item),
                    CategoryCode = item.TryGetProperty("categoryCode", out var code)
                        ? code.GetString() ?? "Other"
                        : "Other",
                    FileCount = item.TryGetProperty("fileCount", out var fc) ? fc.GetInt64() : 0,
                    TotalSizeBytes = item.TryGetProperty("totalSizeBytes", out var tb) ? tb.GetInt64() : 0,
                    PercentageOfTotalBytes = item.TryGetProperty("percentageOfTotalBytes", out var pct)
                        ? pct.GetDouble()
                        : 0
                });
            }
        }

        // Collect directory sizes; keep largest if over the cap.
        var directories = new List<(string Path, long Size)>();
        if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in entries.EnumerateArray())
            {
                if ((++i & 0x3FF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var isDir = item.TryGetProperty("isDirectory", out var dirProp) && dirProp.GetBoolean();
                if (!isDir)
                {
                    continue;
                }

                var rel = item.TryGetProperty("relativePath", out var relProp)
                    ? relProp.GetString() ?? string.Empty
                    : string.Empty;
                if (!SnapshotPathNormalizer.TryNormalizeRelative(rel, out var key))
                {
                    continue;
                }

                var size = item.TryGetProperty("sizeBytes", out var sizeProp) ? sizeProp.GetInt64() : 0;
                directories.Add((key, size));
            }
        }

        if (directories.Count > maxDirectoryEntries)
        {
            directories = directories
                .OrderByDescending(d => d.Size)
                .Take(maxDirectoryEntries)
                .ToList();
        }

        var directoryMap = new Dictionary<string, long>(directories.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (path, size) in directories)
        {
            directoryMap[path] = size;
        }

        return new SnapshotTrendPayload
        {
            Id = id,
            CreatedAtUtc = created,
            RootPath = rootPath,
            NormalizedRootPath = normalized,
            TotalBytes = root.TryGetProperty("totalBytes", out var total) ? total.GetInt64() : 0,
            IsIncomplete = root.TryGetProperty("isIncomplete", out var incomplete) && incomplete.GetBoolean(),
            Categories = categories,
            Extensions = extensions,
            DirectorySizes = directoryMap
        };
    }

    private static FileCategory ParseCategory(JsonElement item)
    {
        if (item.TryGetProperty("category", out var catProp))
        {
            var raw = catProp.ValueKind == JsonValueKind.String
                ? catProp.GetString()
                : catProp.GetRawText();
            if (Enum.TryParse<FileCategory>(raw, ignoreCase: true, out var parsed))
            {
                return parsed;
            }
        }

        return FileCategory.Other;
    }

    private static DiskSnapshot CloneWithNewId(DiskSnapshot source, Guid newId) =>
        new()
        {
            SchemaVersion = source.SchemaVersion,
            Id = newId,
            CreatedAtUtc = source.CreatedAtUtc,
            DisplayName = source.DisplayName,
            RootPath = source.RootPath,
            RootName = source.RootName,
            NormalizedRootPath = source.NormalizedRootPath,
            TotalBytes = source.TotalBytes,
            TotalFiles = source.TotalFiles,
            TotalDirectories = source.TotalDirectories,
            DurationSeconds = source.DurationSeconds,
            FinalStatus = source.FinalStatus,
            CompletenessCode = source.CompletenessCode,
            IsIncomplete = source.IsIncomplete,
            Categories = source.Categories,
            Extensions = source.Extensions,
            Entries = source.Entries
        };

    private static bool TryReadInfo(JsonElement root, string filePath, out SnapshotInfo info)
    {
        info = null!;
        if (!root.TryGetProperty("id", out var idProp)
            || !Guid.TryParse(idProp.GetString(), out var id))
        {
            return false;
        }

        var schema = root.TryGetProperty("schemaVersion", out var schemaProp)
            ? schemaProp.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(schema))
        {
            return false;
        }

        var displayName = root.TryGetProperty("displayName", out var nameProp)
            ? nameProp.GetString() ?? id.ToString("N")
            : id.ToString("N");
        var created = root.TryGetProperty("createdAtUtc", out var createdProp)
            && createdProp.TryGetDateTimeOffset(out var dto)
            ? dto
            : DateTimeOffset.MinValue;
        var rootPath = root.TryGetProperty("rootPath", out var rootProp)
            ? rootProp.GetString() ?? string.Empty
            : string.Empty;

        info = new SnapshotInfo
        {
            Id = id,
            DisplayName = displayName,
            CreatedAtUtc = created,
            RootPath = rootPath,
            TotalBytes = root.TryGetProperty("totalBytes", out var bytes) ? bytes.GetInt64() : 0,
            TotalFiles = root.TryGetProperty("totalFiles", out var files) ? files.GetInt64() : 0,
            TotalDirectories = root.TryGetProperty("totalDirectories", out var dirs) ? dirs.GetInt64() : 0,
            IsIncomplete = root.TryGetProperty("isIncomplete", out var incomplete) && incomplete.GetBoolean(),
            SchemaVersion = schema,
            FilePath = filePath
        };
        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best effort — never used for paths outside our temp/sandbox writes
        }
    }
}

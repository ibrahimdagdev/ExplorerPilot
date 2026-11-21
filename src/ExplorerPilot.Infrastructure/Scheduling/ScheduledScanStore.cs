using System.Text.Json;
using ExplorerPilot.Core.Diagnostics;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Scheduling;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.Infrastructure.Scheduling;

public sealed class ScheduledScanStore : IScheduledScanStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<ScheduledScanStore>? _logger;
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ScheduledScanStore(ILogger<ScheduledScanStore>? logger = null)
        : this(DefaultFilePath(), logger)
    {
    }

    public ScheduledScanStore(string filePath, ILogger<ScheduledScanStore>? logger = null)
    {
        _filePath = filePath;
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
    }

    public string StorageFilePath => _filePath;

    public static string DefaultFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExplorerPilot",
            "schedules",
            "scheduled-scans.json");

    public async Task<IReadOnlyList<ScheduledScanDefinition>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            await using var stream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var plans = await JsonSerializer.DeserializeAsync<List<ScheduledScanDefinition>>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            return plans ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Corrupt or unreadable schedule store at {Path}", PathRedaction.ForLog(_filePath));
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(IReadOnlyList<ScheduledScanDefinition> plans, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plans);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);
            var temp = _filePath + ".tmp";
            await using (var stream = new FileStream(
                             temp,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, plans, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Copy(temp, _filePath, overwrite: true);
            File.Delete(temp);
        }
        finally
        {
            _gate.Release();
        }
    }
}

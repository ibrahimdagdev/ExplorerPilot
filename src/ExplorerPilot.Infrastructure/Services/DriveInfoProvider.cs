using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.Infrastructure.Services;

/// <summary>
/// Lists Windows drives and capacity information, safely handling unavailable volumes.
/// Network and slow volumes are probed with a short timeout so discovery cannot hang startup.
/// </summary>
public sealed class DriveInfoProvider : IDriveInfoProvider
{
    private static readonly TimeSpan SlowDriveProbeTimeout = TimeSpan.FromSeconds(1.5);

    private readonly ILogger<DriveInfoProvider>? _logger;

    public DriveInfoProvider(ILogger<DriveInfoProvider>? logger = null)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<DriveInfoModel>> GetDrivesAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => (IReadOnlyList<DriveInfoModel>)GetDrives(cancellationToken), cancellationToken);

    public IReadOnlyList<DriveInfoModel> GetDrives() => GetDrives(CancellationToken.None);

    private List<DriveInfoModel> GetDrives(CancellationToken cancellationToken)
    {
        var results = new List<DriveInfoModel>();

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to enumerate drives");
            return results;
        }

        foreach (var drive in drives)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                results.Add(CreateModelSafely(drive, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Skipping drive {Name}", drive.Name);
                results.Add(CreateUnavailableModel(drive, drive.DriveType.ToString()));
            }
        }

        return results
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private DriveInfoModel CreateModelSafely(DriveInfo drive, CancellationToken cancellationToken)
    {
        // Network / removable / unknown volumes can block for a long time on IsReady / metadata.
        if (drive.DriveType is DriveType.Network or DriveType.Removable or DriveType.Unknown)
        {
            return ProbeWithTimeout(drive, SlowDriveProbeTimeout, cancellationToken);
        }

        return CreateModel(drive);
    }

    private DriveInfoModel ProbeWithTimeout(
        DriveInfo drive,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        try
        {
            var task = Task.Run(() => CreateModel(drive), linked.Token);
            task.Wait(linked.Token);
            return task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger?.LogInformation(
                "Timed out probing {DriveType} drive {Name} after {Timeout}",
                drive.DriveType,
                drive.Name,
                timeout);
            return CreateUnavailableModel(drive, $"{drive.DriveType} (slow/unavailable)");
        }
        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException
                                            && !cancellationToken.IsCancellationRequested)
        {
            _logger?.LogInformation(
                "Timed out probing {DriveType} drive {Name} after {Timeout}",
                drive.DriveType,
                drive.Name,
                timeout);
            return CreateUnavailableModel(drive, $"{drive.DriveType} (slow/unavailable)");
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Probe failed for drive {Name}", drive.Name);
            return CreateUnavailableModel(drive, drive.DriveType.ToString());
        }
    }

    private static DriveInfoModel CreateUnavailableModel(DriveInfo drive, string driveType) =>
        new()
        {
            Name = drive.Name,
            RootPath = drive.Name,
            DriveType = driveType,
            IsReady = false
        };

    private static DriveInfoModel CreateModel(DriveInfo drive)
    {
        // IsReady can block on disconnected network shares — callers for slow types use a timeout.
        if (!drive.IsReady)
        {
            return CreateUnavailableModel(drive, drive.DriveType.ToString());
        }

        string volumeLabel;
        string driveFormat;
        long totalSize;
        long totalFree;
        long availableFree;

        try
        {
            volumeLabel = drive.VolumeLabel;
            driveFormat = drive.DriveFormat;
            totalSize = drive.TotalSize;
            totalFree = drive.TotalFreeSpace;
            availableFree = drive.AvailableFreeSpace;
        }
        catch (Exception)
        {
            return CreateUnavailableModel(drive, drive.DriveType.ToString());
        }

        return new DriveInfoModel
        {
            Name = drive.Name,
            RootPath = drive.RootDirectory.FullName,
            VolumeLabel = volumeLabel,
            DriveType = drive.DriveType.ToString(),
            DriveFormat = driveFormat,
            TotalSizeBytes = totalSize,
            TotalFreeSpaceBytes = totalFree,
            AvailableFreeSpaceBytes = availableFree,
            IsReady = true
        };
    }
}

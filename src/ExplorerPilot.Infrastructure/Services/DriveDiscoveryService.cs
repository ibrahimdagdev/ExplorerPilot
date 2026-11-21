using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using Microsoft.Extensions.Logging;
using IoDriveType = System.IO.DriveType;

namespace ExplorerPilot.Infrastructure.Services;

/// <summary>
/// Windows logical-volume discovery via <see cref="DriveInfo.GetDrives"/>.
/// Network/removable probes use a short timeout so discovery cannot hang the UI.
/// </summary>
public sealed class DriveDiscoveryService : IDriveDiscoveryService
{
    private static readonly TimeSpan SlowDriveProbeTimeout = TimeSpan.FromSeconds(1.5);

    private readonly ILogger<DriveDiscoveryService>? _logger;

    public DriveDiscoveryService(ILogger<DriveDiscoveryService>? logger = null)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<DriveInformation>> DiscoverAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Discover(cancellationToken), cancellationToken);

    public IReadOnlyList<DriveInformation> Discover(CancellationToken cancellationToken = default)
    {
        var results = new List<DriveInformation>();

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
                results.Add(CreateSafely(drive, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Skipping drive {Name}", drive.Name);
                results.Add(CreateUnavailable(drive, MapKind(drive.DriveType), DriveReadyStatus.Unavailable));
            }
        }

        return results
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private DriveInformation CreateSafely(DriveInfo drive, CancellationToken cancellationToken)
    {
        if (drive.DriveType is IoDriveType.Network or IoDriveType.Removable or IoDriveType.Unknown)
        {
            return ProbeWithTimeout(drive, SlowDriveProbeTimeout, cancellationToken);
        }

        return CreateModel(drive);
    }

    private DriveInformation ProbeWithTimeout(
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
            return CreateUnavailable(drive, MapKind(drive.DriveType), DriveReadyStatus.TimedOut);
        }
        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException
                                            && !cancellationToken.IsCancellationRequested)
        {
            _logger?.LogInformation(
                "Timed out probing {DriveType} drive {Name} after {Timeout}",
                drive.DriveType,
                drive.Name,
                timeout);
            return CreateUnavailable(drive, MapKind(drive.DriveType), DriveReadyStatus.TimedOut);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Probe failed for drive {Name}", drive.Name);
            return CreateUnavailable(drive, MapKind(drive.DriveType), DriveReadyStatus.Unavailable);
        }
    }

    private static DriveInformation CreateModel(DriveInfo drive)
    {
        var kind = MapKind(drive.DriveType);

        bool ready;
        try
        {
            ready = drive.IsReady;
        }
        catch
        {
            return CreateUnavailable(drive, kind, DriveReadyStatus.Unavailable);
        }

        if (!ready)
        {
            return CreateUnavailable(drive, kind, DriveReadyStatus.NotReady);
        }

        string volumeLabel;
        string fileSystem;
        long totalSize;
        long totalFree;
        long availableFree;

        try
        {
            volumeLabel = drive.VolumeLabel ?? string.Empty;
            fileSystem = drive.DriveFormat ?? string.Empty;
            totalSize = drive.TotalSize;
            totalFree = drive.TotalFreeSpace;
            availableFree = drive.AvailableFreeSpace;
        }
        catch
        {
            return CreateUnavailable(drive, kind, DriveReadyStatus.Unavailable);
        }

        if (totalSize < 0)
        {
            totalSize = 0;
        }

        if (availableFree < 0)
        {
            availableFree = 0;
        }

        if (totalFree < 0)
        {
            totalFree = 0;
        }

        var used = DriveCapacityMath.SafeUsedBytes(totalSize, availableFree);
        var ratio = DriveCapacityMath.SafeUsageRatio(totalSize, used);

        string rootPath;
        try
        {
            rootPath = drive.RootDirectory.FullName;
        }
        catch
        {
            rootPath = drive.Name;
        }

        return new DriveInformation
        {
            Name = drive.Name,
            RootPath = rootPath,
            VolumeLabel = volumeLabel,
            Kind = kind,
            Status = DriveReadyStatus.Ready,
            FileSystem = fileSystem,
            TotalSizeBytes = totalSize,
            AvailableFreeSpaceBytes = availableFree,
            TotalFreeSpaceBytes = totalFree,
            UsedSpaceBytes = used,
            UsageRatio = ratio,
            UsagePercentage = ratio * 100d
        };
    }

    private static DriveInformation CreateUnavailable(
        DriveInfo drive,
        VolumeDriveKind kind,
        DriveReadyStatus status) =>
        new()
        {
            Name = drive.Name,
            RootPath = drive.Name,
            Kind = kind,
            Status = status
        };

    internal static VolumeDriveKind MapKind(IoDriveType driveType) =>
        driveType switch
        {
            IoDriveType.Fixed => VolumeDriveKind.Fixed,
            IoDriveType.Removable => VolumeDriveKind.Removable,
            IoDriveType.Network => VolumeDriveKind.Network,
            IoDriveType.CDRom => VolumeDriveKind.Optical,
            IoDriveType.Ram => VolumeDriveKind.Ram,
            _ => VolumeDriveKind.Unknown
        };
}

using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.Infrastructure.Services;

/// <summary>
/// Compatibility adapter over <see cref="IDriveDiscoveryService"/> for the v1 drive combo.
/// </summary>
public sealed class DriveInfoProvider : IDriveInfoProvider
{
    private readonly IDriveDiscoveryService _discovery;
    private readonly ILogger<DriveInfoProvider>? _logger;

    public DriveInfoProvider(
        IDriveDiscoveryService discovery,
        ILogger<DriveInfoProvider>? logger = null)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _logger = logger;
    }

    /// <summary>Test / fallback constructor that owns a discovery instance.</summary>
    public DriveInfoProvider(ILogger<DriveInfoProvider>? logger = null)
        : this(new DriveDiscoveryService(), logger)
    {
    }

    public Task<IReadOnlyList<DriveInfoModel>> GetDrivesAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => (IReadOnlyList<DriveInfoModel>)GetDrives(cancellationToken), cancellationToken);

    public IReadOnlyList<DriveInfoModel> GetDrives() => GetDrives(CancellationToken.None);

    private List<DriveInfoModel> GetDrives(CancellationToken cancellationToken)
    {
        try
        {
            return _discovery.Discover(cancellationToken)
                .Select(Map)
                .ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Drive discovery adapter failed");
            return [];
        }
    }

    private static DriveInfoModel Map(DriveInformation drive) =>
        new()
        {
            Name = drive.Name,
            RootPath = drive.RootPath,
            VolumeLabel = drive.VolumeLabel,
            DriveType = drive.Kind.ToString(),
            DriveFormat = drive.FileSystem,
            TotalSizeBytes = drive.TotalSizeBytes,
            TotalFreeSpaceBytes = drive.TotalFreeSpaceBytes,
            AvailableFreeSpaceBytes = drive.AvailableFreeSpaceBytes,
            IsReady = drive.IsReady
        };
}

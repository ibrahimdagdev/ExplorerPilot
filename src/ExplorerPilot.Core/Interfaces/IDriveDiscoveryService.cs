using ExplorerPilot.Core.Models;

namespace ExplorerPilot.Core.Interfaces;

/// <summary>
/// Discovers Windows logical volumes for the multi-drive overview.
/// </summary>
public interface IDriveDiscoveryService
{
    /// <summary>
    /// Enumerates drives on a background thread (UI-safe).
    /// </summary>
    Task<IReadOnlyList<DriveInformation>> DiscoverAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronous discovery. Prefer <see cref="DiscoverAsync"/> from UI code.
    /// </summary>
    IReadOnlyList<DriveInformation> Discover(CancellationToken cancellationToken = default);
}

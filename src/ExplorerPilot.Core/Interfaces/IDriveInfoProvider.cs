using ExplorerPilot.Core.Models;

namespace ExplorerPilot.Core.Interfaces;

/// <summary>
/// Discovers available Windows drives and their capacity information.
/// </summary>
public interface IDriveInfoProvider
{
    /// <summary>
    /// Enumerates drives synchronously. Prefer <see cref="GetDrivesAsync"/> from UI code —
    /// metadata access (especially network/removable volumes) can block for a long time.
    /// </summary>
    IReadOnlyList<DriveInfoModel> GetDrives();

    /// <summary>
    /// Enumerates drives on a background thread so the UI dispatcher stays responsive.
    /// </summary>
    Task<IReadOnlyList<DriveInfoModel>> GetDrivesAsync(CancellationToken cancellationToken = default);
}

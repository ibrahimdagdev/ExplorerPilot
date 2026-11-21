using ExplorerPilot.Core.Models;

namespace ExplorerPilot.Core.Interfaces;

public interface ISystemInformationProvider
{
    /// <summary>
    /// Collects system information on a background-friendly path. Does not require elevation.
    /// </summary>
    Task<SystemInformationModel> GetSystemInformationAsync(CancellationToken cancellationToken = default);
}

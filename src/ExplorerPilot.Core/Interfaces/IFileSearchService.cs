using ExplorerPilot.Core.Models;

namespace ExplorerPilot.Core.Interfaces;

/// <summary>
/// Searches an in-memory scan tree. Never enumerates the filesystem.
/// </summary>
public interface IFileSearchService
{
    Task<FileSearchPage> SearchAsync(
        FileSystemNode root,
        FileSearchCriteria criteria,
        bool fromIncompleteScan,
        CancellationToken cancellationToken = default);
}

namespace ExplorerPilot.App.Services;

public interface IFileActionService
{
    /// <summary>
    /// Opens Explorer and selects the file when possible; otherwise opens the parent folder.
    /// Returns false when the path no longer exists or Explorer could not be started.
    /// </summary>
    bool TryOpenContainingFolder(string fullPath, out string? errorMessage);

    /// <summary>
    /// Copies <paramref name="fullPath"/> to the clipboard.
    /// </summary>
    bool TryCopyPath(string fullPath, out string? errorMessage);
}

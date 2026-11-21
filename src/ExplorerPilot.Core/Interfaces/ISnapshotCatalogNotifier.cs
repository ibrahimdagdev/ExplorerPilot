namespace ExplorerPilot.Core.Interfaces;

/// <summary>
/// Broadcasts snapshot catalog mutations (save/import/delete) so Trends can refresh safely.
/// </summary>
public interface ISnapshotCatalogNotifier
{
    event EventHandler? CatalogChanged;

    void NotifyChanged();
}

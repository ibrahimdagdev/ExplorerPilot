using ExplorerPilot.Core.Interfaces;

namespace ExplorerPilot.Infrastructure.Snapshots;

public sealed class SnapshotCatalogNotifier : ISnapshotCatalogNotifier
{
    public event EventHandler? CatalogChanged;

    public void NotifyChanged() => CatalogChanged?.Invoke(this, EventArgs.Empty);
}

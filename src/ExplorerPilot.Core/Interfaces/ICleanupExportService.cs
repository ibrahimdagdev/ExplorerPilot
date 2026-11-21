using ExplorerPilot.Core.Cleanup;
using ExplorerPilot.Core.Reporting;

namespace ExplorerPilot.Core.Interfaces;

/// <summary>
/// Exports cleanup recommendation previews to CSV/HTML/JSON.
/// Never deletes or modifies the scanned disk files — only writes the chosen report destination.
/// </summary>
public interface ICleanupExportService
{
    Task<ReportExportResult> ExportAsync(
        CleanupExportOptions options,
        IProgress<ReportExportProgress>? progress = null,
        Func<Guid, bool>? sessionStillValid = null,
        CancellationToken cancellationToken = default);
}

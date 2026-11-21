using ExplorerPilot.Core.Reporting;

namespace ExplorerPilot.Core.Interfaces;

/// <summary>
/// Builds a disk analysis report from an existing scan tree and writes CSV, HTML, or JSON.
/// Does not rescan the disk. Implementations must support cancellation and must not leave
/// partial destination files on failure or cancel.
/// </summary>
public interface IReportExportService
{
    /// <summary>
    /// Exports <paramref name="options"/>.ScanResult to <paramref name="options"/>.DestinationPath.
    /// </summary>
    /// <param name="sessionStillValid">
    /// Optional callback invoked before finalizing the file. Return false if the UI session
    /// no longer matches <see cref="ReportExportOptions.ScanSessionId"/>.
    /// </param>
    Task<ReportExportResult> ExportAsync(
        ReportExportOptions options,
        IProgress<ReportExportProgress>? progress = null,
        Func<Guid, bool>? sessionStillValid = null,
        CancellationToken cancellationToken = default);
}

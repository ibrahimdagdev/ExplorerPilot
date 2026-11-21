using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Reporting;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.Infrastructure.Reporting;

public sealed class ReportExportService : IReportExportService
{
    private readonly ILogger<ReportExportService> _logger;

    public ReportExportService(ILogger<ReportExportService> logger)
    {
        _logger = logger;
    }

    public async Task<ReportExportResult> ExportAsync(
        ReportExportOptions options,
        IProgress<ReportExportProgress>? progress = null,
        Func<Guid, bool>? sessionStillValid = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ScanResult);

        if (string.IsNullOrWhiteSpace(options.DestinationPath))
        {
            return ReportExportResult.Failed("Destination path is required.");
        }

        var destination = Path.GetFullPath(options.DestinationPath);
        var directory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return ReportExportResult.Failed("Destination directory is invalid.");
        }

        Directory.CreateDirectory(directory);

        var tempPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            progress?.Report(new ReportExportProgress { Percent = 0, Stage = "build" });
            cancellationToken.ThrowIfCancellationRequested();

            if (sessionStillValid is not null && !sessionStillValid(options.ScanSessionId))
            {
                return ReportExportResult.StaleSession();
            }

            var report = DiskAnalysisReportBuilder.Build(
                options.ScanResult,
                options.ToBuildOptions(),
                cancellationToken);

            progress?.Report(new ReportExportProgress { Percent = 15, Stage = "write" });

            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                switch (options.Format)
                {
                    case ReportFormat.Csv:
                        await CsvReportWriter.WriteAsync(report, options.Labels, stream, progress, cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case ReportFormat.Html:
                        await HtmlReportWriter.WriteAsync(report, options.Labels, stream, progress, cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case ReportFormat.Json:
                        await JsonReportWriter.WriteAsync(report, stream, progress, cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    default:
                        TryDelete(tempPath);
                        return ReportExportResult.Failed("Unsupported report format.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (sessionStillValid is not null && !sessionStillValid(options.ScanSessionId))
            {
                TryDelete(tempPath);
                return ReportExportResult.StaleSession();
            }

            // Replace only after a successful temp write — never delete the existing
            // destination first, so failures leave the previous report intact.
            ReplaceAtomically(tempPath, destination);
            progress?.Report(new ReportExportProgress { Percent = 100, Stage = "done" });
            _logger.LogInformation("Exported {Format} report to {Path}", options.Format, destination);
            return ReportExportResult.Success(destination);
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            return ReportExportResult.Cancelled();
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            _logger.LogError(ex, "Report export failed for {Path}", destination);
            return ReportExportResult.Failed(ex.Message);
        }
    }

    public static void ReplaceAtomically(string tempPath, string destination)
    {
        if (File.Exists(destination))
        {
            // Overwrite in place without deleting first.
            File.Move(tempPath, destination, overwrite: true);
        }
        else
        {
            File.Move(tempPath, destination);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup of partial temp files.
        }
    }
}

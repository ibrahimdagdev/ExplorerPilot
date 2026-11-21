using System.Globalization;
using System.Text;
using System.Text.Json;
using ExplorerPilot.Core.Cleanup;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Reporting;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.Infrastructure.Reporting;

public sealed class CleanupExportService : ICleanupExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<CleanupExportService> _logger;

    public CleanupExportService(ILogger<CleanupExportService> logger)
    {
        _logger = logger;
    }

    public async Task<ReportExportResult> ExportAsync(
        CleanupExportOptions options,
        IProgress<ReportExportProgress>? progress = null,
        Func<Guid, bool>? sessionStillValid = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

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
            progress?.Report(new ReportExportProgress { Percent = 5, Stage = "prepare" });
            if (sessionStillValid is not null && !sessionStillValid(options.ScanSessionId))
            {
                return ReportExportResult.StaleSession();
            }

            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                switch (options.Format)
                {
                    case ReportFormat.Csv:
                        await WriteCsvAsync(options, stream, progress, cancellationToken).ConfigureAwait(false);
                        break;
                    case ReportFormat.Html:
                        await WriteHtmlAsync(options, stream, progress, cancellationToken).ConfigureAwait(false);
                        break;
                    case ReportFormat.Json:
                        await WriteJsonAsync(options, stream, progress, cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        return ReportExportResult.Failed("Unsupported report format.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (sessionStillValid is not null && !sessionStillValid(options.ScanSessionId))
            {
                TryDelete(tempPath);
                return ReportExportResult.StaleSession();
            }

            progress?.Report(new ReportExportProgress { Percent = 95, Stage = "finalize" });
            ReportExportService.ReplaceAtomically(tempPath, destination);
            progress?.Report(new ReportExportProgress { Percent = 100, Stage = "done" });
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
            _logger.LogError(ex, "Cleanup export failed for {Path}", destination);
            return ReportExportResult.Failed(ex.Message);
        }
    }

    private static async Task WriteCsvAsync(
        CleanupExportOptions options,
        Stream stream,
        IProgress<ReportExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        var labels = options.Labels;

        await writer.WriteLineAsync(CsvReportEscaping.Escape("# " + labels.Disclaimer)).ConfigureAwait(false);
        if (options.FromIncompleteScan)
        {
            await writer.WriteLineAsync(CsvReportEscaping.Escape("# " + labels.IncompleteBanner)).ConfigureAwait(false);
        }

        if (options.ResultsTruncated)
        {
            await writer.WriteLineAsync(CsvReportEscaping.Escape("# " + labels.TruncatedBanner)).ConfigureAwait(false);
        }

        await WriteCsvRowAsync(writer,
            labels.Name,
            labels.Path,
            labels.SizeBytes,
            labels.PotentialSavingsBytes,
            labels.LastModifiedUtc,
            labels.Category,
            labels.Risk,
            labels.Reason,
            labels.IsDirectory).ConfigureAwait(false);

        var total = Math.Max(1, options.Candidates.Count);
        for (var i = 0; i < options.Candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var c = options.Candidates[i];
            await WriteCsvRowAsync(writer,
                c.Name,
                c.FullPath,
                c.SizeBytes.ToString(CultureInfo.InvariantCulture),
                c.PotentialSavingsBytes.ToString(CultureInfo.InvariantCulture),
                c.LastModified?.UtcDateTime.ToString("o", CultureInfo.InvariantCulture) ?? string.Empty,
                labels.CategoryFormatter(c.Category),
                labels.RiskFormatter(c.Risk),
                labels.ReasonFormatter(c),
                c.IsDirectory ? "true" : "false").ConfigureAwait(false);

            if ((i & 0xFF) == 0)
            {
                progress?.Report(new ReportExportProgress
                {
                    Percent = 10 + (80.0 * (i + 1) / total),
                    Stage = "csv"
                });
            }
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteCsvRowAsync(StreamWriter writer, params string[] fields)
    {
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0)
            {
                await writer.WriteAsync(',').ConfigureAwait(false);
            }

            await writer.WriteAsync(CsvReportEscaping.Escape(fields[i])).ConfigureAwait(false);
        }

        await writer.WriteLineAsync().ConfigureAwait(false);
    }

    private static async Task WriteHtmlAsync(
        CleanupExportOptions options,
        Stream stream,
        IProgress<ReportExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        var labels = options.Labels;
        progress?.Report(new ReportExportProgress { Percent = 15, Stage = "html-head" });

        await writer.WriteAsync("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><title>")
            .ConfigureAwait(false);
        await writer.WriteAsync(HtmlReportEscaping.Escape(labels.Title)).ConfigureAwait(false);
        await writer.WriteAsync("</title><style>body{font-family:Segoe UI,sans-serif;margin:24px;}table{border-collapse:collapse;width:100%;}th,td{border:1px solid #ccc;padding:6px 8px;text-align:left;}th{background:#f3f4f6;}.banner{padding:10px;margin:12px 0;background:#fff7ed;border:1px solid #fed7aa;}.disclaimer{padding:10px;margin:12px 0;background:#eff6ff;border:1px solid #bfdbfe;}</style></head><body>")
            .ConfigureAwait(false);

        await writer.WriteAsync("<h1>").ConfigureAwait(false);
        await writer.WriteAsync(HtmlReportEscaping.Escape(labels.Title)).ConfigureAwait(false);
        await writer.WriteAsync("</h1>").ConfigureAwait(false);

        await writer.WriteAsync("<div class=\"disclaimer\"><strong>")
            .ConfigureAwait(false);
        await writer.WriteAsync(HtmlReportEscaping.Escape(labels.Disclaimer)).ConfigureAwait(false);
        await writer.WriteAsync("</strong></div>").ConfigureAwait(false);

        if (options.FromIncompleteScan)
        {
            await writer.WriteAsync("<div class=\"banner\">").ConfigureAwait(false);
            await writer.WriteAsync(HtmlReportEscaping.Escape(labels.IncompleteBanner)).ConfigureAwait(false);
            await writer.WriteAsync("</div>").ConfigureAwait(false);
        }

        if (options.ResultsTruncated)
        {
            await writer.WriteAsync("<div class=\"banner\">").ConfigureAwait(false);
            await writer.WriteAsync(HtmlReportEscaping.Escape(labels.TruncatedBanner)).ConfigureAwait(false);
            await writer.WriteAsync("</div>").ConfigureAwait(false);
        }

        await writer.WriteAsync("<p>").ConfigureAwait(false);
        await writer.WriteAsync(HtmlReportEscaping.Escape(labels.GeneratedAt)).ConfigureAwait(false);
        await writer.WriteAsync(": ").ConfigureAwait(false);
        await writer.WriteAsync(HtmlReportEscaping.Escape(DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await writer.WriteAsync("<br/>").ConfigureAwait(false);
        await writer.WriteAsync(HtmlReportEscaping.Escape(labels.UniquePotential)).ConfigureAwait(false);
        await writer.WriteAsync(": ").ConfigureAwait(false);
        await writer.WriteAsync(options.UniquePotentialSavingsBytes.ToString("N0", CultureInfo.InvariantCulture))
            .ConfigureAwait(false);
        await writer.WriteAsync("<br/>").ConfigureAwait(false);
        await writer.WriteAsync(HtmlReportEscaping.Escape(labels.UniquePaths)).ConfigureAwait(false);
        await writer.WriteAsync(": ").ConfigureAwait(false);
        await writer.WriteAsync(options.UniquePathCount.ToString("N0", CultureInfo.InvariantCulture))
            .ConfigureAwait(false);
        await writer.WriteAsync("<br/>").ConfigureAwait(false);
        await writer.WriteAsync(HtmlReportEscaping.Escape(labels.CandidateCount)).ConfigureAwait(false);
        await writer.WriteAsync(": ").ConfigureAwait(false);
        await writer.WriteAsync(options.Candidates.Count.ToString("N0", CultureInfo.InvariantCulture))
            .ConfigureAwait(false);
        await writer.WriteAsync("</p>").ConfigureAwait(false);

        await writer.WriteAsync("<table><thead><tr>").ConfigureAwait(false);
        foreach (var header in new[]
                 {
                     labels.Name, labels.Path, labels.SizeBytes, labels.PotentialSavingsBytes,
                     labels.LastModifiedUtc, labels.Category, labels.Risk, labels.Reason
                 })
        {
            await writer.WriteAsync("<th>").ConfigureAwait(false);
            await writer.WriteAsync(HtmlReportEscaping.Escape(header)).ConfigureAwait(false);
            await writer.WriteAsync("</th>").ConfigureAwait(false);
        }

        await writer.WriteAsync("</tr></thead><tbody>").ConfigureAwait(false);

        var total = Math.Max(1, options.Candidates.Count);
        for (var i = 0; i < options.Candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var c = options.Candidates[i];
            await writer.WriteAsync("<tr>").ConfigureAwait(false);
            await WriteHtmlCell(writer, c.Name).ConfigureAwait(false);
            await WriteHtmlCell(writer, c.FullPath).ConfigureAwait(false);
            await WriteHtmlCell(writer, c.SizeBytes.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            await WriteHtmlCell(writer, c.PotentialSavingsBytes.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            await WriteHtmlCell(writer, c.LastModified?.UtcDateTime.ToString("o", CultureInfo.InvariantCulture) ?? string.Empty)
                .ConfigureAwait(false);
            await WriteHtmlCell(writer, labels.CategoryFormatter(c.Category)).ConfigureAwait(false);
            await WriteHtmlCell(writer, labels.RiskFormatter(c.Risk)).ConfigureAwait(false);
            await WriteHtmlCell(writer, labels.ReasonFormatter(c)).ConfigureAwait(false);
            await writer.WriteAsync("</tr>").ConfigureAwait(false);

            if ((i & 0xFF) == 0)
            {
                progress?.Report(new ReportExportProgress
                {
                    Percent = 20 + (70.0 * (i + 1) / total),
                    Stage = "html"
                });
            }
        }

        await writer.WriteAsync("</tbody></table></body></html>").ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteHtmlCell(StreamWriter writer, string value)
    {
        await writer.WriteAsync("<td>").ConfigureAwait(false);
        await writer.WriteAsync(HtmlReportEscaping.Escape(value)).ConfigureAwait(false);
        await writer.WriteAsync("</td>").ConfigureAwait(false);
    }

    private static async Task WriteJsonAsync(
        CleanupExportOptions options,
        Stream stream,
        IProgress<ReportExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new ReportExportProgress { Percent = 20, Stage = "json" });
        var payload = new
        {
            schema = "ExplorerPilot.CleanupPreview.1.0",
            disclaimer = options.Labels.Disclaimer,
            isDeletionInstruction = false,
            fromIncompleteScan = options.FromIncompleteScan,
            resultsTruncated = options.ResultsTruncated,
            generatedAtUtc = DateTimeOffset.UtcNow,
            uniquePotentialSavingsBytes = options.UniquePotentialSavingsBytes,
            uniquePathCount = options.UniquePathCount,
            candidates = options.Candidates.Select(c => new
            {
                c.Name,
                c.FullPath,
                c.PathKey,
                c.SizeBytes,
                c.PotentialSavingsBytes,
                lastModifiedUtc = c.LastModified,
                category = c.Category.ToString(),
                risk = c.Risk.ToString(),
                reason = options.Labels.ReasonFormatter(c),
                c.IsDirectory
            })
        };

        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new ReportExportProgress { Percent = 90, Stage = "json" });
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
            // best effort
        }
    }
}

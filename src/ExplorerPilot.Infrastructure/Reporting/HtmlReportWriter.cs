using System.Globalization;
using System.Text;
using ExplorerPilot.Core.Reporting;

namespace ExplorerPilot.Infrastructure.Reporting;

/// <summary>
/// Streams an offline HTML report section-by-section to avoid holding the full document in memory.
/// </summary>
internal static class HtmlReportWriter
{
    private const int FlushEveryNodes = 256;

    public static async Task WriteAsync(
        DiskAnalysisReport report,
        ReportDisplayLabels labels,
        Stream stream,
        IProgress<ReportExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 64 * 1024,
            leaveOpen: true);

        progress?.Report(new ReportExportProgress { Percent = 10, Stage = "html-head" });
        await WriteHeadAsync(writer, report, labels, cancellationToken).ConfigureAwait(false);

        progress?.Report(new ReportExportProgress { Percent = 20, Stage = "summary" });
        await WriteSummaryAsync(writer, report, labels, cancellationToken).ConfigureAwait(false);

        if (report.Categories.Count > 0)
        {
            progress?.Report(new ReportExportProgress { Percent = 35, Stage = "categories" });
            await WriteCategoriesAsync(writer, report, labels, cancellationToken).ConfigureAwait(false);
        }

        progress?.Report(new ReportExportProgress { Percent = 50, Stage = "largest-files" });
        await WriteLargestFilesAsync(writer, report, labels, cancellationToken).ConfigureAwait(false);

        progress?.Report(new ReportExportProgress { Percent = 65, Stage = "largest-folders" });
        await WriteLargestFoldersAsync(writer, report, labels, cancellationToken).ConfigureAwait(false);

        if (report.Extensions.Count > 0)
        {
            progress?.Report(new ReportExportProgress { Percent = 78, Stage = "extensions" });
            await WriteExtensionsAsync(writer, report, labels, cancellationToken).ConfigureAwait(false);
        }

        if (report.Errors.Count > 0 || labels.ErrorsSection.Length > 0)
        {
            // Always emit errors section when present; skip empty when omitted from build.
            if (report.Errors.Count > 0)
            {
                progress?.Report(new ReportExportProgress { Percent = 88, Stage = "errors" });
                await WriteErrorsAsync(writer, report, labels, cancellationToken).ConfigureAwait(false);
            }
        }

        if (report.DirectoryTree is not null)
        {
            progress?.Report(new ReportExportProgress { Percent = 92, Stage = "tree" });
            await WriteTreeAsync(writer, report.DirectoryTree, labels, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await writer.WriteAsync("<footer class=\"muted\">Schema ").ConfigureAwait(false);
        await WriteEscapedAsync(writer, report.SchemaVersion, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(" · ").ConfigureAwait(false);
        await WriteEscapedAsync(
            writer,
            report.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</footer></main></body></html>").ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new ReportExportProgress { Percent = 100, Stage = "done" });
    }

    private static async Task WriteHeadAsync(
        StreamWriter writer,
        DiskAnalysisReport report,
        ReportDisplayLabels labels,
        CancellationToken cancellationToken)
    {
        await writer.WriteAsync("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"/>").ConfigureAwait(false);
        await writer.WriteAsync("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>").ConfigureAwait(false);
        await writer.WriteAsync("<title>").ConfigureAwait(false);
        await WriteEscapedAsync(writer, labels.ReportTitle, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</title><style>").ConfigureAwait(false);
        await writer.WriteAsync(GetCss()).ConfigureAwait(false);
        await writer.WriteAsync("</style></head><body><main class=\"page\"><header class=\"hero\"><h1>").ConfigureAwait(false);
        await WriteEscapedAsync(writer, labels.ReportTitle, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</h1><p class=\"muted\">").ConfigureAwait(false);
        await WriteEscapedAsync(writer, report.Summary.RootPath, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</p>").ConfigureAwait(false);
        if (report.Summary.IsIncomplete)
        {
            await writer.WriteAsync("<div class=\"banner\">").ConfigureAwait(false);
            await WriteEscapedAsync(writer, labels.IncompleteBanner, cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync("</div>").ConfigureAwait(false);
        }

        await writer.WriteAsync("</header>").ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteSummaryAsync(
        StreamWriter writer,
        DiskAnalysisReport report,
        ReportDisplayLabels labels,
        CancellationToken cancellationToken)
    {
        var s = report.Summary;
        await writer.WriteAsync("<section><h2>").ConfigureAwait(false);
        await WriteEscapedAsync(writer, labels.SummarySection, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</h2><div class=\"cards\">").ConfigureAwait(false);
        await WriteCardAsync(writer, labels.TotalSize, s.TotalBytes.ToString("N0", CultureInfo.InvariantCulture) + " B", cancellationToken).ConfigureAwait(false);
        await WriteCardAsync(writer, labels.TotalFiles, s.TotalFiles.ToString("N0", CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        await WriteCardAsync(writer, labels.TotalFolders, s.TotalDirectories.ToString("N0", CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        await WriteCardAsync(writer, labels.Completeness, labels.CompletenessText(s.Completeness), cancellationToken).ConfigureAwait(false);
        await WriteCardAsync(writer, labels.Duration, s.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " s", cancellationToken).ConfigureAwait(false);
        await WriteCardAsync(writer, labels.ErrorCount, s.ErrorCount.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</div></section>").ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteCategoriesAsync(
        StreamWriter writer,
        DiskAnalysisReport report,
        ReportDisplayLabels labels,
        CancellationToken cancellationToken)
    {
        await writer.WriteAsync("<section><h2>").ConfigureAwait(false);
        await WriteEscapedAsync(writer, labels.CategoriesSection, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</h2><div class=\"bars\">").ConfigureAwait(false);
        foreach (var category in report.Categories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pct = Math.Clamp(category.PercentageOfTotalBytes, 0, 100);
            await writer.WriteAsync("<div class=\"bar-row\"><div class=\"bar-label\">").ConfigureAwait(false);
            await WriteEscapedAsync(writer, labels.CategoryName(category.Category), cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync("</div><div class=\"bar-track\"><div class=\"bar-fill\" style=\"width:").ConfigureAwait(false);
            await writer.WriteAsync(pct.ToString("0.##", CultureInfo.InvariantCulture)).ConfigureAwait(false);
            await writer.WriteAsync("%\"></div></div><div class=\"bar-meta\">").ConfigureAwait(false);
            await WriteEscapedAsync(writer, pct.ToString("0.##", CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync("% · ").ConfigureAwait(false);
            await WriteEscapedAsync(writer, category.TotalSizeBytes.ToString("N0", CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync(" B</div></div>").ConfigureAwait(false);
        }

        await writer.WriteAsync("</div></section>").ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteLargestFilesAsync(
        StreamWriter writer,
        DiskAnalysisReport report,
        ReportDisplayLabels labels,
        CancellationToken cancellationToken)
    {
        await writer.WriteAsync("<section><h2>").ConfigureAwait(false);
        await WriteEscapedAsync(writer, labels.LargestFilesSection, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</h2><div class=\"table-wrap\"><table><thead><tr>").ConfigureAwait(false);
        await WriteThAsync(writer, labels.Name, cancellationToken).ConfigureAwait(false);
        await WriteThAsync(writer, labels.Path, cancellationToken).ConfigureAwait(false);
        await WriteThAsync(writer, labels.SizeBytes, cancellationToken).ConfigureAwait(false);
        await WriteThAsync(writer, labels.Extension, cancellationToken).ConfigureAwait(false);
        await WriteThAsync(writer, labels.Category, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</tr></thead><tbody>").ConfigureAwait(false);

        var row = 0;
        foreach (var file in report.LargestFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteAsync("<tr>").ConfigureAwait(false);
            await WriteTdAsync(writer, file.Name, cancellationToken).ConfigureAwait(false);
            await WriteTdAsync(writer, file.FullPath, cancellationToken).ConfigureAwait(false);
            await WriteTdAsync(writer, file.SizeBytes.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
            await WriteTdAsync(writer, file.Extension, cancellationToken).ConfigureAwait(false);
            await WriteTdAsync(writer, labels.CategoryName(file.Category), cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync("</tr>").ConfigureAwait(false);
            if ((++row & 0x3F) == 0)
            {
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await writer.WriteAsync("</tbody></table></div></section>").ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteLargestFoldersAsync(
        StreamWriter writer,
        DiskAnalysisReport report,
        ReportDisplayLabels labels,
        CancellationToken cancellationToken)
    {
        await writer.WriteAsync("<section><h2>").ConfigureAwait(false);
        await WriteEscapedAsync(writer, labels.LargestFoldersSection, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</h2><div class=\"table-wrap\"><table><thead><tr>").ConfigureAwait(false);
        await WriteThAsync(writer, labels.Name, cancellationToken).ConfigureAwait(false);
        await WriteThAsync(writer, labels.Path, cancellationToken).ConfigureAwait(false);
        await WriteThAsync(writer, labels.SizeBytes, cancellationToken).ConfigureAwait(false);
        await WriteThAsync(writer, labels.FileCount, cancellationToken).ConfigureAwait(false);
        await WriteThAsync(writer, labels.DirectoryCount, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</tr></thead><tbody>").ConfigureAwait(false);

        foreach (var folder in report.LargestFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteAsync("<tr>").ConfigureAwait(false);
            await WriteTdAsync(writer, folder.Name, cancellationToken).ConfigureAwait(false);
            await WriteTdAsync(writer, folder.FullPath, cancellationToken).ConfigureAwait(false);
            await WriteTdAsync(writer, folder.SizeBytes.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
            await WriteTdAsync(writer, folder.FileCount.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
            await WriteTdAsync(writer, folder.DirectoryCount.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync("</tr>").ConfigureAwait(false);
        }

        await writer.WriteAsync("</tbody></table></div></section>").ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteExtensionsAsync(
        StreamWriter writer,
        DiskAnalysisReport report,
        ReportDisplayLabels labels,
        CancellationToken cancellationToken)
    {
        await writer.WriteAsync("<section><h2>").ConfigureAwait(false);
        await WriteEscapedAsync(writer, labels.ExtensionsSection, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</h2><div class=\"table-wrap\"><table><thead><tr>").ConfigureAwait(false);
        await WriteThAsync(writer, labels.Extension, cancellationToken).ConfigureAwait(false);
        await WriteThAsync(writer, labels.Category, cancellationToken).ConfigureAwait(false);
        await WriteThAsync(writer, labels.FileCount, cancellationToken).ConfigureAwait(false);
        await WriteThAsync(writer, labels.SizeBytes, cancellationToken).ConfigureAwait(false);
        await WriteThAsync(writer, labels.Percentage, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</tr></thead><tbody>").ConfigureAwait(false);

        foreach (var extension in report.Extensions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteAsync("<tr>").ConfigureAwait(false);
            await WriteTdAsync(writer, string.IsNullOrEmpty(extension.Extension) ? "(none)" : extension.Extension, cancellationToken).ConfigureAwait(false);
            await WriteTdAsync(writer, labels.CategoryName(extension.Category), cancellationToken).ConfigureAwait(false);
            await WriteTdAsync(writer, extension.FileCount.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
            await WriteTdAsync(writer, extension.TotalSizeBytes.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
            await WriteTdAsync(writer, extension.PercentageOfTotalBytes.ToString("0.####", CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync("</tr>").ConfigureAwait(false);
        }

        await writer.WriteAsync("</tbody></table></div></section>").ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteErrorsAsync(
        StreamWriter writer,
        DiskAnalysisReport report,
        ReportDisplayLabels labels,
        CancellationToken cancellationToken)
    {
        await writer.WriteAsync("<section><h2>").ConfigureAwait(false);
        await WriteEscapedAsync(writer, labels.ErrorsSection, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</h2><div class=\"table-wrap\"><table><thead><tr>").ConfigureAwait(false);
        await WriteThAsync(writer, labels.Path, cancellationToken).ConfigureAwait(false);
        await WriteThAsync(writer, labels.ErrorType, cancellationToken).ConfigureAwait(false);
        await WriteThAsync(writer, labels.Message, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</tr></thead><tbody>").ConfigureAwait(false);

        foreach (var error in report.Errors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteAsync("<tr>").ConfigureAwait(false);
            await WriteTdAsync(writer, error.Path, cancellationToken).ConfigureAwait(false);
            await WriteTdAsync(writer, error.ErrorTypeCode, cancellationToken).ConfigureAwait(false);
            await WriteTdAsync(writer, error.Message, cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync("</tr>").ConfigureAwait(false);
        }

        await writer.WriteAsync("</tbody></table></div></section>").ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteTreeAsync(
        StreamWriter writer,
        ReportDirectoryNode root,
        ReportDisplayLabels labels,
        CancellationToken cancellationToken)
    {
        await writer.WriteAsync("<section><h2>").ConfigureAwait(false);
        await WriteEscapedAsync(writer, labels.DirectoryTreeSection, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</h2><ul class=\"tree\">").ConfigureAwait(false);

        var counter = new WriteCounter();
        await WriteTreeNodeAsync(writer, root, counter, cancellationToken).ConfigureAwait(false);

        await writer.WriteAsync("</ul></section>").ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class WriteCounter
    {
        public int Value;
    }

    private static async Task WriteTreeNodeAsync(
        StreamWriter writer,
        ReportDirectoryNode node,
        WriteCounter written,
        CancellationToken cancellationToken)
    {
        if ((++written.Value % FlushEveryNodes) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        await writer.WriteAsync("<li><span class=\"tree-name\">").ConfigureAwait(false);
        await WriteEscapedAsync(writer, node.Name, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</span><span class=\"muted\"> · ").ConfigureAwait(false);
        await WriteEscapedAsync(writer, node.SizeBytes.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(" B</span>").ConfigureAwait(false);

        if (node.Children.Count > 0)
        {
            await writer.WriteAsync("<ul>").ConfigureAwait(false);
            foreach (var child in node.Children)
            {
                await WriteTreeNodeAsync(writer, child, written, cancellationToken).ConfigureAwait(false);
            }

            await writer.WriteAsync("</ul>").ConfigureAwait(false);
        }

        await writer.WriteAsync("</li>").ConfigureAwait(false);
    }

    private static async Task WriteCardAsync(StreamWriter writer, string label, string value, CancellationToken cancellationToken)
    {
        await writer.WriteAsync("<div class=\"card\"><div class=\"label\">").ConfigureAwait(false);
        await WriteEscapedAsync(writer, label, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</div><div class=\"value\">").ConfigureAwait(false);
        await WriteEscapedAsync(writer, value, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</div></div>").ConfigureAwait(false);
    }

    private static async Task WriteThAsync(StreamWriter writer, string text, CancellationToken cancellationToken)
    {
        await writer.WriteAsync("<th>").ConfigureAwait(false);
        await WriteEscapedAsync(writer, text, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</th>").ConfigureAwait(false);
    }

    private static async Task WriteTdAsync(StreamWriter writer, string text, CancellationToken cancellationToken)
    {
        await writer.WriteAsync("<td>").ConfigureAwait(false);
        await WriteEscapedAsync(writer, text, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync("</td>").ConfigureAwait(false);
    }

    private static Task WriteEscapedAsync(StreamWriter writer, string? value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return writer.WriteAsync(HtmlReportEscaping.Escape(value));
    }

    private static string GetCss() => """
:root{color-scheme:light dark;--bg:#f4f6f8;--card:#fff;--text:#1a1d21;--muted:#5c6570;--accent:#0b6e4f;--border:#d7dde3;--banner:#fff4e5;--banner-text:#7a4d00}
@media (prefers-color-scheme:dark){:root{--bg:#12151a;--card:#1c2128;--text:#e8eef4;--muted:#9aa7b5;--accent:#3dd68c;--border:#2c3440;--banner:#3a2a12;--banner-text:#ffd79a}}
*{box-sizing:border-box}body{margin:0;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;background:var(--bg);color:var(--text);line-height:1.45}
.page{max-width:1100px;margin:0 auto;padding:24px 16px 48px}.hero h1{margin:0 0 8px;font-size:1.75rem}.muted{color:var(--muted)}
.banner{margin-top:12px;padding:10px 12px;border-radius:8px;background:var(--banner);color:var(--banner-text);font-weight:600}
.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:12px;margin:16px 0 8px}
.card{background:var(--card);border:1px solid var(--border);border-radius:12px;padding:14px}.card .label{font-size:.75rem;text-transform:uppercase;letter-spacing:.04em;color:var(--muted)}.card .value{margin-top:6px;font-size:1.15rem;font-weight:700}
section{margin-top:28px}h2{margin:0 0 12px;font-size:1.2rem}
.table-wrap{overflow:auto;border:1px solid var(--border);border-radius:12px;background:var(--card)}
table{width:100%;border-collapse:collapse;font-size:.92rem}th,td{padding:10px 12px;border-bottom:1px solid var(--border);text-align:left;vertical-align:top}
th{font-size:.72rem;letter-spacing:.04em;text-transform:uppercase;color:var(--muted);background:transparent}tr:last-child td{border-bottom:none}
.bars{display:flex;flex-direction:column;gap:10px}.bar-row{display:grid;grid-template-columns:140px 1fr auto;gap:10px;align-items:center}
.bar-track{height:10px;background:var(--border);border-radius:999px;overflow:hidden}.bar-fill{height:100%;background:var(--accent)}.bar-meta{font-size:.85rem;color:var(--muted);white-space:nowrap}
.tree{list-style:none;padding-left:0}.tree ul{list-style:none;padding-left:18px;margin:4px 0;border-left:1px solid var(--border)}.tree-name{font-weight:600}
footer{margin-top:36px;font-size:.85rem}
@media (max-width:700px){.bar-row{grid-template-columns:1fr}.bar-meta{justify-self:start}}
""";
}

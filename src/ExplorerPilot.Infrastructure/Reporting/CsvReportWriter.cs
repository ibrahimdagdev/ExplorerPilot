using System.Globalization;
using System.Text;
using ExplorerPilot.Core.Reporting;

namespace ExplorerPilot.Infrastructure.Reporting;

internal static class CsvReportWriter
{
    public static async Task WriteAsync(
        DiskAnalysisReport report,
        ReportDisplayLabels labels,
        Stream stream,
        IProgress<ReportExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        // UTF-8 with BOM for Excel compatibility.
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), bufferSize: 64 * 1024, leaveOpen: true);

        progress?.Report(new ReportExportProgress { Percent = 5, Stage = "summary" });
        await WriteSectionHeaderAsync(writer, labels.SummarySection, cancellationToken).ConfigureAwait(false);
        await WriteRowAsync(writer, cancellationToken, labels.RootPath, report.Summary.RootPath).ConfigureAwait(false);
        await WriteRowAsync(writer, cancellationToken, labels.GeneratedAt, report.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture)).ConfigureAwait(false);
        await WriteRowAsync(writer, cancellationToken, labels.TotalSize, report.Summary.TotalBytes.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        await WriteRowAsync(writer, cancellationToken, labels.TotalFiles, report.Summary.TotalFiles.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        await WriteRowAsync(writer, cancellationToken, labels.TotalFolders, report.Summary.TotalDirectories.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        await WriteRowAsync(writer, cancellationToken, labels.Duration, report.Summary.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture)).ConfigureAwait(false);
        await WriteRowAsync(writer, cancellationToken, labels.Completeness, labels.CompletenessText(report.Summary.Completeness)).ConfigureAwait(false);
        await WriteRowAsync(writer, cancellationToken, labels.ErrorCount, report.Summary.ErrorCount.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        if (report.Summary.IsIncomplete)
        {
            await WriteRowAsync(writer, cancellationToken, "Note", labels.IncompleteBanner).ConfigureAwait(false);
        }

        await writer.WriteLineAsync().ConfigureAwait(false);
        progress?.Report(new ReportExportProgress { Percent = 25, Stage = "largest-files" });
        await WriteSectionHeaderAsync(writer, labels.LargestFilesSection, cancellationToken).ConfigureAwait(false);
        await WriteRowAsync(writer, cancellationToken, labels.Name, labels.Path, labels.SizeBytes, labels.Extension, labels.Category, labels.LastModifiedUtc).ConfigureAwait(false);
        foreach (var file in report.LargestFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteRowAsync(
                writer,
                cancellationToken,
                file.Name,
                file.FullPath,
                file.SizeBytes.ToString(CultureInfo.InvariantCulture),
                file.Extension,
                labels.CategoryName(file.Category),
                file.LastModifiedUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty).ConfigureAwait(false);
        }

        await writer.WriteLineAsync().ConfigureAwait(false);
        progress?.Report(new ReportExportProgress { Percent = 45, Stage = "largest-folders" });
        await WriteSectionHeaderAsync(writer, labels.LargestFoldersSection, cancellationToken).ConfigureAwait(false);
        await WriteRowAsync(writer, cancellationToken, labels.Name, labels.Path, labels.SizeBytes, labels.FileCount, labels.DirectoryCount).ConfigureAwait(false);
        foreach (var folder in report.LargestFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteRowAsync(
                writer,
                cancellationToken,
                folder.Name,
                folder.FullPath,
                folder.SizeBytes.ToString(CultureInfo.InvariantCulture),
                folder.FileCount.ToString(CultureInfo.InvariantCulture),
                folder.DirectoryCount.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        }

        if (report.Categories.Count > 0)
        {
            await writer.WriteLineAsync().ConfigureAwait(false);
            progress?.Report(new ReportExportProgress { Percent = 60, Stage = "categories" });
            await WriteSectionHeaderAsync(writer, labels.CategoriesSection, cancellationToken).ConfigureAwait(false);
            await WriteRowAsync(writer, cancellationToken, labels.Category, labels.FileCount, labels.SizeBytes, labels.Percentage).ConfigureAwait(false);
            foreach (var category in report.Categories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteRowAsync(
                    writer,
                    cancellationToken,
                    labels.CategoryName(category.Category),
                    category.FileCount.ToString(CultureInfo.InvariantCulture),
                    category.TotalSizeBytes.ToString(CultureInfo.InvariantCulture),
                    category.PercentageOfTotalBytes.ToString("0.####", CultureInfo.InvariantCulture)).ConfigureAwait(false);
            }
        }

        if (report.Extensions.Count > 0)
        {
            await writer.WriteLineAsync().ConfigureAwait(false);
            progress?.Report(new ReportExportProgress { Percent = 75, Stage = "extensions" });
            await WriteSectionHeaderAsync(writer, labels.ExtensionsSection, cancellationToken).ConfigureAwait(false);
            await WriteRowAsync(writer, cancellationToken, labels.Extension, labels.Category, labels.FileCount, labels.SizeBytes, labels.Percentage).ConfigureAwait(false);
            foreach (var extension in report.Extensions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteRowAsync(
                    writer,
                    cancellationToken,
                    string.IsNullOrEmpty(extension.Extension) ? "(none)" : extension.Extension,
                    labels.CategoryName(extension.Category),
                    extension.FileCount.ToString(CultureInfo.InvariantCulture),
                    extension.TotalSizeBytes.ToString(CultureInfo.InvariantCulture),
                    extension.PercentageOfTotalBytes.ToString("0.####", CultureInfo.InvariantCulture)).ConfigureAwait(false);
            }
        }

        if (report.Errors.Count > 0)
        {
            await writer.WriteLineAsync().ConfigureAwait(false);
            progress?.Report(new ReportExportProgress { Percent = 85, Stage = "errors" });
            await WriteSectionHeaderAsync(writer, labels.ErrorsSection, cancellationToken).ConfigureAwait(false);
            await WriteRowAsync(writer, cancellationToken, labels.Path, labels.ErrorType, labels.Message).ConfigureAwait(false);
            foreach (var error in report.Errors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteRowAsync(writer, cancellationToken, error.Path, error.ErrorTypeCode, error.Message).ConfigureAwait(false);
            }
        }

        if (report.DirectoryTree is not null)
        {
            await writer.WriteLineAsync().ConfigureAwait(false);
            progress?.Report(new ReportExportProgress { Percent = 90, Stage = "tree" });
            await WriteSectionHeaderAsync(writer, labels.DirectoryTreeSection, cancellationToken).ConfigureAwait(false);
            await WriteRowAsync(writer, cancellationToken, "Depth", labels.Name, labels.Path, labels.SizeBytes, labels.FileCount, labels.DirectoryCount, labels.ScanStatus).ConfigureAwait(false);
            await WriteTreeAsync(writer, report.DirectoryTree, depth: 0, cancellationToken).ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new ReportExportProgress { Percent = 100, Stage = "done" });
    }

    private static async Task WriteTreeAsync(
        StreamWriter writer,
        ReportDirectoryNode node,
        int depth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WriteRowAsync(
            writer,
            cancellationToken,
            depth.ToString(CultureInfo.InvariantCulture),
            node.Name,
            node.FullPath,
            node.SizeBytes.ToString(CultureInfo.InvariantCulture),
            node.FileCount.ToString(CultureInfo.InvariantCulture),
            node.DirectoryCount.ToString(CultureInfo.InvariantCulture),
            node.ScanStatusCode).ConfigureAwait(false);

        foreach (var child in node.Children)
        {
            await WriteTreeAsync(writer, child, depth + 1, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task WriteSectionHeaderAsync(StreamWriter writer, string title, CancellationToken cancellationToken) =>
        WriteRowAsync(writer, cancellationToken, "# " + title);

    private static async Task WriteRowAsync(StreamWriter writer, CancellationToken cancellationToken, params string[] fields)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
}

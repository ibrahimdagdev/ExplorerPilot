using System.Text.Json;
using System.Text.Json.Serialization;
using ExplorerPilot.Core.Reporting;

namespace ExplorerPilot.Infrastructure.Reporting;

internal static class JsonReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task WriteAsync(
        DiskAnalysisReport report,
        Stream stream,
        IProgress<ReportExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new ReportExportProgress { Percent = 20, Stage = "serialize" });
        await JsonSerializer.SerializeAsync(stream, report, Options, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new ReportExportProgress { Percent = 100, Stage = "done" });
    }
}

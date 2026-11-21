using System.Text.Json;
using System.Text.Json.Serialization;
using ExplorerPilot.Core.Snapshots;

namespace ExplorerPilot.Infrastructure.Snapshots;

internal static class SnapshotJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static SnapshotInfo ToInfo(DiskSnapshot snapshot, string filePath) =>
        new()
        {
            Id = snapshot.Id,
            DisplayName = snapshot.DisplayName,
            CreatedAtUtc = snapshot.CreatedAtUtc,
            RootPath = snapshot.RootPath,
            TotalBytes = snapshot.TotalBytes,
            TotalFiles = snapshot.TotalFiles,
            TotalDirectories = snapshot.TotalDirectories,
            IsIncomplete = snapshot.IsIncomplete,
            SchemaVersion = snapshot.SchemaVersion,
            FilePath = filePath
        };
}

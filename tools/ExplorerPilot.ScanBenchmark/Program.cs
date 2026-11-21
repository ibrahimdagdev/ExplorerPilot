using System.Globalization;
using System.Text;
using ExplorerPilot.Infrastructure.Scanning;
using ExplorerPilot.ScanBenchmark;

var sizes = new[] { 1_000, 10_000, 100_000 };
var iterations = 5;
var keepFixtures = args.Contains("--keep-fixtures", StringComparer.OrdinalIgnoreCase);
var includeOptional = args.Contains("--optional-250k", StringComparer.OrdinalIgnoreCase);
var compareModes = !args.Contains("--single-mode", StringComparer.OrdinalIgnoreCase);
if (includeOptional)
{
    sizes = sizes.Concat([250_000]).ToArray();
}

var benchRoot = Path.Combine(Path.GetTempPath(), "ExplorerPilot.ScanBenchmark");
Directory.CreateDirectory(benchRoot);
var outDir = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "benchmarks");
var repoArtifacts = FindRepoArtifacts();
if (repoArtifacts is not null)
{
    outDir = Path.Combine(repoArtifacts, "benchmarks");
}

Directory.CreateDirectory(outDir);
var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
var reportPath = Path.Combine(outDir, $"diskscanner-v11-metadata-{stamp}.md");
var csvPath = Path.Combine(outDir, $"diskscanner-v11-metadata-{stamp}.csv");

Console.WriteLine("ExplorerPilot DiskScanner Metadata Optimization Benchmark");
Console.WriteLine($"Fixture root: {benchRoot}");
Console.WriteLine($"Compare modes: {compareModes}");
Console.WriteLine($"Output: {outDir}");
Console.WriteLine();

var storage = StorageMediaDetector.Probe(benchRoot);
Console.WriteLine($"Storage: root={storage.RootPath}, DriveType={storage.DriveType}, Media={storage.MediaKind}");
Console.WriteLine();

var comparisons = new List<(int Files, ScenarioResult Legacy, ScenarioResult Optimized, bool Parity)>();
var singleScenarios = new List<ScenarioResult>();
var sb = new StringBuilder();
sb.AppendLine("# DiskScanner v1.1 Metadata Optimization Benchmark");
sb.AppendLine();
sb.AppendLine($"- Generated (UTC): {DateTime.UtcNow:O}");
sb.AppendLine($"- Machine: {Environment.MachineName}");
sb.AppendLine($"- OS: {Environment.OSVersion}");
sb.AppendLine($"- Processors: {Environment.ProcessorCount}");
sb.AppendLine($"- .NET: {Environment.Version}");
sb.AppendLine($"- Fixture root: `{benchRoot}`");
sb.AppendLine($"- Media: {storage.MediaKind}");
sb.AppendLine($"- Iterations per engine: {iterations} (median over repeats)");
sb.AppendLine("- Engine order shuffled per fixture to reduce warmup bias");
sb.AppendLine();

var csv = new StringBuilder();
csv.AppendLine("files,engine,run,kind,wall_ms,files_per_sec,total_files,total_dirs,errors,progress_reports,cpu_ms,cpu_wall_ratio,ws_delta_bytes,managed_delta_bytes,ws_after_bytes");

var seed = 20261121;
foreach (var fileCount in sizes)
{
    var fixtureDir = Path.Combine(benchRoot, $"n{fileCount}");
    Console.WriteLine($"Preparing fixture n={fileCount} ...");
    var fixture = FixtureGenerator.EnsureFixture(fixtureDir, fileCount);
    Console.WriteLine($"  {(fixture.Created ? "created" : "reused")}: files={fixture.FileCount}");

    if (compareModes)
    {
        Console.WriteLine("  Paired Legacy vs Optimized ...");
        var (legacy, optimized, parity) = ScannerBenchmark.RunPairedComparison(
            fixtureDir, fileCount, iterations, seed + fileCount);
        comparisons.Add((fileCount, legacy, optimized, parity));
        AppendCsv(csv, legacy);
        AppendCsv(csv, optimized);
        PrintScenario(legacy);
        PrintScenario(optimized);
        var speedup = legacy.MedianRepeatWallMs <= 0
            ? 0
            : (legacy.MedianRepeatWallMs - optimized.MedianRepeatWallMs) / legacy.MedianRepeatWallMs * 100.0;
        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"  => median speedup (optimized vs legacy): {speedup:F1}% | parity={(parity ? "OK" : "FAIL")}"));
    }
    else
    {
        var scenario = ScannerBenchmark.RunScenario(
            $"{fileCount:N0} files",
            fixtureDir,
            fileCount,
            iterations,
            dropMemoryBetweenRuns: true,
            DiskScannerEnumerationMode.FileSystemInfoMetadata);
        singleScenarios.Add(scenario);
        AppendCsv(csv, scenario);
        PrintScenario(scenario);
    }

    Console.WriteLine();
}

if (compareModes)
{
    WriteComparisonReport(sb, comparisons, storage);
}
else
{
    sb.AppendLine("## Single-mode results");
    sb.AppendLine();
    foreach (var s in singleScenarios)
    {
        sb.AppendLine($"- {s.Label}: median repeat {s.MedianRepeatWallMs:F1} ms ({s.MedianRepeatFilesPerSecond:F0} files/s)");
    }
}

File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
File.WriteAllText(csvPath, csv.ToString(), Encoding.UTF8);
Console.WriteLine("Wrote:");
Console.WriteLine("  " + reportPath);
Console.WriteLine("  " + csvPath);

if (!keepFixtures)
{
    FixtureGenerator.TryDeleteTree(benchRoot);
}

return comparisons.Any(c => !c.Parity) ? 2 : 0;

static void PrintScenario(ScenarioResult s)
{
    Console.WriteLine(
        string.Create(
            CultureInfo.InvariantCulture,
            $"    {s.Engine}: first={s.FirstWallMs:F1}ms medianRepeat={s.MedianRepeatWallMs:F1}ms ({s.MedianRepeatFilesPerSecond:F0} files/s) CPU/wall={s.MedianRepeatCpuWallRatio:F2} ΔWS={FormatBytes(s.MedianRepeatWorkingSetDeltaBytes)}"));
}

static void AppendCsv(StringBuilder csv, ScenarioResult s)
{
    foreach (var run in s.Runs)
    {
        csv.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{s.ExpectedFiles},{s.Engine},{run.Index},{run.Kind},{run.WallMs:F3},{run.FilesPerSecond:F1},{run.TotalFiles},{run.TotalDirectories},{run.ErrorCount},{run.ProgressReports},{run.CpuTimeMs:F3},{run.CpuWallRatio:F3},{run.WorkingSetDeltaBytes},{run.ManagedHeapDeltaBytes},{run.WorkingSetAfterBytes}"));
    }
}

static void WriteComparisonReport(
    StringBuilder sb,
    List<(int Files, ScenarioResult Legacy, ScenarioResult Optimized, bool Parity)> comparisons,
    StorageProfile storage)
{
    sb.AppendLine("## Before / after (LegacyPathAndStat vs FileSystemInfoMetadata)");
    sb.AppendLine();
    sb.AppendLine("| Files | Legacy median ms | Optimized median ms | Speedup % | Legacy files/s | Optimized files/s | Legacy CPU/wall | Opt CPU/wall | Legacy ΔWS | Opt ΔWS | Parity |");
    sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|:---:|");
    foreach (var (files, legacy, optimized, parity) in comparisons)
    {
        var speedup = legacy.MedianRepeatWallMs <= 0
            ? 0
            : (legacy.MedianRepeatWallMs - optimized.MedianRepeatWallMs) / legacy.MedianRepeatWallMs * 100.0;
        sb.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"| {files:N0} | {legacy.MedianRepeatWallMs:F1} | {optimized.MedianRepeatWallMs:F1} | {speedup:F1}% | {legacy.MedianRepeatFilesPerSecond:F0} | {optimized.MedianRepeatFilesPerSecond:F0} | {legacy.MedianRepeatCpuWallRatio:F2} | {optimized.MedianRepeatCpuWallRatio:F2} | {FormatBytes(legacy.MedianRepeatWorkingSetDeltaBytes)} | {FormatBytes(optimized.MedianRepeatWorkingSetDeltaBytes)} | {(parity ? "PASS" : "FAIL")} |"));
    }

    sb.AppendLine();
    sb.AppendLine("### First-scan (cold-ish) comparison");
    sb.AppendLine();
    sb.AppendLine("| Files | Legacy first ms | Optimized first ms | First speedup % |");
    sb.AppendLine("|---:|---:|---:|---:|");
    foreach (var (files, legacy, optimized, _) in comparisons)
    {
        var speedup = legacy.FirstWallMs <= 0
            ? 0
            : (legacy.FirstWallMs - optimized.FirstWallMs) / legacy.FirstWallMs * 100.0;
        sb.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"| {files:N0} | {legacy.FirstWallMs:F1} | {optimized.FirstWallMs:F1} | {speedup:F1}% |"));
    }

    sb.AppendLine();
    sb.AppendLine("## Metadata syscall assessment");
    sb.AppendLine();
    sb.AppendLine("- **Legacy**: `EnumerateFileSystemEntries` + per entry `GetAttributes` + (`FileInfo` Length/LastWrite **or** `Directory.GetLastWriteTimeUtc`).");
    sb.AppendLine("- **Optimized**: `DirectoryInfo.EnumerateFileSystemInfos` reuses Find* metadata for attributes, times, and file length (no `Refresh`).");
    sb.AppendLine("- Expected syscall reduction ≈ eliminating 1–2 extra metadata opens/stats per entry on Windows.");
    sb.AppendLine($"- Measured media: **{storage.MediaKind}**");
    sb.AppendLine();
    sb.AppendLine("## Decision criteria");
    sb.AppendLine();
    sb.AppendLine("- Adopt optimized as default only if parity PASS on all sizes **and** median repeat wall improves (or matches within noise) with evidence of fewer metadata round-trips.");
}

static string? FindRepoArtifacts()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    for (var i = 0; i < 6 && dir is not null; i++)
    {
        if (File.Exists(Path.Combine(dir.FullName, "ExplorerPilot.sln")))
        {
            return Path.Combine(dir.FullName, "artifacts");
        }

        dir = dir.Parent;
    }

    return null;
}

static string FormatBytes(long bytes)
{
    var sign = bytes < 0 ? "-" : "";
    double v = Math.Abs(bytes);
    string[] units = ["B", "KB", "MB", "GB"];
    var u = 0;
    while (v >= 1024 && u < units.Length - 1)
    {
        v /= 1024;
        u++;
    }

    return string.Create(CultureInfo.InvariantCulture, $"{sign}{v:0.##} {units[u]}");
}

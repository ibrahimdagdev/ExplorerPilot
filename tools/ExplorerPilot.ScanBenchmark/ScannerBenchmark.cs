using System.Diagnostics;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Infrastructure.Scanning;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExplorerPilot.ScanBenchmark;

internal static class ScannerBenchmark
{
    public static ScenarioResult RunScenario(
        string label,
        string root,
        int expectedFiles,
        int iterations,
        bool dropMemoryBetweenRuns,
        DiskScannerEnumerationMode mode)
    {
        var scanner = new DiskScanner(NullLogger<DiskScanner>.Instance, mode);
        var runs = new List<RunSample>(iterations);

        for (var i = 0; i < iterations; i++)
        {
            if (dropMemoryBetweenRuns)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            var process = Process.GetCurrentProcess();
            process.Refresh();
            var cpuBefore = process.TotalProcessorTime;
            var wsBefore = process.WorkingSet64;
            var managedBefore = GC.GetTotalMemory(forceFullCollection: false);
            var progressReports = 0L;

            var sw = Stopwatch.StartNew();
            var result = scanner.ScanAsync(
                    root,
                    new Progress<ScanProgress>(_ => Interlocked.Increment(ref progressReports)))
                .GetAwaiter()
                .GetResult();
            sw.Stop();

            process.Refresh();
            var cpuAfter = process.TotalProcessorTime;
            var wsAfter = process.WorkingSet64;
            var managedAfter = GC.GetTotalMemory(forceFullCollection: false);

            var cpuDelta = (cpuAfter - cpuBefore).TotalMilliseconds;
            var wallMs = sw.Elapsed.TotalMilliseconds;
            var cpuRatio = wallMs <= 0 ? 0 : cpuDelta / wallMs;

            runs.Add(new RunSample(
                Index: i,
                Kind: i == 0 ? "first" : "repeat",
                Engine: mode.ToString(),
                WallMs: wallMs,
                FilesPerSecond: wallMs <= 0 ? 0 : result.TotalFiles * 1000.0 / wallMs,
                TotalFiles: result.TotalFiles,
                TotalDirectories: result.TotalDirectories,
                TotalBytes: result.TotalBytes,
                ErrorCount: result.Errors.Count,
                ProgressReports: progressReports,
                CpuTimeMs: cpuDelta,
                CpuWallRatio: cpuRatio,
                WorkingSetDeltaBytes: wsAfter - wsBefore,
                ManagedHeapDeltaBytes: managedAfter - managedBefore,
                WorkingSetAfterBytes: wsAfter));
        }

        var first = runs[0];
        var repeats = runs.Skip(1).ToList();
        var medianWall = Median(repeats.Count > 0 ? repeats.Select(r => r.WallMs) : runs.Select(r => r.WallMs));
        var medianFps = Median(repeats.Count > 0 ? repeats.Select(r => r.FilesPerSecond) : runs.Select(r => r.FilesPerSecond));
        var medianCpu = Median(repeats.Count > 0 ? repeats.Select(r => r.CpuWallRatio) : runs.Select(r => r.CpuWallRatio));
        var medianWs = Median(repeats.Count > 0 ? repeats.Select(r => (double)r.WorkingSetDeltaBytes) : runs.Select(r => (double)r.WorkingSetDeltaBytes));

        return new ScenarioResult(
            Label: label,
            Engine: mode.ToString(),
            ExpectedFiles: expectedFiles,
            Root: root,
            Runs: runs,
            FirstWallMs: first.WallMs,
            FirstFilesPerSecond: first.FilesPerSecond,
            MedianRepeatWallMs: medianWall,
            MedianRepeatFilesPerSecond: medianFps,
            MedianRepeatCpuWallRatio: medianCpu,
            MedianRepeatWorkingSetDeltaBytes: (long)medianWs);
    }

    /// <summary>
    /// Alternates engines in a shuffled order per size to reduce cache/warmup bias.
    /// </summary>
    public static (ScenarioResult Legacy, ScenarioResult Optimized, bool ParityOk) RunPairedComparison(
        string root,
        int expectedFiles,
        int iterations,
        int seed)
    {
        var rng = new Random(seed);
        var order = new List<DiskScannerEnumerationMode>
        {
            DiskScannerEnumerationMode.LegacyPathAndStat,
            DiskScannerEnumerationMode.FileSystemInfoMetadata
        };
        // Shuffle which engine runs the "first" block first for this fixture.
        if (rng.Next(0, 2) == 1)
        {
            order.Reverse();
        }

        ScenarioResult? firstBlock = null;
        ScenarioResult? secondBlock = null;
        foreach (var mode in order)
        {
            var label = $"{expectedFiles:N0} files / {mode}";
            var result = RunScenario(label, root, expectedFiles, iterations, dropMemoryBetweenRuns: true, mode);
            if (mode == order[0])
            {
                firstBlock = result;
            }
            else
            {
                secondBlock = result;
            }
        }

        var legacy = order[0] == DiskScannerEnumerationMode.LegacyPathAndStat ? firstBlock! : secondBlock!;
        var optimized = order[0] == DiskScannerEnumerationMode.FileSystemInfoMetadata ? firstBlock! : secondBlock!;

        // Correctness on last run samples of each engine.
        var legacyScan = new DiskScanner(enumerationMode: DiskScannerEnumerationMode.LegacyPathAndStat)
            .ScanAsync(root).GetAwaiter().GetResult();
        var optimizedScan = new DiskScanner(enumerationMode: DiskScannerEnumerationMode.FileSystemInfoMetadata)
            .ScanAsync(root).GetAwaiter().GetResult();
        var parity = legacyScan.TotalFiles == optimizedScan.TotalFiles
                     && legacyScan.TotalDirectories == optimizedScan.TotalDirectories
                     && legacyScan.TotalBytes == optimizedScan.TotalBytes
                     && legacyScan.Errors.Count == optimizedScan.Errors.Count
                     && legacyScan.FinalStatus == optimizedScan.FinalStatus;

        // Re-label engines correctly regardless of run order.
        legacy = legacy with { Engine = DiskScannerEnumerationMode.LegacyPathAndStat.ToString(), Label = $"{expectedFiles:N0} files / Legacy" };
        optimized = optimized with { Engine = DiskScannerEnumerationMode.FileSystemInfoMetadata.ToString(), Label = $"{expectedFiles:N0} files / Optimized" };

        return (legacy, optimized, parity);
    }

    private static double Median(IEnumerable<double> values)
    {
        var list = values.OrderBy(v => v).ToList();
        if (list.Count == 0)
        {
            return 0;
        }

        var mid = list.Count / 2;
        if (list.Count % 2 == 1)
        {
            return list[mid];
        }

        return (list[mid - 1] + list[mid]) / 2.0;
    }
}

internal sealed record RunSample(
    int Index,
    string Kind,
    string Engine,
    double WallMs,
    double FilesPerSecond,
    long TotalFiles,
    long TotalDirectories,
    long TotalBytes,
    int ErrorCount,
    long ProgressReports,
    double CpuTimeMs,
    double CpuWallRatio,
    long WorkingSetDeltaBytes,
    long ManagedHeapDeltaBytes,
    long WorkingSetAfterBytes);

internal sealed record ScenarioResult(
    string Label,
    string Engine,
    int ExpectedFiles,
    string Root,
    IReadOnlyList<RunSample> Runs,
    double FirstWallMs,
    double FirstFilesPerSecond,
    double MedianRepeatWallMs,
    double MedianRepeatFilesPerSecond,
    double MedianRepeatCpuWallRatio,
    long MedianRepeatWorkingSetDeltaBytes);

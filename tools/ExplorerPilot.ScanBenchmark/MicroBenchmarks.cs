using System.Diagnostics;
using ExplorerPilot.Core.Models;

namespace ExplorerPilot.ScanBenchmark;

/// <summary>
/// Bottleneck probes that mirror DiskScanner I/O patterns without modifying DiskScanner.
/// Timings are wall-clock on the same fixture used for full scans.
/// </summary>
internal static class MicroBenchmarks
{
    public static BottleneckReport Run(string root)
    {
        // 1) Enumeration only
        var enumSw = Stopwatch.StartNew();
        long enumEntries = 0;
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .Prepend(root))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                _ = entry;
                enumEntries++;
            }
        }

        enumSw.Stop();

        // 2) Attributes + FileInfo metadata (files only)
        var metaSw = Stopwatch.StartNew();
        long metaFiles = 0;
        long metaBytes = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.Directory) != 0)
            {
                continue;
            }

            var info = new FileInfo(file);
            metaBytes += info.Length;
            var _lastWrite = info.LastWriteTimeUtc;
            _ = _lastWrite;
            metaFiles++;
        }

        metaSw.Stop();

        // 3) Node allocation cost (synthetic, mirrors FileSystemNode creation)
        var nodeSw = Stopwatch.StartNew();
        var nodes = new List<FileSystemNode>(checked((int)Math.Min(metaFiles + 1024, int.MaxValue)));
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            nodes.Add(new FileSystemNode
            {
                Name = Path.GetFileName(file),
                FullPath = file,
                IsDirectory = false,
                SizeBytes = 8,
                FileCount = 1
            });
        }

        nodeSw.Stop();

        // 4) Progress update simulation (same throttle shape as DiskScanner: every 250 items)
        var progressSw = Stopwatch.StartNew();
        long progressCalls = 0;
        long items = 0;
        var last = TimeSpan.Zero;
        var clock = Stopwatch.StartNew();
        foreach (var filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            items++;
            if (items % 250 == 0 || clock.Elapsed - last >= TimeSpan.FromMilliseconds(100))
            {
                progressCalls++;
                last = clock.Elapsed;
                // Simulate UI-bound progress payload construction cost.
                var payload = new
                {
                    Path = filePath,
                    Count = items,
                    Elapsed = clock.Elapsed
                };
                _ = payload;
            }
        }

        progressSw.Stop();

        // 5) ScanResult-like post-processing: sort children by size (FinalizeNode style)
        var resultSw = Stopwatch.StartNew();
        nodes.Sort(static (a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        var take = nodes.Take(200).ToList();
        resultSw.Stop();

        // 6) ApplyResult-like walk: build lightweight view rows (no WPF)
        var applySw = Stopwatch.StartNew();
        var rows = new List<(string Name, long Size)>(nodes.Count);
        foreach (var n in nodes)
        {
            rows.Add((n.Name, n.SizeBytes));
        }

        rows.Sort(static (a, b) => b.Size.CompareTo(a.Size));
        applySw.Stop();

        return new BottleneckReport(
            EnumerationMs: enumSw.Elapsed.TotalMilliseconds,
            EnumerationEntries: enumEntries,
            MetadataMs: metaSw.Elapsed.TotalMilliseconds,
            MetadataFiles: metaFiles,
            MetadataBytes: metaBytes,
            NodeAllocMs: nodeSw.Elapsed.TotalMilliseconds,
            NodeCount: nodes.Count,
            ProgressSimMs: progressSw.Elapsed.TotalMilliseconds,
            ProgressCallbacks: progressCalls,
            ResultBuildMs: resultSw.Elapsed.TotalMilliseconds,
            ApplyResultSimMs: applySw.Elapsed.TotalMilliseconds,
            ApplyRows: rows.Count,
            LargestSample: take.Count);
    }
}

internal sealed record BottleneckReport(
    double EnumerationMs,
    long EnumerationEntries,
    double MetadataMs,
    long MetadataFiles,
    long MetadataBytes,
    double NodeAllocMs,
    int NodeCount,
    double ProgressSimMs,
    long ProgressCallbacks,
    double ResultBuildMs,
    double ApplyResultSimMs,
    int ApplyRows,
    int LargestSample);

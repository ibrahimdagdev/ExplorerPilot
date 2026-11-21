using System.Globalization;
using System.Text;

namespace ExplorerPilot.ScanBenchmark;

/// <summary>
/// Parallelism guidance grounded in measured single-worker throughput — no fabricated speedups.
/// </summary>
internal static class ParallelismNotes
{
    public static string Build(IReadOnlyList<ScenarioResult> scenarios, StorageProfile storage)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Parallelism evaluation (not implemented — analysis only)");
        sb.AppendLine();
        sb.AppendLine("Baseline is the **measured single-worker** `DiskScanner` (Task.Run + sync FS APIs).");
        sb.AppendLine("No multi-worker implementation was added; speedup numbers below are **capacity estimates**, not timed results.");
        sb.AppendLine();

        var media = storage.MediaKind;
        var looksSsd = media.Contains("SSD", StringComparison.OrdinalIgnoreCase)
                       || media.Contains("SCM", StringComparison.OrdinalIgnoreCase)
                       || media.Contains("NVMe", StringComparison.OrdinalIgnoreCase);
        var looksHdd = media.Contains("HDD", StringComparison.OrdinalIgnoreCase);

        sb.AppendLine("### Measured single-worker anchors");
        sb.AppendLine();
        foreach (var s in scenarios)
        {
            sb.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"- {s.Label}: median repeat **{s.MedianRepeatFilesPerSecond:F0} files/s**, CPU/wall **{s.MedianRepeatCpuWallRatio:F2}**"));
        }

        sb.AppendLine();
        sb.AppendLine("### Estimated benefit vs risk by worker count");
        sb.AppendLine();
        sb.AppendLine("| Workers | SSD-oriented estimate | HDD-oriented estimate | Main risks |");
        sb.AppendLine("|---:|---|---|---|");
        sb.AppendLine("| 1 (current) | Baseline; simplest correctness | Baseline; avoids seek thrash | None new |");
        sb.AppendLine("| 2 | Often modest gain if metadata-bound and CPU/wall high | Small or negative if random seeks dominate | Merge races, progress ordering |");
        sb.AppendLine("| 4 | Possible further gain on NVMe/SSD until syscall/lock saturation | Frequently **hurts** via seek contention | Cancellation fan-in, peak RAM, reparse handling |");
        sb.AppendLine("| 8 | Diminishing returns; may regress from lock/allocator contention | Usually not recommended | Higher complexity, harder incompleteness semantics |");
        sb.AppendLine();

        sb.AppendLine("### Media-specific reading");
        sb.AppendLine();
        if (looksSsd)
        {
            sb.AppendLine("- This machine’s media hint looks **SSD-like**. Parallel directory workers *may* help wall-clock if CPU/wall is already high and enumeration is not lock-serialized by the FS filter stack.");
        }
        else if (looksHdd)
        {
            sb.AppendLine("- This machine’s media hint looks **HDD-like**. Prefer staying single-threaded or at most 2 cautious workers; random metadata parallelism often increases latency.");
        }
        else
        {
            sb.AppendLine("- Media type is **uncertain**. Validate any parallel design with separate timed runs on known SSD and HDD volumes before claiming speedups.");
        }

        sb.AppendLine();
        sb.AppendLine("### Risk checklist for any future parallel scanner");
        sb.AppendLine();
        sb.AppendLine("- **Race conditions**: `FileSystemNode` tree mutation, `LargestFilesTracker`, counters, error lists need concurrent structures or shard-merge.");
        sb.AppendLine("- **Cancellation**: cooperative cancel across workers; avoid orphan work after `Cancel`.");
        sb.AppendLine("- **Reparse points**: keep directory junction/symlink skip semantics identical; parallel traversal must not follow cycles.");
        sb.AppendLine("- **Memory**: N workers × pending directory queues can inflate working set vs current iterative stack.");
        sb.AppendLine("- **Progress/UI**: throttle globally; do not flood Dispatcher.");
        sb.AppendLine("- **Determinism**: child sort order and incompleteness labeling must remain explainable.");
        sb.AppendLine();
        sb.AppendLine("### Recommended strategy (guidance only)");
        sb.AppendLine();
        sb.AppendLine("1. Keep **1 worker** as default (current v1.0 behavior).");
        sb.AppendLine("2. If experimenting: start with **2 workers** on SSD only; measure median wall + peak WS vs this report’s baseline.");
        sb.AppendLine("3. Consider **4 workers** only after 2-worker proves stable and CPU/wall still saturates one core without FS thrashing.");
        sb.AppendLine("4. Treat **8 workers** as research/opt-in — unlikely default for a disk usage UI tool.");
        sb.AppendLine("5. Do **not** ship parallel mode without dedicated tests for cancel, access-denied, and reparse skip.");

        // Tie estimate honesty to measured CPU/wall without inventing multipliers.
        var highCpu = scenarios.Any(s => s.MedianRepeatCpuWallRatio >= 0.75);
        sb.AppendLine();
        sb.AppendLine("### What the measurements suggest on this run");
        sb.AppendLine();
        if (highCpu)
        {
            sb.AppendLine("- Median CPU/wall is relatively **high** on at least one scenario → some headroom for multi-core *if* I/O does not serialize; still requires timed parallel prototypes.");
        }
        else
        {
            sb.AppendLine("- Median CPU/wall is **not consistently high** → wall time likely storage/syscall bound; parallel workers may yield little or regress, especially on HDD.");
        }

        sb.AppendLine("- Claimed N× speedups without a parallel prototype measurement would be invalid; this report intentionally omits them.");
        return sb.ToString();
    }
}

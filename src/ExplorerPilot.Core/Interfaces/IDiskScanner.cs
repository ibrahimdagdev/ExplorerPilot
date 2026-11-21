using ExplorerPilot.Core.Models;

namespace ExplorerPilot.Core.Interfaces;

/// <summary>
/// Scans a directory or drive and builds a hierarchical size model.
/// Implementations must be UI-agnostic and support cancellation and progress reporting.
/// </summary>
public interface IDiskScanner
{
    /// <summary>
    /// Scans <paramref name="rootPath"/> (folder or drive root) using a single controlled worker.
    /// Reports logical file sizes only; does not read file contents.
    /// </summary>
    Task<ScanResult> ScanAsync(
        string rootPath,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

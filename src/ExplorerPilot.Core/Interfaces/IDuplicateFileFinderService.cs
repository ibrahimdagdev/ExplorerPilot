using ExplorerPilot.Core.Models;

namespace ExplorerPilot.Core.Interfaces;

/// <summary>
/// Finds duplicate file content using an existing in-memory scan tree.
/// Enumerates <see cref="FileSystemNode"/> only; reads file bytes for hashing without re-walking directories.
/// </summary>
public interface IDuplicateFileFinderService
{
    Task<DuplicateAnalysisResult> AnalyzeAsync(
        FileSystemNode root,
        DuplicateAnalysisCriteria criteria,
        bool fromIncompleteScan,
        IProgress<DuplicateAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

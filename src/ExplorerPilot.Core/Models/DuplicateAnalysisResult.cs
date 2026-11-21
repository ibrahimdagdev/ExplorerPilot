using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Models;

public sealed class DuplicateAnalysisResult
{
    public Guid ScanSessionId { get; init; }

    public required IReadOnlyList<DuplicateGroup> Groups { get; init; }

    public long AnalyzedFiles { get; init; }

    public long DuplicateGroupCount { get; init; }

    public long DuplicateFileCount { get; init; }

    public long PotentialSavingsBytes { get; init; }

    public long SkippedFiles { get; init; }

    public long FailedFiles { get; init; }

    public DuplicateAnalysisPhase FinalStatus { get; init; }

    public bool IsComplete => FinalStatus == DuplicateAnalysisPhase.Completed;

    public bool WasCancelled => FinalStatus == DuplicateAnalysisPhase.Cancelled;

    public bool FromIncompleteScan { get; init; }
}

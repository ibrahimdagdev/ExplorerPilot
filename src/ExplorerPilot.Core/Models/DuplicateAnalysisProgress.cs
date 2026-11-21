using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Models;

public sealed class DuplicateAnalysisProgress
{
    public DuplicateAnalysisPhase Phase { get; init; }

    public long AnalyzedFiles { get; init; }

    public long CandidateFiles { get; init; }

    public long HashedFiles { get; init; }

    public long SkippedFiles { get; init; }

    public string? CurrentPath { get; init; }

    public string? StatusDetail { get; init; }
}

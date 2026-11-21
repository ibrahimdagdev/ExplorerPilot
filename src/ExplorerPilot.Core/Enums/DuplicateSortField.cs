namespace ExplorerPilot.Core.Enums;

public enum DuplicateSortField
{
    PotentialSavings = 0,
    FileSize = 1,
    DuplicateCount = 2,
    FileName = 3
}

public enum DuplicateAnalysisPhase
{
    Idle = 0,
    GroupingBySize = 1,
    PartialHashing = 2,
    FullHashing = 3,
    Finalizing = 4,
    Completed = 5,
    Cancelled = 6,
    Failed = 7
}

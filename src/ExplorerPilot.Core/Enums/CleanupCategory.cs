namespace ExplorerPilot.Core.Enums;

/// <summary>Classification of a cleanup recommendation candidate.</summary>
public enum CleanupCategory
{
    LargeFiles = 0,
    OldFiles = 1,
    TemporaryFiles = 2,
    OldLogFiles = 3,
    EmptyFolders = 4,
    VerifiedDuplicates = 5
}

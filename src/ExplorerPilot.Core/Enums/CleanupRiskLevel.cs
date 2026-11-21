namespace ExplorerPilot.Core.Enums;

/// <summary>
/// Advisory risk only — never implies that deletion is safe.
/// </summary>
public enum CleanupRiskLevel
{
    /// <summary>Relatively lower-risk candidate that still warrants review.</summary>
    Low = 0,

    /// <summary>User verification strongly recommended before any action outside this app.</summary>
    Medium = 1,

    /// <summary>Likely system, application, or user data — treat as high risk.</summary>
    High = 2
}

namespace ExplorerPilot.Core.Scanning;

/// <summary>
/// Bounded ranking capacity for largest-file tracking during a scan.
/// </summary>
public static class LargestFilesOptions
{
    /// <summary>
    /// Maximum number of file records retained in memory for ranking (O(K)).
    /// </summary>
    public const int MaxTrackedFiles = 1000;

    public static readonly IReadOnlyList<int> DisplayLimits = [100, 500, 1000];

    public const int DefaultDisplayLimit = 100;
}

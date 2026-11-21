namespace ExplorerPilot.Core.Snapshots;

/// <summary>Versioned on-disk snapshot document schema.</summary>
public static class SnapshotSchema
{
    /// <summary>Current schema version (semver major.minor).</summary>
    public const string CurrentVersion = "1.0";

    /// <summary>File extension used under the app snapshot directory.</summary>
    public const string FileExtension = ".snapshot.json";
}

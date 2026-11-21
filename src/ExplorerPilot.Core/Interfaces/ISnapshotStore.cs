using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Snapshots;

namespace ExplorerPilot.Core.Interfaces;

/// <summary>
/// Persists disk snapshots under the application data directory.
/// Delete operations must only target files inside that sandbox.
/// Import may read external files; export may copy to a user-chosen path without deleting it.
/// </summary>
public interface ISnapshotStore
{
    string StorageDirectory { get; }

    Task<IReadOnlyList<SnapshotInfo>> ListAsync(CancellationToken cancellationToken = default);

    Task<SnapshotInfo> SaveAsync(
        ScanResult scanResult,
        string? displayName = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    Task<DiskSnapshot> LoadAsync(Guid snapshotId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads categories, extensions, and directory sizes for trend analysis without
    /// materializing every file entry as a <see cref="DiskSnapshot"/>.
    /// </summary>
    Task<SnapshotTrendPayload> LoadTrendPayloadAsync(
        Guid snapshotId,
        int maxDirectoryEntries = 2_000,
        CancellationToken cancellationToken = default);

    /// <summary>Loads a snapshot that already resides inside the application sandbox.</summary>
    Task<DiskSnapshot> LoadFromPathAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates and loads a snapshot from an arbitrary readable path (used by import).
    /// Does not delete or modify the source file.
    /// </summary>
    Task<DiskSnapshot> LoadExternalAsync(string filePath, CancellationToken cancellationToken = default);

    Task<SnapshotInfo> ExportAsync(
        Guid snapshotId,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    Task<SnapshotImportResult> ImportAsync(
        string sourcePath,
        SnapshotIdCollisionPolicy collisionPolicy = SnapshotIdCollisionPolicy.AssignNewId,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a snapshot file that belongs to <see cref="StorageDirectory"/>.
    /// Returns false when the id is unknown. Never deletes paths outside the sandbox.
    /// </summary>
    Task<bool> DeleteAsync(Guid snapshotId, CancellationToken cancellationToken = default);
}

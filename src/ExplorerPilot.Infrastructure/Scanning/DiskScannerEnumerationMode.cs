namespace ExplorerPilot.Infrastructure.Scanning;

/// <summary>
/// Controls how <see cref="DiskScanner"/> obtains per-entry metadata during directory walks.
/// </summary>
public enum DiskScannerEnumerationMode
{
    /// <summary>
    /// v1.0 path: <c>EnumerateFileSystemEntries</c> then separate <c>GetAttributes</c> / <c>FileInfo</c> / <c>GetLastWriteTimeUtc</c>.
    /// </summary>
    LegacyPathAndStat = 0,

    /// <summary>
    /// v1.1 candidate: <c>DirectoryInfo.EnumerateFileSystemInfos</c> reuses Find* metadata (attributes, times, length).
    /// </summary>
    FileSystemInfoMetadata = 1
}

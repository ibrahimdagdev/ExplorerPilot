using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Models;

/// <summary>
/// Represents a file or directory discovered during a disk usage scan.
/// Directory <see cref="SizeBytes"/> includes the logical sizes of all descendants.
/// </summary>
public sealed class FileSystemNode
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public bool IsDirectory { get; init; }

    /// <summary>
    /// Logical file size in bytes (FileInfo.Length equivalent). For directories,
    /// this is the sum of descendant logical sizes. Physical allocated size may differ.
    /// </summary>
    public long SizeBytes { get; set; }

    public long FileCount { get; set; }

    public long DirectoryCount { get; set; }

    public FileSystemNode? Parent { get; set; }

    public List<FileSystemNode> Children { get; } = [];

    public NodeScanStatus ScanStatus { get; set; } = NodeScanStatus.Pending;

    public DateTimeOffset? LastModified { get; set; }

    public double PercentageOfParent
    {
        get
        {
            if (Parent is null || Parent.SizeBytes <= 0)
            {
                return IsDirectory && Parent is null ? 100.0 : 0.0;
            }

            return (double)SizeBytes / Parent.SizeBytes * 100.0;
        }
    }
}

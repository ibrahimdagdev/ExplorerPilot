using System.Diagnostics;
using ExplorerPilot.Core.Diagnostics;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Scanning;
using ExplorerPilot.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.Infrastructure.Scanning;

/// <summary>
/// Iterative disk usage scanner. Uses streaming enumeration and a single worker.
/// Does not follow directory junctions or directory symbolic links by default.
/// Reports logical file sizes (metadata Length), not physical allocated clusters.
/// </summary>
public sealed class DiskScanner : IDiskScanner
{
    private static readonly TimeSpan ProgressThrottleInterval = TimeSpan.FromMilliseconds(100);
    private const int ProgressThrottleItemCount = 250;

    private readonly ILogger<DiskScanner>? _logger;
    private readonly DiskScannerEnumerationMode _enumerationMode;

    /// <summary>
    /// Creates a scanner. Default is <see cref="DiskScannerEnumerationMode.FileSystemInfoMetadata"/>
    /// (v1.1): reuses Find* metadata from <c>EnumerateFileSystemInfos</c>.
    /// Pass <see cref="DiskScannerEnumerationMode.LegacyPathAndStat"/> for A/B benchmarks.
    /// </summary>
    public DiskScanner(
        ILogger<DiskScanner>? logger = null,
        DiskScannerEnumerationMode enumerationMode = DiskScannerEnumerationMode.FileSystemInfoMetadata)
    {
        _logger = logger;
        _enumerationMode = enumerationMode;
    }

    public DiskScannerEnumerationMode EnumerationMode => _enumerationMode;

    public Task<ScanResult> ScanAsync(
        string rootPath,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        return Task.Run(
            () => ScanCore(rootPath, progress, cancellationToken),
            cancellationToken);
    }

    private ScanResult ScanCore(
        string rootPath,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<ScanError>();
        var largestFiles = new LargestFilesTracker(LargestFilesOptions.MaxTrackedFiles);
        var fileTypes = new FileTypeStatisticsTracker();
        long scannedFiles = 0;
        long scannedDirectories = 0;
        long totalBytes = 0;
        long itemsSinceProgress = 0;
        var lastProgressReport = TimeSpan.Zero;

        void Report(ScanPhase phase, string currentPath, string? message = null)
        {
            progress?.Report(new ScanProgress
            {
                CurrentPath = PathHelper.GetDisplayPath(currentPath),
                ScannedFileCount = scannedFiles,
                ScannedDirectoryCount = scannedDirectories,
                TotalBytesScanned = totalBytes,
                ElapsedTime = stopwatch.Elapsed,
                CurrentStatus = phase,
                StatusMessage = message ?? phase.ToString()
            });
        }

        void ThrottledReport(string currentPath)
        {
            itemsSinceProgress++;
            if (itemsSinceProgress < ProgressThrottleItemCount
                && stopwatch.Elapsed - lastProgressReport < ProgressThrottleInterval)
            {
                return;
            }

            itemsSinceProgress = 0;
            lastProgressReport = stopwatch.Elapsed;
            Report(ScanPhase.Scanning, currentPath);
        }

        Report(ScanPhase.Preparing, rootPath, "Preparing scan");

        string normalizedRoot;
        try
        {
            // Honour cancel from Preparing progress before filesystem work (sync or async Progress handlers).
            cancellationToken.ThrowIfCancellationRequested();
            normalizedRoot = Path.GetFullPath(rootPath);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            Report(ScanPhase.Cancelled, rootPath, "Scan cancelled");
            return new ScanResult
            {
                RootNode = new FileSystemNode
                {
                    Name = rootPath,
                    FullPath = rootPath,
                    IsDirectory = true,
                    ScanStatus = NodeScanStatus.Completed
                },
                Duration = stopwatch.Elapsed,
                Errors = errors,
                FinalStatus = ScanPhase.Cancelled
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Invalid scan root path: {Path}", PathRedaction.ForLog(rootPath));
            var invalidRoot = new FileSystemNode
            {
                Name = rootPath,
                FullPath = rootPath,
                IsDirectory = true,
                ScanStatus = NodeScanStatus.Error
            };

            errors.Add(new ScanError
            {
                Path = rootPath,
                ErrorType = ScanErrorType.InvalidPath,
                Message = ex.Message
            });

            stopwatch.Stop();
            Report(ScanPhase.Failed, rootPath, "Invalid path");
            return new ScanResult
            {
                RootNode = invalidRoot,
                Duration = stopwatch.Elapsed,
                Errors = errors,
                FinalStatus = ScanPhase.Failed
            };
        }

        if (!Directory.Exists(normalizedRoot))
        {
            var missing = new FileSystemNode
            {
                Name = PathHelper.GetNodeName(normalizedRoot),
                FullPath = normalizedRoot,
                IsDirectory = true,
                ScanStatus = NodeScanStatus.Inaccessible
            };

            errors.Add(new ScanError
            {
                Path = normalizedRoot,
                ErrorType = ScanErrorType.NotFound,
                Message = "The specified directory was not found."
            });

            stopwatch.Stop();
            Report(ScanPhase.Failed, normalizedRoot, "Directory not found");
            return new ScanResult
            {
                RootNode = missing,
                Duration = stopwatch.Elapsed,
                Errors = errors,
                FinalStatus = ScanPhase.Failed
            };
        }

        var rootNode = new FileSystemNode
        {
            Name = PathHelper.GetNodeName(normalizedRoot),
            FullPath = normalizedRoot,
            IsDirectory = true,
            ScanStatus = NodeScanStatus.Scanning,
            LastModified = TryGetLastWriteTime(normalizedRoot)
        };

        scannedDirectories = 1;
        Report(ScanPhase.Scanning, normalizedRoot);

        // Explicit stack: push children first, then a "finalize" marker for post-order rollup.
        var stack = new Stack<ScanFrame>();
        stack.Push(ScanFrame.Finalize(rootNode));

        var cancelled = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            PushChildren(rootNode, stack, errors, largestFiles, fileTypes, ref scannedFiles, ref scannedDirectories, ref totalBytes, ThrottledReport, cancellationToken);

            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var frame = stack.Pop();
                if (frame.Kind == FrameKind.Finalize)
                {
                    FinalizeNode(frame.Node);
                    continue;
                }

                var directory = frame.Node;
                directory.ScanStatus = NodeScanStatus.Scanning;
                ThrottledReport(directory.FullPath);
                PushChildren(directory, stack, errors, largestFiles, fileTypes, ref scannedFiles, ref scannedDirectories, ref totalBytes, ThrottledReport, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            _logger?.LogInformation("Scan cancelled at {Path}", PathRedaction.ForLog(normalizedRoot));
        }

        // Finalize any remaining nodes that were not popped due to cancellation.
        while (stack.Count > 0)
        {
            var frame = stack.Pop();
            if (frame.Kind == FrameKind.Finalize)
            {
                FinalizeNode(frame.Node);
                if (cancelled && frame.Node.ScanStatus == NodeScanStatus.Scanning)
                {
                    frame.Node.ScanStatus = NodeScanStatus.Completed;
                }
            }
        }

        if (rootNode.ScanStatus is NodeScanStatus.Scanning or NodeScanStatus.Pending)
        {
            rootNode.ScanStatus = NodeScanStatus.Completed;
        }

        stopwatch.Stop();

        var finalStatus = cancelled ? ScanPhase.Cancelled : ScanPhase.Completed;
        Report(
            cancelled ? ScanPhase.Cancelled : ScanPhase.Finalizing,
            normalizedRoot,
            cancelled ? "Scan cancelled" : "Finalizing results");

        var result = new ScanResult
        {
            RootNode = rootNode,
            TotalFiles = rootNode.FileCount,
            TotalDirectories = rootNode.DirectoryCount + (rootNode.IsDirectory ? 1 : 0),
            TotalBytes = rootNode.SizeBytes,
            Duration = stopwatch.Elapsed,
            Errors = errors,
            LargestFiles = largestFiles.ToSortedList(),
            ExtensionStatistics = fileTypes.BuildExtensionStatistics(),
            CategoryStatistics = fileTypes.BuildCategoryStatistics(),
            FinalStatus = finalStatus
        };

        Report(
            finalStatus,
            normalizedRoot,
            cancelled ? "Cancelled — totals may be incomplete" : "Scan complete");

        return result;
    }

    private void PushChildren(
        FileSystemNode directory,
        Stack<ScanFrame> stack,
        List<ScanError> errors,
        LargestFilesTracker largestFiles,
        FileTypeStatisticsTracker fileTypes,
        ref long scannedFiles,
        ref long scannedDirectories,
        ref long totalBytes,
        Action<string> reportProgress,
        CancellationToken cancellationToken)
    {
        if (_enumerationMode == DiskScannerEnumerationMode.FileSystemInfoMetadata)
        {
            PushChildrenFromFileSystemInfos(
                directory,
                stack,
                errors,
                largestFiles,
                fileTypes,
                ref scannedFiles,
                ref scannedDirectories,
                ref totalBytes,
                reportProgress,
                cancellationToken);
            return;
        }

        PushChildrenLegacyPathAndStat(
            directory,
            stack,
            errors,
            largestFiles,
            fileTypes,
            ref scannedFiles,
            ref scannedDirectories,
            ref totalBytes,
            reportProgress,
            cancellationToken);
    }

    /// <summary>
    /// v1.1 path: one directory enumeration yields <see cref="FileSystemInfo"/> with attributes/times/length
    /// from FindFirstFile/FindNextFile, avoiding per-entry GetAttributes + FileInfo round-trips.
    /// </summary>
    private void PushChildrenFromFileSystemInfos(
        FileSystemNode directory,
        Stack<ScanFrame> stack,
        List<ScanError> errors,
        LargestFilesTracker largestFiles,
        FileTypeStatisticsTracker fileTypes,
        ref long scannedFiles,
        ref long scannedDirectories,
        ref long totalBytes,
        Action<string> reportProgress,
        CancellationToken cancellationToken)
    {
        var enumPath = directory.FullPath.Length >= 240
            ? PathHelper.EnsureExtendedLengthPath(directory.FullPath)
            : directory.FullPath;

        IEnumerable<FileSystemInfo> infos;
        try
        {
            infos = new DirectoryInfo(enumPath).EnumerateFileSystemInfos();
        }
        catch (Exception ex) when (IsExpectedFilesystemException(ex))
        {
            directory.ScanStatus = NodeScanStatus.Inaccessible;
            AddError(errors, directory.FullPath, ex);
            _logger?.LogDebug(ex, "Unable to enumerate {Path}", PathRedaction.ForLog(directory.FullPath));
            return;
        }

        var childDirectories = new List<FileSystemNode>();

        foreach (var info in infos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string entryPath;
            FileAttributes attributes;
            try
            {
                entryPath = PathHelper.GetDisplayPath(info.FullName);
                // Uses metadata captured during enumeration when available (no Refresh).
                attributes = info.Attributes;
            }
            catch (Exception ex) when (IsExpectedFilesystemException(ex))
            {
                AddError(errors, PathHelper.GetDisplayPath(info.FullName), ex);
                continue;
            }

            var isDirectory = attributes.HasFlag(FileAttributes.Directory);

            if (isDirectory && ReparsePointHelper.IsDirectoryReparsePoint(attributes))
            {
                var skipped = new FileSystemNode
                {
                    Name = PathHelper.GetNodeName(entryPath),
                    FullPath = entryPath,
                    IsDirectory = true,
                    Parent = directory,
                    ScanStatus = NodeScanStatus.SkippedReparsePoint,
                    LastModified = TryReadLastWrite(info)
                };

                directory.Children.Add(skipped);
                errors.Add(new ScanError
                {
                    Path = entryPath,
                    ErrorType = ScanErrorType.ReparsePointSkipped,
                    Message = "Directory symbolic link or junction was skipped and not traversed."
                });
                scannedDirectories++;
                reportProgress(entryPath);
                continue;
            }

            if (isDirectory)
            {
                var childDir = new FileSystemNode
                {
                    Name = PathHelper.GetNodeName(entryPath),
                    FullPath = entryPath,
                    IsDirectory = true,
                    Parent = directory,
                    ScanStatus = NodeScanStatus.Pending,
                    LastModified = TryReadLastWrite(info)
                };

                directory.Children.Add(childDir);
                childDirectories.Add(childDir);
                scannedDirectories++;
                reportProgress(entryPath);
                continue;
            }

            // File (including file symlinks: logical size only; do not follow as a directory).
            long length;
            DateTimeOffset? lastWrite;
            try
            {
                if (info is FileInfo fileInfo)
                {
                    length = fileInfo.Length;
                    lastWrite = TryReadLastWrite(fileInfo);
                }
                else
                {
                    // Unusual: non-directory that is not FileInfo — fall back once.
                    var fallback = new FileInfo(
                        entryPath.Length >= 240 ? PathHelper.EnsureExtendedLengthPath(entryPath) : entryPath);
                    length = fallback.Length;
                    lastWrite = fallback.Exists ? fallback.LastWriteTimeUtc : null;
                }
            }
            catch (Exception ex) when (IsExpectedFilesystemException(ex))
            {
                AddError(errors, entryPath, ex);
                continue;
            }

            var fileName = PathHelper.GetNodeName(entryPath);
            var fileNode = new FileSystemNode
            {
                Name = fileName,
                FullPath = entryPath,
                IsDirectory = false,
                Parent = directory,
                SizeBytes = length,
                FileCount = 1,
                ScanStatus = NodeScanStatus.Completed,
                LastModified = lastWrite
            };

            directory.Children.Add(fileNode);
            largestFiles.Offer(fileName, entryPath, length, lastWrite);
            fileTypes.Offer(fileName, length);
            scannedFiles++;
            totalBytes += length;
            reportProgress(entryPath);
        }

        for (var i = childDirectories.Count - 1; i >= 0; i--)
        {
            var child = childDirectories[i];
            stack.Push(ScanFrame.Finalize(child));
            stack.Push(ScanFrame.Process(child));
        }
    }

    /// <summary>v1.0 path: enumerate names, then re-stat each entry.</summary>
    private void PushChildrenLegacyPathAndStat(
        FileSystemNode directory,
        Stack<ScanFrame> stack,
        List<ScanError> errors,
        LargestFilesTracker largestFiles,
        FileTypeStatisticsTracker fileTypes,
        ref long scannedFiles,
        ref long scannedDirectories,
        ref long totalBytes,
        Action<string> reportProgress,
        CancellationToken cancellationToken)
    {
        IEnumerable<string> entries;
        var enumPath = directory.FullPath.Length >= 240
            ? PathHelper.EnsureExtendedLengthPath(directory.FullPath)
            : directory.FullPath;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(enumPath);
        }
        catch (Exception ex) when (IsExpectedFilesystemException(ex))
        {
            directory.ScanStatus = NodeScanStatus.Inaccessible;
            AddError(errors, directory.FullPath, ex);
            _logger?.LogDebug(ex, "Unable to enumerate {Path}", PathRedaction.ForLog(directory.FullPath));
            return;
        }

        var childDirectories = new List<FileSystemNode>();

        foreach (var rawEntryPath in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryPath = PathHelper.GetDisplayPath(rawEntryPath);

            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(
                    rawEntryPath.Length >= 260 ? rawEntryPath : entryPath);
            }
            catch (Exception ex) when (IsExpectedFilesystemException(ex))
            {
                AddError(errors, entryPath, ex);
                continue;
            }

            var isDirectory = attributes.HasFlag(FileAttributes.Directory);

            if (isDirectory && ReparsePointHelper.IsDirectoryReparsePoint(attributes))
            {
                var skipped = new FileSystemNode
                {
                    Name = PathHelper.GetNodeName(entryPath),
                    FullPath = entryPath,
                    IsDirectory = true,
                    Parent = directory,
                    ScanStatus = NodeScanStatus.SkippedReparsePoint,
                    LastModified = TryGetLastWriteTime(entryPath)
                };

                directory.Children.Add(skipped);
                errors.Add(new ScanError
                {
                    Path = entryPath,
                    ErrorType = ScanErrorType.ReparsePointSkipped,
                    Message = "Directory symbolic link or junction was skipped and not traversed."
                });
                scannedDirectories++;
                reportProgress(entryPath);
                continue;
            }

            if (isDirectory)
            {
                var childDir = new FileSystemNode
                {
                    Name = PathHelper.GetNodeName(entryPath),
                    FullPath = entryPath,
                    IsDirectory = true,
                    Parent = directory,
                    ScanStatus = NodeScanStatus.Pending,
                    LastModified = TryGetLastWriteTime(entryPath)
                };

                directory.Children.Add(childDir);
                childDirectories.Add(childDir);
                scannedDirectories++;
                reportProgress(entryPath);
                continue;
            }

            long length = 0;
            DateTimeOffset? lastWrite = null;
            try
            {
                var infoPath = entryPath.Length >= 240
                    ? PathHelper.EnsureExtendedLengthPath(entryPath)
                    : entryPath;
                var info = new FileInfo(infoPath);
                length = info.Length;
                lastWrite = info.Exists ? info.LastWriteTimeUtc : null;
            }
            catch (Exception ex) when (IsExpectedFilesystemException(ex))
            {
                AddError(errors, entryPath, ex);
                continue;
            }

            var fileName = PathHelper.GetNodeName(entryPath);
            var fileNode = new FileSystemNode
            {
                Name = fileName,
                FullPath = entryPath,
                IsDirectory = false,
                Parent = directory,
                SizeBytes = length,
                FileCount = 1,
                ScanStatus = NodeScanStatus.Completed,
                LastModified = lastWrite
            };

            directory.Children.Add(fileNode);
            largestFiles.Offer(fileName, entryPath, length, lastWrite);
            fileTypes.Offer(fileName, length);
            scannedFiles++;
            totalBytes += length;
            reportProgress(entryPath);
        }

        for (var i = childDirectories.Count - 1; i >= 0; i--)
        {
            var child = childDirectories[i];
            stack.Push(ScanFrame.Finalize(child));
            stack.Push(ScanFrame.Process(child));
        }
    }

    private static void FinalizeNode(FileSystemNode node)
    {
        if (!node.IsDirectory)
        {
            return;
        }

        long size = 0;
        long files = 0;
        long dirs = 0;

        foreach (var child in node.Children)
        {
            size += child.SizeBytes;
            files += child.FileCount;
            dirs += child.DirectoryCount;
            if (child.IsDirectory)
            {
                dirs += 1;
            }
        }

        node.SizeBytes = size;
        node.FileCount = files;
        node.DirectoryCount = dirs;

        if (node.ScanStatus is NodeScanStatus.Pending or NodeScanStatus.Scanning)
        {
            node.ScanStatus = NodeScanStatus.Completed;
        }

        node.Children.Sort(static (a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
    }

    private static void AddError(List<ScanError> errors, string path, Exception ex)
    {
        errors.Add(new ScanError
        {
            Path = PathHelper.GetDisplayPath(path),
            ErrorType = ClassifyError(ex),
            Message = ex.Message
        });
    }

    private static ScanErrorType ClassifyError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => ScanErrorType.AccessDenied,
        DirectoryNotFoundException or FileNotFoundException => ScanErrorType.NotFound,
        PathTooLongException => ScanErrorType.PathTooLong,
        IOException => ScanErrorType.IOError,
        _ => ScanErrorType.Unknown
    };

    private static bool IsExpectedFilesystemException(Exception ex) =>
        ex is UnauthorizedAccessException
            or DirectoryNotFoundException
            or FileNotFoundException
            or PathTooLongException
            or IOException
            or NotSupportedException;

    private static DateTimeOffset? TryGetLastWriteTime(string path)
    {
        try
        {
            return Directory.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (IsExpectedFilesystemException(ex))
        {
            return null;
        }
    }

    private static DateTimeOffset? TryReadLastWrite(FileSystemInfo info)
    {
        try
        {
            return info.LastWriteTimeUtc;
        }
        catch (Exception ex) when (IsExpectedFilesystemException(ex))
        {
            return null;
        }
    }

    private enum FrameKind
    {
        Process,
        Finalize
    }

    private readonly struct ScanFrame
    {
        private ScanFrame(FileSystemNode node, FrameKind kind)
        {
            Node = node;
            Kind = kind;
        }

        public FileSystemNode Node { get; }
        public FrameKind Kind { get; }

        public static ScanFrame Process(FileSystemNode node) => new(node, FrameKind.Process);
        public static ScanFrame Finalize(FileSystemNode node) => new(node, FrameKind.Finalize);
    }
}

using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Scanning;

namespace ExplorerPilot.Core.Reporting;

/// <summary>Builds a <see cref="DiskAnalysisReport"/> from an existing <see cref="ScanResult"/> (no I/O).</summary>
public static class DiskAnalysisReportBuilder
{
    public static DiskAnalysisReport Build(
        ScanResult scan,
        bool includeDirectoryTree,
        int maxLargestFiles = 100,
        int maxLargestFolders = 100,
        CancellationToken cancellationToken = default) =>
        Build(
            scan,
            new ReportBuildOptions
            {
                IncludeDirectoryTree = includeDirectoryTree,
                MaxLargestFiles = maxLargestFiles,
                MaxLargestFolders = maxLargestFolders
            },
            cancellationToken);

    public static DiskAnalysisReport Build(
        ScanResult scan,
        ReportBuildOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(options);

        var maxLargestFiles = Math.Clamp(options.MaxLargestFiles, 0, 10_000);
        var maxLargestFolders = Math.Clamp(options.MaxLargestFolders, 0, 10_000);

        cancellationToken.ThrowIfCancellationRequested();

        var completeness = ResolveCompleteness(scan);
        var summary = new ReportScanSummary
        {
            RootPath = scan.RootNode.FullPath,
            RootName = scan.RootNode.Name,
            TotalBytes = scan.TotalBytes,
            TotalFiles = scan.TotalFiles,
            TotalDirectories = scan.TotalDirectories,
            DurationSeconds = scan.Duration.TotalSeconds,
            FinalStatus = scan.FinalStatus,
            Completeness = completeness,
            CompletenessCode = completeness.ToString(),
            ErrorCount = scan.Errors.Count,
            IsIncomplete = completeness is ReportCompleteness.Cancelled or ReportCompleteness.Failed
        };

        var largestFiles = scan.LargestFiles
            .Take(maxLargestFiles)
            .Select(f => new ReportFileEntry
            {
                Name = f.Name,
                FullPath = f.FullPath,
                SizeBytes = f.SizeBytes,
                Extension = f.Extension,
                Category = FileCategoryMapper.GetCategory(
                    FileCategoryMapper.NormalizeExtension(string.IsNullOrEmpty(f.Extension) ? f.Name : f.Extension)),
                LastModifiedUtc = f.LastModifiedUtc
            })
            .ToList();

        cancellationToken.ThrowIfCancellationRequested();

        var largestFolders = CollectLargestFolders(scan.RootNode, maxLargestFolders, cancellationToken);

        IReadOnlyList<ReportCategoryEntry> categories = [];
        if (options.IncludeCategories)
        {
            categories = scan.CategoryStatistics
                .Select(c => new ReportCategoryEntry
                {
                    Category = c.Category,
                    CategoryCode = c.Category.ToString(),
                    FileCount = c.FileCount,
                    TotalSizeBytes = c.TotalSizeBytes,
                    PercentageOfTotalBytes = c.PercentageOfTotalBytes
                })
                .ToList();
        }

        IReadOnlyList<ReportExtensionEntry> extensions = [];
        if (options.IncludeExtensions)
        {
            extensions = scan.ExtensionStatistics
                .Select(e => new ReportExtensionEntry
                {
                    Extension = e.Extension,
                    Category = e.Category,
                    CategoryCode = e.Category.ToString(),
                    FileCount = e.FileCount,
                    TotalSizeBytes = e.TotalSizeBytes,
                    PercentageOfTotalBytes = e.PercentageOfTotalBytes
                })
                .ToList();
        }

        IReadOnlyList<ReportErrorEntry> errors = [];
        if (options.IncludeErrors)
        {
            errors = scan.Errors
                .Select(err => new ReportErrorEntry
                {
                    Path = err.Path,
                    ErrorType = err.ErrorType,
                    ErrorTypeCode = err.ErrorType.ToString(),
                    Message = err.Message
                })
                .ToList();
        }

        ReportDirectoryNode? tree = null;
        if (options.IncludeDirectoryTree)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var visited = 0;
            tree = MapTree(scan.RootNode, ref visited, cancellationToken);
        }

        return new DiskAnalysisReport
        {
            SchemaVersion = DiskAnalysisReport.CurrentSchemaVersion,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Summary = summary,
            LargestFiles = largestFiles,
            LargestFolders = largestFolders,
            Categories = categories,
            Extensions = extensions,
            Errors = errors,
            DirectoryTree = tree
        };
    }

    private static ReportCompleteness ResolveCompleteness(ScanResult scan)
    {
        if (scan.WasCancelled || scan.FinalStatus == ScanPhase.Cancelled)
        {
            return ReportCompleteness.Cancelled;
        }

        if (scan.FinalStatus == ScanPhase.Failed)
        {
            return ReportCompleteness.Failed;
        }

        if (scan.Errors.Count > 0)
        {
            return ReportCompleteness.Warnings;
        }

        return ReportCompleteness.Complete;
    }

    private static List<ReportFolderEntry> CollectLargestFolders(
        FileSystemNode root,
        int maxCount,
        CancellationToken cancellationToken)
    {
        if (maxCount <= 0)
        {
            return [];
        }

        var heap = new PriorityQueue<FileSystemNode, long>();
        var stack = new Stack<FileSystemNode>();
        stack.Push(root);
        var visited = 0;

        while (stack.Count > 0)
        {
            if ((++visited & 0x3FF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var node = stack.Pop();
            if (!node.IsDirectory)
            {
                continue;
            }

            if (heap.Count < maxCount)
            {
                heap.Enqueue(node, node.SizeBytes);
            }
            else if (heap.TryPeek(out _, out var smallest) && node.SizeBytes > smallest)
            {
                heap.Dequeue();
                heap.Enqueue(node, node.SizeBytes);
            }

            foreach (var child in node.Children)
            {
                if (child.IsDirectory)
                {
                    stack.Push(child);
                }
            }
        }

        var list = new List<ReportFolderEntry>(heap.Count);
        while (heap.Count > 0)
        {
            var node = heap.Dequeue();
            list.Add(new ReportFolderEntry
            {
                Name = node.Name,
                FullPath = node.FullPath,
                SizeBytes = node.SizeBytes,
                FileCount = node.FileCount,
                DirectoryCount = node.DirectoryCount
            });
        }

        list.Sort(static (a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        return list;
    }

    private static ReportDirectoryNode MapTree(
        FileSystemNode node,
        ref int visited,
        CancellationToken cancellationToken)
    {
        if ((++visited & 0x1FF) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        List<ReportDirectoryNode>? children = null;
        if (node.Children.Count > 0)
        {
            children = new List<ReportDirectoryNode>(node.Children.Count);
            foreach (var child in node.Children)
            {
                children.Add(MapTree(child, ref visited, cancellationToken));
            }
        }

        return new ReportDirectoryNode
        {
            Name = node.Name,
            FullPath = node.FullPath,
            IsDirectory = node.IsDirectory,
            SizeBytes = node.SizeBytes,
            FileCount = node.FileCount,
            DirectoryCount = node.DirectoryCount,
            ScanStatus = node.ScanStatus,
            ScanStatusCode = node.ScanStatus.ToString(),
            Children = children ?? []
        };
    }
}

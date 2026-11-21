using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.Infrastructure.Services;

/// <summary>
/// Computes dashboard summaries on a worker thread. Does not mutate the filesystem.
/// </summary>
public sealed class DashboardSummaryService : IDashboardSummaryService
{
    private readonly ILogger<DashboardSummaryService>? _logger;

    public DashboardSummaryService(ILogger<DashboardSummaryService>? logger = null)
    {
        _logger = logger;
    }

    public Task<DashboardSummary> BuildAsync(
        DashboardBuildRequest request,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Build(request, cancellationToken), cancellationToken);

    private DashboardSummary Build(DashboardBuildRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var capacity = BuildCapacity(request.Drives, cancellationToken);
        DashboardScanSummary? scanSummary = null;
        IReadOnlyList<DashboardPathEntry> largestFiles = [];
        IReadOnlyList<DashboardPathEntry> largestFolders = [];
        IReadOnlyList<DashboardCategorySummary> categories = [];

        if (request.Scan is { } scan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scanSummary = new DashboardScanSummary
            {
                RootPath = scan.RootNode.FullPath,
                CompletedAtUtc = request.ScanCompletedAtUtc,
                TotalBytes = scan.TotalBytes,
                TotalFiles = scan.TotalFiles,
                TotalDirectories = scan.TotalDirectories,
                Duration = scan.Duration,
                FinalStatus = scan.FinalStatus,
                IsIncomplete = request.ScanIncomplete || !scan.IsComplete,
                ErrorCount = scan.Errors.Count
            };

            var topFiles = Math.Clamp(request.TopFiles, 0, 100);
            largestFiles = scan.LargestFiles
                .Take(topFiles)
                .Select(f => new DashboardPathEntry
                {
                    Name = f.Name,
                    FullPath = f.FullPath,
                    SizeBytes = f.SizeBytes,
                    IsDirectory = false
                })
                .ToList();

            var topFolders = Math.Clamp(request.TopFolders, 0, 100);
            largestFolders = CollectLargestFolders(scan.RootNode, topFolders, cancellationToken);

            categories = scan.CategoryStatistics
                .Select(c => new DashboardCategorySummary
                {
                    Category = c.Category,
                    TotalSizeBytes = c.TotalSizeBytes,
                    FileCount = c.FileCount,
                    PercentageOfTotalBytes = c.PercentageOfTotalBytes
                })
                .ToList();
        }

        return new DashboardSummary
        {
            Capacity = capacity,
            LastScan = scanSummary,
            LargestFiles = largestFiles,
            LargestFolders = largestFolders,
            Categories = categories,
            Snapshot = request.Snapshot,
            Cleanup = request.Cleanup,
            ScanSessionId = request.ScanSessionId,
            BuiltAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static DashboardCapacitySummary BuildCapacity(
        IReadOnlyList<DriveInformation> drives,
        CancellationToken cancellationToken)
    {
        var cards = new List<DashboardDriveSummary>(drives.Count);
        long total = 0;
        long used = 0;
        long free = 0;
        var readyCount = 0;
        DashboardDriveSummary? highest = null;

        foreach (var drive in drives)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var include = drive.IsReady && drive.TotalSizeBytes > 0;
            var card = new DashboardDriveSummary
            {
                Name = drive.Name,
                RootPath = drive.RootPath,
                VolumeLabel = drive.VolumeLabel,
                Kind = drive.Kind,
                Status = drive.Status,
                TotalSizeBytes = drive.TotalSizeBytes,
                UsedSpaceBytes = drive.UsedSpaceBytes,
                AvailableFreeSpaceBytes = drive.AvailableFreeSpaceBytes,
                UsagePercentage = drive.UsagePercentage,
                IsReady = drive.IsReady,
                IncludeInCapacityTotals = include,
                CanScan = drive.CanScan
            };
            cards.Add(card);

            if (drive.IsReady)
            {
                readyCount++;
            }

            if (!include)
            {
                continue;
            }

            total += drive.TotalSizeBytes;
            used += drive.UsedSpaceBytes;
            free += drive.AvailableFreeSpaceBytes;

            if (highest is null || card.UsagePercentage > highest.UsagePercentage)
            {
                highest = card;
            }
        }

        return new DashboardCapacitySummary
        {
            DriveCount = drives.Count,
            ReadyDriveCount = readyCount,
            TotalCapacityBytes = total,
            UsedSpaceBytes = used,
            FreeSpaceBytes = free,
            UsagePercentage = DriveCapacityMath.SafeUsagePercentage(total, used),
            HighestUsageDrive = highest,
            Drives = cards
        };
    }

    /// <summary>
    /// Bounded top-N directories by logical size. Skips the scan root itself.
    /// </summary>
    private static IReadOnlyList<DashboardPathEntry> CollectLargestFolders(
        FileSystemNode root,
        int topN,
        CancellationToken cancellationToken)
    {
        if (topN <= 0)
        {
            return [];
        }

        var heap = new List<FileSystemNode>(topN + 1);
        var stack = new Stack<FileSystemNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = stack.Pop();

            foreach (var child in node.Children)
            {
                if (!child.IsDirectory)
                {
                    continue;
                }

                stack.Push(child);

                if (child.SizeBytes <= 0 || ReferenceEquals(child, root))
                {
                    continue;
                }

                if (heap.Count < topN)
                {
                    heap.Add(child);
                    if (heap.Count == topN)
                    {
                        heap.Sort(static (a, b) => a.SizeBytes.CompareTo(b.SizeBytes));
                    }

                    continue;
                }

                if (child.SizeBytes <= heap[0].SizeBytes)
                {
                    continue;
                }

                heap[0] = child;
                heap.Sort(static (a, b) => a.SizeBytes.CompareTo(b.SizeBytes));
            }
        }

        return heap
            .OrderByDescending(n => n.SizeBytes)
            .Select(n => new DashboardPathEntry
            {
                Name = n.Name,
                FullPath = n.FullPath,
                SizeBytes = n.SizeBytes,
                IsDirectory = true
            })
            .ToList();
    }
}

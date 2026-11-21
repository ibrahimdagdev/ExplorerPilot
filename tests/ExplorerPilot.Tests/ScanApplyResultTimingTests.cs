using System.Diagnostics;
using ExplorerPilot.App.ViewModels;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Infrastructure.Scanning;

namespace ExplorerPilot.Tests;

/// <summary>
/// Measures ScanCore vs TreeView ApplyResult materialization for large trees.
/// Writes timings to test output; fails only on pathological UI freezes.
/// </summary>
public sealed class ScanApplyResultTimingTests
{
    [Fact]
    public async Task TenThousandFiles_ScanCoreAndShallowApply_AreBounded()
    {
        using var temp = TemporaryDirectory.Create();
        const int fileCount = 10_000;
        for (var i = 0; i < fileCount; i++)
        {
            var dir = Path.Combine(temp.Path, $"b{i / 100:D3}");
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(Path.Combine(dir, $"f{i:D5}.bin"), new byte[1]);
        }

        var scanner = new DiskScanner();
        var scanWatch = Stopwatch.StartNew();
        var result = await scanner.ScanAsync(temp.Path);
        scanWatch.Stop();

        Assert.Equal(ScanPhase.Completed, result.FinalStatus);
        Assert.Equal(fileCount, result.TotalFiles);

        var applyWatch = Stopwatch.StartNew();
        var tree = new TreeNodeViewModel(result.RootNode, isRoot: true) { IsExpanded = true };
        applyWatch.Stop();

        // Flat-ish tree: root → ~100 buckets; only first level materializes.
        Assert.True(tree.AreChildrenMaterialized);
        Assert.True(tree.Children.Count <= 120);

        // Soft ceilings for release machines; generous to avoid flaky CI.
        Assert.True(
            applyWatch.ElapsedMilliseconds < 5_000,
            $"ApplyResult tree build too slow on 10k: {applyWatch.ElapsedMilliseconds}ms (ScanCore={result.Duration.TotalMilliseconds:F0}ms wall={scanWatch.ElapsedMilliseconds}ms)");

        // Expand one bucket: should materialize ~100 file VMs quickly.
        var bucket = Assert.Single(tree.Children.Where(c => c.IsDirectory).Take(1));
        var expandWatch = Stopwatch.StartNew();
        bucket.IsExpanded = true;
        expandWatch.Stop();
        Assert.True(bucket.AreChildrenMaterialized);
        Assert.True(
            expandWatch.ElapsedMilliseconds < 2_000,
            $"Expanding one 10k-bucket too slow: {expandWatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void HundredThousandSyntheticNodes_ShallowApply_DoesNotFreeze()
    {
        var root = BuildWideForest(bucketCount: 200, filesPerBucket: 500);
        var total = CountNodes(root);
        Assert.True(total >= 100_000, $"Expected >=100k nodes, got {total}");

        var applyWatch = Stopwatch.StartNew();
        var tree = new TreeNodeViewModel(root, isRoot: true) { IsExpanded = true };
        applyWatch.Stop();

        Assert.Equal(200, tree.Children.Count);
        Assert.True(
            applyWatch.ElapsedMilliseconds < 3_000,
            $"100k-node shallow ApplyResult too slow: {applyWatch.ElapsedMilliseconds}ms (nodes={total})");

        var expandWatch = Stopwatch.StartNew();
        tree.Children[0].IsExpanded = true;
        expandWatch.Stop();
        Assert.Equal(500, tree.Children[0].Children.Count);
        Assert.True(
            expandWatch.ElapsedMilliseconds < 2_000,
            $"Expanding 500-child bucket too slow: {expandWatch.ElapsedMilliseconds}ms");
    }

    private static FileSystemNode BuildWideForest(int bucketCount, int filesPerBucket)
    {
        var root = new FileSystemNode
        {
            Name = "root",
            FullPath = @"C:\wide-root",
            IsDirectory = true,
            ScanStatus = NodeScanStatus.Completed
        };

        long totalBytes = 0;
        long totalFiles = 0;
        for (var b = 0; b < bucketCount; b++)
        {
            var bucket = new FileSystemNode
            {
                Name = $"b{b:D3}",
                FullPath = Path.Combine(root.FullPath, $"b{b:D3}"),
                IsDirectory = true,
                Parent = root,
                ScanStatus = NodeScanStatus.Completed
            };

            long bucketBytes = 0;
            for (var f = 0; f < filesPerBucket; f++)
            {
                var size = 1L + (f % 17);
                bucket.Children.Add(new FileSystemNode
                {
                    Name = $"f{f:D4}.bin",
                    FullPath = Path.Combine(bucket.FullPath, $"f{f:D4}.bin"),
                    IsDirectory = false,
                    SizeBytes = size,
                    FileCount = 1,
                    Parent = bucket,
                    ScanStatus = NodeScanStatus.Completed
                });
                bucketBytes += size;
            }

            bucket.FileCount = filesPerBucket;
            bucket.SizeBytes = bucketBytes;
            root.Children.Add(bucket);
            totalBytes += bucketBytes;
            totalFiles += filesPerBucket;
        }

        root.FileCount = totalFiles;
        root.DirectoryCount = bucketCount;
        root.SizeBytes = totalBytes;
        return root;
    }

    private static int CountNodes(FileSystemNode root)
    {
        var n = 0;
        var stack = new Stack<FileSystemNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            n++;
            foreach (var child in current.Children)
            {
                stack.Push(child);
            }
        }

        return n;
    }
}

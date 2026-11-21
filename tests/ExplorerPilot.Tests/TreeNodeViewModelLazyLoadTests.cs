using System.Diagnostics;
using ExplorerPilot.App.ViewModels;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;

namespace ExplorerPilot.Tests;

public sealed class TreeNodeViewModelLazyLoadTests
{
    [Fact]
    public void ExpandingRoot_MaterializesOnlyDirectChildren()
    {
        var root = BuildNestedTree(depth: 3, filesPerDir: 20, dirsPerLevel: 5);
        var vm = new TreeNodeViewModel(root, isRoot: true);

        Assert.False(vm.AreChildrenMaterialized);

        vm.IsExpanded = true;

        Assert.True(vm.AreChildrenMaterialized);
        Assert.Equal(root.Children.Count, vm.Children.Count);
        Assert.Contains(vm.Children, c => c.IsDirectory && !c.AreChildrenMaterialized);
    }

    [Fact]
    public void ApplyResultStyleConstruction_StaysSubLinearForDeepForest()
    {
        // ~5 * 5 * 5 dirs + files ≈ nested tree with thousands of nodes; eager ctor would allocate all.
        var root = BuildNestedTree(depth: 4, filesPerDir: 40, dirsPerLevel: 5);
        var totalNodes = CountNodes(root);
        Assert.True(totalNodes > 5_000, $"Fixture too small: {totalNodes}");

        var sw = Stopwatch.StartNew();
        var vm = new TreeNodeViewModel(root, isRoot: true) { IsExpanded = true };
        sw.Stop();

        Assert.True(vm.AreChildrenMaterialized);
        var materialized = 1 + vm.Children.Count;
        Assert.True(
            materialized < totalNodes / 10,
            $"Expected shallow materialization; got {materialized} of {totalNodes} in {sw.ElapsedMilliseconds}ms");
        Assert.True(
            sw.ElapsedMilliseconds < 2_000,
            $"Shallow ApplyResult tree build too slow: {sw.ElapsedMilliseconds}ms for {totalNodes} nodes");
    }

    [Fact]
    public void EnsureChildrenMaterialized_IsIdempotent()
    {
        var root = BuildNestedTree(depth: 2, filesPerDir: 3, dirsPerLevel: 2);
        var vm = new TreeNodeViewModel(root, isRoot: true);
        vm.EnsureChildrenMaterialized();
        var first = vm.Children.ToList();
        vm.EnsureChildrenMaterialized();
        Assert.Equal(first.Count, vm.Children.Count);
        Assert.True(ReferenceEquals(first[0], vm.Children[0]));
    }

    private static FileSystemNode BuildNestedTree(int depth, int filesPerDir, int dirsPerLevel)
    {
        var root = new FileSystemNode
        {
            Name = "root",
            FullPath = @"C:\fixture-root",
            IsDirectory = true,
            ScanStatus = NodeScanStatus.Completed
        };

        BuildLevel(root, depth, filesPerDir, dirsPerLevel, 0);
        Rollup(root);
        return root;
    }

    private static void BuildLevel(FileSystemNode dir, int depthRemaining, int filesPerDir, int dirsPerLevel, int id)
    {
        for (var f = 0; f < filesPerDir; f++)
        {
            var file = new FileSystemNode
            {
                Name = $"f{id}_{f}.bin",
                FullPath = Path.Combine(dir.FullPath, $"f{id}_{f}.bin"),
                IsDirectory = false,
                SizeBytes = 10 + f,
                FileCount = 1,
                Parent = dir,
                ScanStatus = NodeScanStatus.Completed
            };
            dir.Children.Add(file);
        }

        if (depthRemaining <= 1)
        {
            return;
        }

        for (var d = 0; d < dirsPerLevel; d++)
        {
            var child = new FileSystemNode
            {
                Name = $"d{id}_{d}",
                FullPath = Path.Combine(dir.FullPath, $"d{id}_{d}"),
                IsDirectory = true,
                Parent = dir,
                ScanStatus = NodeScanStatus.Completed
            };
            dir.Children.Add(child);
            BuildLevel(child, depthRemaining - 1, filesPerDir, dirsPerLevel, id * 10 + d + 1);
        }
    }

    private static void Rollup(FileSystemNode node)
    {
        if (!node.IsDirectory)
        {
            return;
        }

        long files = 0;
        long dirs = 0;
        long bytes = 0;
        foreach (var child in node.Children)
        {
            Rollup(child);
            if (child.IsDirectory)
            {
                dirs += 1 + child.DirectoryCount;
                files += child.FileCount;
                bytes += child.SizeBytes;
            }
            else
            {
                files += 1;
                bytes += child.SizeBytes;
            }
        }

        node.FileCount = files;
        node.DirectoryCount = dirs;
        node.SizeBytes = bytes;
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

using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Snapshots;
using ExplorerPilot.Infrastructure.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExplorerPilot.Tests;

public sealed class SnapshotComparisonTests
{
    [Fact]
    public async Task SaveLoad_RoundTrip_PreservesSchemaAndEntries()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, dir);
            var scan = CreateScan(@"C:\Data", MakeFile("a.txt", 100), Dir("sub", MakeFile("b.txt", 50)));
            var info = await store.SaveAsync(scan, "Test Snap");
            var loaded = await store.LoadAsync(info.Id);

            Assert.Equal(SnapshotSchema.CurrentVersion, loaded.SchemaVersion);
            Assert.Equal(info.Id, loaded.Id);
            Assert.Equal("Test Snap", loaded.DisplayName);
            Assert.Contains(loaded.Entries, e => e.RelativePath.Equals("a.txt", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(loaded.Entries, e => e.RelativePath.Equals(@"sub\b.txt", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Compare_DetectsAddedRemovedAndChangedFiles()
    {
        var leftScan = CreateScan(@"C:\Data", MakeFile("keep.txt", 10), MakeFile("gone.txt", 20), MakeFile("grow.txt", 30));
        var rightScan = CreateScan(@"C:\Data", MakeFile("keep.txt", 10), MakeFile("grow.txt", 80), MakeFile("new.txt", 40));
        var left = DiskSnapshotBuilder.Build(leftScan, "L");
        var right = DiskSnapshotBuilder.Build(rightScan, "R");

        var service = new SnapshotComparisonService(NullLogger<SnapshotComparisonService>.Instance);
        var result = await service.CompareAsync(left, right);

        Assert.Contains(result.AddedFiles, f => f.Name == "new.txt");
        Assert.Contains(result.RemovedFiles, f => f.Name == "gone.txt");
        var changed = Assert.Single(result.ChangedFiles, f => f.Name == "grow.txt");
        Assert.Equal(50, changed.DeltaBytes);
        Assert.Equal(70, result.Summary.TotalBytesDelta); // 130 - 60
    }

    [Fact]
    public async Task Compare_FolderGrowthAndShrink()
    {
        var leftScan = CreateScan(
            @"C:\Data",
            Dir("big", MakeFile("a.bin", 100)),
            Dir("small", MakeFile("b.bin", 200)));
        var rightScan = CreateScan(
            @"C:\Data",
            Dir("big", MakeFile("a.bin", 300)),
            Dir("small", MakeFile("b.bin", 50)));

        var left = DiskSnapshotBuilder.Build(leftScan);
        var right = DiskSnapshotBuilder.Build(rightScan);
        var result = await new SnapshotComparisonService(NullLogger<SnapshotComparisonService>.Instance)
            .CompareAsync(left, right);

        Assert.Contains(result.GrowingFolders, f => f.RelativePath.Equals("big", StringComparison.OrdinalIgnoreCase) && f.DeltaBytes == 200);
        Assert.Contains(result.ShrinkingFolders, f => f.RelativePath.Equals("small", StringComparison.OrdinalIgnoreCase) && f.DeltaBytes == -150);
    }

    [Fact]
    public void PathNormalizer_IsCaseInsensitive_AndRelative()
    {
        var root = SnapshotPathNormalizer.NormalizeAbsolute(@"c:\Data\");
        Assert.Equal(@"C:\Data", root);
        Assert.True(SnapshotPathNormalizer.RootsEquivalent(@"C:\DATA", @"c:\data"));
        Assert.Equal(
            @"folder\file.txt",
            SnapshotPathNormalizer.MakeRelative(@"C:\Data", @"C:\data\folder\file.txt"),
            ignoreCase: true);
        Assert.True(SnapshotPathNormalizer.RootsEquivalent(@"c:\data", @"C:\Data\"));
    }

    [Fact]
    public async Task Compare_DifferentRoots_UsesRelativePaths()
    {
        var left = DiskSnapshotBuilder.Build(CreateScan(@"D:\OldRoot", MakeFile("x.bin", 10)));
        var right = DiskSnapshotBuilder.Build(CreateScan(@"E:\NewRoot", MakeFile("x.bin", 25)));
        var result = await new SnapshotComparisonService(NullLogger<SnapshotComparisonService>.Instance)
            .CompareAsync(left, right);

        Assert.True(result.Summary.RootsDiffer);
        var changed = Assert.Single(result.ChangedFiles);
        Assert.Equal("x.bin", changed.RelativePath);
        Assert.Equal(15, changed.DeltaBytes);
    }

    [Fact]
    public async Task Load_RejectsUnsupportedMajorSchema()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, dir);
            var id = Guid.NewGuid();
            var path = Path.Combine(dir, id.ToString("N") + SnapshotSchema.FileExtension);
            var json = $$"""
                {"schemaVersion":"9.0","id":"{{id:D}}","createdAtUtc":"2024-01-01T00:00:00Z","displayName":"x","rootPath":"C:\\","rootName":"C","normalizedRootPath":"C:\\","completenessCode":"Complete","entries":[]}
                """;
            await File.WriteAllTextAsync(path, json);

            await Assert.ThrowsAsync<SnapshotValidationException>(() => store.LoadAsync(id));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Load_RejectsCorruptJson()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, dir);
            var id = Guid.NewGuid();
            var path = Path.Combine(dir, id.ToString("N") + SnapshotSchema.FileExtension);
            await File.WriteAllTextAsync(path, "{not-json");

            await Assert.ThrowsAsync<SnapshotValidationException>(() => store.LoadAsync(id));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task CancelledSave_LeavesNoPartialDestination()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, dir);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var scan = CreateScan(@"C:\Data", Enumerable.Range(0, 200).Select(i => MakeFile($"f{i}.bin", i + 1)).ToArray());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(scan, cancellationToken: cts.Token));
            Assert.Empty(Directory.GetFiles(dir, "*" + SnapshotSchema.FileExtension));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Delete_OnlyAllowsSandboxSnapshotFiles()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, dir);
            var info = await store.SaveAsync(CreateScan(@"C:\Data", MakeFile("a.bin", 1)), "del-me");
            Assert.True(await store.DeleteAsync(info.Id));
            Assert.False(await store.DeleteAsync(info.Id));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                store.LoadFromPathAsync(Path.Combine(Path.GetTempPath(), "outside.snapshot.json")));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task LargeDataset_CompareWithinBudget()
    {
        var filesLeft = Enumerable.Range(0, 3_000).Select(i => MakeFile($"f{i}.bin", 10 + i)).ToArray();
        var filesRight = Enumerable.Range(0, 3_000).Select(i => MakeFile($"f{i}.bin", 12 + i)).ToArray();
        var left = DiskSnapshotBuilder.Build(CreateScan(@"C:\Big", filesLeft));
        var right = DiskSnapshotBuilder.Build(CreateScan(@"C:\Big", filesRight));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await new SnapshotComparisonService(NullLogger<SnapshotComparisonService>.Instance)
            .CompareAsync(left, right);
        sw.Stop();

        Assert.Equal(3_000, result.Summary.ChangedFileCount);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Compare_Cancellation_Throws()
    {
        var left = DiskSnapshotBuilder.Build(CreateScan(@"C:\Data", MakeFile("a.bin", 1)));
        var right = DiskSnapshotBuilder.Build(CreateScan(@"C:\Data", MakeFile("a.bin", 2)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SnapshotComparisonService(NullLogger<SnapshotComparisonService>.Instance)
                .CompareAsync(left, right, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task IncompleteScan_IsFlaggedInSnapshot()
    {
        var scan = CreateScan(@"C:\Data", MakeFile("a.bin", 1));
        scan = new ScanResult
        {
            RootNode = scan.RootNode,
            TotalFiles = scan.TotalFiles,
            TotalDirectories = scan.TotalDirectories,
            TotalBytes = scan.TotalBytes,
            Duration = scan.Duration,
            FinalStatus = ScanPhase.Cancelled,
            CategoryStatistics = scan.CategoryStatistics,
            ExtensionStatistics = scan.ExtensionStatistics,
            LargestFiles = scan.LargestFiles,
            Errors = scan.Errors
        };

        var snap = DiskSnapshotBuilder.Build(scan);
        Assert.True(snap.IsIncomplete);
        Assert.Equal("Cancelled", snap.CompletenessCode);
        Assert.Equal(SnapshotSchema.CurrentVersion, snap.SchemaVersion);
    }

    private static ScanResult CreateScan(string rootPath, params FileSystemNode[] children)
    {
        var rootName = Path.GetFileName(rootPath.TrimEnd('\\', '/')) is { Length: > 0 } name
            ? name
            : rootPath;
        var root = new FileSystemNode
        {
            Name = rootName,
            FullPath = rootPath,
            IsDirectory = true
        };

        foreach (var child in children)
        {
            var attached = child.IsDirectory
                ? RelocateDirectory(child, root.FullPath)
                : RelocateFile(child, root.FullPath);
            attached.Parent = root;
            root.Children.Add(attached);
            root.SizeBytes += attached.SizeBytes;
            root.FileCount += attached.IsDirectory ? attached.FileCount : 1;
            root.DirectoryCount += attached.IsDirectory ? 1 + attached.DirectoryCount : 0;
        }

        return new ScanResult
        {
            RootNode = root,
            TotalFiles = root.FileCount,
            TotalDirectories = root.DirectoryCount,
            TotalBytes = root.SizeBytes,
            Duration = TimeSpan.FromSeconds(1),
            FinalStatus = ScanPhase.Completed,
            CategoryStatistics =
            [
                new FileCategoryStatistics
                {
                    Category = FileCategory.Other,
                    FileCount = root.FileCount,
                    TotalSizeBytes = root.SizeBytes,
                    PercentageOfTotalBytes = 100
                }
            ],
            ExtensionStatistics =
            [
                new FileExtensionStatistics
                {
                    Extension = ".bin",
                    Category = FileCategory.Other,
                    FileCount = root.FileCount,
                    TotalSizeBytes = root.SizeBytes,
                    PercentageOfTotalBytes = 100
                }
            ]
        };
    }

    private static FileSystemNode Dir(string name, params FileSystemNode[] children)
    {
        var node = new FileSystemNode
        {
            Name = name,
            FullPath = @"C:\tests\" + name,
            IsDirectory = true
        };

        foreach (var child in children)
        {
            var attached = child.IsDirectory
                ? RelocateDirectory(child, node.FullPath)
                : RelocateFile(child, node.FullPath);
            attached.Parent = node;
            node.Children.Add(attached);
            node.SizeBytes += attached.SizeBytes;
            node.FileCount += attached.IsDirectory ? attached.FileCount : 1;
            node.DirectoryCount += attached.IsDirectory ? 1 + attached.DirectoryCount : 0;
        }

        return node;
    }

    private static FileSystemNode RelocateFile(FileSystemNode source, string parentPath) =>
        new()
        {
            Name = source.Name,
            FullPath = Path.Combine(parentPath, source.Name),
            IsDirectory = false,
            SizeBytes = source.SizeBytes,
            FileCount = 1
        };

    private static FileSystemNode RelocateDirectory(FileSystemNode source, string parentPath)
    {
        var relocated = new FileSystemNode
        {
            Name = source.Name,
            FullPath = Path.Combine(parentPath, source.Name),
            IsDirectory = true,
            SizeBytes = source.SizeBytes,
            FileCount = source.FileCount,
            DirectoryCount = source.DirectoryCount
        };

        foreach (var child in source.Children)
        {
            var attached = child.IsDirectory
                ? RelocateDirectory(child, relocated.FullPath)
                : RelocateFile(child, relocated.FullPath);
            attached.Parent = relocated;
            relocated.Children.Add(attached);
        }

        return relocated;
    }

    private static FileSystemNode MakeFile(string name, long size) =>
        new()
        {
            Name = name,
            FullPath = @"C:\tests\" + name,
            IsDirectory = false,
            SizeBytes = size,
            FileCount = 1
        };

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ep-snap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // ignore
        }
    }
}

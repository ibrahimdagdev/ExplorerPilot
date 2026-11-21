using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Snapshots;
using ExplorerPilot.Infrastructure.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExplorerPilot.Tests;

public sealed class SnapshotStabilizationTests
{
    [Fact]
    public async Task ImportExport_RoundTrip_PreservesContent()
    {
        var storeDir = CreateTempDir();
        var exportDir = CreateTempDir();
        try
        {
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, storeDir);
            var info = await store.SaveAsync(CreateScan(@"C:\Data", MakeFile("a.txt", 42)), "RoundTrip");

            var exportPath = Path.Combine(exportDir, "exported" + SnapshotSchema.FileExtension);
            await store.ExportAsync(info.Id, exportPath);
            Assert.True(File.Exists(exportPath));

            // External file must survive; store must not delete it.
            Assert.True(File.Exists(exportPath));

            var emptyStoreDir = CreateTempDir();
            try
            {
                var importStore = new SnapshotStore(NullLogger<SnapshotStore>.Instance, emptyStoreDir);
                var imported = await importStore.ImportAsync(exportPath, SnapshotIdCollisionPolicy.AssignNewId);
                var loaded = await importStore.LoadAsync(imported.Info.Id);

                Assert.Equal(SnapshotSchema.CurrentVersion, loaded.SchemaVersion);
                Assert.Equal("RoundTrip", loaded.DisplayName);
                Assert.Contains(loaded.Entries, e => e.RelativePath.Equals("a.txt", StringComparison.OrdinalIgnoreCase));
                Assert.True(File.Exists(exportPath));
            }
            finally
            {
                TryDeleteDir(emptyStoreDir);
            }
        }
        finally
        {
            TryDeleteDir(storeDir);
            TryDeleteDir(exportDir);
        }
    }

    [Fact]
    public async Task Import_RejectsCorruptJson_WithoutTouchingSandbox()
    {
        var storeDir = CreateTempDir();
        var external = Path.Combine(CreateTempDir(), "bad" + SnapshotSchema.FileExtension);
        try
        {
            await File.WriteAllTextAsync(external, "{not-json");
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, storeDir);

            await Assert.ThrowsAsync<SnapshotValidationException>(() =>
                store.ImportAsync(external, SnapshotIdCollisionPolicy.AssignNewId));

            Assert.Empty(Directory.GetFiles(storeDir, "*" + SnapshotSchema.FileExtension));
            Assert.True(File.Exists(external));
        }
        finally
        {
            TryDeleteDir(storeDir);
            TryDeleteDir(Path.GetDirectoryName(external)!);
        }
    }

    [Fact]
    public async Task Import_RejectsOversizedFile()
    {
        var storeDir = CreateTempDir();
        var externalDir = CreateTempDir();
        var external = Path.Combine(externalDir, "huge" + SnapshotSchema.FileExtension);
        try
        {
            // Sparse-ish large file via SetLength without filling content fully is enough for size check.
            await using (var fs = new FileStream(external, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.SetLength(SnapshotStore.MaxImportFileBytes + 1);
            }

            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, storeDir);
            var ex = await Assert.ThrowsAsync<SnapshotValidationException>(() =>
                store.ImportAsync(external));
            Assert.Contains("maximum", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.GetFiles(storeDir, "*" + SnapshotSchema.FileExtension));
        }
        finally
        {
            TryDeleteDir(storeDir);
            TryDeleteDir(externalDir);
        }
    }

    [Fact]
    public async Task Import_DuplicateId_AssignNewId_KeepsBoth()
    {
        var storeDir = CreateTempDir();
        var exportDir = CreateTempDir();
        try
        {
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, storeDir);
            var original = await store.SaveAsync(CreateScan(@"C:\Data", MakeFile("a.bin", 1)), "Original");
            var exportPath = Path.Combine(exportDir, "dup" + SnapshotSchema.FileExtension);
            await store.ExportAsync(original.Id, exportPath);

            var imported = await store.ImportAsync(exportPath, SnapshotIdCollisionPolicy.AssignNewId);
            Assert.True(imported.IdWasReassigned);
            Assert.NotEqual(original.Id, imported.Info.Id);

            var list = await store.ListAsync();
            Assert.Equal(2, list.Count);
            Assert.Contains(list, s => s.Id == original.Id);
            Assert.Contains(list, s => s.Id == imported.Info.Id);
        }
        finally
        {
            TryDeleteDir(storeDir);
            TryDeleteDir(exportDir);
        }
    }

    [Fact]
    public async Task Import_DuplicateId_ReplaceExisting_OverwritesSandboxOnly()
    {
        var storeDir = CreateTempDir();
        var exportDir = CreateTempDir();
        try
        {
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, storeDir);
            var original = await store.SaveAsync(CreateScan(@"C:\Data", MakeFile("old.bin", 10)), "Old");
            var exportPath = Path.Combine(exportDir, "replace" + SnapshotSchema.FileExtension);

            var replacement = DiskSnapshotBuilder.Build(
                CreateScan(@"C:\Data", MakeFile("new.bin", 99)),
                "Replaced");
            replacement = CloneWithId(replacement, original.Id);

            var jsonOptions = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                Converters =
                {
                    new System.Text.Json.Serialization.JsonStringEnumConverter(
                        System.Text.Json.JsonNamingPolicy.CamelCase)
                }
            };
            await using (var stream = File.Create(exportPath))
            {
                await System.Text.Json.JsonSerializer.SerializeAsync(stream, replacement, jsonOptions);
            }

            var imported = await store.ImportAsync(exportPath, SnapshotIdCollisionPolicy.ReplaceExisting);
            Assert.True(imported.ReplacedExisting);
            Assert.Equal(original.Id, imported.Info.Id);

            var loaded = await store.LoadAsync(original.Id);
            Assert.Equal("Replaced", loaded.DisplayName);
            Assert.Contains(loaded.Entries, e => e.Name.Equals("new.bin", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(exportPath));
        }
        finally
        {
            TryDeleteDir(storeDir);
            TryDeleteDir(exportDir);
        }
    }

    [Fact]
    public async Task Import_DuplicateId_KeepExisting_Aborts()
    {
        var storeDir = CreateTempDir();
        var exportDir = CreateTempDir();
        try
        {
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, storeDir);
            var original = await store.SaveAsync(CreateScan(@"C:\Data", MakeFile("a.bin", 1)), "Keep");
            var exportPath = Path.Combine(exportDir, "keep" + SnapshotSchema.FileExtension);
            await store.ExportAsync(original.Id, exportPath);

            await Assert.ThrowsAsync<SnapshotValidationException>(() =>
                store.ImportAsync(exportPath, SnapshotIdCollisionPolicy.KeepExisting));

            var list = await store.ListAsync();
            Assert.Single(list);
            Assert.Equal(original.Id, list[0].Id);
            Assert.True(File.Exists(exportPath));
        }
        finally
        {
            TryDeleteDir(storeDir);
            TryDeleteDir(exportDir);
        }
    }

    [Fact]
    public async Task Import_RejectsPathTraversalEntries()
    {
        var storeDir = CreateTempDir();
        var externalDir = CreateTempDir();
        var external = Path.Combine(externalDir, "evil" + SnapshotSchema.FileExtension);
        try
        {
            var id = Guid.NewGuid();
            var json = $$"""
                {"schemaVersion":"1.0","id":"{{id:D}}","createdAtUtc":"2024-01-01T00:00:00Z","displayName":"evil","rootPath":"C:\\Data","rootName":"Data","normalizedRootPath":"C:\\Data","completenessCode":"Complete","isIncomplete":false,"entries":[{"relativePath":"..\\Windows\\evil.txt","name":"evil.txt","isDirectory":false,"sizeBytes":1}]}
                """;
            await File.WriteAllTextAsync(external, json);

            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, storeDir);
            await Assert.ThrowsAsync<SnapshotValidationException>(() => store.ImportAsync(external));
            Assert.Empty(Directory.GetFiles(storeDir, "*" + SnapshotSchema.FileExtension));
        }
        finally
        {
            TryDeleteDir(storeDir);
            TryDeleteDir(externalDir);
        }
    }

    [Fact]
    public async Task Delete_NeverRemovesExternalExportFile()
    {
        var storeDir = CreateTempDir();
        var exportDir = CreateTempDir();
        try
        {
            var store = new SnapshotStore(NullLogger<SnapshotStore>.Instance, storeDir);
            var info = await store.SaveAsync(CreateScan(@"C:\Data", MakeFile("a.bin", 1)), "x");
            var exportPath = Path.Combine(exportDir, "copy" + SnapshotSchema.FileExtension);
            await store.ExportAsync(info.Id, exportPath);

            Assert.True(await store.DeleteAsync(info.Id));
            Assert.True(File.Exists(exportPath));
            Assert.False(await store.DeleteAsync(info.Id));
        }
        finally
        {
            TryDeleteDir(storeDir);
            TryDeleteDir(exportDir);
        }
    }

    [Fact]
    public async Task Compare_CaseOnlyRename_MatchesAsSamePath()
    {
        var left = ManualSnapshot(@"C:\Data",
            FileEntry("Report.TXT", 100));
        var right = ManualSnapshot(@"C:\Data",
            FileEntry("report.txt", 150));

        var result = await new SnapshotComparisonService(NullLogger<SnapshotComparisonService>.Instance)
            .CompareAsync(left, right);

        Assert.Empty(result.AddedFiles);
        Assert.Empty(result.RemovedFiles);
        var changed = Assert.Single(result.ChangedFiles);
        Assert.Equal(50, changed.DeltaBytes);
        Assert.Equal("report.txt", changed.RelativePath, ignoreCase: true);
    }

    [Fact]
    public async Task Compare_TypeChange_FileToFolder_TreatedAsRemoveAndAdd()
    {
        var left = ManualSnapshot(@"C:\Data",
            FileEntry("item", 40));
        var right = ManualSnapshot(@"C:\Data",
            DirEntry("item", 0),
            FileEntry(@"item\inner.bin", 40));

        var result = await new SnapshotComparisonService(NullLogger<SnapshotComparisonService>.Instance)
            .CompareAsync(left, right);

        Assert.Contains(result.RemovedFiles, f => f.RelativePath.Equals("item", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.AddedFiles, f => f.RelativePath.Equals(@"item\inner.bin", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.ChangedFiles, f => f.RelativePath.Equals("item", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Compare_DuplicateRelativePaths_KeepsLaterEntry()
    {
        var left = ManualSnapshot(@"C:\Data",
            FileEntry("dup.bin", 10),
            FileEntry("dup.bin", 99)); // later wins
        var right = ManualSnapshot(@"C:\Data",
            FileEntry("dup.bin", 50));

        var result = await new SnapshotComparisonService(NullLogger<SnapshotComparisonService>.Instance)
            .CompareAsync(left, right);

        var changed = Assert.Single(result.ChangedFiles);
        Assert.Equal(SnapshotMath.SafeDelta(50, 99), changed.DeltaBytes);
    }

    [Fact]
    public async Task Compare_IncompleteSnapshots_MarkedNotDefinitive()
    {
        var leftScan = CreateScan(@"C:\Data", MakeFile("a.bin", 1));
        leftScan = WithStatus(leftScan, ScanPhase.Cancelled);
        var rightScan = CreateScan(@"C:\Other", MakeFile("a.bin", 2));

        var left = DiskSnapshotBuilder.Build(leftScan, "L");
        var right = DiskSnapshotBuilder.Build(rightScan, "R");
        var result = await new SnapshotComparisonService(NullLogger<SnapshotComparisonService>.Instance)
            .CompareAsync(left, right);

        Assert.True(result.Summary.LeftIncomplete);
        Assert.True(result.Summary.RootsDiffer);
        Assert.False(result.Summary.IsDefinitive);
    }

    [Fact]
    public void SnapshotMath_SafeDelta_ClampsOverflow()
    {
        Assert.Equal(long.MaxValue, SnapshotMath.SafeDelta(long.MaxValue, long.MinValue));
        Assert.Equal(long.MinValue, SnapshotMath.SafeDelta(long.MinValue, long.MaxValue));
        Assert.Equal(5, SnapshotMath.SafeDelta(10, 5));
        Assert.Equal(long.MaxValue, SnapshotMath.AbsClamped(long.MinValue));
    }

    [Fact]
    public async Task Compare_OverflowingTotals_DoesNotThrow()
    {
        var left = ManualSnapshot(@"C:\Data", totalBytes: long.MinValue + 10, FileEntry("a.bin", 1));
        var right = ManualSnapshot(@"C:\Data", totalBytes: long.MaxValue - 10, FileEntry("a.bin", 2));

        var result = await new SnapshotComparisonService(NullLogger<SnapshotComparisonService>.Instance)
            .CompareAsync(left, right);

        Assert.Equal(long.MaxValue, result.Summary.TotalBytesDelta);
    }

    [Fact]
    public async Task Compare_AlreadyCancelled_Throws()
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
    public async Task LargeCompare_CompletesWithinBudget()
    {
        var leftFiles = Enumerable.Range(0, 8_000).Select(i => MakeFile($"n{i}.dat", 100 + i)).ToArray();
        var rightFiles = Enumerable.Range(0, 8_000).Select(i => MakeFile($"n{i}.dat", 101 + i)).ToArray();
        var left = DiskSnapshotBuilder.Build(CreateScan(@"C:\Perf", leftFiles));
        var right = DiskSnapshotBuilder.Build(CreateScan(@"C:\Perf", rightFiles));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await new SnapshotComparisonService(NullLogger<SnapshotComparisonService>.Instance)
            .CompareAsync(left, right);
        sw.Stop();

        Assert.Equal(8_000, result.Summary.ChangedFileCount);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"Took {sw.Elapsed}");
    }

    [Fact]
    public void PathNormalizer_RejectsTraversal_AndNormalizesSeparators()
    {
        Assert.False(SnapshotPathNormalizer.TryNormalizeRelative(@"folder\..\secret", out _));
        Assert.Equal(@"a\b\c.txt", SnapshotPathNormalizer.NormalizeRelative(@"a/b\\c.txt"));
        Assert.True(SnapshotPathNormalizer.RootsEquivalent(@"c:\data\", @"C:\Data"));
    }

    private static DiskSnapshot CloneWithId(DiskSnapshot source, Guid id) =>
        new()
        {
            SchemaVersion = source.SchemaVersion,
            Id = id,
            CreatedAtUtc = source.CreatedAtUtc,
            DisplayName = source.DisplayName,
            RootPath = source.RootPath,
            RootName = source.RootName,
            NormalizedRootPath = source.NormalizedRootPath,
            TotalBytes = source.TotalBytes,
            TotalFiles = source.TotalFiles,
            TotalDirectories = source.TotalDirectories,
            DurationSeconds = source.DurationSeconds,
            FinalStatus = source.FinalStatus,
            CompletenessCode = source.CompletenessCode,
            IsIncomplete = source.IsIncomplete,
            Categories = source.Categories,
            Extensions = source.Extensions,
            Entries = source.Entries
        };

    private static DiskSnapshot ManualSnapshot(string root, params SnapshotEntry[] entries) =>
        ManualSnapshot(root, totalBytes: entries.Where(e => !e.IsDirectory).Sum(e => e.SizeBytes), entries);

    private static DiskSnapshot ManualSnapshot(string root, long totalBytes, params SnapshotEntry[] entries) =>
        new()
        {
            SchemaVersion = SnapshotSchema.CurrentVersion,
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            DisplayName = "manual",
            RootPath = root,
            RootName = Path.GetFileName(root.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : root,
            NormalizedRootPath = SnapshotPathNormalizer.NormalizeAbsolute(root),
            TotalBytes = totalBytes,
            TotalFiles = entries.Count(e => !e.IsDirectory),
            TotalDirectories = entries.Count(e => e.IsDirectory),
            CompletenessCode = "Complete",
            IsIncomplete = false,
            Entries = entries
        };

    private static SnapshotEntry FileEntry(string relativePath, long size) =>
        new()
        {
            RelativePath = relativePath.Replace('/', '\\'),
            Name = Path.GetFileName(relativePath.Replace('/', '\\')),
            IsDirectory = false,
            SizeBytes = size,
            FileCount = 1,
            Extension = Path.GetExtension(relativePath)
        };

    private static SnapshotEntry DirEntry(string relativePath, long size) =>
        new()
        {
            RelativePath = relativePath.Replace('/', '\\'),
            Name = Path.GetFileName(relativePath.Replace('/', '\\')),
            IsDirectory = true,
            SizeBytes = size,
            DirectoryCount = 1
        };

    private static ScanResult WithStatus(ScanResult scan, ScanPhase status) =>
        new()
        {
            RootNode = scan.RootNode,
            TotalFiles = scan.TotalFiles,
            TotalDirectories = scan.TotalDirectories,
            TotalBytes = scan.TotalBytes,
            Duration = scan.Duration,
            FinalStatus = status,
            CategoryStatistics = scan.CategoryStatistics,
            ExtensionStatistics = scan.ExtensionStatistics,
            LargestFiles = scan.LargestFiles,
            Errors = scan.Errors
        };

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
        var dir = Path.Combine(Path.GetTempPath(), "ep-stab-" + Guid.NewGuid().ToString("N"));
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

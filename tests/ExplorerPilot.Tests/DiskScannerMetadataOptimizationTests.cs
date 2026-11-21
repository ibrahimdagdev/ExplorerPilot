using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Infrastructure.Scanning;

namespace ExplorerPilot.Tests;

public sealed class DiskScannerMetadataOptimizationTests
{
    [Fact]
    public async Task Optimized_MatchesLegacy_OnNestedTree()
    {
        using var temp = TemporaryDirectory.Create();
        var nested = Path.Combine(temp.Path, "a", "b");
        Directory.CreateDirectory(nested);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "root.bin"), new byte[11]);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "a", "mid.bin"), new byte[22]);
        await File.WriteAllBytesAsync(Path.Combine(nested, "leaf.bin"), new byte[33]);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "readme.txt"), "hello");

        var legacy = new DiskScanner(enumerationMode: DiskScannerEnumerationMode.LegacyPathAndStat);
        var optimized = new DiskScanner(enumerationMode: DiskScannerEnumerationMode.FileSystemInfoMetadata);

        var left = await legacy.ScanAsync(temp.Path);
        var right = await optimized.ScanAsync(temp.Path);

        AssertParity(left, right);
    }

    [Fact]
    public async Task Optimized_MatchesLegacy_OnEmptyDirectory()
    {
        using var temp = TemporaryDirectory.Create();
        var legacy = new DiskScanner(enumerationMode: DiskScannerEnumerationMode.LegacyPathAndStat);
        var optimized = new DiskScanner(enumerationMode: DiskScannerEnumerationMode.FileSystemInfoMetadata);

        AssertParity(await legacy.ScanAsync(temp.Path), await optimized.ScanAsync(temp.Path));
    }

    [Fact]
    public async Task Optimized_SkipsDirectoryReparsePoint_LikeLegacy()
    {
        using var temp = TemporaryDirectory.Create();
        var real = Path.Combine(temp.Path, "real");
        Directory.CreateDirectory(real);
        await File.WriteAllTextAsync(Path.Combine(real, "a.txt"), "x");
        var link = Path.Combine(temp.Path, "link");
        try
        {
            Directory.CreateSymbolicLink(link, real);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        var legacy = await new DiskScanner(enumerationMode: DiskScannerEnumerationMode.LegacyPathAndStat)
            .ScanAsync(temp.Path);
        var optimized = await new DiskScanner(enumerationMode: DiskScannerEnumerationMode.FileSystemInfoMetadata)
            .ScanAsync(temp.Path);

        Assert.Equal(legacy.FinalStatus, optimized.FinalStatus);
        Assert.Equal(
            legacy.Errors.Count(e => e.ErrorType == ScanErrorType.ReparsePointSkipped),
            optimized.Errors.Count(e => e.ErrorType == ScanErrorType.ReparsePointSkipped));
        Assert.Equal(
            legacy.RootNode.Children.Count(c => c.ScanStatus == NodeScanStatus.SkippedReparsePoint),
            optimized.RootNode.Children.Count(c => c.ScanStatus == NodeScanStatus.SkippedReparsePoint));
        AssertParity(legacy, optimized, compareErrorMessages: false);
    }

    [Fact]
    public async Task Optimized_Cancellation_BehavesLikeLegacy()
    {
        using var temp = TemporaryDirectory.Create();
        for (var i = 0; i < 300; i++)
        {
            var dir = Path.Combine(temp.Path, $"d{i:D3}");
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(Path.Combine(dir, "f.bin"), new byte[4]);
        }

        async Task<ScanResult> Run(DiskScannerEnumerationMode mode)
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMilliseconds(1));
            try
            {
                return await new DiskScanner(enumerationMode: mode).ScanAsync(temp.Path, cancellationToken: cts.Token);
            }
            catch (OperationCanceledException)
            {
                return await new DiskScanner(enumerationMode: mode).ScanAsync(temp.Path);
            }
        }

        var legacy = await Run(DiskScannerEnumerationMode.LegacyPathAndStat);
        var optimized = await Run(DiskScannerEnumerationMode.FileSystemInfoMetadata);
        Assert.True(legacy.FinalStatus is ScanPhase.Cancelled or ScanPhase.Completed);
        Assert.True(optimized.FinalStatus is ScanPhase.Cancelled or ScanPhase.Completed);
    }

    [Fact]
    public async Task Optimized_Progress_ReportsPositiveFileCounts()
    {
        using var temp = TemporaryDirectory.Create();
        for (var i = 0; i < 40; i++)
        {
            await File.WriteAllBytesAsync(Path.Combine(temp.Path, $"f{i}.bin"), new byte[2]);
        }

        var reports = new List<ScanProgress>();
        var gate = new object();
        var result = await new DiskScanner(enumerationMode: DiskScannerEnumerationMode.FileSystemInfoMetadata)
            .ScanAsync(temp.Path, new Progress<ScanProgress>(p =>
            {
                lock (gate)
                {
                    reports.Add(p);
                }
            }));

        // Progress<T> may post a final callback after ScanAsync returns; allow it to flush.
        await Task.Delay(50);

        List<ScanProgress> snapshot;
        lock (gate)
        {
            snapshot = reports.ToList();
        }

        Assert.Equal(ScanPhase.Completed, result.FinalStatus);
        Assert.NotEmpty(snapshot);
        Assert.Contains(snapshot, r => r.ScannedFileCount > 0);
        Assert.Equal(ScanPhase.Completed, snapshot[^1].CurrentStatus);
    }

    private static void AssertParity(ScanResult left, ScanResult right, bool compareErrorMessages = true)
    {
        Assert.Equal(left.FinalStatus, right.FinalStatus);
        Assert.Equal(left.TotalFiles, right.TotalFiles);
        Assert.Equal(left.TotalDirectories, right.TotalDirectories);
        Assert.Equal(left.TotalBytes, right.TotalBytes);
        Assert.Equal(left.Errors.Count, right.Errors.Count);

        var leftMap = Flatten(left.RootNode);
        var rightMap = Flatten(right.RootNode);
        Assert.Equal(leftMap.Count, rightMap.Count);

        foreach (var (path, leftNode) in leftMap)
        {
            Assert.True(rightMap.TryGetValue(path, out var rightNode), $"Missing path in optimized: {path}");
            Assert.Equal(leftNode.IsDirectory, rightNode!.IsDirectory);
            Assert.Equal(leftNode.SizeBytes, rightNode.SizeBytes);
            Assert.Equal(leftNode.FileCount, rightNode.FileCount);
            Assert.Equal(leftNode.ScanStatus, rightNode.ScanStatus);
            Assert.Equal(leftNode.LastModified, rightNode.LastModified);
        }

        if (compareErrorMessages)
        {
            var leftErrors = left.Errors.Select(e => (Normalize(e.Path), e.ErrorType)).OrderBy(x => x.Item1).ToList();
            var rightErrors = right.Errors.Select(e => (Normalize(e.Path), e.ErrorType)).OrderBy(x => x.Item1).ToList();
            Assert.Equal(leftErrors, rightErrors);
        }
    }

    private static Dictionary<string, FileSystemNode> Flatten(FileSystemNode root)
    {
        var map = new Dictionary<string, FileSystemNode>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<FileSystemNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            map[Normalize(n.FullPath)] = n;
            foreach (var child in n.Children)
            {
                stack.Push(child);
            }
        }

        return map;
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

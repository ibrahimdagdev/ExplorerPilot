using System.Diagnostics;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Infrastructure.Scanning;
using Microsoft.Extensions.DependencyInjection;

namespace ExplorerPilot.Tests;

/// <summary>
/// Filesystem edge-case parity and reliability for Legacy vs Optimized scanners.
/// Uses only controlled temp directories — never mutates user files.
/// </summary>
public sealed class DiskScannerCorrectnessTests
{
    [Fact]
    public void Optimized_DefaultMode_IsFileSystemInfoMetadata()
    {
        var scanner = new DiskScanner();
        Assert.Equal(DiskScannerEnumerationMode.FileSystemInfoMetadata, scanner.EnumerationMode);
    }

    [Fact]
    public void DiRegistration_ResolvesOptimizedDefault()
    {
        var services = new ServiceCollection();
        ExplorerPilot.Infrastructure.DependencyInjection.AddExplorerPilotInfrastructure(services);
        using var provider = services.BuildServiceProvider();
        var scanner = provider.GetRequiredService<IDiskScanner>();
        Assert.IsType<DiskScanner>(scanner);
        Assert.Equal(DiskScannerEnumerationMode.FileSystemInfoMetadata, ((DiskScanner)scanner).EnumerationMode);
    }

    [Fact]
    public async Task Optimized_MatchesLegacy_HiddenSystemReadOnly()
    {
        using var temp = TemporaryDirectory.Create();
        var hidden = Path.Combine(temp.Path, "hidden.dat");
        var system = Path.Combine(temp.Path, "system.dat");
        var readOnly = Path.Combine(temp.Path, "readonly.dat");
        await File.WriteAllBytesAsync(hidden, new byte[7]);
        await File.WriteAllBytesAsync(system, new byte[9]);
        await File.WriteAllBytesAsync(readOnly, new byte[11]);
        File.SetAttributes(hidden, FileAttributes.Hidden);
        File.SetAttributes(system, FileAttributes.System | FileAttributes.Hidden);
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);

        try
        {
            await AssertParityAsync(temp.Path);
        }
        finally
        {
            File.SetAttributes(hidden, FileAttributes.Normal);
            File.SetAttributes(system, FileAttributes.Normal);
            File.SetAttributes(readOnly, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Optimized_MatchesLegacy_UnicodeNames()
    {
        using var temp = TemporaryDirectory.Create();
        var unicodeDir = Path.Combine(temp.Path, "Документы-中文-📁");
        Directory.CreateDirectory(unicodeDir);
        await File.WriteAllTextAsync(Path.Combine(unicodeDir, "файл-日本語.txt"), "üñîçødé");
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "Öğrenci.dat"), new byte[5]);

        await AssertParityAsync(temp.Path);
    }

    [Fact]
    public async Task Optimized_MatchesLegacy_LongPath()
    {
        using var temp = TemporaryDirectory.Create();
        var deep = temp.Path;
        // Build a path that exceeds legacy MAX_PATH when combined with file name.
        while (deep.Length < 230)
        {
            deep = Path.Combine(deep, "long-segment-name-aaaa");
            Directory.CreateDirectory(deep);
        }

        var file = Path.Combine(deep, "payload.bin");
        await File.WriteAllBytesAsync(file, new byte[17]);

        await AssertParityAsync(temp.Path);
    }

    [Fact]
    public async Task Optimized_MatchesLegacy_FileSymlink_NotTraversedAsDirectory()
    {
        using var temp = TemporaryDirectory.Create();
        var target = Path.Combine(temp.Path, "target.bin");
        await File.WriteAllBytesAsync(target, new byte[64]);
        var link = Path.Combine(temp.Path, "link.bin");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        await AssertParityAsync(temp.Path);
    }

    [Fact]
    public async Task Optimized_SurvivesDeletedFileDuringScan_LikeLegacy()
    {
        using var temp = TemporaryDirectory.Create();
        for (var i = 0; i < 80; i++)
        {
            var dir = Path.Combine(temp.Path, $"d{i:D2}");
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(Path.Combine(dir, "keep.bin"), new byte[3]);
            await File.WriteAllBytesAsync(Path.Combine(dir, "volatile.bin"), new byte[3]);
        }

        async Task<ScanResult> Run(DiskScannerEnumerationMode mode)
        {
            var scanner = new DiskScanner(enumerationMode: mode);
            var progress = new Progress<ScanProgress>(p =>
            {
                if (string.IsNullOrEmpty(p.CurrentPath))
                {
                    return;
                }

                try
                {
                    var sibling = Path.Combine(Path.GetDirectoryName(p.CurrentPath)!, "volatile.bin");
                    if (File.Exists(sibling))
                    {
                        File.Delete(sibling);
                    }
                }
                catch
                {
                    // Race with scanner — acceptable.
                }
            });

            return await scanner.ScanAsync(temp.Path, progress);
        }

        var legacy = await Run(DiskScannerEnumerationMode.LegacyPathAndStat);
        var optimized = await Run(DiskScannerEnumerationMode.FileSystemInfoMetadata);

        Assert.Equal(ScanPhase.Completed, legacy.FinalStatus);
        Assert.Equal(ScanPhase.Completed, optimized.FinalStatus);
        Assert.True(legacy.TotalFiles >= 80);
        Assert.True(optimized.TotalFiles >= 80);
    }

    [Fact]
    public async Task Optimized_RapidRestart_ProducesConsistentTotals()
    {
        using var temp = TemporaryDirectory.Create();
        for (var i = 0; i < 40; i++)
        {
            await File.WriteAllBytesAsync(Path.Combine(temp.Path, $"f{i}.bin"), new byte[i + 1]);
        }

        var scanner = new DiskScanner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try
        {
            await scanner.ScanAsync(temp.Path, cancellationToken: cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected for already-cancelled token on some hosts.
        }

        var first = await scanner.ScanAsync(temp.Path);
        var second = await scanner.ScanAsync(temp.Path);

        Assert.Equal(ScanPhase.Completed, first.FinalStatus);
        Assert.Equal(first.TotalFiles, second.TotalFiles);
        Assert.Equal(first.TotalBytes, second.TotalBytes);
        Assert.Equal(first.TotalDirectories, second.TotalDirectories);
    }

    [Fact]
    public async Task Optimized_Duration_ReflectsScanCoreOnly()
    {
        using var temp = TemporaryDirectory.Create();
        for (var i = 0; i < 25; i++)
        {
            await File.WriteAllBytesAsync(Path.Combine(temp.Path, $"f{i}.bin"), new byte[4]);
        }

        var sw = Stopwatch.StartNew();
        var result = await new DiskScanner().ScanAsync(temp.Path);
        sw.Stop();

        Assert.Equal(ScanPhase.Completed, result.FinalStatus);
        Assert.True(result.Duration > TimeSpan.Zero);
        Assert.True(result.Duration <= sw.Elapsed + TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task Optimized_MetadataIsEnumerationSnapshot_NotLiveRefresh()
    {
        // Documented behavior: FileSystemInfo length/times come from Find* at enum time (no Refresh).
        // After the scan returns, mutating files must not alter the completed ScanResult tree.
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "snap.bin");
        await File.WriteAllBytesAsync(path, new byte[20]);

        var result = await new DiskScanner().ScanAsync(temp.Path);
        var before = Assert.Single(result.RootNode.Children, c => c.Name == "snap.bin").SizeBytes;

        await File.WriteAllBytesAsync(path, new byte[200]);

        Assert.Equal(20, before);
        Assert.Equal(20, result.RootNode.Children.Single(c => c.Name == "snap.bin").SizeBytes);
        Assert.Equal(20, result.TotalBytes);
    }

    private static async Task AssertParityAsync(string path)
    {
        var legacy = await new DiskScanner(enumerationMode: DiskScannerEnumerationMode.LegacyPathAndStat)
            .ScanAsync(path);
        var optimized = await new DiskScanner(enumerationMode: DiskScannerEnumerationMode.FileSystemInfoMetadata)
            .ScanAsync(path);

        Assert.Equal(legacy.FinalStatus, optimized.FinalStatus);
        Assert.Equal(legacy.TotalFiles, optimized.TotalFiles);
        Assert.Equal(legacy.TotalDirectories, optimized.TotalDirectories);
        Assert.Equal(legacy.TotalBytes, optimized.TotalBytes);
        Assert.Equal(legacy.LargestFiles.Count, optimized.LargestFiles.Count);
        Assert.Equal(legacy.ExtensionStatistics.Count, optimized.ExtensionStatistics.Count);

        var left = Flatten(legacy.RootNode);
        var right = Flatten(optimized.RootNode);
        Assert.Equal(left.Count, right.Count);
        foreach (var (key, leftNode) in left)
        {
            Assert.True(right.TryGetValue(key, out var rightNode));
            Assert.Equal(leftNode.SizeBytes, rightNode!.SizeBytes);
            Assert.Equal(leftNode.ScanStatus, rightNode.ScanStatus);
            Assert.Equal(leftNode.FileCount, rightNode.FileCount);
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
            map[Path.GetFullPath(n.FullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)] = n;
            foreach (var child in n.Children)
            {
                stack.Push(child);
            }
        }

        return map;
    }
}

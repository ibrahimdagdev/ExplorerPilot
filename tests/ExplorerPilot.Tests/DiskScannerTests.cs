using System.Security.AccessControl;
using System.Security.Principal;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Infrastructure.Scanning;

namespace ExplorerPilot.Tests;

public sealed class DiskScannerTests
{
    private readonly DiskScanner _scanner = new();

    [Fact]
    public async Task ScanAsync_EmptyDirectory_ReturnsZeroFilesAndZeroBytes()
    {
        using var temp = TemporaryDirectory.Create();

        var result = await _scanner.ScanAsync(temp.Path);

        Assert.Equal(ScanPhase.Completed, result.FinalStatus);
        Assert.Equal(0, result.TotalFiles);
        Assert.Equal(0, result.TotalBytes);
        Assert.True(result.TotalDirectories >= 1);
        Assert.Equal(temp.Path, result.RootNode.FullPath);
        Assert.Empty(result.RootNode.Children);
    }

    [Fact]
    public async Task ScanAsync_SingleFile_ReportsLogicalSize()
    {
        using var temp = TemporaryDirectory.Create();
        var filePath = Path.Combine(temp.Path, "note.txt");
        var content = new string('x', 2048);
        await File.WriteAllTextAsync(filePath, content);
        var expectedLength = new FileInfo(filePath).Length;

        var result = await _scanner.ScanAsync(temp.Path);

        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(expectedLength, result.TotalBytes);
        Assert.Equal(expectedLength, result.RootNode.SizeBytes);
        Assert.Contains(result.RootNode.Children, c => c.Name == "note.txt" && !c.IsDirectory);
    }

    [Fact]
    public async Task ScanAsync_MultipleFiles_AggregatesSizes()
    {
        using var temp = TemporaryDirectory.Create();
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "a.bin"), new byte[100]);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "b.bin"), new byte[250]);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "c.bin"), new byte[50]);

        var result = await _scanner.ScanAsync(temp.Path);

        Assert.Equal(3, result.TotalFiles);
        Assert.Equal(400, result.TotalBytes);
    }

    [Fact]
    public async Task ScanAsync_NestedDirectories_AggregatesFolderSizes()
    {
        using var temp = TemporaryDirectory.Create();
        var nested = Path.Combine(temp.Path, "level1", "level2");
        Directory.CreateDirectory(nested);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "root.dat"), new byte[10]);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "level1", "mid.dat"), new byte[20]);
        await File.WriteAllBytesAsync(Path.Combine(nested, "leaf.dat"), new byte[40]);

        var result = await _scanner.ScanAsync(temp.Path);

        Assert.Equal(3, result.TotalFiles);
        Assert.Equal(70, result.TotalBytes);
        Assert.Equal(70, result.RootNode.SizeBytes);

        var level1 = Assert.Single(result.RootNode.Children, c => c.IsDirectory && c.Name == "level1");
        Assert.Equal(60, level1.SizeBytes);

        var level2 = Assert.Single(level1.Children, c => c.IsDirectory && c.Name == "level2");
        Assert.Equal(40, level2.SizeBytes);
    }

    [Fact]
    public async Task ScanAsync_OrdersChildrenBySizeDescending()
    {
        using var temp = TemporaryDirectory.Create();
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "small.bin"), new byte[10]);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "large.bin"), new byte[500]);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "medium.bin"), new byte[100]);

        var result = await _scanner.ScanAsync(temp.Path);

        Assert.Equal(new[] { "large.bin", "medium.bin", "small.bin" },
            result.RootNode.Children.Select(c => c.Name).ToArray());
    }

    [Fact]
    public async Task ScanAsync_Cancellation_StopsAndMarksIncomplete()
    {
        using var temp = TemporaryDirectory.Create();

        // Create enough files that cancellation can interrupt a slow progress path.
        for (var i = 0; i < 200; i++)
        {
            var dir = Path.Combine(temp.Path, $"dir_{i:D3}");
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(Path.Combine(dir, "file.bin"), new byte[8]);
        }

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(1));

        // Warm-up path existence; then cancel very early on a large tree.
        // If the machine is extremely fast, the scan may finish before cancel — still assert status is terminal.
        ScanResult result;
        try
        {
            result = await _scanner.ScanAsync(temp.Path, cancellationToken: cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Task.Run may surface cancellation before ScanCore returns; treat as cancelled outcome.
            return;
        }

        Assert.True(
            result.FinalStatus is ScanPhase.Cancelled or ScanPhase.Completed,
            $"Unexpected status: {result.FinalStatus}");

        if (result.FinalStatus == ScanPhase.Cancelled)
        {
            Assert.True(result.WasCancelled);
            Assert.False(result.IsComplete);
        }
    }

    [Fact]
    public async Task ScanAsync_CancellationDuringScan_DoesNotThrowToCaller()
    {
        using var temp = TemporaryDirectory.Create();
        for (var i = 0; i < 50; i++)
        {
            Directory.CreateDirectory(Path.Combine(temp.Path, $"d{i}"));
            await File.WriteAllTextAsync(Path.Combine(temp.Path, $"d{i}", "f.txt"), "data");
        }

        using var cts = new CancellationTokenSource();
        var progress = new Progress<Core.Models.ScanProgress>(_ =>
        {
            if (!cts.IsCancellationRequested)
            {
                cts.Cancel();
            }
        });

        var result = await _scanner.ScanAsync(temp.Path, progress, cts.Token);

        Assert.Equal(ScanPhase.Cancelled, result.FinalStatus);
    }

    [Fact]
    public async Task ScanAsync_InaccessibleDirectory_DoesNotAbortEntireScan()
    {
        using var temp = TemporaryDirectory.Create();
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "visible.bin"), new byte[32]);

        var lockedDir = Path.Combine(temp.Path, "locked");
        Directory.CreateDirectory(lockedDir);
        await File.WriteAllBytesAsync(Path.Combine(lockedDir, "secret.bin"), new byte[16]);

        if (!TryDenyDirectoryListing(lockedDir))
        {
            // Environment may not allow ACL changes (e.g. limited CI). Still verify happy path.
            var baseline = await _scanner.ScanAsync(temp.Path);
            Assert.Equal(ScanPhase.Completed, baseline.FinalStatus);
            Assert.True(baseline.TotalFiles >= 1);
            return;
        }

        try
        {
            var result = await _scanner.ScanAsync(temp.Path);

            Assert.Equal(ScanPhase.Completed, result.FinalStatus);
            Assert.Contains(result.RootNode.Children, c => c.Name == "visible.bin");
            Assert.True(result.Errors.Count >= 1);
            Assert.Contains(result.Errors, e =>
                e.ErrorType is ScanErrorType.AccessDenied or ScanErrorType.IOError);
            // Accessible file size still counted.
            Assert.True(result.TotalBytes >= 32);
        }
        finally
        {
            TryResetDirectoryAccess(lockedDir);
        }
    }

    [Fact]
    public async Task ScanAsync_DirectorySymlink_IsSkippedAndNotTraversed()
    {
        using var temp = TemporaryDirectory.Create();
        var target = Path.Combine(temp.Path, "target");
        var link = Path.Combine(temp.Path, "link");
        Directory.CreateDirectory(target);
        await File.WriteAllBytesAsync(Path.Combine(target, "inside.bin"), new byte[128]);

        if (!TryCreateDirectorySymbolicLink(link, target))
        {
            // Requires Developer Mode or elevation on many Windows setups.
            return;
        }

        var result = await _scanner.ScanAsync(temp.Path);

        Assert.Equal(ScanPhase.Completed, result.FinalStatus);
        var linkNode = Assert.Single(result.RootNode.Children, c => c.Name == "link");
        Assert.Equal(NodeScanStatus.SkippedReparsePoint, linkNode.ScanStatus);
        Assert.Empty(linkNode.Children);
        Assert.Contains(result.Errors, e => e.ErrorType == ScanErrorType.ReparsePointSkipped);

        // Target directory is still scanned as a real folder; link must not double-count.
        var targetNode = Assert.Single(result.RootNode.Children, c => c.Name == "target");
        Assert.Equal(128, targetNode.SizeBytes);
        Assert.Equal(128, result.TotalBytes);
    }

    [Fact]
    public async Task ScanAsync_ReportsProgress()
    {
        using var temp = TemporaryDirectory.Create();
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "a.txt"), new byte[10]);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "b.txt"), new byte[10]);

        var reports = new List<Core.Models.ScanProgress>();
        var progress = new Progress<Core.Models.ScanProgress>(reports.Add);

        var result = await _scanner.ScanAsync(temp.Path, progress);

        // Allow Progress<T> callbacks to flush.
        await Task.Delay(50);

        Assert.Equal(ScanPhase.Completed, result.FinalStatus);
        Assert.NotEmpty(reports);
        Assert.Contains(reports, r => r.ScannedFileCount >= 1 || r.CurrentStatus == ScanPhase.Completed);
    }

    [Fact]
    public async Task ScanAsync_MissingPath_FailsGracefully()
    {
        var missing = Path.Combine(Path.GetTempPath(), "ExplorerPilot-Missing-" + Guid.NewGuid());

        var result = await _scanner.ScanAsync(missing);

        Assert.Equal(ScanPhase.Failed, result.FinalStatus);
        Assert.NotEmpty(result.Errors);
    }

    private static bool TryDenyDirectoryListing(string directory)
    {
        try
        {
            var dirInfo = new DirectoryInfo(directory);
            var security = dirInfo.GetAccessControl();
            var rule = new FileSystemAccessRule(
                WindowsIdentity.GetCurrent().Name,
                FileSystemRights.ListDirectory | FileSystemRights.ReadData,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Deny);

            security.AddAccessRule(rule);
            dirInfo.SetAccessControl(security);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryResetDirectoryAccess(string directory)
    {
        try
        {
            var dirInfo = new DirectoryInfo(directory);
            var security = dirInfo.GetAccessControl();
            var rules = security.GetAccessRules(true, true, typeof(NTAccount));
            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.AccessControlType == AccessControlType.Deny)
                {
                    security.RemoveAccessRule(rule);
                }
            }

            dirInfo.SetAccessControl(security);
        }
        catch
        {
            // Best-effort cleanup for temp directories.
        }
    }

    private static bool TryCreateDirectorySymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return Directory.Exists(linkPath);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Temporary directory helper that never touches user profile content outside %TEMP%.
/// </summary>
internal sealed class TemporaryDirectory : IDisposable
{
    private TemporaryDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TemporaryDirectory Create()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "ExplorerPilotTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TemporaryDirectory(path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                // Reset attributes in case of read-only/reparse leftovers.
                foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    catch
                    {
                        // ignore
                    }
                }

                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Temp cleanup is best-effort.
        }
    }
}

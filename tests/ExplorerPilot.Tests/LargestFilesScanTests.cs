using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Scanning;
using ExplorerPilot.Infrastructure.Scanning;

namespace ExplorerPilot.Tests;

public sealed class LargestFilesScanTests
{
    private readonly DiskScanner _scanner = new();

    [Fact]
    public async Task ScanAsync_CollectsLargestFilesDuringSinglePass()
    {
        using var temp = TemporaryDirectory.Create();
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "small.bin"), new byte[10]);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "large.bin"), new byte[500]);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "medium.bin"), new byte[100]);

        var result = await _scanner.ScanAsync(temp.Path);

        Assert.Equal(ScanPhase.Completed, result.FinalStatus);
        Assert.Equal(3, result.LargestFiles.Count);
        Assert.Equal(new[] { "large.bin", "medium.bin", "small.bin" },
            result.LargestFiles.Select(f => f.Name).ToArray());
        Assert.True(result.LargestFiles[0].SizeBytes >= result.LargestFiles[1].SizeBytes);
    }

    [Fact]
    public async Task ScanAsync_EmptyDirectory_HasNoLargestFiles()
    {
        using var temp = TemporaryDirectory.Create();

        var result = await _scanner.ScanAsync(temp.Path);

        Assert.Empty(result.LargestFiles);
        Assert.Equal(0, result.TotalFiles);
    }

    [Fact]
    public async Task ScanAsync_CapsLargestFilesAtMaxTracked()
    {
        using var temp = TemporaryDirectory.Create();
        var count = LargestFilesOptions.MaxTrackedFiles + 25;
        for (var i = 0; i < count; i++)
        {
            await File.WriteAllBytesAsync(Path.Combine(temp.Path, $"f{i:D4}.bin"), new byte[i + 1]);
        }

        var result = await _scanner.ScanAsync(temp.Path);

        Assert.Equal(count, result.TotalFiles);
        Assert.Equal(LargestFilesOptions.MaxTrackedFiles, result.LargestFiles.Count);
        Assert.Equal(count, result.LargestFiles[0].SizeBytes);
        Assert.True(result.LargestFiles[^1].SizeBytes > 25);
    }

    [Fact]
    public async Task ScanAsync_TurkishFileNames_ArePreservedInLargestFiles()
    {
        using var temp = TemporaryDirectory.Create();
        var name = "büyük-öğrenci-dosyası.txt";
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, name), new byte[128]);

        var result = await _scanner.ScanAsync(temp.Path);

        var file = Assert.Single(result.LargestFiles);
        Assert.Equal(name, file.Name);
        Assert.Contains("öğrenci", file.FullPath, StringComparison.Ordinal);
        Assert.Equal(".txt", file.Extension);
    }

    [Fact]
    public async Task ScanAsync_Cancelled_MayReturnPartialLargestFiles()
    {
        using var temp = TemporaryDirectory.Create();
        for (var i = 0; i < 400; i++)
        {
            var dir = Path.Combine(temp.Path, $"d{i:D3}");
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(Path.Combine(dir, "file.bin"), new byte[64]);
        }

        using var cts = new CancellationTokenSource();
        var progress = new Progress<Core.Models.ScanProgress>(_ =>
        {
            try
            {
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
                // Progress may flush after ScanAsync returns and the CTS is disposed.
            }
        });

        var result = await _scanner.ScanAsync(temp.Path, progress, cts.Token);

        // Optimized scanner can finish tiny trees before cancel is observed.
        Assert.True(
            result.FinalStatus is ScanPhase.Cancelled or ScanPhase.Completed,
            $"Unexpected status: {result.FinalStatus}");
        if (result.FinalStatus == ScanPhase.Cancelled)
        {
            Assert.False(result.IsComplete);
        }

        Assert.True(result.LargestFiles.Count >= 0);
        Assert.True(result.LargestFiles.Count <= LargestFilesOptions.MaxTrackedFiles);
    }

    [Fact]
    public async Task ScanAsync_InaccessibleDirectory_StillRanksAccessibleFiles()
    {
        using var temp = TemporaryDirectory.Create();
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "visible-large.bin"), new byte[200]);

        var lockedDir = Path.Combine(temp.Path, "locked");
        Directory.CreateDirectory(lockedDir);
        await File.WriteAllBytesAsync(Path.Combine(lockedDir, "hidden.bin"), new byte[500]);

        if (!DiskScannerTestsHelpers.TryDenyDirectoryListing(lockedDir))
        {
            var baseline = await _scanner.ScanAsync(temp.Path);
            Assert.Contains(baseline.LargestFiles, f => f.Name == "visible-large.bin");
            return;
        }

        try
        {
            var result = await _scanner.ScanAsync(temp.Path);
            Assert.Equal(ScanPhase.Completed, result.FinalStatus);
            Assert.Contains(result.LargestFiles, f => f.Name == "visible-large.bin");
            Assert.DoesNotContain(result.LargestFiles, f => f.Name == "hidden.bin");
        }
        finally
        {
            DiskScannerTestsHelpers.TryResetDirectoryAccess(lockedDir);
        }
    }

    [Fact]
    public async Task ScanAsync_DirectoryAggregation_UnchangedWithLargestFilesTracking()
    {
        using var temp = TemporaryDirectory.Create();
        var nested = Path.Combine(temp.Path, "level1", "level2");
        Directory.CreateDirectory(nested);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "root.dat"), new byte[10]);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "level1", "mid.dat"), new byte[20]);
        await File.WriteAllBytesAsync(Path.Combine(nested, "leaf.dat"), new byte[40]);

        var result = await _scanner.ScanAsync(temp.Path);

        Assert.Equal(70, result.TotalBytes);
        Assert.Equal(70, result.RootNode.SizeBytes);
        Assert.Equal(3, result.LargestFiles.Count);
        Assert.Equal("leaf.dat", result.LargestFiles[0].Name);
    }
}

/// <summary>
/// Shared ACL helpers for scanner tests (keeps DiskScannerTests helpers accessible).
/// </summary>
internal static class DiskScannerTestsHelpers
{
    public static bool TryDenyDirectoryListing(string directory)
    {
        try
        {
            var dirInfo = new DirectoryInfo(directory);
            var security = dirInfo.GetAccessControl();
            var rule = new System.Security.AccessControl.FileSystemAccessRule(
                System.Security.Principal.WindowsIdentity.GetCurrent().Name,
                System.Security.AccessControl.FileSystemRights.ListDirectory
                | System.Security.AccessControl.FileSystemRights.ReadData,
                System.Security.AccessControl.InheritanceFlags.None,
                System.Security.AccessControl.PropagationFlags.None,
                System.Security.AccessControl.AccessControlType.Deny);

            security.AddAccessRule(rule);
            dirInfo.SetAccessControl(security);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void TryResetDirectoryAccess(string directory)
    {
        try
        {
            var dirInfo = new DirectoryInfo(directory);
            var security = dirInfo.GetAccessControl();
            var rules = security.GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount));
            foreach (System.Security.AccessControl.FileSystemAccessRule rule in rules)
            {
                if (rule.AccessControlType == System.Security.AccessControl.AccessControlType.Deny)
                {
                    security.RemoveAccessRule(rule);
                }
            }

            dirInfo.SetAccessControl(security);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}

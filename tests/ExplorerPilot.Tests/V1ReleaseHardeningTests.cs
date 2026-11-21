using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Infrastructure.FileSystem;
using ExplorerPilot.Infrastructure.Scanning;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExplorerPilot.Tests;

public sealed class V1ReleaseHardeningTests
{
    [Fact]
    public void PathHelper_ExtendedLength_AndDisplay_RoundTrip()
    {
        var local = @"C:\Temp\ExplorerPilot\sample";
        var extended = PathHelper.EnsureExtendedLengthPath(local);
        Assert.StartsWith(@"\\?\", extended, StringComparison.Ordinal);
        Assert.Equal(Path.GetFullPath(local), PathHelper.GetDisplayPath(extended), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiskScanner_Cancel_AllowsRestartWithFreshTotals()
    {
        var root = CreateTempTree(fileCount: 80);
        try
        {
            var scanner = new DiskScanner(NullLogger<DiskScanner>.Instance);
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMilliseconds(1));

            ScanResult cancelled;
            try
            {
                cancelled = await scanner.ScanAsync(root, cancellationToken: cts.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = await scanner.ScanAsync(root, cancellationToken: CancellationToken.None);
                Assert.Equal(ScanPhase.Completed, cancelled.FinalStatus);
                return;
            }

            Assert.True(
                cancelled.FinalStatus is ScanPhase.Cancelled or ScanPhase.Completed,
                $"Unexpected status {cancelled.FinalStatus}");

            var restarted = await scanner.ScanAsync(root);
            Assert.Equal(ScanPhase.Completed, restarted.FinalStatus);
            Assert.True(restarted.TotalFiles >= 80);
            Assert.True(restarted.TotalBytes > 0);
            Assert.Equal(restarted.RootNode.FileCount, restarted.TotalFiles);
            Assert.Equal(restarted.RootNode.SizeBytes, restarted.TotalBytes);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task DiskScanner_SkipsDirectoryJunction_WithoutLooping()
    {
        var root = CreateTempDir();
        try
        {
            var real = Path.Combine(root, "real");
            Directory.CreateDirectory(real);
            await File.WriteAllTextAsync(Path.Combine(real, "a.txt"), "hello");

            var link = Path.Combine(root, "link");
            try
            {
                Directory.CreateSymbolicLink(link, real);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                // Creating symlinks may require privilege; skip assertion in that environment.
                return;
            }

            var scanner = new DiskScanner(NullLogger<DiskScanner>.Instance);
            var result = await scanner.ScanAsync(root);
            Assert.Equal(ScanPhase.Completed, result.FinalStatus);
            Assert.Contains(result.Errors, e => e.ErrorType == ScanErrorType.ReparsePointSkipped);
            Assert.Contains(
                result.RootNode.Children,
                c => c.ScanStatus == NodeScanStatus.SkippedReparsePoint);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateTempTree(int fileCount)
    {
        var root = CreateTempDir();
        for (var i = 0; i < fileCount; i++)
        {
            var dir = Path.Combine(root, "d" + (i % 10));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"f{i}.txt"), new string('x', 32 + (i % 17)));
        }

        return root;
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ep-v1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
        catch
        {
            // ignore
        }
    }
}

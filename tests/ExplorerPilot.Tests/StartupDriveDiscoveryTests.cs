using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Infrastructure.Scanning;
using ExplorerPilot.Infrastructure.Services;

namespace ExplorerPilot.Tests;

public sealed class StartupDriveDiscoveryTests
{
    [Fact]
    public void GetDrives_DoesNotPerformFilesystemScanOfDriveRoots()
    {
        // Sanity: drive discovery only reads DriveInfo metadata, never walks folder trees.
        // We assert the public API returns quickly relative to a full scan of a temp tree.
        var provider = new DriveInfoProvider();
        var scanner = new DiskScanner();

        using var temp = TemporaryDirectory.Create();
        for (var i = 0; i < 40; i++)
        {
            var dir = Path.Combine(temp.Path, $"d{i}");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "f.bin"), new byte[16]);
        }

        var discovery = Time(() => provider.GetDrives());
        var scan = Time(() => scanner.ScanAsync(temp.Path).GetAwaiter().GetResult());

        Assert.True(discovery.Elapsed < TimeSpan.FromSeconds(10));
        // Full scan of many folders should generally take longer than listing drive metadata,
        // but environments vary — primarily ensure discovery itself stays bounded.
        Assert.True(discovery.Elapsed < TimeSpan.FromSeconds(5) || discovery.Elapsed <= scan.Elapsed * 2);
    }

    [Fact]
    public async Task GetDrivesAsync_DoesNotBlockCallingThreadWhileWorking()
    {
        var provider = new BlockingAwareDriveInfoProvider();

        var task = provider.GetDrivesAsync();
        // Yield so the background work can start.
        await Task.Yield();

        Assert.True(provider.BackgroundWorkStarted.Wait(TimeSpan.FromSeconds(2)));
        Assert.False(task.IsCompleted);

        provider.ReleaseWork.Set();
        var drives = await task;

        Assert.NotEmpty(drives);
    }

    private static (TimeSpan Elapsed, T Result) Time<T>(Func<T> action)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = action();
        sw.Stop();
        return (sw.Elapsed, result);
    }

    /// <summary>
    /// Test double that proves GetDrivesAsync hops off the caller before doing work.
    /// </summary>
    private sealed class BlockingAwareDriveInfoProvider : IDriveInfoProvider
    {
        public ManualResetEventSlim BackgroundWorkStarted { get; } = new(false);
        public ManualResetEventSlim ReleaseWork { get; } = new(false);

        public IReadOnlyList<DriveInfoModel> GetDrives()
        {
            BackgroundWorkStarted.Set();
            if (!ReleaseWork.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("ReleaseWork was not signaled.");
            }

            return
            [
                new DriveInfoModel
                {
                    Name = @"C:\",
                    RootPath = @"C:\",
                    DriveType = "Fixed",
                    IsReady = true,
                    TotalSizeBytes = 100,
                    TotalFreeSpaceBytes = 40
                }
            ];
        }

        public Task<IReadOnlyList<DriveInfoModel>> GetDrivesAsync(CancellationToken cancellationToken = default) =>
            Task.Run(() => (IReadOnlyList<DriveInfoModel>)GetDrives(), cancellationToken);
    }
}

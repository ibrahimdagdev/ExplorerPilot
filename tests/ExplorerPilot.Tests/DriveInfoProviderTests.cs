using ExplorerPilot.Infrastructure.Services;

namespace ExplorerPilot.Tests;

public sealed class DriveInfoProviderTests
{
    [Fact]
    public void GetDrives_ReturnsAtLeastOneDriveOnWindows()
    {
        var provider = new DriveInfoProvider();
        var drives = provider.GetDrives();

        Assert.NotEmpty(drives);
        Assert.All(drives, d => Assert.False(string.IsNullOrWhiteSpace(d.Name)));
        Assert.All(drives, d => Assert.False(string.IsNullOrWhiteSpace(d.RootPath)));
    }

    [Fact]
    public void GetDrives_ReadyDrivesHaveNonNegativeCapacity()
    {
        var provider = new DriveInfoProvider();
        var ready = provider.GetDrives().Where(d => d.IsReady).ToList();

        Assert.NotEmpty(ready);
        Assert.All(ready, d =>
        {
            Assert.True(d.TotalSizeBytes >= 0);
            Assert.True(d.TotalFreeSpaceBytes >= 0);
            Assert.True(d.UsedSpaceBytes >= 0);
            Assert.False(string.IsNullOrWhiteSpace(d.DisplayName));
        });
    }

    [Fact]
    public async Task GetDrivesAsync_CompletesAndMatchesSyncShape()
    {
        var provider = new DriveInfoProvider();
        var drives = await provider.GetDrivesAsync();

        Assert.NotEmpty(drives);
        Assert.All(drives, d => Assert.False(string.IsNullOrWhiteSpace(d.Name)));
    }

    [Fact]
    public async Task GetDrivesAsync_RespectsCancellation()
    {
        var provider = new DriveInfoProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetDrivesAsync(cts.Token));
    }
}

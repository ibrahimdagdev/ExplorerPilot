using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Infrastructure;
using ExplorerPilot.Infrastructure.Scanning;
using ExplorerPilot.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ExplorerPilot.Tests;

public sealed class DriveDiscoveryServiceTests
{
    [Fact]
    public void DiRegistration_ResolvesDriveDiscoveryAndScannerDefaults()
    {
        var services = new ServiceCollection();
        DependencyInjection.AddExplorerPilotInfrastructure(services);
        using var provider = services.BuildServiceProvider();

        var discovery = provider.GetRequiredService<IDriveDiscoveryService>();
        var drives = provider.GetRequiredService<IDriveInfoProvider>();
        var scanner = provider.GetRequiredService<IDiskScanner>();

        Assert.IsType<DriveDiscoveryService>(discovery);
        Assert.IsType<DriveInfoProvider>(drives);
        Assert.IsType<DiskScanner>(scanner);
        Assert.Equal(DiskScannerEnumerationMode.FileSystemInfoMetadata, ((DiskScanner)scanner).EnumerationMode);
    }

    [Fact]
    public async Task DiscoverAsync_ReturnsDrivesWithoutThrowing()
    {
        var service = new DriveDiscoveryService();
        var drives = await service.DiscoverAsync();

        Assert.NotNull(drives);
        Assert.All(drives, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Name));
            Assert.False(string.IsNullOrWhiteSpace(d.RootPath));
            Assert.True(Enum.IsDefined(d.Kind));
            Assert.True(Enum.IsDefined(d.Status));
            Assert.True(d.TotalSizeBytes >= 0);
            Assert.True(d.AvailableFreeSpaceBytes >= 0);
            Assert.True(d.UsedSpaceBytes >= 0);
            Assert.InRange(d.UsageRatio, 0d, 1d);
            Assert.InRange(d.UsagePercentage, 0d, 100d);
            Assert.Equal(d.Status == DriveReadyStatus.Ready, d.IsReady);
        });
    }

    [Fact]
    public void MapKind_ClassifiesKnownDriveTypes()
    {
        Assert.Equal(VolumeDriveKind.Fixed, DriveDiscoveryService.MapKind(DriveType.Fixed));
        Assert.Equal(VolumeDriveKind.Removable, DriveDiscoveryService.MapKind(DriveType.Removable));
        Assert.Equal(VolumeDriveKind.Network, DriveDiscoveryService.MapKind(DriveType.Network));
        Assert.Equal(VolumeDriveKind.Optical, DriveDiscoveryService.MapKind(DriveType.CDRom));
        Assert.Equal(VolumeDriveKind.Ram, DriveDiscoveryService.MapKind(DriveType.Ram));
        Assert.Equal(VolumeDriveKind.Unknown, DriveDiscoveryService.MapKind(DriveType.Unknown));
        Assert.Equal(VolumeDriveKind.Unknown, DriveDiscoveryService.MapKind(DriveType.NoRootDirectory));
    }

    [Fact]
    public void ReadyDrives_HaveCanScanTrue_NotReadyDoNot()
    {
        var ready = new DriveInformation
        {
            Name = @"Z:\",
            RootPath = @"Z:\",
            Status = DriveReadyStatus.Ready,
            Kind = VolumeDriveKind.Fixed,
            TotalSizeBytes = 100,
            AvailableFreeSpaceBytes = 40,
            UsedSpaceBytes = 60
        };
        var notReady = new DriveInformation
        {
            Name = @"Y:\",
            RootPath = @"Y:\",
            Status = DriveReadyStatus.NotReady,
            Kind = VolumeDriveKind.Removable
        };

        Assert.True(ready.CanScan);
        Assert.False(notReady.CanScan);
        Assert.False(notReady.IsReady);
    }

    [Fact]
    public async Task Discover_Cancellation_ThrowsOrReturnsPartial()
    {
        var service = new DriveDiscoveryService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await service.DiscoverAsync(cts.Token));
    }

    [Fact]
    public void DriveInfoProvider_MapsDiscoveryResults()
    {
        var provider = new DriveInfoProvider();
        var models = provider.GetDrives();
        Assert.NotNull(models);
        Assert.All(models, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Name));
            Assert.True(m.TotalSizeBytes >= 0);
            Assert.True(m.UsedSpaceBytes >= 0);
        });
    }
}

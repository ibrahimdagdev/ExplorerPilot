using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Infrastructure;
using ExplorerPilot.Infrastructure.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace ExplorerPilot.Tests;

public sealed class ScheduledScanDiSmokeTests
{
    [Fact]
    public async Task Di_RegistersScheduledScanStoreAndService()
    {
        var services = new ServiceCollection();
        DependencyInjection.AddExplorerPilotInfrastructure(services);
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<ScheduledScanStore>(provider.GetRequiredService<IScheduledScanStore>());
        Assert.IsType<ScheduledScanService>(provider.GetRequiredService<IScheduledScanService>());
    }
}

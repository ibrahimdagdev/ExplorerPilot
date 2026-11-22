using System.Reflection;
using ExplorerPilot.App.Services;

namespace ExplorerPilot.Tests;

/// <summary>Stable release identity checks for ExplorerPilot v2.0.0.</summary>
public sealed class StableReleaseVersionTests
{
    [Fact]
    public void AppAssembly_ReportsStableProductVersion()
    {
        var asm = typeof(ApplicationInfoService).Assembly;
        var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        Assert.Equal("2.0.0", informational);

        var fileVersion = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        Assert.Equal("2.0.0.0", fileVersion);

        var assemblyVersion = asm.GetName().Version;
        Assert.NotNull(assemblyVersion);
        Assert.Equal(new Version(2, 0, 0, 0), assemblyVersion);
    }

    [Fact]
    public void ApplicationInfoService_BrandedLabel_MatchesStableVersion()
    {
        var info = new ApplicationInfoService();
        Assert.Equal("2.0.0", info.Version);
        Assert.Equal("v2.0.0", info.VersionLabel);
        Assert.Equal("ExplorerPilot v2.0.0", info.BrandedVersionLabel);
        Assert.Equal("ExplorerPilot", info.ProductName);
    }
}

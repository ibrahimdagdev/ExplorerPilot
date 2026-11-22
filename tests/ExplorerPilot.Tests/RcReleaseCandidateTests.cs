using System.Reflection;
using ExplorerPilot.App.Services;

namespace ExplorerPilot.Tests;

/// <summary>Release Candidate identity checks for v2.0.0-rc.1.</summary>
public sealed class RcReleaseCandidateTests
{
    [Fact]
    public void AppAssembly_ReportsRcProductVersion()
    {
        var asm = typeof(ApplicationInfoService).Assembly;
        var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        Assert.Equal("2.0.0-rc.1", informational);

        var fileVersion = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        Assert.Equal("2.0.0.0", fileVersion);

        var assemblyVersion = asm.GetName().Version;
        Assert.NotNull(assemblyVersion);
        Assert.Equal(new Version(2, 0, 0, 0), assemblyVersion);
    }

    [Fact]
    public void ApplicationInfoService_BrandedLabel_IncludesRcVersion()
    {
        var info = new ApplicationInfoService();
        Assert.Equal("2.0.0-rc.1", info.Version);
        Assert.Equal("v2.0.0-rc.1", info.VersionLabel);
        Assert.Contains("2.0.0-rc.1", info.BrandedVersionLabel, StringComparison.Ordinal);
        Assert.Equal("ExplorerPilot", info.ProductName);
    }
}

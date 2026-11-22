namespace ExplorerPilot.Tests;

/// <summary>
/// RC1 identity assertions were replaced by <see cref="StableReleaseVersionTests"/> for v2.0.0.
/// This empty marker file is intentionally removed of RC-specific asserts to avoid conflicting expectations.
/// </summary>
public sealed class RcReleaseCandidateTests
{
    [Fact]
    public void StableSuite_OwnsCurrentVersionIdentity()
    {
        Assert.Equal("2.0.0", new ExplorerPilot.App.Services.ApplicationInfoService().Version);
    }
}

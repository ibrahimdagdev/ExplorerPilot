using ExplorerPilot.Core.Scheduling;

namespace ExplorerPilot.Tests;

public sealed class ScheduledScanPathValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative\\folder")]
    [InlineData("folder")]
    public void TryNormalize_RejectsInvalid(string? path)
    {
        Assert.False(ScheduledScanPathValidator.TryNormalize(path, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryNormalize_AcceptsAbsoluteFolderAndDriveRoot()
    {
        Assert.True(ScheduledScanPathValidator.TryNormalize(@"C:\", out var drive, out _));
        Assert.Equal(@"C:\", drive);

        using var temp = TemporaryDirectory.Create();
        Assert.True(ScheduledScanPathValidator.TryNormalize(temp.Path, out var folder, out _));
        Assert.Equal(Path.GetFullPath(temp.Path), folder);
    }
}

using ExplorerPilot.Core.Models;

namespace ExplorerPilot.Tests;

public sealed class DriveCapacityMathTests
{
    [Theory]
    [InlineData(1000, 400, 600)]
    [InlineData(1000, 0, 1000)]
    [InlineData(1000, 1000, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(0, 50, 0)]
    public void SafeUsedBytes_ComputesTotalMinusAvailable(long total, long available, long expected) =>
        Assert.Equal(expected, DriveCapacityMath.SafeUsedBytes(total, available));

    [Fact]
    public void SafeUsedBytes_ClampsAvailableAboveTotal() =>
        Assert.Equal(0, DriveCapacityMath.SafeUsedBytes(100, 250));

    [Fact]
    public void SafeUsedBytes_ClampsNegativeAvailable() =>
        Assert.Equal(100, DriveCapacityMath.SafeUsedBytes(100, -20));

    [Fact]
    public void SafeUsedBytes_NegativeTotal_ReturnsZero() =>
        Assert.Equal(0, DriveCapacityMath.SafeUsedBytes(-5, 1));

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(100, 0, 0)]
    [InlineData(100, 25, 0.25)]
    [InlineData(100, 100, 1.0)]
    [InlineData(100, 150, 1.0)]
    public void SafeUsageRatio_IsClamped(long total, long used, double expected) =>
        Assert.Equal(expected, DriveCapacityMath.SafeUsageRatio(total, used), 5);

    [Fact]
    public void SafeUsagePercentage_ScalesRatio() =>
        Assert.Equal(50d, DriveCapacityMath.SafeUsagePercentage(200, 100), 5);

    [Fact]
    public void SafeUsedBytes_LargeCapacities_DoNotOverflow()
    {
        const long total = long.MaxValue / 2;
        const long available = long.MaxValue / 4;
        var used = DriveCapacityMath.SafeUsedBytes(total, available);
        Assert.Equal(total - available, used);
        Assert.InRange(DriveCapacityMath.SafeUsageRatio(total, used), 0d, 1d);
    }
}

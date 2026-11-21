using ExplorerPilot.Core.Diagnostics;

namespace ExplorerPilot.Tests;

public sealed class PathRedactionTests
{
    [Theory]
    [InlineData(null, "(empty)")]
    [InlineData("", "(empty)")]
    [InlineData("   ", "(empty)")]
    public void ForLog_Empty_ReturnsPlaceholder(string? path, string expected) =>
        Assert.Equal(expected, PathRedaction.ForLog(path));

    [Fact]
    public void ForLog_DriveRoot_HidesRemainder()
    {
        var redacted = PathRedaction.ForLog(@"D:\");
        Assert.StartsWith("D:", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("***", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("Users", redacted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForLog_KeepsLeafName_Only()
    {
        var redacted = PathRedaction.ForLog(@"C:\Users\SecretPerson\Documents\report.csv");
        Assert.Contains("report.csv", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SecretPerson", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("***", redacted, StringComparison.Ordinal);
    }
}

using System.Runtime.InteropServices;
using ExplorerPilot.Core.Formatting;

namespace ExplorerPilot.Tests;

public sealed class ArchitectureFormatterTests
{
    [Theory]
    [InlineData(Architecture.X86, "x86 (32-bit)")]
    [InlineData(Architecture.X64, "x64")]
    [InlineData(Architecture.Arm, "ARM")]
    [InlineData(Architecture.Arm64, "ARM64")]
    public void Format_KnownArchitectures(Architecture architecture, string expected)
    {
        Assert.Equal(expected, ArchitectureFormatter.Format(architecture));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("64-bit", "x64")]
    [InlineData("x64-based PC", "x64")]
    [InlineData("AMD64", "x64")]
    [InlineData("32-bit", "x86 (32-bit)")]
    [InlineData("x86", "x86 (32-bit)")]
    [InlineData("ARM64-based PC", "ARM64")]
    [InlineData("AArch64", "ARM64")]
    [InlineData("ARM-based PC", "ARM")]
    public void NormalizeReportedArchitecture_MapsCommonValues(string? input, string expected)
    {
        Assert.Equal(expected, ArchitectureFormatter.NormalizeReportedArchitecture(input));
    }
}

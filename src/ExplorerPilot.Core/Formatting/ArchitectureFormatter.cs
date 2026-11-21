using System.Runtime.InteropServices;

namespace ExplorerPilot.Core.Formatting;

/// <summary>
/// Formats OS / process architecture values into stable display labels (x86, x64, ARM64, …).
/// </summary>
public static class ArchitectureFormatter
{
    public static string Format(Architecture architecture) =>
        architecture switch
        {
            Architecture.X86 => "x86 (32-bit)",
            Architecture.X64 => "x64",
            Architecture.Arm => "ARM",
            Architecture.Arm64 => "ARM64",
            Architecture.Wasm => "Wasm",
            Architecture.S390x => "s390x",
            Architecture.LoongArch64 => "LoongArch64",
            Architecture.Armv6 => "ARMv6",
            Architecture.Ppc64le => "ppc64le",
            Architecture.RiscV64 => "RISC-V 64",
            _ => architecture.ToString()
        };

    public static string FormatRuntimeOs() => Format(RuntimeInformation.OSArchitecture);

    public static string FormatRuntimeProcess() => Format(RuntimeInformation.ProcessArchitecture);

    /// <summary>
    /// Maps WMI <c>OSArchitecture</c> / similar strings to a normalized label.
    /// </summary>
    public static string NormalizeReportedArchitecture(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var lower = trimmed.ToLowerInvariant();

        if (lower.Contains("arm64", StringComparison.Ordinal)
            || lower.Contains("aarch64", StringComparison.Ordinal))
        {
            return "ARM64";
        }

        if (lower.Contains("arm", StringComparison.Ordinal))
        {
            return "ARM";
        }

        if (lower.Contains("64", StringComparison.Ordinal)
            || lower.Contains("x64", StringComparison.Ordinal)
            || lower.Contains("amd64", StringComparison.Ordinal))
        {
            return "x64";
        }

        if (lower.Contains("32", StringComparison.Ordinal)
            || lower.Contains("x86", StringComparison.Ordinal)
            || lower.Contains("i386", StringComparison.Ordinal))
        {
            return "x86 (32-bit)";
        }

        return trimmed;
    }
}

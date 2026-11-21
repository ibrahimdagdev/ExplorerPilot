using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ExplorerPilot.Core.Formatting;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.Infrastructure.Services;

/// <summary>
/// Collects local Windows system information via WMI and runtime APIs. No elevation required.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemInformationProvider : ISystemInformationProvider
{
    private readonly ILogger<WindowsSystemInformationProvider>? _logger;

    public WindowsSystemInformationProvider(ILogger<WindowsSystemInformationProvider>? logger = null)
    {
        _logger = logger;
    }

    public Task<SystemInformationModel> GetSystemInformationAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Collect(cancellationToken), cancellationToken);

    private SystemInformationModel Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<string>();

        var os = SafeCollect(() => QueryOperatingSystem(cancellationToken), "Win32_OperatingSystem", warnings);
        var processor = SafeCollect(() => QueryProcessor(cancellationToken), "Win32_Processor", warnings);
        var memory = SafeCollect(() => QueryMemory(cancellationToken), "Win32_OperatingSystem/Memory", warnings);
        var board = SafeCollect(() => QueryMotherboard(cancellationToken), "Win32_BaseBoard", warnings);
        var bios = SafeCollect(() => QueryBios(cancellationToken), "Win32_BIOS", warnings);
        var computer = SafeCollect(() => QueryComputer(cancellationToken), "Win32_ComputerSystem", warnings);
        var gpus = SafeCollect(() => QueryGraphics(cancellationToken), "Win32_VideoController", warnings) ?? [];
        var disks = SafeCollect(() => QueryStorage(cancellationToken), "Win32_DiskDrive", warnings) ?? [];

        // Fill architecture from runtime when WMI omitted it.
        os = EnsureOsArchitectures(os);
        computer = EnsureSystemArchitecture(computer, os);

        return new SystemInformationModel
        {
            OperatingSystem = os ?? new OperatingSystemInfo
            {
                OsArchitecture = ArchitectureFormatter.FormatRuntimeOs(),
                ProcessArchitecture = ArchitectureFormatter.FormatRuntimeProcess()
            },
            Processor = processor ?? new ProcessorInfo
            {
                LogicalProcessorCount = Environment.ProcessorCount
            },
            Memory = memory ?? new MemoryInfo(),
            Motherboard = board ?? new MotherboardInfo(),
            Bios = bios ?? new BiosInfo(),
            Computer = computer ?? new ComputerSystemInfo
            {
                SystemArchitecture = ArchitectureFormatter.FormatRuntimeOs()
            },
            GraphicsAdapters = gpus,
            StorageDevices = disks,
            Warnings = warnings,
            CollectedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private T? SafeCollect<T>(Func<T> query, string source, List<string> warnings) where T : class
    {
        try
        {
            return query();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "System information query failed for {Source}", source);
            warnings.Add(source);
            return null;
        }
    }

    private static OperatingSystemInfo EnsureOsArchitectures(OperatingSystemInfo? os)
    {
        os ??= new OperatingSystemInfo();
        return new OperatingSystemInfo
        {
            Name = os.Name,
            Version = os.Version,
            BuildNumber = os.BuildNumber,
            OsArchitecture = string.IsNullOrWhiteSpace(os.OsArchitecture)
                ? ArchitectureFormatter.FormatRuntimeOs()
                : ArchitectureFormatter.NormalizeReportedArchitecture(os.OsArchitecture),
            ProcessArchitecture = string.IsNullOrWhiteSpace(os.ProcessArchitecture)
                ? ArchitectureFormatter.FormatRuntimeProcess()
                : ArchitectureFormatter.NormalizeReportedArchitecture(os.ProcessArchitecture)
        };
    }

    private static ComputerSystemInfo EnsureSystemArchitecture(ComputerSystemInfo? computer, OperatingSystemInfo? os)
    {
        computer ??= new ComputerSystemInfo();
        var arch = !string.IsNullOrWhiteSpace(computer.SystemArchitecture)
            ? ArchitectureFormatter.NormalizeReportedArchitecture(computer.SystemArchitecture)
            : os?.OsArchitecture ?? ArchitectureFormatter.FormatRuntimeOs();

        return new ComputerSystemInfo
        {
            Manufacturer = computer.Manufacturer,
            Model = computer.Model,
            SystemArchitecture = arch
        };
    }

    private OperatingSystemInfo QueryOperatingSystem(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var searcher = new ManagementObjectSearcher("SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem");
        using var results = searcher.Get();
        foreach (ManagementBaseObject obj in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (obj as IDisposable)
            {
                return new OperatingSystemInfo
                {
                    Name = ReadString(obj, "Caption"),
                    Version = ReadString(obj, "Version"),
                    BuildNumber = ReadString(obj, "BuildNumber"),
                    OsArchitecture = ReadString(obj, "OSArchitecture"),
                    ProcessArchitecture = ArchitectureFormatter.FormatRuntimeProcess()
                };
            }
        }

        return new OperatingSystemInfo
        {
            Name = RuntimeInformation.OSDescription,
            OsArchitecture = ArchitectureFormatter.FormatRuntimeOs(),
            ProcessArchitecture = ArchitectureFormatter.FormatRuntimeProcess()
        };
    }

    private ProcessorInfo QueryProcessor(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
        using var results = searcher.Get();

        string? name = null;
        string? manufacturer = null;
        int physical = 0;
        int logical = 0;
        var any = false;

        foreach (ManagementBaseObject obj in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (obj as IDisposable)
            {
                any = true;
                name ??= ReadString(obj, "Name");
                manufacturer ??= ReadString(obj, "Manufacturer");
                physical += ReadInt(obj, "NumberOfCores") ?? 0;
                logical += ReadInt(obj, "NumberOfLogicalProcessors") ?? 0;
            }
        }

        if (!any)
        {
            return new ProcessorInfo { LogicalProcessorCount = Environment.ProcessorCount };
        }

        return new ProcessorInfo
        {
            Manufacturer = CleanCpuManufacturer(manufacturer),
            Model = name?.Trim(),
            PhysicalCoreCount = physical > 0 ? physical : null,
            LogicalProcessorCount = logical > 0 ? logical : Environment.ProcessorCount
        };
    }

    private MemoryInfo QueryMemory(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var searcher = new ManagementObjectSearcher(
            "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
        using var results = searcher.Get();
        foreach (ManagementBaseObject obj in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (obj as IDisposable)
            {
                // Values are in KB.
                var totalKb = ReadULong(obj, "TotalVisibleMemorySize");
                var freeKb = ReadULong(obj, "FreePhysicalMemory");
                return new MemoryInfo
                {
                    TotalPhysicalBytes = totalKb is null ? null : totalKb.Value * 1024UL,
                    AvailablePhysicalBytes = freeKb is null ? null : freeKb.Value * 1024UL
                };
            }
        }

        return new MemoryInfo();
    }

    private MotherboardInfo QueryMotherboard(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Manufacturer, Product FROM Win32_BaseBoard");
        using var results = searcher.Get();
        foreach (ManagementBaseObject obj in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (obj as IDisposable)
            {
                return new MotherboardInfo
                {
                    Manufacturer = SanitizePlaceholder(ReadString(obj, "Manufacturer")),
                    Model = SanitizePlaceholder(ReadString(obj, "Product"))
                };
            }
        }

        return new MotherboardInfo();
    }

    private BiosInfo QueryBios(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
        using var results = searcher.Get();
        foreach (ManagementBaseObject obj in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (obj as IDisposable)
            {
                return new BiosInfo
                {
                    Manufacturer = SanitizePlaceholder(ReadString(obj, "Manufacturer")),
                    Version = SanitizePlaceholder(ReadString(obj, "SMBIOSBIOSVersion")),
                    ReleaseDate = FormatBiosDate(ReadString(obj, "ReleaseDate"))
                };
            }
        }

        return new BiosInfo();
    }

    private ComputerSystemInfo QueryComputer(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Manufacturer, Model, SystemType FROM Win32_ComputerSystem");
        using var results = searcher.Get();
        foreach (ManagementBaseObject obj in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (obj as IDisposable)
            {
                return new ComputerSystemInfo
                {
                    Manufacturer = SanitizePlaceholder(ReadString(obj, "Manufacturer")),
                    Model = SanitizePlaceholder(ReadString(obj, "Model")),
                    SystemArchitecture = ReadString(obj, "SystemType")
                };
            }
        }

        return new ComputerSystemInfo();
    }

    private List<GraphicsAdapterInfo> QueryGraphics(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var list = new List<GraphicsAdapterInfo>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, AdapterCompatibility, AdapterRAM FROM Win32_VideoController");
        using var results = searcher.Get();
        foreach (ManagementBaseObject obj in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (obj as IDisposable)
            {
                var name = SanitizePlaceholder(ReadString(obj, "Name"));
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var ram = ReadULong(obj, "AdapterRAM");
                // AdapterRAM is often capped / unreliable; still surface reported value when non-zero.
                list.Add(new GraphicsAdapterInfo
                {
                    Name = name,
                    Manufacturer = SanitizePlaceholder(ReadString(obj, "AdapterCompatibility")),
                    AdapterRamBytes = ram is null or 0 ? null : ram
                });
            }
        }

        return list;
    }

    private List<StorageDeviceInfo> QueryStorage(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var list = new List<StorageDeviceInfo>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT Model, Size, MediaType, InterfaceType FROM Win32_DiskDrive");
        using var results = searcher.Get();
        foreach (ManagementBaseObject obj in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (obj as IDisposable)
            {
                var model = SanitizePlaceholder(ReadString(obj, "Model"));
                if (string.IsNullOrWhiteSpace(model))
                {
                    continue;
                }

                list.Add(new StorageDeviceInfo
                {
                    Model = model,
                    SizeBytes = ReadULong(obj, "Size"),
                    // Report OS-provided media description only — do not infer SSD/HDD.
                    MediaType = SanitizePlaceholder(ReadString(obj, "MediaType")),
                    InterfaceType = SanitizePlaceholder(ReadString(obj, "InterfaceType"))
                });
            }
        }

        return list;
    }

    private static string? ReadString(ManagementBaseObject obj, string property)
    {
        try
        {
            var value = obj[property];
            if (value is null || value is DBNull)
            {
                return null;
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static int? ReadInt(ManagementBaseObject obj, string property)
    {
        try
        {
            var value = obj[property];
            if (value is null || value is DBNull)
            {
                return null;
            }

            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static ulong? ReadULong(ManagementBaseObject obj, string property)
    {
        try
        {
            var value = obj[property];
            if (value is null || value is DBNull)
            {
                return null;
            }

            return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static string? SanitizePlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("To Be Filled By O.E.M.", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Default string", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("System Product Name", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("System manufacturer", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("None", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("N/A", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }

    private static string? CleanCpuManufacturer(string? value)
    {
        var cleaned = SanitizePlaceholder(value);
        if (cleaned is null)
        {
            return null;
        }

        if (cleaned.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            return "Intel";
        }

        if (cleaned.Contains("AMD", StringComparison.OrdinalIgnoreCase)
            || cleaned.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase))
        {
            return "AMD";
        }

        return cleaned;
    }

    private static string? FormatBiosDate(string? wmiDate)
    {
        if (string.IsNullOrWhiteSpace(wmiDate) || wmiDate.Length < 8)
        {
            return null;
        }

        // WMI datetime: yyyymmddHHMMSS.mmmmmmsUUU
        if (DateTime.TryParseExact(
                wmiDate[..8],
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return SanitizePlaceholder(wmiDate);
    }
}

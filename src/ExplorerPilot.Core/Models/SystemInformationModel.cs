namespace ExplorerPilot.Core.Models;

/// <summary>
/// Locally collected hardware and OS details. Contains no serial numbers or network identifiers.
/// </summary>
public sealed record SystemInformationModel
{
    public required OperatingSystemInfo OperatingSystem { get; init; }

    public required ProcessorInfo Processor { get; init; }

    public required MemoryInfo Memory { get; init; }

    public required MotherboardInfo Motherboard { get; init; }

    public required BiosInfo Bios { get; init; }

    public required ComputerSystemInfo Computer { get; init; }

    public IReadOnlyList<GraphicsAdapterInfo> GraphicsAdapters { get; init; } = [];

    public IReadOnlyList<StorageDeviceInfo> StorageDevices { get; init; } = [];

    /// <summary>
    /// Non-fatal collection warnings (e.g. a single WMI class failed).
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public DateTimeOffset CollectedAtUtc { get; init; }
}

public sealed record OperatingSystemInfo
{
    public string? Name { get; init; }

    public string? Version { get; init; }

    public string? BuildNumber { get; init; }

    public string? OsArchitecture { get; init; }

    public string? ProcessArchitecture { get; init; }
}

public sealed record ProcessorInfo
{
    public string? Manufacturer { get; init; }

    public string? Model { get; init; }

    public int? PhysicalCoreCount { get; init; }

    public int? LogicalProcessorCount { get; init; }
}

public sealed record MemoryInfo
{
    public ulong? TotalPhysicalBytes { get; init; }

    public ulong? AvailablePhysicalBytes { get; init; }
}

public sealed record MotherboardInfo
{
    public string? Manufacturer { get; init; }

    public string? Model { get; init; }
}

public sealed record BiosInfo
{
    public string? Manufacturer { get; init; }

    public string? Version { get; init; }

    public string? ReleaseDate { get; init; }
}

public sealed record ComputerSystemInfo
{
    public string? Manufacturer { get; init; }

    public string? Model { get; init; }

    public string? SystemArchitecture { get; init; }
}

public sealed record GraphicsAdapterInfo
{
    public string? Name { get; init; }

    public string? Manufacturer { get; init; }

    public ulong? AdapterRamBytes { get; init; }
}

public sealed record StorageDeviceInfo
{
    public string? Model { get; init; }

    public ulong? SizeBytes { get; init; }

    /// <summary>
    /// Raw media description as reported by the OS (not inferred SSD/HDD).
    /// </summary>
    public string? MediaType { get; init; }

    public string? InterfaceType { get; init; }
}

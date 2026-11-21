using System.Management;
using System.Runtime.Versioning;

namespace ExplorerPilot.ScanBenchmark;

internal static class StorageMediaDetector
{
    public static StorageProfile Probe(string path)
    {
        string? root;
        try
        {
            root = Path.GetPathRoot(Path.GetFullPath(path));
        }
        catch
        {
            root = null;
        }

        var driveLetter = root is { Length: >= 1 } ? root[0].ToString() : null;
        var driveType = DriveType.Unknown;
        long totalSize = 0;
        long freeSpace = 0;
        if (!string.IsNullOrEmpty(root))
        {
            try
            {
                var di = new DriveInfo(root);
                driveType = di.DriveType;
                if (di.IsReady)
                {
                    totalSize = di.TotalSize;
                    freeSpace = di.AvailableFreeSpace;
                }
            }
            catch
            {
                // ignore
            }
        }

        var media = "Unknown";
        if (OperatingSystem.IsWindows())
        {
            media = TryDetectWindowsMedia(driveLetter) ?? media;
        }

        return new StorageProfile(
            RootPath: root ?? "(unknown)",
            DriveType: driveType.ToString(),
            MediaKind: media,
            TotalBytes: totalSize,
            FreeBytes: freeSpace);
    }

    [SupportedOSPlatform("windows")]
    private static string? TryDetectWindowsMedia(string? driveLetter)
    {
        if (string.IsNullOrWhiteSpace(driveLetter))
        {
            return null;
        }

        try
        {
            // MSFT_PhysicalDisk MediaType: 3=HDD, 4=SSD, 5=SCM
            using var searcher = new ManagementObjectSearcher(
                "root\\Microsoft\\Windows\\Storage",
                "SELECT FriendlyName, MediaType, SpindleSpeed FROM MSFT_PhysicalDisk");
            foreach (ManagementObject disk in searcher.Get())
            {
                var mediaType = disk["MediaType"] is uint mt ? mt : 0u;
                var name = disk["FriendlyName"]?.ToString() ?? "disk";
                var kind = mediaType switch
                {
                    3 => "HDD",
                    4 => "SSD",
                    5 => "SCM",
                    _ => InferKindFromName(name) ?? $"Other({mediaType})"
                };

                // Multiple disks possible; report first classified disk + name.
                // Volume↔disk mapping is non-trivial; we annotate system storage mix.
                return $"{kind} (sample physical disk: {name}; volume {driveLetter}: may span multiple disks)";
            }
        }
        catch
        {
            // Fall through to Win32_DiskDrive
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Model, MediaType, InterfaceType FROM Win32_DiskDrive");
            foreach (ManagementObject disk in searcher.Get())
            {
                var model = disk["Model"]?.ToString() ?? "disk";
                var media = disk["MediaType"]?.ToString() ?? "";
                var iface = disk["InterfaceType"]?.ToString() ?? "";
                var lower = (model + " " + media).ToLowerInvariant();
                var kind = lower.Contains("ssd") || lower.Contains("nvme")
                    ? "SSD-like"
                    : lower.Contains("fixed hard disk")
                        ? "HDD-or-fixed"
                        : "Unknown";
                return $"{kind} (Win32_DiskDrive: {model}; {iface})";
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string? InferKindFromName(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("nvme") || lower.Contains("ssd") || lower.Contains("mzvl") || lower.Contains("solid"))
        {
            return "SSD";
        }

        if (lower.Contains("hdd") || lower.Contains("st[0-9]") || lower.Contains("barracuda"))
        {
            return "HDD";
        }

        return null;
    }
}

internal sealed record StorageProfile(
    string RootPath,
    string DriveType,
    string MediaKind,
    long TotalBytes,
    long FreeBytes);

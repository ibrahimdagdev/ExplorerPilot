using System.IO;
using System.Reflection;

namespace ExplorerPilot.App.Services;

public sealed class ApplicationInfoService : IApplicationInfoService
{
    public ApplicationInfoService()
    {
        var assembly = Assembly.GetExecutingAssembly();

        ProductName = ReadAttribute<AssemblyProductAttribute>(assembly)?.Product
                      ?? assembly.GetName().Name
                      ?? "ExplorerPilot";

        Company = ReadAttribute<AssemblyCompanyAttribute>(assembly)?.Company ?? "Tasarisoft";
        Author = ReadMetadata(assembly, "Authors") ?? "İbrahim Dağ";
        Copyright = ReadAttribute<AssemblyCopyrightAttribute>(assembly)?.Copyright
                    ?? "© 2026 Tasarisoft. All rights reserved.";

        WebsiteUrl = "https://tasarisoft.com";
        WebsiteDisplay = "tasarisoft.com";
        RepositoryUrl = "https://github.com/ibrahimdagdev/ExplorerPilot";
        RepositoryDisplay = "github.com/ibrahimdagdev/ExplorerPilot";
        Platform = "Windows / .NET 10";

        Version = ResolveVersion(assembly);
        VersionLabel = "v" + Version;
        BrandedVersionLabel = $"{ProductName} {VersionLabel}";

        var license = DetectLicense();
        HasLicenseFile = license is not null;
        LicenseName = license ?? string.Empty;
    }

    public string ProductName { get; }

    public string Company { get; }

    public string Author { get; }

    public string Copyright { get; }

    public string WebsiteUrl { get; }

    public string WebsiteDisplay { get; }

    public string RepositoryUrl { get; }

    public string RepositoryDisplay { get; }

    public string Platform { get; }

    public string Version { get; }

    public string VersionLabel { get; }

    public string BrandedVersionLabel { get; }

    public bool HasLicenseFile { get; }

    public string LicenseName { get; }

    private static string ResolveVersion(Assembly assembly)
    {
        var informational = ReadAttribute<AssemblyInformationalVersionAttribute>(assembly)
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            if (plus > 0)
            {
                informational = informational[..plus];
            }

            return informational.Trim();
        }

        var version = assembly.GetName().Version;
        return version is null ? "0.1.0" : version.ToString(3);
    }

    private static string? ReadMetadata(Assembly assembly, string key) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    private static T? ReadAttribute<T>(Assembly assembly) where T : Attribute =>
        assembly.GetCustomAttribute<T>();

    private static string? DetectLicense()
    {
        try
        {
            var start = new DirectoryInfo(AppContext.BaseDirectory);
            for (var dir = start; dir is not null; dir = dir.Parent)
            {
                foreach (var name in new[] { "LICENSE", "LICENSE.md", "LICENSE.txt", "COPYING" })
                {
                    var path = Path.Combine(dir.FullName, name);
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var text = File.ReadAllText(path);
                    if (text.Contains("MIT License", StringComparison.OrdinalIgnoreCase)
                        || text.StartsWith("MIT", StringComparison.OrdinalIgnoreCase))
                    {
                        return "MIT";
                    }

                    if (text.Contains("Apache License", StringComparison.OrdinalIgnoreCase))
                    {
                        return "Apache-2.0";
                    }

                    if (text.Contains("GNU GENERAL PUBLIC LICENSE", StringComparison.OrdinalIgnoreCase))
                    {
                        return "GPL";
                    }

                    return Path.GetFileName(path);
                }

                if (File.Exists(Path.Combine(dir.FullName, "ExplorerPilot.sln")))
                {
                    break;
                }
            }
        }
        catch
        {
            // License detection is best-effort.
        }

        return null;
    }
}

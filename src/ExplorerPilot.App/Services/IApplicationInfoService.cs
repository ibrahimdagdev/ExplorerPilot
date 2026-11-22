namespace ExplorerPilot.App.Services;

public interface IApplicationInfoService
{
    string ProductName { get; }

    string Company { get; }

    string Author { get; }

    string Copyright { get; }

    string WebsiteUrl { get; }

    string WebsiteDisplay { get; }

    string RepositoryUrl { get; }

    string RepositoryDisplay { get; }

    string Platform { get; }

    /// <summary>Semantic version without prefix, e.g. 0.1.0</summary>
    string Version { get; }

    /// <summary>Version with prefix, e.g. v0.1.0</summary>
    string VersionLabel { get; }

    /// <summary>e.g. ExplorerPilot v0.1.0</summary>
    string BrandedVersionLabel { get; }

    /// <summary>True when a LICENSE file was found in/near the application directory.</summary>
    bool HasLicenseFile { get; }

    /// <summary>Detected license name when available; otherwise empty.</summary>
    string LicenseName { get; }
}

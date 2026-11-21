using System.IO;

namespace ExplorerPilot.App.Diagnostics;

/// <summary>Canonical local application data locations (no roaming / no Program Files writes).</summary>
public static class AppDataPaths
{
    public static string RootDirectory { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExplorerPilot");

    public static string LogsDirectory { get; } = Path.Combine(RootDirectory, "logs");

    public static string SettingsDirectory { get; } = Path.Combine(RootDirectory, "settings");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(SettingsDirectory);
    }
}

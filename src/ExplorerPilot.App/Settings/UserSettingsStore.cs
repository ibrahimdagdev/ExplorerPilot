using System.IO;
using System.Text.Json;

namespace ExplorerPilot.App.Settings;

public sealed class UserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _settingsPath;
    private readonly object _sync = new();

    public UserSettingsStore()
        : this(DefaultSettingsPath())
    {
    }

    public UserSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = Path.GetFullPath(settingsPath);
        var folder = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }
    }

    public string SettingsPath => _settingsPath;

    public static string DefaultSettingsPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExplorerPilot",
            "settings.json");

    public UserSettings Load()
    {
        lock (_sync)
        {
            return LoadUnlocked();
        }
    }

    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_sync)
        {
            SaveUnlocked(settings);
        }
    }

    public void Update(Action<UserSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        lock (_sync)
        {
            var settings = LoadUnlocked();
            mutate(settings);
            SaveUnlocked(settings);
        }
    }

    private UserSettings LoadUnlocked()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new UserSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new UserSettings();
            }

            return JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
        }
        catch
        {
            // Corrupt / unreadable settings must not crash startup — fall back to defaults (Light).
            return new UserSettings();
        }
    }

    private void SaveUnlocked(UserSettings settings)
    {
        try
        {
            var folder = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tempPath = _settingsPath + ".tmp";
            File.WriteAllText(tempPath, json);

            // Atomic replace: copy-over then delete temp (works across volumes better than Move on Windows).
            File.Copy(tempPath, _settingsPath, overwrite: true);
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // temp cleanup is best-effort
            }
        }
        catch
        {
            // Preference persistence is best-effort.
        }
    }
}

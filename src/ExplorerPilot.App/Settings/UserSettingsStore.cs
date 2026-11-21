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
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExplorerPilot");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.json");
    }

    public UserSettings Load()
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return new UserSettings();
                }

                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
            }
            catch
            {
                return new UserSettings();
            }
        }
    }

    public void Save(UserSettings settings)
    {
        lock (_sync)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(_settingsPath, json);
            }
            catch
            {
                // Preference persistence is best-effort.
            }
        }
    }

    public void Update(Action<UserSettings> mutate)
    {
        lock (_sync)
        {
            var settings = LoadUnlocked();
            mutate(settings);
            try
            {
                var json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(_settingsPath, json);
            }
            catch
            {
                // best-effort
            }
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
            return JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }
}

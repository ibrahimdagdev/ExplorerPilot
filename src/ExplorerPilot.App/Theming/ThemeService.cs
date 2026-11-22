using System.Windows;
using ExplorerPilot.App.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ExplorerPilot.App.Theming;

public sealed class ThemeService : IThemeService
{
    private const string ThemeDictionaryTag = "ExplorerPilot.ThemeDictionary";
    private const string AppsUseLightThemeValueName = "AppsUseLightTheme";
    private const string PersonalizeKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly ILogger<ThemeService> _logger;
    private readonly UserSettingsStore _settingsStore;
    private readonly List<ThemeOption> _themes;
    private readonly object _sync = new();
    private bool _initialized;
    private bool _disposed;

    public ThemeService(ILogger<ThemeService> logger, UserSettingsStore settingsStore)
    {
        _logger = logger;
        _settingsStore = settingsStore;
        _themes =
        [
            new ThemeOption(AppThemeMode.System, "System"),
            new ThemeOption(AppThemeMode.Light, "Light"),
            new ThemeOption(AppThemeMode.Dark, "Dark")
        ];
        AvailableThemes = _themes;
    }

    public AppThemeMode Preference { get; private set; } = AppThemeMode.Light;

    public AppThemeMode EffectiveTheme { get; private set; } = AppThemeMode.Light;

    public IReadOnlyList<ThemeOption> AvailableThemes { get; }

    public event EventHandler? ThemeChanged;

    public void Initialize()
    {
        lock (_sync)
        {
            if (_initialized)
            {
                return;
            }

            var settings = _settingsStore.Load();
            Preference = ParseThemePreference(settings.Theme);
            // Never rewrite settings during Initialize — missing/corrupt files stay absent
            // until the user explicitly changes the theme (default effective theme is Light).
            ApplyThemeCore(force: true);
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _initialized = true;
            _logger.LogInformation(
                "Theme initialized from {Path}: preference={Preference}, effective={Effective}",
                _settingsStore.SettingsPath,
                Preference,
                EffectiveTheme);
        }
    }

    public void SetPreference(AppThemeMode mode)
    {
        lock (_sync)
        {
            if (Preference == mode && _initialized)
            {
                if (mode != AppThemeMode.System)
                {
                    return;
                }
            }

            Preference = mode;
            _settingsStore.Update(s => s.Theme = mode.ToString());
            ApplyThemeCore(force: true);
        }
    }

    public void RefreshOptionDisplayNames(Func<string, string> localize)
    {
        foreach (var option in _themes)
        {
            option.DisplayName = option.Mode switch
            {
                AppThemeMode.System => localize("Theme_System"),
                AppThemeMode.Light => localize("Theme_Light"),
                AppThemeMode.Dark => localize("Theme_Dark"),
                _ => option.Mode.ToString()
            };
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Preference != AppThemeMode.System)
        {
            return;
        }

        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color))
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            ApplyThemeCore(force: false);
        }
        else
        {
            dispatcher.Invoke(() => ApplyThemeCore(force: false));
        }
    }

    private void ApplyThemeCore(bool force)
    {
        var resolved = ResolveEffectiveTheme(Preference);
        if (!force && resolved == EffectiveTheme)
        {
            return;
        }

        EffectiveTheme = resolved;
        var uri = resolved == AppThemeMode.Light
            ? new Uri("Resources/Themes/LightTheme.xaml", UriKind.Relative)
            : new Uri("Resources/Themes/DarkTheme.xaml", UriKind.Relative);

        try
        {
            var app = Application.Current;
            if (app is null)
            {
                return;
            }

            var themeDictionary = new ResourceDictionary { Source = uri };
            themeDictionary["ThemeDictionaryTag"] = ThemeDictionaryTag;

            var merged = app.Resources.MergedDictionaries;
            for (var i = merged.Count - 1; i >= 0; i--)
            {
                if (IsThemeDictionary(merged[i]))
                {
                    merged.RemoveAt(i);
                }
            }

            merged.Insert(0, themeDictionary);

            _logger.LogInformation("Applied {Theme} theme (preference: {Preference})", resolved, Preference);
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply theme {Theme}", resolved);
        }
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        if (dictionary.Source is not null)
        {
            var path = dictionary.Source.OriginalString;
            if (path.Contains("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase)
                || path.Contains("LightTheme.xaml", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return dictionary.Contains("ThemeDictionaryTag")
               && Equals(dictionary["ThemeDictionaryTag"], ThemeDictionaryTag);
    }

    private AppThemeMode ResolveEffectiveTheme(AppThemeMode preference) =>
        preference switch
        {
            AppThemeMode.Light => AppThemeMode.Light,
            AppThemeMode.Dark => AppThemeMode.Dark,
            _ => IsWindowsAppsLightTheme() ? AppThemeMode.Light : AppThemeMode.Dark
        };

    private bool IsWindowsAppsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            var value = key?.GetValue(AppsUseLightThemeValueName);
            if (value is int intValue)
            {
                return intValue != 0;
            }

            if (value is null)
            {
                return true;
            }

            return Convert.ToInt32(value) != 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to read Windows app theme; defaulting to Light");
            return true;
        }
    }

    /// <summary>
    /// Maps a persisted theme string to <see cref="AppThemeMode"/>.
    /// Missing or invalid values fall back to Light (never force Dark).
    /// </summary>
    internal static AppThemeMode ParseThemePreference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AppThemeMode.Light;
        }

        if (Enum.TryParse<AppThemeMode>(value.Trim(), ignoreCase: true, out var mode)
            && Enum.IsDefined(mode))
        {
            return mode;
        }

        return AppThemeMode.Light;
    }
}

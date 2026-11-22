using ExplorerPilot.App.Settings;
using ExplorerPilot.App.Theming;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExplorerPilot.Tests;

/// <summary>
/// ThemeService preference persistence (ApplyThemeCore no-ops without Application.Current,
/// but Preference + settings.json are still updated).
/// </summary>
public sealed class ThemePersistenceStaTests
{
    [Fact]
    public void Initialize_MissingSettings_PreferenceIsLight_AndDoesNotWriteFile()
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new UserSettingsStore(path);
        using var theme = new ThemeService(NullLogger<ThemeService>.Instance, store);

        theme.Initialize();

        Assert.Equal(AppThemeMode.Light, theme.Preference);
        Assert.Equal(AppThemeMode.Light, theme.EffectiveTheme);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void SetPreference_DarkThenLight_PersistsAcrossNewServiceInstance()
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new UserSettingsStore(path);

        using (var theme = new ThemeService(NullLogger<ThemeService>.Instance, store))
        {
            theme.Initialize();
            theme.SetPreference(AppThemeMode.Dark);
            Assert.Equal(AppThemeMode.Dark, theme.Preference);
            Assert.Equal("Dark", store.Load().Theme);
        }

        using (var theme2 = new ThemeService(NullLogger<ThemeService>.Instance, store))
        {
            theme2.Initialize();
            Assert.Equal(AppThemeMode.Dark, theme2.Preference);

            theme2.SetPreference(AppThemeMode.Light);
            Assert.Equal(AppThemeMode.Light, theme2.Preference);
            Assert.Equal("Light", store.Load().Theme);
        }

        using var theme3 = new ThemeService(NullLogger<ThemeService>.Instance, store);
        theme3.Initialize();
        Assert.Equal(AppThemeMode.Light, theme3.Preference);
    }

    [Fact]
    public void CorruptSettings_Initialize_FallsBackToLight()
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "{ broken");
        var store = new UserSettingsStore(path);
        using var theme = new ThemeService(NullLogger<ThemeService>.Instance, store);

        theme.Initialize();

        Assert.Equal(AppThemeMode.Light, theme.Preference);
        Assert.Equal(AppThemeMode.Light, theme.EffectiveTheme);
    }

    [Fact]
    public void Initialize_DoesNotOverwriteExistingDarkSetting()
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new UserSettingsStore(path);
        store.Update(s =>
        {
            s.Theme = "Dark";
            s.Language = "English";
        });

        using var theme = new ThemeService(NullLogger<ThemeService>.Instance, store);
        theme.Initialize();

        Assert.Equal(AppThemeMode.Dark, theme.Preference);
        var loaded = store.Load();
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal("English", loaded.Language);
    }
}

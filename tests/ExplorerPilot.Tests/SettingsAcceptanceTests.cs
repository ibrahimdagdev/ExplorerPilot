using ExplorerPilot.App.Localization;
using ExplorerPilot.App.Settings;
using ExplorerPilot.App.Theming;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExplorerPilot.Tests;

/// <summary>
/// Isolated-profile acceptance for Settings persistence across "restarts"
/// (new ThemeService / LocalizationService instances sharing a temp settings file).
/// Never touches the developer's LocalAppData settings.json.
/// </summary>
public sealed class SettingsAcceptanceTests
{
    [Fact]
    public void MissingSettingsFile_DefaultsToLightTheme()
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "missing", "settings.json");
        var store = new UserSettingsStore(path);
        using var theme = new ThemeService(NullLogger<ThemeService>.Instance, store);
        theme.Initialize();

        Assert.Equal(AppThemeMode.Light, theme.Preference);
        Assert.Equal(AppThemeMode.Light, theme.EffectiveTheme);
        Assert.False(File.Exists(path), "Initialize must not create settings until the user changes a preference.");
    }

    [Theory]
    [InlineData("Light", "Dark", AppThemeMode.Dark)]
    [InlineData("Dark", "Light", AppThemeMode.Light)]
    public void Theme_SurvivesRestart_ViaIsolatedStore(string first, string second, AppThemeMode expectedAfterRestart)
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new UserSettingsStore(path);

        using (var theme1 = new ThemeService(NullLogger<ThemeService>.Instance, store))
        {
            theme1.Initialize();
            theme1.SetPreference(Enum.Parse<AppThemeMode>(first));
            theme1.SetPreference(Enum.Parse<AppThemeMode>(second));
        }

        using var theme2 = new ThemeService(NullLogger<ThemeService>.Instance, store);
        theme2.Initialize();
        Assert.Equal(expectedAfterRestart, theme2.Preference);
        Assert.Equal(expectedAfterRestart, theme2.EffectiveTheme);
        Assert.Equal(second, store.Load().Theme);
    }

    [Theory]
    [InlineData(AppLanguageMode.English, AppLanguageMode.Turkish)]
    [InlineData(AppLanguageMode.Turkish, AppLanguageMode.English)]
    public void Language_SurvivesRestart_ViaIsolatedStore(AppLanguageMode first, AppLanguageMode second)
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new UserSettingsStore(path);

        using (var loc1 = new LocalizationService(NullLogger<LocalizationService>.Instance, store))
        {
            loc1.Initialize();
            loc1.SetPreference(first);
            loc1.SetPreference(second);
        }

        using var loc2 = new LocalizationService(NullLogger<LocalizationService>.Instance, store);
        loc2.Initialize();
        Assert.Equal(second, loc2.Preference);
        Assert.Equal(second.ToString(), store.Load().Language);
    }

    [Fact]
    public void SidebarExpanded_SurvivesRestart_ViaIsolatedStore()
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new UserSettingsStore(path);

        store.Update(s => s.SidebarExpanded = false);
        Assert.False(new UserSettingsStore(path).Load().SidebarExpanded);

        store.Update(s => s.SidebarExpanded = true);
        Assert.True(new UserSettingsStore(path).Load().SidebarExpanded);
    }

    [Fact]
    public void CorruptSettingsJson_FallsBackToSafeDefaults()
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not valid json {{{");

        var loaded = new UserSettingsStore(path).Load();
        Assert.Equal("Light", loaded.Theme);
        Assert.Equal("System", loaded.Language);
        Assert.True(loaded.SidebarExpanded);

        using var theme = new ThemeService(NullLogger<ThemeService>.Instance, new UserSettingsStore(path));
        theme.Initialize();
        Assert.Equal(AppThemeMode.Light, theme.Preference);
    }

    [Fact]
    public void LegacySystemThemePreference_RemainsReadable()
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new UserSettingsStore(path);
        store.Update(s =>
        {
            s.Theme = "System";
            s.Language = "System";
        });

        using var theme = new ThemeService(NullLogger<ThemeService>.Instance, store);
        theme.Initialize();
        Assert.Equal(AppThemeMode.System, theme.Preference);
        Assert.True(theme.EffectiveTheme is AppThemeMode.Light or AppThemeMode.Dark);

        using var loc = new LocalizationService(NullLogger<LocalizationService>.Instance, store);
        loc.Initialize();
        Assert.Equal(AppLanguageMode.System, loc.Preference);
        Assert.True(loc.EffectiveLanguage is AppLanguageMode.English or AppLanguageMode.Turkish);
    }

    [Fact]
    public void ThemeAndLanguageChanges_DoNotClearSiblingPreferences()
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new UserSettingsStore(path);

        using var theme = new ThemeService(NullLogger<ThemeService>.Instance, store);
        using var loc = new LocalizationService(NullLogger<LocalizationService>.Instance, store);
        theme.Initialize();
        loc.Initialize();

        theme.SetPreference(AppThemeMode.Dark);
        loc.SetPreference(AppLanguageMode.Turkish);
        store.Update(s => s.SidebarExpanded = false);

        theme.SetPreference(AppThemeMode.Light);
        var after = store.Load();
        Assert.Equal("Light", after.Theme);
        Assert.Equal("Turkish", after.Language);
        Assert.False(after.SidebarExpanded);
    }
}

using ExplorerPilot.App.Settings;
using ExplorerPilot.App.Theming;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExplorerPilot.Tests;

public sealed class ThemePreferenceTests
{
    [Fact]
    public void UserSettings_DefaultTheme_IsLight()
    {
        Assert.Equal("Light", new UserSettings().Theme);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotATheme")]
    [InlineData("Purple")]
    public void ParseThemePreference_MissingOrInvalid_FallsBackToLight(string? value)
    {
        Assert.Equal(AppThemeMode.Light, ThemeService.ParseThemePreference(value));
    }

    [Theory]
    [InlineData("Light", AppThemeMode.Light)]
    [InlineData("light", AppThemeMode.Light)]
    [InlineData("Dark", AppThemeMode.Dark)]
    [InlineData("DARK", AppThemeMode.Dark)]
    [InlineData("System", AppThemeMode.System)]
    [InlineData(" system ", AppThemeMode.System)]
    public void ParseThemePreference_PreservesValidSavedValues(string value, AppThemeMode expected)
    {
        Assert.Equal(expected, ThemeService.ParseThemePreference(value));
    }

    [Fact]
    public void ParseThemePreference_DoesNotRewriteDarkToLight()
    {
        Assert.Equal(AppThemeMode.Dark, ThemeService.ParseThemePreference("Dark"));
    }

    [Fact]
    public void FirstRun_MissingSettingsFile_PreferenceIsLight_AndDoesNotCreateFile()
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new UserSettingsStore(path);
        using var theme = new ThemeService(NullLogger<ThemeService>.Instance, store);

        // Initialize requires Application.Current for dictionary swap; Preference is set before Apply.
        // Call Parse path via Load + public Preference after Initialize would need WPF.
        // Validate store/default contract used by Initialize:
        var loaded = store.Load();
        Assert.Equal("Light", loaded.Theme);
        Assert.False(File.Exists(path));
        Assert.Equal(AppThemeMode.Light, ThemeService.ParseThemePreference(loaded.Theme));
    }

    [Fact]
    public void Store_RoundTripsDarkAndLight_Atomically()
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new UserSettingsStore(path);

        store.Update(s =>
        {
            s.Theme = "Dark";
            s.Language = "Turkish";
        });
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal("Dark", store.Load().Theme);
        Assert.Equal("Turkish", store.Load().Language);

        store.Update(s => s.Theme = "Light");
        Assert.Equal("Light", store.Load().Theme);
        Assert.Equal("Turkish", store.Load().Language);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Store_CorruptJson_FallsBackToLightDefaults()
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "{ not-json ");
        var store = new UserSettingsStore(path);
        var loaded = store.Load();
        Assert.Equal("Light", loaded.Theme);
        Assert.Equal("System", loaded.Language);
    }

    [Fact]
    public void Store_EmptyFile_FallsBackToLightDefaults()
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "   ");
        var loaded = new UserSettingsStore(path).Load();
        Assert.Equal("Light", loaded.Theme);
    }

    [Fact]
    public void LanguageUpdate_DoesNotClearTheme()
    {
        using var temp = TemporaryDirectory.Create();
        var store = new UserSettingsStore(Path.Combine(temp.Path, "settings.json"));
        store.Update(s =>
        {
            s.Theme = "Dark";
            s.Language = "English";
        });

        store.Update(s => s.Language = "Turkish");
        var loaded = store.Load();
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal("Turkish", loaded.Language);
    }

    [Fact]
    public void ThemeResourceDictionaries_ExistAndLoad()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var light = Path.Combine(repoRoot, "src", "ExplorerPilot.App", "Resources", "Themes", "LightTheme.xaml");
        var dark = Path.Combine(repoRoot, "src", "ExplorerPilot.App", "Resources", "Themes", "DarkTheme.xaml");
        Assert.True(File.Exists(light), light);
        Assert.True(File.Exists(dark), dark);

        var lightText = File.ReadAllText(light);
        var darkText = File.ReadAllText(dark);
        Assert.Contains("BgMainBrush", lightText, StringComparison.Ordinal);
        Assert.Contains("BgMainBrush", darkText, StringComparison.Ordinal);
        Assert.Contains("#FFF5F7FA", lightText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#FFF5F7FA", darkText, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ExplorerPilot.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate ExplorerPilot.sln from " + start);
    }
}

using ExplorerPilot.App.Localization;
using ExplorerPilot.App.Services;
using ExplorerPilot.App.Settings;
using ExplorerPilot.App.Theming;

namespace ExplorerPilot.Tests;

public sealed class SettingsPanelTests
{
    [Fact]
    public void ApplicationInfo_ExposesGitHubRepository()
    {
        var info = new ApplicationInfoService();
        Assert.Equal("ExplorerPilot", info.ProductName);
        Assert.Contains("github.com/ibrahimdagdev/ExplorerPilot", info.RepositoryUrl, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(info.RepositoryDisplay));
    }

    [Fact]
    public void LocalizationDictionaries_IncludeSettingsKeys()
    {
        var root = FindRepoRoot();
        foreach (var file in new[]
                 {
                     Path.Combine(root, "src", "ExplorerPilot.App", "Localization", "Strings.en-US.xaml"),
                     Path.Combine(root, "src", "ExplorerPilot.App", "Localization", "Strings.tr-TR.xaml")
                 })
        {
            var xaml = File.ReadAllText(file);
            foreach (var key in new[]
                     {
                         "Settings_Title", "Settings_Appearance", "Settings_Theme", "Settings_Language",
                         "Settings_Application", "Settings_Repository", "Settings_OpenRepository"
                     })
            {
                Assert.Contains($"x:Key=\"{key}\"", xaml, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void UserSettingsStore_PersistsThemeLanguageAndSidebarIndependently()
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new UserSettingsStore(path);

        store.Update(s =>
        {
            s.Theme = "Dark";
            s.Language = "Turkish";
            s.SidebarExpanded = false;
        });

        var loaded = new UserSettingsStore(path).Load();
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal("Turkish", loaded.Language);
        Assert.False(loaded.SidebarExpanded);

        store.Update(s => s.Theme = "Light");
        loaded = new UserSettingsStore(path).Load();
        Assert.Equal("Light", loaded.Theme);
        Assert.Equal("Turkish", loaded.Language);
        Assert.False(loaded.SidebarExpanded);
    }

    [Fact]
    public void DefaultUserSettings_UseLightThemeAndExpandedSidebar()
    {
        var settings = new UserSettings();
        Assert.Equal("Light", settings.Theme);
        Assert.True(settings.SidebarExpanded);
        Assert.Equal(AppThemeMode.Light, ThemeService.ParseThemePreference(settings.Theme));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ExplorerPilot.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate ExplorerPilot.sln from test base directory.");
    }
}

using ExplorerPilot.App.Settings;
using ExplorerPilot.App.ViewModels;

namespace ExplorerPilot.Tests;

/// <summary>
/// Non-STA coverage for sidebar navigation contracts, persistence, and dashboard tab indices.
/// </summary>
public sealed class SidebarStabilizationTests
{
    [Fact]
    public void NavigationTargets_CoverTwelveAnalysisPagesPlusSettings()
    {
        var expected = Enumerable.Range(0, 12).ToArray();
        var sections = BuildCanonicalSections();
        var indices = sections.SelectMany(s => s.Items).Select(i => i.PageIndex).OrderBy(i => i).ToArray();

        Assert.Equal(expected, indices);
        Assert.Equal(4, sections.Count);
        Assert.Equal(2, sections[0].Items.Count); // Overview
        Assert.Equal(6, sections[1].Items.Count); // Disk analysis
        Assert.Equal(3, sections[2].Items.Count); // Monitoring
        Assert.Single(sections[3].Items); // Tools (Cleanup)
        Assert.Equal(9, sections[3].Items[0].PageIndex);
        Assert.Equal(12, MainViewModel.SettingsTabIndex);
        Assert.False(SidebarNavIcons.Settings.IsEmpty());
    }

    [Fact]
    public void DashboardShortcutIndices_MatchWorkspaceTabs()
    {
        Assert.Equal(1, DashboardTabIndex.Drives);
        Assert.Equal(2, DashboardTabIndex.Explorer);
        Assert.Equal(7, DashboardTabIndex.Treemap);
        Assert.Equal(9, DashboardTabIndex.Cleanup);
        Assert.Equal(10, DashboardTabIndex.Trends);
    }

    [Fact]
    public void UserSettingsStore_PersistsSidebarExpandedAcrossReload()
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new UserSettingsStore(path);

        store.Update(s =>
        {
            s.SidebarExpanded = false;
            s.Theme = "Dark";
            s.Language = "Turkish";
        });

        var reloaded = new UserSettingsStore(path).Load();
        Assert.False(reloaded.SidebarExpanded);
        Assert.Equal("Dark", reloaded.Theme);
        Assert.Equal("Turkish", reloaded.Language);

        store.Update(s => s.SidebarExpanded = true);
        Assert.True(new UserSettingsStore(path).Load().SidebarExpanded);
    }

    [Fact]
    public void LightAndDarkThemes_ExposeSidebarHighlightBrushes()
    {
        var root = FindRepoRoot();
        AssertThemeBrushKeys(Path.Combine(root, "src", "ExplorerPilot.App", "Resources", "Themes", "LightTheme.xaml"));
        AssertThemeBrushKeys(Path.Combine(root, "src", "ExplorerPilot.App", "Resources", "Themes", "DarkTheme.xaml"));
    }

    private static void AssertThemeBrushKeys(string themePath)
    {
        Assert.True(File.Exists(themePath), $"Missing theme file: {themePath}");
        var xaml = File.ReadAllText(themePath);
        foreach (var key in new[]
                 {
                     "AccentSubtleBrush", "BgControlBrush", "FocusBrush",
                     "TextPrimaryBrush", "TextSecondaryBrush"
                 })
        {
            Assert.Contains($"x:Key=\"{key}\"", xaml, StringComparison.Ordinal);
        }
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

    private static List<SidebarNavSectionViewModel> BuildCanonicalSections() =>
    [
        new("Nav_Section_Overview",
        [
            new SidebarNavItemViewModel(0, "Dashboard_Title", SidebarNavIcons.Dashboard),
            new SidebarNavItemViewModel(1, "Drives_Title", SidebarNavIcons.Drives)
        ]),
        new("Nav_Section_DiskAnalysis",
        [
            new SidebarNavItemViewModel(2, "Explorer_Title", SidebarNavIcons.Explorer),
            new SidebarNavItemViewModel(3, "LargestFiles_Title", SidebarNavIcons.LargestFiles),
            new SidebarNavItemViewModel(4, "FileTypes_Title", SidebarNavIcons.FileTypes),
            new SidebarNavItemViewModel(5, "Search_Title", SidebarNavIcons.Search),
            new SidebarNavItemViewModel(6, "Duplicates_Title", SidebarNavIcons.Duplicates),
            new SidebarNavItemViewModel(7, "Treemap_Title", SidebarNavIcons.Treemap)
        ]),
        new("Nav_Section_Monitoring",
        [
            new SidebarNavItemViewModel(8, "Comparison_Title", SidebarNavIcons.Comparison),
            new SidebarNavItemViewModel(10, "Trends_Title", SidebarNavIcons.Trends),
            new SidebarNavItemViewModel(11, "Scheduled_Title", SidebarNavIcons.Scheduled)
        ]),
        new("Nav_Section_Tools",
        [
            new SidebarNavItemViewModel(9, "Cleanup_Title", SidebarNavIcons.Cleanup)
        ])
    ];
}

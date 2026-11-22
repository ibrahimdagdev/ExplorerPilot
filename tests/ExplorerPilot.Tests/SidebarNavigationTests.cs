using ExplorerPilot.App.Settings;
using ExplorerPilot.App.ViewModels;

namespace ExplorerPilot.Tests;

public sealed class SidebarNavigationTests
{
    [Fact]
    public void NavIcons_ParseWithoutThrowing()
    {
        Assert.False(SidebarNavIcons.Dashboard.IsEmpty());
        Assert.False(SidebarNavIcons.Search.IsEmpty());
        Assert.False(SidebarNavIcons.Cleanup.IsEmpty());
        Assert.False(SidebarNavIcons.Settings.IsEmpty());
        Assert.Equal(13, new[]
        {
            SidebarNavIcons.Dashboard, SidebarNavIcons.Drives, SidebarNavIcons.Explorer,
            SidebarNavIcons.LargestFiles, SidebarNavIcons.FileTypes, SidebarNavIcons.Search,
            SidebarNavIcons.Duplicates, SidebarNavIcons.Treemap, SidebarNavIcons.Comparison,
            SidebarNavIcons.Trends, SidebarNavIcons.Scheduled, SidebarNavIcons.Cleanup,
            SidebarNavIcons.Settings
        }.Length);
    }

    [Fact]
    public void UserSettings_DefaultSidebarExpanded()
    {
        Assert.True(new UserSettings().SidebarExpanded);
    }

    [Fact]
    public void SidebarSection_HoldsOrderedPages()
    {
        var overview = new SidebarNavSectionViewModel("Nav_Section_Overview",
        [
            new SidebarNavItemViewModel(0, "Dashboard_Title", SidebarNavIcons.Dashboard),
            new SidebarNavItemViewModel(1, "Drives_Title", SidebarNavIcons.Drives)
        ]);
        Assert.Equal(2, overview.Items.Count);
        Assert.Equal(0, overview.Items[0].PageIndex);
        Assert.Equal(1, overview.Items[1].PageIndex);
    }

    [Fact]
    public void SidebarWidths_MatchDesignSpec()
    {
        Assert.InRange(MainViewModel.ExpandedSidebarWidth, 220, 240);
        Assert.True(MainViewModel.CollapsedSidebarWidth < MainViewModel.ExpandedSidebarWidth);
        Assert.Equal(64, MainViewModel.CollapsedSidebarWidth);
    }
}

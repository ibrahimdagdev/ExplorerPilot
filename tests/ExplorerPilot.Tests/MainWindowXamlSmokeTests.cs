using System.Windows;
using ExplorerPilot.App.Localization;
using ExplorerPilot.App.Services;
using ExplorerPilot.App.Settings;
using ExplorerPilot.App.Theming;
using ExplorerPilot.App.ViewModels;
using ExplorerPilot.App.Views;
using ExplorerPilot.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExplorerPilot.Tests;

/// <summary>
/// STA smoke test that loads MainWindow XAML to catch cyclical MarkupExtension failures.
/// Does not scan drives.
/// </summary>
public sealed class MainWindowXamlSmokeTests
{
    [Fact]
    public void MainWindow_InitializeComponent_DoesNotThrowCyclicalBinding()
    {
        Exception? caught = null;

        var thread = new Thread(() =>
        {
            Application? app = null;
            ServiceProvider? services = null;
            try
            {
                app = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };

                MergeAppResources(app);

                services = BuildServices();
                var localization = services.GetRequiredService<ILocalizationService>();
                localization.Initialize();
                services.GetRequiredService<IThemeService>().Initialize();

                var viewModel = services.GetRequiredService<MainViewModel>();
                var window = new MainWindow(viewModel);

                Assert.NotNull(window.Content);
                window.Show();
                window.UpdateLayout();

                // Sidebar + headerless TabControl host (12 analysis pages + Settings).
                Assert.Equal(13, viewModel.AllNavItems.Count());
                Assert.Equal(0, viewModel.SelectedMainTabIndex);
                Assert.Contains(viewModel.AllNavItems, n => n.PageIndex == 0 && n.IsSelected);
                Assert.Equal(MainViewModel.SettingsTabIndex, viewModel.SettingsNavItem.PageIndex);
                Assert.True(viewModel.IsSidebarExpanded);

                if (FindTabControl(window) is { } tabs)
                {
                    Assert.True(tabs.Items.Count >= 13,
                        "Expected 12 analysis tabs plus Settings.");

                    for (var i = 0; i < tabs.Items.Count; i++)
                    {
                        var tab = Assert.IsType<System.Windows.Controls.TabItem>(tabs.Items[i]);
                        var headerText = tab.Header as string;
                        Assert.False(string.IsNullOrWhiteSpace(headerText),
                            $"Tab {i} Header must resolve to a non-empty localized string.");

                        viewModel.NavigateToPageCommand.Execute(i);
                        window.UpdateLayout();
                        Assert.Equal(i, tabs.SelectedIndex);
                        Assert.Equal(i, viewModel.SelectedMainTabIndex);
                        Assert.Contains(viewModel.AllNavItems, n => n.PageIndex == i && n.IsSelected);
                        Assert.NotNull(tab.Content);
                    }
                }
                else
                {
                    Assert.Fail("Workspace TabControl was not found.");
                }

                // Settings selectors persist theme/language without changing the selected page mid-flight incorrectly.
                viewModel.NavigateToPageCommand.Execute(MainViewModel.SettingsTabIndex);
                window.UpdateLayout();
                Assert.Equal(MainViewModel.SettingsTabIndex, viewModel.SelectedMainTabIndex);
                Assert.True(viewModel.SettingsNavItem.IsSelected);
                Assert.Contains(viewModel.SettingsThemeOptions, t => t.Mode == AppThemeMode.Light);
                Assert.Contains(viewModel.SettingsThemeOptions, t => t.Mode == AppThemeMode.Dark);
                Assert.DoesNotContain(viewModel.SettingsThemeOptions, t => t.Mode == AppThemeMode.System);
                Assert.Equal(2, viewModel.SettingsLanguageOptions.Count);

                // Dashboard shortcut commands keep SelectedMainTabIndex + sidebar selection in sync.
                viewModel.NavigateToPageCommand.Execute(0);
                window.UpdateLayout();
                viewModel.Dashboard.OpenTreemapCommand.Execute(null);
                Assert.Equal(DashboardTabIndex.Treemap, viewModel.SelectedMainTabIndex);
                Assert.Contains(viewModel.AllNavItems, n => n.PageIndex == DashboardTabIndex.Treemap && n.IsSelected);
                viewModel.Dashboard.OpenCleanupCommand.Execute(null);
                Assert.Equal(DashboardTabIndex.Cleanup, viewModel.SelectedMainTabIndex);
                Assert.Contains(viewModel.AllNavItems, n => n.PageIndex == DashboardTabIndex.Cleanup && n.IsSelected);
                viewModel.Dashboard.OpenTrendsCommand.Execute(null);
                Assert.Equal(DashboardTabIndex.Trends, viewModel.SelectedMainTabIndex);
                Assert.Contains(viewModel.AllNavItems, n => n.PageIndex == DashboardTabIndex.Trends && n.IsSelected);
                viewModel.Dashboard.OpenExplorerCommand.Execute(null);
                Assert.Equal(DashboardTabIndex.Explorer, viewModel.SelectedMainTabIndex);
                Assert.Contains(viewModel.AllNavItems, n => n.PageIndex == DashboardTabIndex.Explorer && n.IsSelected);

                // Language switch must keep the selected page.
                var pageBeforeLanguage = viewModel.SelectedMainTabIndex;
                localization.SetPreference(AppLanguageMode.Turkish);
                window.UpdateLayout();
                Assert.Equal(pageBeforeLanguage, viewModel.SelectedMainTabIndex);
                Assert.Contains(viewModel.AllNavItems, n => n.PageIndex == pageBeforeLanguage && n.IsSelected);
                Assert.Contains(viewModel.AllNavItems, n => n.Title.Length > 0 && n.Title != n.TitleKey);
                localization.SetPreference(AppLanguageMode.English);
                window.UpdateLayout();
                Assert.Equal(pageBeforeLanguage, viewModel.SelectedMainTabIndex);

                // Theme switch keeps navigation selection (brush resources swap under DynamicResource).
                var themeService = services.GetRequiredService<IThemeService>();
                var pageBeforeTheme = viewModel.SelectedMainTabIndex;
                themeService.SetPreference(AppThemeMode.Dark);
                window.UpdateLayout();
                Assert.Equal(pageBeforeTheme, viewModel.SelectedMainTabIndex);
                themeService.SetPreference(AppThemeMode.Light);
                window.UpdateLayout();
                Assert.Equal(pageBeforeTheme, viewModel.SelectedMainTabIndex);

                // Collapse persistence: write settings, then verify store round-trip.
                viewModel.IsSidebarExpanded = false;
                window.UpdateLayout();
                Assert.Equal(MainViewModel.CollapsedSidebarWidth, viewModel.SidebarWidth);
                var settingsStore = services.GetRequiredService<UserSettingsStore>();
                Assert.False(settingsStore.Load().SidebarExpanded);

                viewModel.IsSidebarExpanded = true;
                window.UpdateLayout();
                Assert.Equal(MainViewModel.ExpandedSidebarWidth, viewModel.SidebarWidth);
                Assert.True(settingsStore.Load().SidebarExpanded);

                // Responsive workspace sizes: no toolbar ScrollViewer / horizontal overflow chrome.
                foreach (var (width, height) in new[] { (1280, 720), (1366, 768), (1920, 1080) })
                {
                    window.Width = width;
                    window.Height = height;
                    window.UpdateLayout();
                    Assert.True(window.ActualWidth > 0);
                    Assert.True(window.ActualHeight > 0);
                    Assert.Equal(pageBeforeTheme, viewModel.SelectedMainTabIndex);
                    Assert.False(ToolbarContainsScrollViewer(window),
                        $"Toolbar must not host a ScrollViewer at {width}x{height}.");
                }

                window.Close();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
            finally
            {
                try
                {
                    services?.GetService<MainViewModel>()?.Dispose();
                    services?.GetService<IThemeService>()?.Dispose();
                    services?.GetService<ILocalizationService>()?.Dispose();
                    services?.Dispose();
                }
                catch
                {
                    // ignore cleanup failures
                }

                try
                {
                    app?.Shutdown();
                }
                catch
                {
                    // ignore
                }

                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "STA smoke test timed out.");
        Assert.Null(caught);
    }

    [Fact]
    public void StartupDiagnostics_FormatsXamlParseLocation()
    {
        var inner = new InvalidOperationException("Cannot call MarkupExtension.ProvideValue because of a cyclical dependency.");
        var xaml = new System.Windows.Markup.XamlParseException("Markup error", 456, 61, inner);

        var user = ExplorerPilot.App.App.BuildUserFacingMessage(xaml);
        Assert.Contains("line 456", user, StringComparison.Ordinal);
        Assert.Contains("position 61", user, StringComparison.Ordinal);
        Assert.Contains("cyclical dependency", user, StringComparison.OrdinalIgnoreCase);

        var details = ExplorerPilot.App.App.FormatExceptionDetails(xaml);
        Assert.Contains("XamlParseException", details, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", details, StringComparison.Ordinal);
        Assert.Contains("line 456", details, StringComparison.Ordinal);
    }

    private static void MergeAppResources(Application app)
    {
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ExplorerPilot;component/Resources/Themes/LightTheme.xaml", UriKind.Absolute)
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ExplorerPilot;component/Resources/Controls.xaml", UriKind.Absolute)
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ExplorerPilot;component/Localization/Strings.en-US.xaml", UriKind.Absolute)
        });
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddProvider(NullLoggerProvider.Instance);
            builder.SetMinimumLevel(LogLevel.Warning);
        });
        // Isolate preferences so smoke tests never rewrite the developer's LocalAppData settings.
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            "ExplorerPilot-Tests",
            Guid.NewGuid().ToString("N"),
            "settings.json");
        services.AddSingleton(new UserSettingsStore(settingsPath));
        services.AddExplorerPilotInfrastructure();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IApplicationInfoService, ApplicationInfoService>();
        services.AddSingleton<IAboutDialogService, AboutDialogService>();
        services.AddSingleton<ISystemInformationDialogService, SystemInformationDialogService>();
        services.AddSingleton<IFileActionService, FileActionService>();
        services.AddSingleton<IReportExportOptionsDialogService, ReportExportOptionsDialogService>();
        services.AddSingleton<SearchTabViewModel>();
        services.AddSingleton<DuplicatesTabViewModel>();
        services.AddSingleton<TreemapTabViewModel>();
        services.AddSingleton<ComparisonTabViewModel>();
        services.AddSingleton<CleanupTabViewModel>();
        services.AddSingleton<TrendsTabViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ScheduledScansViewModel>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<AboutWindow>();
        services.AddTransient<SystemInformationViewModel>();
        services.AddTransient<SystemInformationWindow>();
        services.AddTransient<ReportExportOptionsViewModel>();
        services.AddTransient<ReportExportOptionsWindow>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }

    private static System.Windows.Controls.TabControl? FindTabControl(DependencyObject root)
    {
        if (root is System.Windows.Controls.TabControl tabControl)
        {
            return tabControl;
        }

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            var found = FindTabControl(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static System.Windows.Controls.ContentPresenter? FindHeaderContentPresenter(
        System.Windows.Controls.TabItem tab)
    {
        return FindVisualDescendant<System.Windows.Controls.ContentPresenter>(tab, presenter =>
        {
            // Header presenters bind ContentSource to Header; content site is the page body.
            return presenter.Content == tab.Header || ReferenceEquals(presenter.Content, tab.Header);
        });
    }

    private static T? FindVisualDescendant<T>(DependencyObject root, Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match && (predicate is null || predicate(match)))
            {
                return match;
            }

            var nested = FindVisualDescendant(child, predicate);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool ToolbarContainsScrollViewer(Window window)
    {
        if (window.Content is not System.Windows.Controls.Grid rootGrid
            || rootGrid.Children.Count == 0
            || rootGrid.Children[0] is not System.Windows.Controls.Border toolbar)
        {
            return false;
        }

        return FindVisualDescendant<System.Windows.Controls.ScrollViewer>(toolbar) is not null;
    }
}

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

                // Force tab content materialization where DataGrid headers live.
                if (FindTabControl(window) is { } tabs)
                {
                    Assert.True(tabs.Items.Count >= 12,
                        "Expected Dashboard, Drives, nine analysis tabs, and Scheduled Scans.");

                    for (var i = 0; i < tabs.Items.Count; i++)
                    {
                        var tab = Assert.IsType<System.Windows.Controls.TabItem>(tabs.Items[i]);
                        var headerText = tab.Header as string;
                        Assert.False(string.IsNullOrWhiteSpace(headerText),
                            $"Tab {i} Header must resolve to a non-empty localized string.");

                        tabs.SelectedIndex = i;
                        window.UpdateLayout();

                        // ContentSource="Header" must paint readable text in the header chrome.
                        var headerPresenter = FindHeaderContentPresenter(tab);
                        Assert.NotNull(headerPresenter);
                        Assert.True(headerPresenter!.ActualWidth > 0 && headerPresenter.ActualHeight > 0,
                            $"Tab '{headerText}' header presenter has no visible layout.");
                    }
                }
                else
                {
                    Assert.Fail("Workspace TabControl was not found.");
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
        services.AddSingleton<UserSettingsStore>();
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
}

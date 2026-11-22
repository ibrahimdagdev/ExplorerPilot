using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using ExplorerPilot.App.Diagnostics;
using ExplorerPilot.App.Localization;
using ExplorerPilot.App.Services;
using ExplorerPilot.App.Settings;
using ExplorerPilot.App.Theming;
using ExplorerPilot.App.ViewModels;
using ExplorerPilot.App.Views;
using ExplorerPilot.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private SplashWindow? _splash;
    private ILogger? _logger;
    private bool _fatalDialogShown;

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            AppDataPaths.EnsureCreated();

            // Build DI and apply theme before any window to avoid Dark bootstrap flash.
            var services = new ServiceCollection();
            ConfigureServices(services);
            _services = services.BuildServiceProvider();
            _logger = _services.GetRequiredService<ILoggerFactory>().CreateLogger("App");
            _services.GetRequiredService<IThemeService>().Initialize();

            _splash = new SplashWindow();
            _splash.Show();

            await Dispatcher.Yield(DispatcherPriority.Render);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);

            await RunStartupAsync(_splash).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _splash?.Close();
            _splash = null;

            LogStartupFailure(ex);

            var title = TryFindResource("Dialog_StartupFailedTitle") as string ?? "ExplorerPilot";
            var format = TryFindResource("Dialog_StartupFailedFormat") as string
                         ?? "ExplorerPilot failed to start.{0}{0}{1}";
            MessageBox.Show(
                string.Format(format, Environment.NewLine, BuildUserFacingMessage(ex)),
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    private async Task RunStartupAsync(SplashWindow splash)
    {
        if (_services is null)
        {
            throw new InvalidOperationException("Application services were not configured.");
        }

        var localization = _services.GetRequiredService<ILocalizationService>();
        localization.Initialize();
        splash.ApplyLocalization(localization);
        splash.SetStatus(localization.GetString("Splash_Status_ConfiguringServices"));
        await Dispatcher.Yield(DispatcherPriority.Background);

        splash.SetStatus(localization.GetString("Splash_Status_LoadingPreferences"));
        await Dispatcher.Yield(DispatcherPriority.Background);

        var logger = _services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        var themeService = _services.GetRequiredService<IThemeService>();
        themeService.RefreshOptionDisplayNames(localization.GetString);

        splash.SetStatus(localization.GetString("Splash_Status_PreparingWorkspace"));
        await Dispatcher.Yield(DispatcherPriority.Background);

        var viewModel = _services.GetRequiredService<MainViewModel>();
        var mainWindow = _services.GetRequiredService<MainWindow>();

        var discoveryProgress = new Progress<string>(splash.SetStatus);
        var discoveryTask = viewModel.DiscoverDrivesAsync(discoveryProgress);

        splash.SetStatus(localization.GetString("Splash_Status_Opening"));
        await Dispatcher.Yield(DispatcherPriority.Background);

        MainWindow = mainWindow;
        mainWindow.Show();
        mainWindow.Activate();

        await mainWindow.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        await Dispatcher.Yield(DispatcherPriority.Render);

        ShutdownMode = ShutdownMode.OnMainWindowClose;

        splash.Close();
        _splash = null;

        try
        {
            await discoveryTask.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background drive discovery failed after MainWindow was shown");
        }

        try
        {
            await _services.GetRequiredService<Core.Interfaces.IScheduledScanService>()
                .StartAsync()
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scheduled scan service failed to start");
        }
    }

    private void LogStartupFailure(Exception ex)
    {
        var details = FormatExceptionDetails(ex);
        System.Diagnostics.Debug.WriteLine(details);

        try
        {
            var logger = _services?.GetService<ILoggerFactory>()?.CreateLogger("Startup") ?? _logger;
            logger?.LogCritical(ex, "ExplorerPilot failed to start. {Details}", details);
        }
        catch
        {
            // Logging must never mask the original startup failure.
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled UI exception");
        e.Handled = true;
        ShowRecoverableError(e.Exception);
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _logger?.LogCritical(ex, "Unhandled domain exception (IsTerminating={IsTerminating})", e.IsTerminating);
            if (!e.IsTerminating)
            {
                ShowRecoverableError(ex);
            }
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unobserved task exception");
        e.SetObserved();
        // Avoid modal spam from background faults; log only unless on UI thread with no active dialog.
    }

    private void ShowRecoverableError(Exception ex)
    {
        if (_fatalDialogShown)
        {
            return;
        }

        try
        {
            _fatalDialogShown = true;
            var title = TryFindResource("Dialog_UnexpectedErrorTitle") as string ?? "ExplorerPilot";
            var format = TryFindResource("Dialog_UnexpectedErrorFormat") as string
                         ?? "An unexpected error occurred.{0}{0}{1}{0}{0}The application will try to continue.";
            MessageBox.Show(
                string.Format(format, Environment.NewLine, BuildUserFacingMessage(ex)),
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _fatalDialogShown = false;
        }
    }

    internal static string BuildUserFacingMessage(Exception ex)
    {
        if (ex is XamlParseException xaml)
        {
            var location = xaml.LineNumber > 0
                ? $" (line {xaml.LineNumber}, position {xaml.LinePosition})"
                : string.Empty;
            var root = GetInnermostMessage(xaml);
            return $"The user interface failed to load{location}.{Environment.NewLine}{root}";
        }

        return GetInnermostMessage(ex);
    }

    internal static string FormatExceptionDetails(Exception ex)
    {
        var sb = new StringBuilder();
        var depth = 0;
        for (var current = ex; current is not null; current = current.InnerException, depth++)
        {
            sb.Append('[').Append(depth).Append("] ")
                .Append(current.GetType().FullName)
                .Append(": ")
                .AppendLine(current.Message);

            if (current is XamlParseException xaml)
            {
                sb.Append("    XAML line ").Append(xaml.LineNumber)
                    .Append(", position ").Append(xaml.LinePosition)
                    .AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(current.StackTrace))
            {
                sb.AppendLine(current.StackTrace);
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string GetInnermostMessage(Exception ex)
    {
        var current = ex;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.AddProvider(new FileLoggerProvider(AppDataPaths.LogsDirectory, LogLevel.Information));
#if DEBUG
            builder.SetMinimumLevel(LogLevel.Debug);
#else
            builder.SetMinimumLevel(LogLevel.Information);
#endif
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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
        {
            try
            {
                // Run stop off the UI thread so a cancelled scheduled scan whose
                // continuations need the dispatcher cannot deadlock OnExit.
                var scheduler = _services.GetService<Core.Interfaces.IScheduledScanService>();
                if (scheduler is not null)
                {
                    var stop = Task.Run(() => scheduler.StopAsync());
                    if (!stop.Wait(TimeSpan.FromSeconds(10)))
                    {
                        _logger?.LogWarning("Scheduled scan service stop timed out during exit");
                    }
                }
            }
            catch
            {
                // ignore scheduler shutdown failures
            }

            try
            {
                _services.GetService<MainViewModel>()?.Dispose();
            }
            catch
            {
                // ignore dispose failures on shutdown
            }

            _services.GetService<IThemeService>()?.Dispose();
            _services.GetService<ILocalizationService>()?.Dispose();
            _services.Dispose();
        }

        base.OnExit(e);
    }
}

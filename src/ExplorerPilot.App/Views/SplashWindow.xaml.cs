using System.Reflection;
using System.Windows;
using ExplorerPilot.App.Localization;

namespace ExplorerPilot.App.Views;

public partial class SplashWindow : Window
{
    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(
            nameof(StatusText),
            typeof(string),
            typeof(SplashWindow),
            new PropertyMetadata("Starting…"));

    private readonly string _versionLabel;

    public SplashWindow()
    {
        InitializeComponent();
        _versionLabel = ResolveVersionLabel();
        VersionTextBlock.Text = _versionLabel;
    }

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public void ApplyLocalization(ILocalizationService localization)
    {
        void Apply()
        {
            TitleText.Text = localization.GetString("App_Name");
            SubtitleText.Text = localization.GetString("App_Subtitle");
            VersionTextBlock.Text = localization.GetString("Splash_VersionFormat", _versionLabel);
            Title = localization.GetString("App_Name");
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(Apply);
            return;
        }

        Apply();
    }

    public void SetStatus(string status)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => StatusText = status);
            return;
        }

        StatusText = status;
    }

    private static string ResolveVersionLabel()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            if (plus > 0)
            {
                informational = informational[..plus];
            }

            return "v" + informational.Trim();
        }

        var version = assembly.GetName().Version;
        return version is null ? "v0.1.0" : "v" + version.ToString(3);
    }
}

namespace ExplorerPilot.App.Theming;

public interface IThemeService : IDisposable
{
    AppThemeMode Preference { get; }

    /// <summary>Resolved Light or Dark after applying System preference.</summary>
    AppThemeMode EffectiveTheme { get; }

    IReadOnlyList<ThemeOption> AvailableThemes { get; }

    void Initialize();

    void SetPreference(AppThemeMode mode);

    void RefreshOptionDisplayNames(Func<string, string> localize);

    event EventHandler? ThemeChanged;
}

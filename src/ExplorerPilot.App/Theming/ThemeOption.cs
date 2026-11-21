using CommunityToolkit.Mvvm.ComponentModel;

namespace ExplorerPilot.App.Theming;

public sealed partial class ThemeOption : ObservableObject
{
    public ThemeOption(AppThemeMode mode, string displayName)
    {
        Mode = mode;
        _displayName = displayName;
    }

    public AppThemeMode Mode { get; }

    [ObservableProperty]
    private string _displayName;

    public override string ToString() => DisplayName;
}

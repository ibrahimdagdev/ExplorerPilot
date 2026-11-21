using CommunityToolkit.Mvvm.ComponentModel;

namespace ExplorerPilot.App.Localization;

public sealed partial class LanguageOption : ObservableObject
{
    public LanguageOption(AppLanguageMode mode, string displayName)
    {
        Mode = mode;
        _displayName = displayName;
    }

    public AppLanguageMode Mode { get; }

    [ObservableProperty]
    private string _displayName;

    public override string ToString() => DisplayName;
}

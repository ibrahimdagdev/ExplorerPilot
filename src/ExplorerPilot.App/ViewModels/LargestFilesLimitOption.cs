using CommunityToolkit.Mvvm.ComponentModel;
using ExplorerPilot.App.Localization;

namespace ExplorerPilot.App.ViewModels;

public sealed partial class LargestFilesLimitOption : ObservableObject
{
    public LargestFilesLimitOption(int limit)
    {
        Limit = limit;
    }

    public int Limit { get; }

    [ObservableProperty]
    private string _displayName = string.Empty;

    public void RefreshDisplayName(ILocalizationService localization) =>
        DisplayName = localization.GetString("LargestFiles_TopFormat", Limit);
}

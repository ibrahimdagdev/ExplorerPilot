using CommunityToolkit.Mvvm.ComponentModel;

namespace ExplorerPilot.App.ViewModels;

public sealed partial class SearchOptionItem<T> : ObservableObject
{
    public SearchOptionItem(T value)
    {
        Value = value;
    }

    public T Value { get; }

    [ObservableProperty]
    private string _displayName = string.Empty;
}

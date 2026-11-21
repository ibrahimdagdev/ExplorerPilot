using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ExplorerPilot.App.ViewModels;

public sealed partial class SystemInfoSectionViewModel : ObservableObject
{
    public SystemInfoSectionViewModel(string title)
    {
        Title = title;
    }

    [ObservableProperty]
    private string _title;

    public ObservableCollection<SystemInfoRowViewModel> Rows { get; } = [];
}

public sealed partial class SystemInfoRowViewModel : ObservableObject
{
    public SystemInfoRowViewModel(string label, string value)
    {
        Label = label;
        Value = value;
    }

    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private string _value;
}

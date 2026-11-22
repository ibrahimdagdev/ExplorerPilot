using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ExplorerPilot.App.ViewModels;

/// <summary>One selectable workspace page in the left sidebar (maps 1:1 to TabControl index).</summary>
public sealed partial class SidebarNavItemViewModel : ObservableObject
{
    public SidebarNavItemViewModel(int pageIndex, string titleKey, Geometry icon)
    {
        PageIndex = pageIndex;
        TitleKey = titleKey;
        Icon = icon;
        Title = titleKey;
    }

    public int PageIndex { get; }

    public string TitleKey { get; }

    public Geometry Icon { get; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class SidebarNavSectionViewModel : ObservableObject
{
    public SidebarNavSectionViewModel(string titleKey, IEnumerable<SidebarNavItemViewModel> items)
    {
        TitleKey = titleKey;
        Title = titleKey;
        Items = new ObservableCollection<SidebarNavItemViewModel>(items);
    }

    public string TitleKey { get; }

    [ObservableProperty]
    private string _title = string.Empty;

    public ObservableCollection<SidebarNavItemViewModel> Items { get; }
}

/// <summary>Lucide-inspired 24×24 path geometries for sidebar icons.</summary>
public static class SidebarNavIcons
{
    public static Geometry Dashboard { get; } = Freeze("M3,3 H10 V10 H3 Z M14,3 H21 V10 H14 Z M3,14 H10 V21 H3 Z M14,14 H21 V21 H14 Z");
    public static Geometry Drives { get; } = Freeze("M4,6 H20 V10 H4 Z M4,14 H20 V18 H4 Z M7,8 H8 M7,16 H8");
    public static Geometry Explorer { get; } = Freeze("M3,7 H9 L11,9 H21 V19 H3 Z");
    public static Geometry LargestFiles { get; } = Freeze("M7,3 H14 L19,8 V21 H7 Z M14,3 V8 H19 M9,13 H17 M9,17 H15");
    public static Geometry FileTypes { get; } = Freeze("M4,6 H12 V12 H4 Z M12,12 H20 V18 H12 Z M4,12 H12 V18 H4 Z");
    public static Geometry Search { get; } = Freeze("M11,4 C7.1,4 4,7.1 4,11 C4,14.9 7.1,18 11,18 C12.9,18 14.6,17.2 15.8,16 L20,20.2 L21.4,18.8 L17.2,14.6 C18.1,13.5 18.6,12.3 18.6,11 C18.6,7.1 15.5,4 11.6,4 Z M11,6 C14.3,6 16.6,8.3 16.6,11.6 C16.6,14.9 14.3,17.2 11,17.2 C7.7,17.2 5.4,14.9 5.4,11.6 C5.4,8.3 7.7,6 11,6 Z");
    public static Geometry Duplicates { get; } = Freeze("M8,6 H20 V18 H8 Z M4,10 H8 V22 H16 V18");
    public static Geometry Treemap { get; } = Freeze("M3,3 H21 V21 H3 Z M3,10 H21 M10,10 V21 M10,3 V10");
    public static Geometry Comparison { get; } = Freeze("M8,4 V20 M16,4 V20 M5,8 L8,5 L11,8 M13,16 L16,19 L19,16");
    public static Geometry Trends { get; } = Freeze("M3,17 L9,11 L13,15 L21,7 M15,7 H21 V13");
    public static Geometry Scheduled { get; } = Freeze("M6,4 V6 M18,4 V6 M4,8 H20 M5,5 H19 V20 H5 Z M9,12 H10 M14,12 H15 M9,16 H10");
    public static Geometry Cleanup { get; } = Freeze("M5,8 H19 M9,8 V5 H15 V8 M7,8 V19 H17 V8 M10,12 V16 M14,12 V16");

    private static Geometry Freeze(string data)
    {
        var geometry = Geometry.Parse(data);
        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        return geometry;
    }
}

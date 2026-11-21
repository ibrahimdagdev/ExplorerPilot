using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ExplorerPilot.App.Converters;
using ExplorerPilot.Core.Models;

namespace ExplorerPilot.App.ViewModels;

public sealed partial class DuplicateGroupRowViewModel : ObservableObject
{
    public DuplicateGroupRowViewModel(DuplicateGroup group)
    {
        Group = group;
        ContentHashSha256 = group.ContentHashSha256;
        SizeBytes = group.SizeBytes;
        DuplicateCount = group.DuplicateCount;
        PotentialSavingsBytes = group.PotentialSavingsBytes;
        RepresentativeName = group.Files.Count > 0 ? group.Files[0].Name : string.Empty;

        foreach (var file in group.Files)
        {
            Files.Add(new DuplicateFileRowViewModel(file));
        }
    }

    public DuplicateGroup Group { get; }

    public string ContentHashSha256 { get; }

    public long SizeBytes { get; }

    public int DuplicateCount { get; }

    public long PotentialSavingsBytes { get; }

    public string RepresentativeName { get; }

    public ObservableCollection<DuplicateFileRowViewModel> Files { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private string _savingsText = string.Empty;

    [ObservableProperty]
    private string _countText = string.Empty;

    [ObservableProperty]
    private string _headerText = string.Empty;

    [ObservableProperty]
    private DuplicateFileRowViewModel? _selectedFile;

    public void RefreshFormatting(CultureInfo culture, Func<string, string> localize)
    {
        var na = localize("EmDash");
        SizeText = ByteSizeConverter.Format(SizeBytes, culture);
        SavingsText = ByteSizeConverter.Format(PotentialSavingsBytes, culture);
        CountText = DuplicateCount.ToString("N0", culture);
        HeaderText = string.Format(
            culture,
            localize("Duplicates_GroupHeaderFormat"),
            RepresentativeName,
            CountText,
            SizeText,
            SavingsText);

        foreach (var file in Files)
        {
            file.RefreshFormatting(culture, na);
        }
    }
}

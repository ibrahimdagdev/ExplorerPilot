using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using ExplorerPilot.App.Converters;
using ExplorerPilot.Core.Models;

namespace ExplorerPilot.App.ViewModels;

public sealed partial class LargestFileRowViewModel : ObservableObject
{
    private readonly string _rawExtension;

    public LargestFileRowViewModel(LargestFileInfo file)
    {
        File = file;
        Name = file.Name;
        FullPath = file.FullPath;
        SizeBytes = file.SizeBytes;
        _rawExtension = file.Extension;
        ParentDirectory = Path.GetDirectoryName(file.FullPath) ?? string.Empty;
        LastModifiedUtc = file.LastModifiedUtc;
    }

    public LargestFileInfo File { get; }

    public string Name { get; }

    public string FullPath { get; }

    public long SizeBytes { get; }

    public string ParentDirectory { get; }

    public DateTimeOffset? LastModifiedUtc { get; }

    [ObservableProperty]
    private string _extension = string.Empty;

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private string _modifiedText = string.Empty;

    public void RefreshFormatting(CultureInfo culture, string notAvailable)
    {
        Extension = string.IsNullOrEmpty(_rawExtension) ? notAvailable : _rawExtension;
        SizeText = ByteSizeConverter.Format(SizeBytes, culture);
        ModifiedText = LastModifiedUtc is { } stamp
            ? stamp.ToLocalTime().ToString("g", culture)
            : notAvailable;
    }
}

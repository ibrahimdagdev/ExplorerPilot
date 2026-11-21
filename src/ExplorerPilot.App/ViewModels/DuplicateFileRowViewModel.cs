using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using ExplorerPilot.App.Converters;
using ExplorerPilot.Core.Models;

namespace ExplorerPilot.App.ViewModels;

public sealed partial class DuplicateFileRowViewModel : ObservableObject
{
    public DuplicateFileRowViewModel(DuplicateFileInfo file)
    {
        File = file;
        Name = file.Name;
        FullPath = file.FullPath;
        SizeBytes = file.SizeBytes;
        LastModified = file.LastModified;
        ParentDirectory = Path.GetDirectoryName(file.FullPath) ?? string.Empty;
    }

    public DuplicateFileInfo File { get; }

    public string Name { get; }

    public string FullPath { get; }

    public long SizeBytes { get; }

    public DateTimeOffset? LastModified { get; }

    public string ParentDirectory { get; }

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private string _modifiedText = string.Empty;

    public void RefreshFormatting(CultureInfo culture, string notAvailable)
    {
        SizeText = ByteSizeConverter.Format(SizeBytes, culture);
        ModifiedText = LastModified is { } stamp
            ? stamp.ToLocalTime().ToString("g", culture)
            : notAvailable;
    }
}

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ExplorerPilot.App.Converters;
using ExplorerPilot.Core.Models;

namespace ExplorerPilot.App.ViewModels;

public sealed partial class SearchResultRowViewModel : ObservableObject
{
    public SearchResultRowViewModel(FileSearchHit hit)
    {
        Hit = hit;
        Name = hit.Name;
        FullPath = hit.FullPath;
        IsDirectory = hit.IsDirectory;
        SizeBytes = hit.SizeBytes;
        ParentPath = hit.ParentPath;
        LastModifiedUtc = hit.LastModifiedUtc;
        Extension = hit.Extension;
    }

    public FileSearchHit Hit { get; }

    public string Name { get; }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    public long SizeBytes { get; }

    public string ParentPath { get; }

    public DateTimeOffset? LastModifiedUtc { get; }

    public string Extension { get; }

    [ObservableProperty]
    private string _typeText = string.Empty;

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private string _modifiedText = string.Empty;

    public void RefreshFormatting(CultureInfo culture, string fileLabel, string folderLabel, string dash)
    {
        TypeText = IsDirectory ? folderLabel : fileLabel;
        SizeText = ByteSizeConverter.Format(SizeBytes, culture);
        ModifiedText = LastModifiedUtc is { } stamp
            ? stamp.ToLocalTime().ToString("g", culture)
            : dash;
    }
}

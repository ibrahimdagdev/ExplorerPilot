using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ExplorerPilot.App.Converters;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;

namespace ExplorerPilot.App.ViewModels;

public sealed partial class FileExtensionRowViewModel : ObservableObject
{
    public FileExtensionRowViewModel(FileExtensionStatistics stats)
    {
        Stats = stats;
        ExtensionKey = stats.Extension;
        Category = stats.Category;
        FileCount = stats.FileCount;
        TotalSizeBytes = stats.TotalSizeBytes;
        AverageSizeBytes = stats.AverageSizeBytes;
        Percentage = stats.PercentageOfTotalBytes;
    }

    public FileExtensionStatistics Stats { get; }

    /// <summary>Normalized extension key (empty = no extension).</summary>
    public string ExtensionKey { get; }

    public FileCategory Category { get; }

    public long FileCount { get; }

    public long TotalSizeBytes { get; }

    public double AverageSizeBytes { get; }

    public double Percentage { get; }

    [ObservableProperty]
    private string _extensionText = string.Empty;

    [ObservableProperty]
    private string _categoryName = string.Empty;

    [ObservableProperty]
    private string _fileCountText = string.Empty;

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private string _averageSizeText = string.Empty;

    [ObservableProperty]
    private string _percentageText = string.Empty;

    public void RefreshFormatting(
        CultureInfo culture,
        string noExtensionLabel,
        Func<FileCategory, string> categoryNameResolver)
    {
        ExtensionText = string.IsNullOrEmpty(ExtensionKey) ? noExtensionLabel : ExtensionKey;
        CategoryName = categoryNameResolver(Category);
        FileCountText = FileCount.ToString("N0", culture);
        SizeText = ByteSizeConverter.Format(TotalSizeBytes, culture);
        AverageSizeText = ByteSizeConverter.Format((long)Math.Round(AverageSizeBytes), culture);
        PercentageText = PercentageConverter.Format(Percentage, culture);
    }

    public bool MatchesFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var needle = filter.Trim();
        return ExtensionText.Contains(needle, StringComparison.OrdinalIgnoreCase)
               || ExtensionKey.Contains(needle, StringComparison.OrdinalIgnoreCase)
               || CategoryName.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}

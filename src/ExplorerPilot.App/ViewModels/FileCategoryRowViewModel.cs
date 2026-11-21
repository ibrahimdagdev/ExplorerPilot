using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ExplorerPilot.App.Converters;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;

namespace ExplorerPilot.App.ViewModels;

public sealed partial class FileCategoryRowViewModel : ObservableObject
{
    public FileCategoryRowViewModel(FileCategoryStatistics stats)
    {
        Stats = stats;
        Category = stats.Category;
        FileCount = stats.FileCount;
        TotalSizeBytes = stats.TotalSizeBytes;
        Percentage = stats.PercentageOfTotalBytes;
        UsageRatio = Math.Clamp(stats.PercentageOfTotalBytes / 100d, 0d, 1d);
    }

    public FileCategoryStatistics Stats { get; }

    public FileCategory Category { get; }

    public long FileCount { get; }

    public long TotalSizeBytes { get; }

    public double Percentage { get; }

    public double UsageRatio { get; }

    [ObservableProperty]
    private string _categoryName = string.Empty;

    [ObservableProperty]
    private string _fileCountText = string.Empty;

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private string _percentageText = string.Empty;

    public void RefreshFormatting(CultureInfo culture, Func<FileCategory, string> categoryNameResolver)
    {
        CategoryName = categoryNameResolver(Category);
        FileCountText = FileCount.ToString("N0", culture);
        SizeText = ByteSizeConverter.Format(TotalSizeBytes, culture);
        PercentageText = PercentageConverter.Format(Percentage, culture);
    }
}

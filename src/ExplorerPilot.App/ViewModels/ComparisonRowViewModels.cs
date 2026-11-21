using ExplorerPilot.App.Converters;
using ExplorerPilot.Core.Snapshots;

namespace ExplorerPilot.App.ViewModels;

public enum ComparisonResultFilter
{
    All = 0,
    Added = 1,
    Removed = 2,
    Changed = 3,
    Grown = 4,
    Shrunk = 5
}

public enum ComparisonSortMode
{
    AbsDeltaDesc = 0,
    AbsDeltaAsc = 1,
    PathAsc = 2,
    NameAsc = 3
}

public sealed class SnapshotListItemViewModel
{
    public SnapshotListItemViewModel(SnapshotInfo info, string sizeText, string createdText, string statusText)
    {
        Info = info;
        SizeText = sizeText;
        CreatedText = createdText;
        StatusText = statusText;
    }

    public SnapshotInfo Info { get; }

    public Guid Id => Info.Id;

    public string DisplayName => Info.DisplayName;

    public string RootPath => Info.RootPath;

    public string SizeText { get; }

    public string CreatedText { get; }

    public string StatusText { get; }
}

public sealed class FolderChangeRowViewModel
{
    public FolderChangeRowViewModel(FolderSizeChange change, string leftText, string rightText, string deltaText, bool isGrowth)
    {
        RelativePath = change.RelativePath;
        Name = string.IsNullOrEmpty(change.RelativePath) ? change.Name : change.RelativePath;
        LeftText = leftText;
        RightText = rightText;
        DeltaText = deltaText;
        IsGrowth = isGrowth;
        DeltaBytes = change.DeltaBytes;
    }

    public string RelativePath { get; }

    public string Name { get; }

    public string LeftText { get; }

    public string RightText { get; }

    public string DeltaText { get; }

    public bool IsGrowth { get; }

    public long DeltaBytes { get; }
}

public sealed class FileChangeRowViewModel
{
    public FileChangeRowViewModel(
        string relativePath,
        string name,
        string sizeText,
        string deltaText,
        long deltaBytes,
        string kind)
    {
        RelativePath = relativePath;
        Name = name;
        SizeText = sizeText;
        DeltaText = deltaText;
        DeltaBytes = deltaBytes;
        Kind = kind;
    }

    public string RelativePath { get; }

    public string Name { get; }

    public string SizeText { get; }

    public string DeltaText { get; }

    public long DeltaBytes { get; }

    public string Kind { get; }

    public bool IsPositive => DeltaBytes > 0;

    public bool IsNegative => DeltaBytes < 0;
}

public sealed class StatChangeRowViewModel
{
    public StatChangeRowViewModel(string label, string leftText, string rightText, string deltaText, long deltaBytes)
    {
        Label = label;
        LeftText = leftText;
        RightText = rightText;
        DeltaText = deltaText;
        DeltaBytes = deltaBytes;
    }

    public string Label { get; }

    public string LeftText { get; }

    public string RightText { get; }

    public string DeltaText { get; }

    public long DeltaBytes { get; }

    public bool IsPositive => DeltaBytes > 0;

    public bool IsNegative => DeltaBytes < 0;
}

public static class SnapshotDeltaFormatter
{
    public static string FormatSignedBytes(long delta, System.Globalization.CultureInfo culture)
    {
        var abs = ByteSizeConverter.Format(SnapshotMath.AbsClamped(delta), culture);
        if (delta > 0)
        {
            return "+" + abs;
        }

        if (delta < 0)
        {
            return "-" + abs;
        }

        return abs;
    }
}

public sealed class ComparisonFilterOption
{
    public ComparisonFilterOption(ComparisonResultFilter filter, string displayName)
    {
        Filter = filter;
        DisplayName = displayName;
    }

    public ComparisonResultFilter Filter { get; }

    public string DisplayName { get; set; }
}

public sealed class ComparisonSortOption
{
    public ComparisonSortOption(ComparisonSortMode mode, string displayName)
    {
        Mode = mode;
        DisplayName = displayName;
    }

    public ComparisonSortMode Mode { get; }

    public string DisplayName { get; set; }
}

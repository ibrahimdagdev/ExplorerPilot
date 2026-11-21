using CommunityToolkit.Mvvm.ComponentModel;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;

namespace ExplorerPilot.App.ViewModels;

public sealed class CleanupCandidateRowViewModel : ObservableObject
{
    public CleanupCandidateRowViewModel(
        CleanupCandidate candidate,
        string categoryText,
        string riskText,
        string sizeText,
        string savingsText,
        string modifiedText,
        string reasonText)
    {
        Candidate = candidate;
        CategoryText = categoryText;
        RiskText = riskText;
        SizeText = sizeText;
        SavingsText = savingsText;
        ModifiedText = modifiedText;
        ReasonText = reasonText;
    }

    public CleanupCandidate Candidate { get; }

    public string Id => Candidate.Id;

    public string Name => Candidate.Name;

    public string FullPath => Candidate.FullPath;

    public string PathKey => Candidate.PathKey;

    public bool IsDirectory => Candidate.IsDirectory;

    public long SizeBytes => Candidate.SizeBytes;

    public long PotentialSavingsBytes => Candidate.PotentialSavingsBytes;

    public CleanupCategory Category => Candidate.Category;

    public CleanupRiskLevel Risk => Candidate.Risk;

    public string CategoryText { get; }

    public string RiskText { get; }

    public string SizeText { get; }

    public string SavingsText { get; }

    public string ModifiedText { get; }

    public string ReasonText { get; }

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? SelectionChanged;
}

public sealed class CleanupFilterOption<T>
{
    public CleanupFilterOption(T value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public T Value { get; }

    public string DisplayName { get; set; }
}

/// <summary>Sentinel for "all categories / all risks" filters.</summary>
public enum CleanupCategoryFilter
{
    All = -1,
    LargeFiles = CleanupCategory.LargeFiles,
    OldFiles = CleanupCategory.OldFiles,
    TemporaryFiles = CleanupCategory.TemporaryFiles,
    OldLogFiles = CleanupCategory.OldLogFiles,
    EmptyFolders = CleanupCategory.EmptyFolders,
    VerifiedDuplicates = CleanupCategory.VerifiedDuplicates
}

public enum CleanupRiskFilter
{
    All = -1,
    Low = CleanupRiskLevel.Low,
    Medium = CleanupRiskLevel.Medium,
    High = CleanupRiskLevel.High
}

public enum CleanupSortMode
{
    PotentialDesc = 0,
    SizeDesc = 1,
    ModifiedDesc = 2,
    RiskDesc = 3,
    NameAsc = 4
}

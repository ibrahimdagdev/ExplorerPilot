using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplorerPilot.App.Localization;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ExplorerPilot.App.ViewModels;

public sealed partial class ScheduledScansViewModel : ObservableObject
{
    private readonly IScheduledScanService _service;
    private readonly ILocalizationService _localization;
    private readonly ILogger<ScheduledScansViewModel> _logger;

    public ScheduledScansViewModel(
        IScheduledScanService service,
        ILocalizationService localization,
        ILogger<ScheduledScansViewModel> logger)
    {
        _service = service;
        _localization = localization;
        _logger = logger;
        FrequencyOptions =
        [
            new FrequencyOption(ScheduledScanFrequency.Daily, "Scheduled_Frequency_Daily"),
            new FrequencyOption(ScheduledScanFrequency.Weekly, "Scheduled_Frequency_Weekly"),
            new FrequencyOption(ScheduledScanFrequency.Monthly, "Scheduled_Frequency_Monthly"),
            new FrequencyOption(ScheduledScanFrequency.Manual, "Scheduled_Frequency_Manual")
        ];
        WeekdayOptions = Enum.GetValues<DayOfWeek>()
            .Select(d => new WeekdayOption(d, $"Scheduled_Weekday_{d}"))
            .ToList();
        SelectedFrequency = FrequencyOptions[0];
        SelectedWeekday = WeekdayOptions.First(w => w.Day == DayOfWeek.Monday);
        _service.PlansChanged += OnPlansChanged;
        RefreshEditorLabels();
        ReloadPlans();
    }

    public ObservableCollection<ScheduledScanRowViewModel> Plans { get; } = [];

    public IReadOnlyList<FrequencyOption> FrequencyOptions { get; }

    public IReadOnlyList<WeekdayOption> WeekdayOptions { get; }

    [ObservableProperty]
    private ScheduledScanRowViewModel? _selectedPlan;

    [ObservableProperty]
    private string _editorName = string.Empty;

    [ObservableProperty]
    private string _editorTargetPath = string.Empty;

    [ObservableProperty]
    private FrequencyOption? _selectedFrequency;

    [ObservableProperty]
    private WeekdayOption? _selectedWeekday;

    [ObservableProperty]
    private int _monthlyDay = 1;

    [ObservableProperty]
    private string _timeText = "09:00";

    [ObservableProperty]
    private bool _editorEnabled = true;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _missedBanner = string.Empty;

    [ObservableProperty]
    private bool _hasMissedPlans;

    public void RefreshLocalization()
    {
        RefreshEditorLabels();
        ReloadPlans();
    }

    private void RefreshEditorLabels()
    {
        foreach (var option in FrequencyOptions)
        {
            option.DisplayName = _localization.GetString(option.ResourceKey);
        }

        foreach (var option in WeekdayOptions)
        {
            option.DisplayName = _localization.GetString(option.ResourceKey);
        }
    }

    private void OnPlansChanged(object? sender, EventArgs e) =>
        System.Windows.Application.Current?.Dispatcher.Invoke(ReloadPlans);

    private void ReloadPlans()
    {
        var selectedId = SelectedPlan?.Id;
        Plans.Clear();
        var culture = _localization.CurrentCulture;
        var anyMissed = false;
        foreach (var plan in _service.Plans.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var row = new ScheduledScanRowViewModel(plan, _localization, culture);
            Plans.Add(row);
            anyMissed |= plan.MissedWhileClosed;
        }

        HasMissedPlans = anyMissed;
        MissedBanner = anyMissed
            ? _localization.GetString("Scheduled_MissedWhileClosed")
            : string.Empty;

        SelectedPlan = selectedId is { } id
            ? Plans.FirstOrDefault(p => p.Id == id)
            : Plans.FirstOrDefault();
        StatusText = _localization.GetString("Scheduled_PlanCountFormat", Plans.Count);
    }

    partial void OnSelectedPlanChanged(ScheduledScanRowViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        EditorName = value.Name;
        EditorTargetPath = value.TargetPath;
        SelectedFrequency = FrequencyOptions.FirstOrDefault(f => f.Frequency == value.Frequency)
                            ?? FrequencyOptions[0];
        SelectedWeekday = WeekdayOptions.FirstOrDefault(w => w.Day == value.WeeklyDay)
                          ?? WeekdayOptions[0];
        MonthlyDay = value.MonthlyDay;
        TimeText = value.LocalTimeOfDay.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        EditorEnabled = value.IsEnabled;
    }

    [RelayCommand]
    private void NewPlan()
    {
        SelectedPlan = null;
        EditorName = _localization.GetString("Scheduled_DefaultName");
        EditorTargetPath = string.Empty;
        SelectedFrequency = FrequencyOptions[0];
        SelectedWeekday = WeekdayOptions.First(w => w.Day == DayOfWeek.Monday);
        MonthlyDay = 1;
        TimeText = "09:00";
        EditorEnabled = true;
    }

    [RelayCommand]
    private void BrowseTarget()
    {
        var dialog = new OpenFolderDialog
        {
            Title = _localization.GetString("Scheduled_BrowseTitle")
        };
        if (dialog.ShowDialog() == true)
        {
            EditorTargetPath = dialog.FolderName;
        }
    }

    [RelayCommand]
    private async Task SavePlanAsync()
    {
        try
        {
            if (!TryBuildDefinition(SelectedPlan?.Id ?? Guid.NewGuid(), out var plan, out var error))
            {
                StatusText = error!;
                return;
            }

            if (SelectedPlan is null)
            {
                await _service.AddAsync(plan).ConfigureAwait(true);
            }
            else
            {
                await _service.UpdateAsync(plan).ConfigureAwait(true);
            }

            StatusText = _localization.GetString("Scheduled_Saved");
            ReloadPlans();
            SelectedPlan = Plans.FirstOrDefault(p => p.Id == plan.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save schedule plan");
            StatusText = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeletePlanAsync()
    {
        if (SelectedPlan is null)
        {
            return;
        }

        await _service.DeleteAsync(SelectedPlan.Id).ConfigureAwait(true);
        StatusText = _localization.GetString("Scheduled_Deleted");
        NewPlan();
        ReloadPlans();
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync()
    {
        if (SelectedPlan is null)
        {
            return;
        }

        await _service.SetEnabledAsync(SelectedPlan.Id, !SelectedPlan.IsEnabled).ConfigureAwait(true);
        ReloadPlans();
    }

    [RelayCommand]
    private async Task RunNowAsync()
    {
        if (SelectedPlan is null)
        {
            return;
        }

        StatusText = _localization.GetString("Scheduled_Running");
        var result = await _service.RunNowAsync(SelectedPlan.Id).ConfigureAwait(true);
        StatusText = result.Outcome switch
        {
            ScheduledScanRunOutcome.Succeeded => _localization.GetString("Scheduled_RunSucceeded"),
            ScheduledScanRunOutcome.SnapshotFailed => _localization.GetString("Scheduled_SnapshotFailed"),
            ScheduledScanRunOutcome.SkippedBusy => _localization.GetString("Scheduled_SkippedBusy"),
            ScheduledScanRunOutcome.Cancelled => _localization.GetString("Scheduled_RunCancelled"),
            ScheduledScanRunOutcome.Incomplete => _localization.GetString("Scheduled_RunIncomplete"),
            _ => result.ErrorMessage ?? _localization.GetString("Scheduled_RunFailed")
        };
        ReloadPlans();
    }

    [RelayCommand]
    private async Task CancelRunAsync() => await _service.CancelActiveAsync().ConfigureAwait(true);

    private bool TryBuildDefinition(Guid id, out ScheduledScanDefinition plan, out string? error)
    {
        plan = new ScheduledScanDefinition { Id = id };
        error = null;

        if (string.IsNullOrWhiteSpace(EditorName))
        {
            error = _localization.GetString("Scheduled_NameRequired");
            return false;
        }

        if (!ScheduledScanPathValidator.TryNormalize(EditorTargetPath, out var path, out _))
        {
            error = _localization.GetString("Scheduled_PathInvalid");
            return false;
        }

        if (!TimeSpan.TryParseExact(TimeText.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var tod)
            && !TimeSpan.TryParse(TimeText.Trim(), CultureInfo.CurrentCulture, out tod))
        {
            error = _localization.GetString("Scheduled_TimeInvalid");
            return false;
        }

        plan.Name = EditorName.Trim();
        plan.TargetPath = path;
        plan.Frequency = SelectedFrequency?.Frequency ?? ScheduledScanFrequency.Daily;
        plan.IsEnabled = EditorEnabled;
        plan.LocalTimeOfDay = tod;
        plan.WeeklyDay = SelectedWeekday?.Day ?? DayOfWeek.Monday;
        plan.MonthlyDay = Math.Clamp(MonthlyDay, 1, 31);
        return true;
    }
}

public sealed partial class FrequencyOption : ObservableObject
{
    public FrequencyOption(ScheduledScanFrequency frequency, string resourceKey)
    {
        Frequency = frequency;
        ResourceKey = resourceKey;
        DisplayName = resourceKey;
    }

    public ScheduledScanFrequency Frequency { get; }

    public string ResourceKey { get; }

    [ObservableProperty]
    private string _displayName = string.Empty;
}

public sealed partial class WeekdayOption : ObservableObject
{
    public WeekdayOption(DayOfWeek day, string resourceKey)
    {
        Day = day;
        ResourceKey = resourceKey;
        DisplayName = resourceKey;
    }

    public DayOfWeek Day { get; }

    public string ResourceKey { get; }

    [ObservableProperty]
    private string _displayName = string.Empty;
}

public sealed class ScheduledScanRowViewModel
{
    public ScheduledScanRowViewModel(
        ScheduledScanDefinition plan,
        ILocalizationService localization,
        CultureInfo culture)
    {
        Id = plan.Id;
        Name = plan.Name;
        TargetPath = plan.TargetPath;
        Frequency = plan.Frequency;
        IsEnabled = plan.IsEnabled;
        WeeklyDay = plan.WeeklyDay;
        MonthlyDay = plan.MonthlyDay;
        LocalTimeOfDay = plan.LocalTimeOfDay;
        MissedWhileClosed = plan.MissedWhileClosed;
        EnabledText = plan.IsEnabled
            ? localization.GetString("Scheduled_Enabled")
            : localization.GetString("Scheduled_Disabled");
        FrequencyText = localization.GetString($"Scheduled_Frequency_{plan.Frequency}");
        NextRunText = plan.NextRunUtc?.ToLocalTime().ToString("g", culture)
                      ?? localization.GetString("Scheduled_NoNextRun");
        LastRunText = plan.LastRunUtc?.ToLocalTime().ToString("g", culture)
                      ?? localization.GetString("Scheduled_NeverRun");
        OutcomeText = plan.LastOutcome is { } outcome
            ? localization.GetString($"Scheduled_Outcome_{outcome}")
            : localization.GetString("Drives_NotAvailable");
        ErrorText = plan.LastError ?? string.Empty;
        SnapshotText = plan.LastSnapshotId is { } sid
            ? sid.ToString("N")[..8]
            : localization.GetString("Drives_NotAvailable");
    }

    public Guid Id { get; }
    public string Name { get; }
    public string TargetPath { get; }
    public ScheduledScanFrequency Frequency { get; }
    public bool IsEnabled { get; }
    public DayOfWeek WeeklyDay { get; }
    public int MonthlyDay { get; }
    public TimeSpan LocalTimeOfDay { get; }
    public bool MissedWhileClosed { get; }
    public string EnabledText { get; }
    public string FrequencyText { get; }
    public string NextRunText { get; }
    public string LastRunText { get; }
    public string OutcomeText { get; }
    public string ErrorText { get; }
    public string SnapshotText { get; }
}

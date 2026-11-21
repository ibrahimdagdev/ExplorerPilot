using ExplorerPilot.Core.Scheduling;

namespace ExplorerPilot.Tests;

public sealed class ScheduledScanScheduleMathTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void Daily_BeforeTod_UsesToday()
    {
        var plan = Enabled(ScheduledScanFrequency.Daily, new TimeSpan(14, 30, 0));
        var localNow = new DateTime(2026, 3, 10, 9, 0, 0);
        var next = ScheduledScanScheduleMath.ComputeNextRunLocal(plan, localNow);
        Assert.Equal(new DateTime(2026, 3, 10, 14, 30, 0), next);
    }

    [Fact]
    public void Daily_AfterTod_UsesTomorrow()
    {
        var plan = Enabled(ScheduledScanFrequency.Daily, new TimeSpan(8, 0, 0));
        var localNow = new DateTime(2026, 3, 10, 9, 0, 0);
        var next = ScheduledScanScheduleMath.ComputeNextRunLocal(plan, localNow);
        Assert.Equal(new DateTime(2026, 3, 11, 8, 0, 0), next);
    }

    [Fact]
    public void Weekly_SameDay_BeforeTod_UsesToday()
    {
        // 2026-03-10 is Tuesday
        var plan = Enabled(ScheduledScanFrequency.Weekly, new TimeSpan(18, 0, 0));
        plan.WeeklyDay = DayOfWeek.Tuesday;
        var localNow = new DateTime(2026, 3, 10, 12, 0, 0);
        var next = ScheduledScanScheduleMath.ComputeNextRunLocal(plan, localNow);
        Assert.Equal(new DateTime(2026, 3, 10, 18, 0, 0), next);
    }

    [Fact]
    public void Weekly_SameDay_AfterTod_UsesNextWeek()
    {
        var plan = Enabled(ScheduledScanFrequency.Weekly, new TimeSpan(8, 0, 0));
        plan.WeeklyDay = DayOfWeek.Tuesday;
        var localNow = new DateTime(2026, 3, 10, 12, 0, 0);
        var next = ScheduledScanScheduleMath.ComputeNextRunLocal(plan, localNow);
        Assert.Equal(new DateTime(2026, 3, 17, 8, 0, 0), next);
    }

    [Fact]
    public void Monthly_ClampsToLastDayOfFebruary()
    {
        var plan = Enabled(ScheduledScanFrequency.Monthly, new TimeSpan(10, 0, 0));
        plan.MonthlyDay = 31;
        var localNow = new DateTime(2026, 2, 1, 8, 0, 0);
        var next = ScheduledScanScheduleMath.ComputeNextRunLocal(plan, localNow);
        Assert.Equal(new DateTime(2026, 2, 28, 10, 0, 0), next);
    }

    [Fact]
    public void Monthly_AfterTod_AdvancesToNextMonth_WithClamp()
    {
        var plan = Enabled(ScheduledScanFrequency.Monthly, new TimeSpan(9, 0, 0));
        plan.MonthlyDay = 31;
        var localNow = new DateTime(2026, 1, 31, 10, 0, 0);
        var next = ScheduledScanScheduleMath.ComputeNextRunLocal(plan, localNow);
        Assert.Equal(new DateTime(2026, 2, 28, 9, 0, 0), next);
    }

    [Fact]
    public void DisabledOrManual_ReturnsNullNextUtc()
    {
        var disabled = Enabled(ScheduledScanFrequency.Daily, TimeSpan.FromHours(9));
        disabled.IsEnabled = false;
        Assert.Null(ScheduledScanScheduleMath.ComputeNextRunUtc(
            disabled, DateTimeOffset.UtcNow, Utc));

        var manual = Enabled(ScheduledScanFrequency.Manual, TimeSpan.FromHours(9));
        Assert.Null(ScheduledScanScheduleMath.ComputeNextRunUtc(
            manual, DateTimeOffset.UtcNow, Utc));
    }

    [Fact]
    public void Dst_SpringForwardGap_NudgesForward()
    {
        // US Pacific 2026: spring forward 2026-03-08 02:00 → 03:00
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        var plan = Enabled(ScheduledScanFrequency.Daily, new TimeSpan(2, 30, 0));
        // Local Sunday 2026-03-08 01:00 PST → next candidate 02:30 is in the gap
        var utcNow = new DateTimeOffset(2026, 3, 8, 9, 0, 0, TimeSpan.Zero); // ~01:00 PST
        var next = ScheduledScanScheduleMath.ComputeNextRunUtc(plan, utcNow, tz);
        Assert.NotNull(next);
        var local = TimeZoneInfo.ConvertTime(next!.Value, tz);
        Assert.True(local.Hour is 3 or 2, $"Expected post-gap local hour, got {local}");
        Assert.Equal(8, local.Day);
    }

    [Fact]
    public void ClampMonthlyDay_RespectsDaysInMonth()
    {
        Assert.Equal(28, ScheduledScanScheduleMath.ClampMonthlyDay(2026, 2, 31));
        Assert.Equal(29, ScheduledScanScheduleMath.ClampMonthlyDay(2024, 2, 31));
        Assert.Equal(1, ScheduledScanScheduleMath.ClampMonthlyDay(2026, 1, 0));
        Assert.Equal(31, ScheduledScanScheduleMath.ClampMonthlyDay(2026, 1, 31));
    }

    private static ScheduledScanDefinition Enabled(ScheduledScanFrequency frequency, TimeSpan tod) =>
        new()
        {
            Name = "t",
            TargetPath = @"C:\",
            Frequency = frequency,
            IsEnabled = true,
            LocalTimeOfDay = tod,
            MonthlyDay = 1,
            WeeklyDay = DayOfWeek.Monday
        };
}

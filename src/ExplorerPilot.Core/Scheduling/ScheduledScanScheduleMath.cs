namespace ExplorerPilot.Core.Scheduling;

/// <summary>
/// Computes next-run instants in a specific time zone (typically local), then exposes UTC.
/// Handles DST transitions via <see cref="TimeZoneInfo"/> and clamps monthly days.
/// </summary>
public static class ScheduledScanScheduleMath
{
    public static DateTimeOffset? ComputeNextRunUtc(
        ScheduledScanDefinition plan,
        DateTimeOffset utcNow,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(timeZone);

        if (!plan.IsEnabled || plan.Frequency == ScheduledScanFrequency.Manual)
        {
            return null;
        }

        var localNow = TimeZoneInfo.ConvertTime(utcNow, timeZone);
        var nextLocal = ComputeNextRunLocal(plan, localNow.DateTime);
        var unspecified = DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified);

        try
        {
            return new DateTimeOffset(unspecified, timeZone.GetUtcOffset(unspecified));
        }
        catch (ArgumentException)
        {
            // DST spring-forward gap: nudge forward one hour and retry.
            var adjusted = unspecified.AddHours(1);
            return new DateTimeOffset(adjusted, timeZone.GetUtcOffset(adjusted));
        }
    }

    public static DateTime ComputeNextRunLocal(ScheduledScanDefinition plan, DateTime localNow)
    {
        var tod = NormalizeTimeOfDay(plan.LocalTimeOfDay);

        return plan.Frequency switch
        {
            ScheduledScanFrequency.Daily => NextDaily(localNow, tod),
            ScheduledScanFrequency.Weekly => NextWeekly(localNow, tod, plan.WeeklyDay),
            ScheduledScanFrequency.Monthly => NextMonthly(localNow, tod, plan.MonthlyDay),
            _ => localNow
        };
    }

    public static TimeSpan NormalizeTimeOfDay(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (value >= TimeSpan.FromDays(1))
        {
            return new TimeSpan(23, 59, 59);
        }

        return new TimeSpan(value.Hours, value.Minutes, value.Seconds);
    }

    public static int ClampMonthlyDay(int year, int month, int requestedDay)
    {
        var day = requestedDay < 1 ? 1 : requestedDay;
        var daysInMonth = DateTime.DaysInMonth(year, month);
        return Math.Min(day, daysInMonth);
    }

    private static DateTime NextDaily(DateTime localNow, TimeSpan tod)
    {
        var candidate = localNow.Date + tod;
        if (candidate > localNow)
        {
            return candidate;
        }

        return candidate.AddDays(1);
    }

    private static DateTime NextWeekly(DateTime localNow, TimeSpan tod, DayOfWeek day)
    {
        var daysUntil = ((int)day - (int)localNow.DayOfWeek + 7) % 7;
        var candidate = localNow.Date.AddDays(daysUntil) + tod;
        if (candidate > localNow)
        {
            return candidate;
        }

        return candidate.AddDays(7);
    }

    private static DateTime NextMonthly(DateTime localNow, TimeSpan tod, int monthlyDay)
    {
        var year = localNow.Year;
        var month = localNow.Month;
        var day = ClampMonthlyDay(year, month, monthlyDay);
        var candidate = new DateTime(year, month, day) + tod;
        if (candidate > localNow)
        {
            return candidate;
        }

        var nextMonth = localNow.Date.AddMonths(1);
        day = ClampMonthlyDay(nextMonth.Year, nextMonth.Month, monthlyDay);
        return new DateTime(nextMonth.Year, nextMonth.Month, day) + tod;
    }
}

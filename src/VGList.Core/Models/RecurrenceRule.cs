using System.Globalization;

namespace VGList.Core.Models;

/// <summary>任务重复规则（P1，ADR-0053）。</summary>
public enum RecurrenceKind
{
    None = 0,
    Daily = 1,
    Weekdays = 2,
    Weekly = 3,
    Monthly = 4,
    Yearly = 5,
    LunarYearly = 6,
}

/// <summary>纯离线重复日期推算、中文展示与 RFC 5545 ICS RRULE 互转。</summary>
public static class RecurrenceCalculator
{
    private static readonly ChineseLunisolarCalendar Lunar = new();

    public static DateOnly NextDate(DateOnly current, RecurrenceKind kind) => kind switch
    {
        RecurrenceKind.Daily => current.AddDays(1),
        RecurrenceKind.Weekdays => NextWeekday(current),
        RecurrenceKind.Weekly => current.AddDays(7),
        RecurrenceKind.Monthly => current.AddMonths(1),
        RecurrenceKind.Yearly => current.AddYears(1),
        RecurrenceKind.LunarYearly => NextLunarYear(current),
        _ => current,
    };

    public static string FormatLabel(RecurrenceKind kind) => kind switch
    {
        RecurrenceKind.Daily => "每天",
        RecurrenceKind.Weekdays => "工作日",
        RecurrenceKind.Weekly => "每周",
        RecurrenceKind.Monthly => "每月",
        RecurrenceKind.Yearly => "每年",
        RecurrenceKind.LunarYearly => "农历每年",
        _ => "不重复",
    };

    public static string FormatShortBadge(RecurrenceKind kind) => kind switch
    {
        RecurrenceKind.Daily => "每天",
        RecurrenceKind.Weekdays => "工作日",
        RecurrenceKind.Weekly => "每周",
        RecurrenceKind.Monthly => "每月",
        RecurrenceKind.Yearly => "每年",
        RecurrenceKind.LunarYearly => "农历年",
        _ => string.Empty,
    };

    public static string? ToIcsRRule(RecurrenceKind kind) => kind switch
    {
        RecurrenceKind.Daily => "FREQ=DAILY",
        RecurrenceKind.Weekdays => "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR",
        RecurrenceKind.Weekly => "FREQ=WEEKLY",
        RecurrenceKind.Monthly => "FREQ=MONTHLY",
        RecurrenceKind.Yearly => "FREQ=YEARLY",
        RecurrenceKind.LunarYearly => "FREQ=YEARLY;X-PLANA-LUNAR=1",
        _ => null,
    };

    public static RecurrenceKind ParseIcsRRule(string? rrule)
    {
        if (string.IsNullOrWhiteSpace(rrule))
        {
            return RecurrenceKind.None;
        }

        var upper = rrule.Trim().ToUpperInvariant();
        if (upper.Contains("X-PLANA-LUNAR=1", StringComparison.Ordinal))
        {
            return RecurrenceKind.LunarYearly;
        }

        if (upper.Contains("FREQ=WEEKLY", StringComparison.Ordinal) &&
            upper.Contains("BYDAY=MO,TU,WE,TH,FR", StringComparison.Ordinal))
        {
            return RecurrenceKind.Weekdays;
        }

        if (upper.Contains("FREQ=DAILY", StringComparison.Ordinal))
        {
            return RecurrenceKind.Daily;
        }

        if (upper.Contains("FREQ=WEEKLY", StringComparison.Ordinal))
        {
            return RecurrenceKind.Weekly;
        }

        if (upper.Contains("FREQ=MONTHLY", StringComparison.Ordinal))
        {
            return RecurrenceKind.Monthly;
        }

        if (upper.Contains("FREQ=YEARLY", StringComparison.Ordinal))
        {
            return RecurrenceKind.Yearly;
        }

        return RecurrenceKind.None;
    }

    private static DateOnly NextWeekday(DateOnly current)
    {
        var next = current.AddDays(1);
        while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            next = next.AddDays(1);
        }

        return next;
    }

    private static DateOnly NextLunarYear(DateOnly current)
    {
        try
        {
            var dt = current.ToDateTime(TimeOnly.MinValue);
            var lunarYear = Lunar.GetYear(dt);
            var rawMonth = Lunar.GetMonth(dt);
            var leapMonth = Lunar.GetLeapMonth(lunarYear);
            var regularMonth = leapMonth > 0 && rawMonth >= leapMonth ? rawMonth - 1 : rawMonth;
            var lunarDay = Lunar.GetDayOfMonth(dt);

            var nextYear = lunarYear + 1;
            var nextLeap = Lunar.GetLeapMonth(nextYear);
            var targetRawMonth = nextLeap > 0 && regularMonth >= nextLeap ? regularMonth + 1 : regularMonth;
            var maxDay = Lunar.GetDaysInMonth(nextYear, targetRawMonth);
            var clampedDay = Math.Min(lunarDay, maxDay);

            var nextDt = Lunar.ToDateTime(nextYear, targetRawMonth, clampedDay, 0, 0, 0, 0);
            return DateOnly.FromDateTime(nextDt);
        }
        catch
        {
            return current.AddYears(1);
        }
    }
}

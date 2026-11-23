using System.Globalization;

namespace VGList.Core.Dates;

/// <summary>农历标签的类型，决定单元格里显示成哪种颜色。</summary>
public enum CalendarLabelKind
{
    Lunar,
    SolarTerm,
    Festival,
}

/// <summary>单元格或历法工具返回的农历/节气/节日文案。</summary>
public readonly record struct CalendarLabel(string Text, CalendarLabelKind Kind);

/// <summary>
/// 农历日、二十四节气、公农历节日与农历→公历反查（1901–2100，完全离线）。
/// 节气采用 21 世纪通用近似公式，边界年份可能差一天。
/// </summary>
public static class CnCalendar
{
    private static readonly ChineseLunisolarCalendar Calendar = new();

    private static readonly string[] DayNames =
    [
        "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
        "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
        "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十",
    ];

    private static readonly string[] MonthNames =
    [
        "正月", "二月", "三月", "四月", "五月", "六月",
        "七月", "八月", "九月", "十月", "冬月", "腊月",
    ];

    private static readonly (int Month, int Day, string Name)[] FixedFestivals =
    [
        (1, 1, "元旦"),
        (2, 14, "情人节"),
        (3, 8, "妇女节"),
        (4, 1, "愚人节"),
        (5, 1, "劳动节"),
        (6, 1, "儿童节"),
        (9, 10, "教师节"),
        (10, 1, "国庆节"),
        (10, 31, "万圣夜"),
        (12, 24, "平安夜"),
        (12, 25, "圣诞节"),
    ];

    private static readonly (int Month, int Day, string Name)[] LunarFestivals =
    [
        (1, 1, "春节"),
        (1, 15, "元宵节"),
        (5, 5, "端午节"),
        (7, 7, "七夕"),
        (8, 15, "中秋节"),
        (9, 9, "重阳节"),
        (12, 8, "腊八节"),
    ];

    // 21 世纪 C 常量：day = int(y * 0.2422 + C) - int((y - 1) / 4)。
    private static readonly (string Name, int Month, double C)[] SolarTerms =
    [
        ("小寒", 1, 5.4055),
        ("大寒", 1, 20.12),
        ("立春", 2, 3.87),
        ("雨水", 2, 18.73),
        ("惊蛰", 3, 5.63),
        ("春分", 3, 20.646),
        ("清明", 4, 4.81),
        ("谷雨", 4, 20.1),
        ("立夏", 5, 5.52),
        ("小满", 5, 21.04),
        ("芒种", 6, 5.678),
        ("夏至", 6, 21.37),
        ("小暑", 7, 7.108),
        ("大暑", 7, 22.83),
        ("立秋", 8, 7.5),
        ("处暑", 8, 23.13),
        ("白露", 9, 7.646),
        ("秋分", 9, 23.042),
        ("寒露", 10, 8.318),
        ("霜降", 10, 23.438),
        ("立冬", 11, 7.438),
        ("小雪", 11, 22.36),
        ("大雪", 12, 7.18),
        ("冬至", 12, 21.94),
    ];

    /// <summary>节日优先于节气，节气优先于农历日。</summary>
    public static CalendarLabel Describe(DateOnly date)
    {
        if (TryGetFestival(date, out var festival))
        {
            return new CalendarLabel(festival, CalendarLabelKind.Festival);
        }

        if (TryGetSolarTerm(date, out var term))
        {
            return new CalendarLabel(term, CalendarLabelKind.SolarTerm);
        }

        return new CalendarLabel(DescribeLunar(date), CalendarLabelKind.Lunar);
    }

    /// <summary>将指定农历年/月/日转为公历日期。</summary>
    public static bool TryLunarToSolar(int lunarYear, int lunarMonth, int lunarDay, bool isLeapMonth, out DateOnly solar)
    {
        solar = default;
        if (lunarYear is < 1902 or > 2099 || lunarMonth is < 1 or > 12 || lunarDay is < 1 or > 30)
        {
            return false;
        }

        try
        {
            var leap = Calendar.GetLeapMonth(lunarYear);
            int ordinal;
            if (isLeapMonth)
            {
                if (leap == 0 || leap != lunarMonth + 1)
                {
                    return false;
                }

                ordinal = leap;
            }
            else
            {
                ordinal = leap > 0 && lunarMonth >= leap ? lunarMonth + 1 : lunarMonth;
            }

            if (lunarDay > Calendar.GetDaysInMonth(lunarYear, ordinal))
            {
                return false;
            }

            var dt = Calendar.ToDateTime(lunarYear, ordinal, lunarDay, 0, 0, 0, 0);
            solar = DateOnly.FromDateTime(dt);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>查找从 today 起（含当天）最近一次指定农历月日的公历日期。</summary>
    public static bool TryFindNextLunarDate(int lunarMonth, int lunarDay, DateOnly today, out DateOnly result)
    {
        for (var y = today.Year - 1; y <= today.Year + 2; y++)
        {
            if (TryLunarToSolar(y, lunarMonth, lunarDay, isLeapMonth: false, out var candidate) && candidate >= today)
            {
                result = candidate;
                return true;
            }
        }

        result = default;
        return false;
    }

    /// <summary>查找从 today 起（含当天）最近一次指定节日的公历日期。</summary>
    public static bool TryFindNextFestival(string festivalName, DateOnly today, out DateOnly result)
    {
        var normalized = festivalName.Trim();
        // 支持简称映射（中秋→中秋节、元宵→元宵节、端午→端午节、国庆→国庆节、清明→节气清明）
        switch (normalized)
        {
            case "除夕":
                for (var y = today.Year - 1; y <= today.Year + 2; y++)
                {
                    if (TryLunarToSolar(y + 1, 1, 1, false, out var spring))
                    {
                        var eve = spring.AddDays(-1);
                        if (eve >= today)
                        {
                            result = eve;
                            return true;
                        }
                    }
                }

                result = default;
                return false;

            case "春节":
                return TryFindNextLunarDate(1, 1, today, out result);
            case "元宵" or "元宵节":
                return TryFindNextLunarDate(1, 15, today, out result);
            case "端午" or "端午节":
                return TryFindNextLunarDate(5, 5, today, out result);
            case "七夕" or "七夕节":
                return TryFindNextLunarDate(7, 7, today, out result);
            case "中秋" or "中秋节":
                return TryFindNextLunarDate(8, 15, today, out result);
            case "重阳" or "重阳节":
                return TryFindNextLunarDate(9, 9, today, out result);
            case "腊八" or "腊八节":
                return TryFindNextLunarDate(12, 8, today, out result);
            case "元旦" or "元旦节":
                return TryFindNextFixed(1, 1, today, out result);
            case "情人节":
                return TryFindNextFixed(2, 14, today, out result);
            case "妇女节":
                return TryFindNextFixed(3, 8, today, out result);
            case "劳动节" or "五一":
                return TryFindNextFixed(5, 1, today, out result);
            case "儿童节" or "六一":
                return TryFindNextFixed(6, 1, today, out result);
            case "教师节":
                return TryFindNextFixed(9, 10, today, out result);
            case "国庆" or "国庆节" or "十一":
                return TryFindNextFixed(10, 1, today, out result);
            case "圣诞" or "圣诞节":
                return TryFindNextFixed(12, 25, today, out result);
            case "平安夜":
                return TryFindNextFixed(12, 24, today, out result);
            case "清明" or "清明节":
                return TryFindNextSolarTerm("清明", today, out result);
        }

        if (TryFindNextSolarTerm(normalized, today, out result))
        {
            return true;
        }

        result = default;
        return false;
    }

    public static bool TryFindNextSolarTerm(string termName, DateOnly today, out DateOnly result)
    {
        foreach (var (name, month, c) in SolarTerms)
        {
            if (!string.Equals(name, termName, StringComparison.Ordinal))
            {
                continue;
            }

            for (var y = today.Year; y <= today.Year + 1; y++)
            {
                var yy = y % 100;
                var day = (int)(yy * 0.2422 + c) - (yy - 1) / 4;
                if (day >= 1 && day <= DateTime.DaysInMonth(y, month))
                {
                    var candidate = new DateOnly(y, month, day);
                    if (candidate >= today)
                    {
                        result = candidate;
                        return true;
                    }
                }
            }
        }

        result = default;
        return false;
    }

    private static bool TryFindNextFixed(int month, int day, DateOnly today, out DateOnly result)
    {
        var candidate = new DateOnly(today.Year, month, day);
        result = candidate >= today ? candidate : new DateOnly(today.Year + 1, month, day);
        return true;
    }

    private static bool TryGetFestival(DateOnly date, out string name)
    {
        foreach (var (month, day, label) in FixedFestivals)
        {
            if (date.Month == month && date.Day == day)
            {
                name = label;
                return true;
            }
        }

        if (date.Month == 5 && IsNthWeekday(date, DayOfWeek.Sunday, 2))
        {
            name = "母亲节";
            return true;
        }

        if (date.Month == 6 && IsNthWeekday(date, DayOfWeek.Sunday, 3))
        {
            name = "父亲节";
            return true;
        }

        if (date.Month == 11 && IsNthWeekday(date, DayOfWeek.Thursday, 4))
        {
            name = "感恩节";
            return true;
        }

        if (TryGetLunar(date, out var lunar) && !lunar.IsLeap)
        {
            foreach (var (month, day, label) in LunarFestivals)
            {
                if (lunar.Month == month && lunar.Day == day)
                {
                    name = label;
                    return true;
                }
            }

            if (lunar.Month == 12 && lunar.Day == lunar.DaysInMonth)
            {
                name = "除夕";
                return true;
            }
        }

        name = string.Empty;
        return false;
    }

    private static bool TryGetSolarTerm(DateOnly date, out string name)
    {
        var year = date.Year % 100;
        var leap = (year - 1) / 4;

        foreach (var (term, month, c) in SolarTerms)
        {
            if (month != date.Month)
            {
                continue;
            }

            var day = (int)(year * 0.2422 + c) - leap;
            if (day == date.Day)
            {
                name = term;
                return true;
            }
        }

        name = string.Empty;
        return false;
    }

    private static string DescribeLunar(DateOnly date)
    {
        if (!TryGetLunar(date, out var lunar))
        {
            return string.Empty;
        }

        if (lunar.Day == 1)
        {
            var month = MonthNames[lunar.Month - 1];
            return lunar.IsLeap ? $"闰{month}" : month;
        }

        return lunar.Day <= DayNames.Length ? DayNames[lunar.Day - 1] : string.Empty;
    }

    private static bool TryGetLunar(DateOnly date, out LunarDate value)
    {
        try
        {
            var time = date.ToDateTime(TimeOnly.MinValue);
            var year = Calendar.GetYear(time);
            var ordinal = Calendar.GetMonth(time);
            var leap = Calendar.GetLeapMonth(year);
            var isLeap = leap > 0 && ordinal == leap;
            var month = leap > 0 && ordinal >= leap ? ordinal - 1 : ordinal;

            value = new LunarDate(
                month,
                Calendar.GetDayOfMonth(time),
                Calendar.GetDaysInMonth(year, ordinal),
                isLeap);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            value = default;
            return false;
        }
    }

    private static bool IsNthWeekday(DateOnly date, DayOfWeek weekday, int n) =>
        date.DayOfWeek == weekday && (date.Day - 1) / 7 + 1 == n;

    private readonly record struct LunarDate(int Month, int Day, int DaysInMonth, bool IsLeap);
}

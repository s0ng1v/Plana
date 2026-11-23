namespace VGList.Widgets.Support;

/// <summary>界面上的日期文案，统一从这里出，避免各页面写法不一致。</summary>
public static class DateLabels
{
    public static string Describe(DateOnly date, DateOnly today)
    {
        var delta = date.DayNumber - today.DayNumber;
        return delta switch
        {
            0 => "今天",
            1 => "明天",
            2 => "后天",
            -1 => "昨天",
            -2 => "前天",
            _ => date.Year == today.Year
                ? $"{date.Month}月{date.Day}日"
                : $"{date.Year}年{date.Month}月{date.Day}日",
        };
    }

    /// <summary>「所有」与「已完成」视图的日期分组标题：近五天用相对说法，其余带星期（跨年带年份）。</summary>
    public static string DescribeHeader(DateOnly date, DateOnly today)
    {
        var delta = date.DayNumber - today.DayNumber;
        return delta switch
        {
            0 => "今天",
            1 => "明天",
            2 => "后天",
            -1 => "昨天",
            -2 => "前天",
            _ => date.Year == today.Year
                ? $"{date.Month}月{date.Day}日 {Weekday(date)}"
                : $"{date.Year}年{date.Month}月{date.Day}日 {Weekday(date)}",
        };
    }

    public static string Weekday(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日",
    };
}

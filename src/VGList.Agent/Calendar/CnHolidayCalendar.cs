using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using VGList.Core.Dates;

namespace VGList.Agent.Calendar;

public sealed record HolidayDayInfo(
    DateOnly Date,
    bool IsHoliday,
    bool IsMakeupWorkday,
    bool IsWorkday,
    bool Precise,
    string? HolidayName,
    string Note);

public sealed record NextWorkdayInfo(
    DateOnly FromDate,
    DateOnly NextWorkday,
    bool Precise,
    string Note);

/// <summary>
/// 中国法定节假日与调休补班离线日历（支持 %LOCALAPPDATA%\VGList\holidays-cn.override.json 覆盖）：
/// 仅收录国务院办公厅已正式发布年份；缺失年份明确返回 Precise=false 与说明，绝不猜测调休。
/// </summary>
public sealed class CnHolidayCalendar
{
    public static CnHolidayCalendar Shared { get; } = LoadDefault();

    private readonly Dictionary<int, YearSchedule> _years;

    private CnHolidayCalendar(Dictionary<int, YearSchedule> years)
    {
        _years = years;
    }

    public static string OverrideFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VGList",
        "holidays-cn.override.json");

    public static CnHolidayCalendar LoadDefault(string? overridePath = null)
    {
        var years = new Dictionary<int, YearSchedule>();

        var asm = Assembly.GetExecutingAssembly();
        var resName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("holidays-cn.json", StringComparison.OrdinalIgnoreCase));
        if (resName is not null)
        {
            using var stream = asm.GetManifestResourceStream(resName)!;
            MergeFromStream(stream, years);
        }

        var path = overridePath ?? OverrideFilePath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                using var fs = File.OpenRead(path);
                MergeFromStream(fs, years);
            }
            catch
            {
                // 覆盖文件损坏时静默忽略，保底使用内嵌官方数据
            }
        }

        return new CnHolidayCalendar(years);
    }

    public bool HasYear(int year) => _years.ContainsKey(year);

    public HolidayDayInfo GetDayInfo(DateOnly date)
    {
        var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        if (!_years.TryGetValue(date.Year, out var schedule))
        {
            return new HolidayDayInfo(
                Date: date,
                IsHoliday: false,
                IsMakeupWorkday: false,
                IsWorkday: !isWeekend,
                Precise: false,
                HolidayName: null,
                Note: $"{date.Year} 年官方放假安排未收录，已按普通周末估算");
        }

        if (schedule.Holidays.TryGetValue(date, out var holidayName))
        {
            return new HolidayDayInfo(
                Date: date,
                IsHoliday: true,
                IsMakeupWorkday: false,
                IsWorkday: false,
                Precise: true,
                HolidayName: holidayName,
                Note: $"法定节假日（{holidayName}）");
        }

        if (schedule.MakeupWorkdays.Contains(date))
        {
            return new HolidayDayInfo(
                Date: date,
                IsHoliday: false,
                IsMakeupWorkday: true,
                IsWorkday: true,
                Precise: true,
                HolidayName: null,
                Note: "调休补班工作日");
        }

        return new HolidayDayInfo(
            Date: date,
            IsHoliday: false,
            IsMakeupWorkday: false,
            IsWorkday: !isWeekend,
            Precise: true,
            HolidayName: null,
            Note: isWeekend ? "常规周末" : "常规工作日");
    }

    /// <summary>返回指定日期之后的下一个工作日（严格跳过法定节假日，计入调休补班日）。</summary>
    public NextWorkdayInfo NextWorkday(DateOnly fromDate)
    {
        var cursor = fromDate.AddDays(1);
        var precise = true;
        string? impreciseYear = null;

        for (var i = 0; i < 31; i++)
        {
            var info = GetDayInfo(cursor);
            if (!info.Precise)
            {
                precise = false;
                impreciseYear ??= cursor.Year.ToString();
            }

            if (info.IsWorkday)
            {
                var note = precise
                    ? (info.IsMakeupWorkday ? "调休补班工作日" : "法定工作日")
                    : $"{impreciseYear} 年官方放假安排未收录，已按普通周末估算";
                return new NextWorkdayInfo(fromDate, cursor, precise, note);
            }

            cursor = cursor.AddDays(1);
        }

        return new NextWorkdayInfo(fromDate, fromDate.AddDays(1), false, "未找到 30 天内工作日");
    }

    public static string WeekdayChinese(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        _ => "星期日",
    };

    public object BuildTodayInfoPayload(DateOnly date)
    {
        var lunar = CnCalendar.Describe(date);
        var holiday = GetDayInfo(date);
        return new
        {
            date = date.ToString("yyyy-MM-dd"),
            weekday = WeekdayChinese(date),
            lunar = lunar.Text,
            isHoliday = holiday.IsHoliday,
            isMakeupWorkday = holiday.IsMakeupWorkday,
            isWorkday = holiday.IsWorkday,
            holidayName = holiday.HolidayName,
            precise = holiday.Precise,
            note = holiday.Note,
        };
    }

    private static void MergeFromStream(Stream stream, Dictionary<int, YearSchedule> target)
    {
        var doc = JsonSerializer.Deserialize<HolidayCatalogDto>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        if (doc?.Years is null)
        {
            return;
        }

        foreach (var (yearStr, dto) in doc.Years)
        {
            if (!int.TryParse(yearStr, out var year))
            {
                continue;
            }

            var holidays = new Dictionary<DateOnly, string>();
            if (dto.Holidays is not null)
            {
                foreach (var range in dto.Holidays)
                {
                    if (!DateOnly.TryParse(range.From, out var from) ||
                        !DateOnly.TryParse(range.To, out var to))
                    {
                        continue;
                    }

                    for (var d = from; d <= to; d = d.AddDays(1))
                    {
                        holidays[d] = range.Name ?? "法定节假日";
                    }
                }
            }

            var makeup = new HashSet<DateOnly>();
            if (dto.MakeupWorkdays is not null)
            {
                foreach (var s in dto.MakeupWorkdays)
                {
                    if (DateOnly.TryParse(s, out var d))
                    {
                        makeup.Add(d);
                    }
                }
            }

            target[year] = new YearSchedule(holidays, makeup);
        }
    }

    private sealed record YearSchedule(Dictionary<DateOnly, string> Holidays, HashSet<DateOnly> MakeupWorkdays);

    private sealed class HolidayCatalogDto
    {
        [JsonPropertyName("years")]
        public Dictionary<string, YearDto>? Years { get; set; }
    }

    private sealed class YearDto
    {
        [JsonPropertyName("holidays")]
        public List<HolidayRangeDto>? Holidays { get; set; }

        [JsonPropertyName("makeupWorkdays")]
        public List<string>? MakeupWorkdays { get; set; }
    }

    private sealed class HolidayRangeDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace VGList.Core.Models;

/// <summary>
/// 任务提醒配置（支持提前任意时间、指定绝对时间与按间隔反复提醒）。
/// </summary>
public sealed class TaskReminder
{
    public const int DefaultOffsetMinutes = 10;
    public const int DefaultRepeatMaxCount = 3;

    public bool Enabled { get; set; } = true;

    /// <summary>提前提醒分钟数：0=准时，10=提前10分钟，60=提前1小时，1440=提前1天，支持任意非负整数。</summary>
    public int OffsetMinutes { get; set; } = DefaultOffsetMinutes;

    /// <summary>可选绝对提醒时间；若非空则优先于 Date + DueTime - OffsetMinutes。</summary>
    public DateTimeOffset? AbsoluteAt { get; set; }

    /// <summary>反复提醒间隔（分钟）；null 或 &lt;= 0 表示仅提醒一次。</summary>
    public int? RepeatIntervalMinutes { get; set; }

    /// <summary>反复提醒总次数上限（含首次）；0 表示持续提醒直至任务完成或删除。</summary>
    public int RepeatMaxCount { get; set; } = DefaultRepeatMaxCount;

    /// <summary>已触发次数（由后台提醒调度器维护）。</summary>
    public int FiredCount { get; set; }

    /// <summary>最近一次触发时间（用于计算下一次反复提醒与防休眠唤醒刷屏）。</summary>
    public DateTimeOffset? LastFiredAt { get; set; }

    public bool IsRepeating => RepeatIntervalMinutes is > 0;

    public TaskReminder Clone() => new()
    {
        Enabled = Enabled,
        OffsetMinutes = OffsetMinutes,
        AbsoluteAt = AbsoluteAt,
        RepeatIntervalMinutes = RepeatIntervalMinutes,
        RepeatMaxCount = RepeatMaxCount,
        FiredCount = FiredCount,
        LastFiredAt = LastFiredAt,
    };

    public void ResetTriggerState()
    {
        FiredCount = 0;
        LastFiredAt = null;
    }

    /// <summary>仅比较提醒计划参数（忽略 FiredCount / LastFiredAt 运行态），用于判断是否应重置触发计数。</summary>
    public bool ScheduleEquals(TaskReminder? other)
    {
        if (other is null)
        {
            return false;
        }

        return Enabled == other.Enabled &&
               OffsetMinutes == other.OffsetMinutes &&
               AbsoluteAt == other.AbsoluteAt &&
               RepeatIntervalMinutes == other.RepeatIntervalMinutes &&
               RepeatMaxCount == other.RepeatMaxCount;
    }

    public static TaskReminder CreateDefault(int offsetMinutes = DefaultOffsetMinutes, int? repeatIntervalMinutes = null, int repeatMaxCount = DefaultRepeatMaxCount) =>
        new()
        {
            Enabled = true,
            OffsetMinutes = Math.Max(0, offsetMinutes),
            RepeatIntervalMinutes = repeatIntervalMinutes is > 0 ? repeatIntervalMinutes : null,
            RepeatMaxCount = Math.Max(0, repeatMaxCount),
        };
}

/// <summary>
/// 提醒触发时间计算、中文时间提取、优先级启发式兜底与可读标签格式化工具（零外部依赖）。
/// </summary>
public static partial class TaskReminderCalculator
{
    /// <summary>未显式指定任务时刻但开启相对提醒时的默认基准时刻（上午 09:00）。</summary>
    public static readonly TimeOnly DefaultBaseTime = new(9, 0);

    [GeneratedRegex(@"(?<!\d)(?<h>[01]?\d|2[0-3])\s*[:：]\s*(?<m>[0-5]\d)(?!\d)")]
    private static partial Regex ColonTimeRegex();

    [GeneratedRegex(@"(?<period>凌晨|半夜|清晨|早晨|早上|明早|今早|上午|中午|正午|下午|傍晚|晚上|今晚|明晚|夜里|夜间)?\s*(?<hour>2[0-3]|1\d|0?\d|二十[一二三]?|十[一二三四五六七八九]?|[零〇一二两三四五六七八九])\s*(?:点钟|点|时)\s*(?<min>半|一刻|三刻|(?:[0-5]?\d|五十[一二三四五六七八九]?|四十[一二三四五六七八九]?|三十[一二三四五六七八九]?|二十[一二三四五六七八九]?|十[一二三四五六七八九]?|零[一二三四五六七八九]|[一二三四五六七八九])\s*分?)?")]
    private static partial Regex ChineseClockRegex();

    public static DateTimeOffset? ComputeInitialTriggerAt(
        DateOnly taskDate,
        TimeOnly? dueTime,
        TaskReminder? reminder,
        TimeZoneInfo? timeZone = null)
    {
        if (reminder is null || !reminder.Enabled)
        {
            return null;
        }

        if (reminder.AbsoluteAt is { } absolute)
        {
            return absolute;
        }

        var tz = timeZone ?? TimeZoneInfo.Local;
        var baseLocal = taskDate.ToDateTime(dueTime ?? DefaultBaseTime, DateTimeKind.Unspecified);
        var utcOffset = tz.GetUtcOffset(baseLocal);
        var scheduled = new DateTimeOffset(baseLocal, utcOffset);
        return scheduled.AddMinutes(-Math.Max(0, reminder.OffsetMinutes));
    }

    public static DateTimeOffset? ComputeNextTriggerAt(
        DateOnly taskDate,
        TimeOnly? dueTime,
        TaskReminder? reminder,
        TimeZoneInfo? timeZone = null)
    {
        var initial = ComputeInitialTriggerAt(taskDate, dueTime, reminder, timeZone);
        if (initial is null || reminder is null)
        {
            return null;
        }

        if (reminder.FiredCount <= 0)
        {
            return initial;
        }

        if (reminder.RepeatIntervalMinutes is not { } interval || interval <= 0)
        {
            return null;
        }

        if (reminder.RepeatMaxCount > 0 && reminder.FiredCount >= reminder.RepeatMaxCount)
        {
            return null;
        }

        var last = reminder.LastFiredAt ?? initial.Value;
        return last.AddMinutes(interval);
    }

    /// <summary>生成重复任务下一期时继承并重置提醒状态。</summary>
    public static TaskReminder? CloneForNextOccurrence(TaskReminder? source, DateOnly previousDate, DateOnly nextDate)
    {
        if (source is null)
        {
            return null;
        }

        var next = source.Clone();
        next.ResetTriggerState();
        if (next.AbsoluteAt is { } abs)
        {
            var deltaDays = nextDate.DayNumber - previousDate.DayNumber;
            next.AbsoluteAt = abs.AddDays(deltaDays);
        }

        return next;
    }

    public static string FormatTime(TimeOnly? time) =>
        time is { } t ? t.ToString("HH:mm", CultureInfo.InvariantCulture) : string.Empty;

    public static string FormatOffsetLabel(int offsetMinutes)
    {
        var m = Math.Max(0, offsetMinutes);
        if (m == 0)
        {
            return "准时";
        }

        if (m < 60)
        {
            return $"提前 {m} 分钟";
        }

        if (m < 1440)
        {
            var h = m / 60;
            var rem = m % 60;
            return rem == 0 ? $"提前 {h} 小时" : $"提前 {h} 小时 {rem} 分";
        }

        var d = m / 1440;
        var remH = (m % 1440) / 60;
        var remM = m % 60;
        if (remH == 0 && remM == 0)
        {
            return $"提前 {d} 天";
        }

        if (remM == 0)
        {
            return $"提前 {d} 天 {remH} 小时";
        }

        return $"提前 {d} 天 {remH} 时 {remM} 分";
    }

    public static string FormatRepeatShort(TaskReminder reminder)
    {
        if (reminder.RepeatIntervalMinutes is not { } interval || interval <= 0)
        {
            return string.Empty;
        }

        string unit = interval switch
        {
            < 60 => $"每{interval}分",
            < 1440 when interval % 60 == 0 => $"每{interval / 60}小时",
            >= 1440 when interval % 1440 == 0 => $"每{interval / 1440}天",
            _ => $"每{interval}分",
        };

        return reminder.RepeatMaxCount == 0
            ? $"{unit}反复"
            : $"{unit}×{reminder.RepeatMaxCount}";
    }

    /// <summary>Lana 卡片与详情栏使用的清晰提醒胶囊文案。</summary>
    public static string FormatPillBadge(DateOnly taskDate, TimeOnly? dueTime, TaskReminder? reminder, DateOnly today)
    {
        if (reminder is null || !reminder.Enabled)
        {
            return "添加提醒";
        }

        var trigger = ComputeInitialTriggerAt(taskDate, dueTime, reminder);
        if (trigger is null)
        {
            return "添加提醒";
        }

        var local = trigger.Value.LocalDateTime;
        var triggerDate = DateOnly.FromDateTime(local);
        string core;

        if (reminder.AbsoluteAt is not null)
        {
            core = triggerDate == today
                ? $"今天 {local:HH:mm} 提醒"
                : triggerDate == today.AddDays(1)
                    ? $"明天 {local:HH:mm} 提醒"
                    : $"{local.Month}月{local.Day}日 {local:HH:mm} 提醒";
        }
        else if (dueTime is not null)
        {
            var offsetLabel = FormatOffsetLabel(reminder.OffsetMinutes);
            core = triggerDate == taskDate
                ? $"{offsetLabel} ({local:HH:mm})"
                : $"{offsetLabel} ({local.Month}月{local.Day}日 {local:HH:mm})";
        }
        else
        {
            core = triggerDate == taskDate
                ? $"{FormatOffsetLabel(reminder.OffsetMinutes)} ({local:HH:mm})"
                : $"{FormatOffsetLabel(reminder.OffsetMinutes)} ({local.Month}月{local.Day}日 {local:HH:mm})";
        }

        var repeat = FormatRepeatShort(reminder);
        return string.IsNullOrEmpty(repeat) ? core : $"{core} · {repeat}";
    }

    /// <summary>任务行右侧使用的超紧凑提醒微标文案。</summary>
    public static string FormatRowMiniBadge(DateOnly taskDate, TimeOnly? dueTime, TaskReminder? reminder)
    {
        if (reminder is null || !reminder.Enabled)
        {
            return string.Empty;
        }

        var trigger = ComputeInitialTriggerAt(taskDate, dueTime, reminder);
        if (trigger is null)
        {
            return string.Empty;
        }

        var local = trigger.Value.LocalDateTime;
        var triggerDate = DateOnly.FromDateTime(local);
        var timePart = triggerDate == taskDate
            ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : $"{local.Month}月{local.Day}日 {local:HH:mm}";
        return reminder.IsRepeating ? $"{timePart} ↻" : timePart;
    }

    public static bool TryParseTimeOnly(string? raw, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        string[] formats = ["HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss", "HH：mm", "H：mm"];
        if (TimeOnly.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
        {
            return true;
        }

        if (TryExtractClockTime(trimmed) is { } extracted)
        {
            time = extracted;
            return true;
        }

        return false;
    }

    /// <summary>从中文或 24 小时制文本中提取具体时钟时间（如「明天早上九点」「14:30」「下午3点半」）。</summary>
    public static TimeOnly? TryExtractClockTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // 1. 先检查带时段前缀的 24h/12h 冒号格式（如 "下午 3:30"、"早上 9:00"、"14:30"）
        var colon = ColonTimeRegex().Match(text);
        if (colon.Success &&
            int.TryParse(colon.Groups["h"].Value, out var ch) &&
            int.TryParse(colon.Groups["m"].Value, out var cm))
        {
            var before = text[..colon.Index];
            var period = ExtractTrailingPeriod(before);
            ch = ApplyPeriod(ch, period);
            if (ch is >= 0 and <= 23 && cm is >= 0 and <= 59)
            {
                return new TimeOnly(ch, cm);
            }
        }

        // 2. 中文「X点(半/Y分)」表达
        var cn = ChineseClockRegex().Match(text);
        if (cn.Success)
        {
            var hourVal = ParseChineseOrArabicNumber(cn.Groups["hour"].Value);
            if (hourVal is not { } h)
            {
                return null;
            }

            var minRaw = cn.Groups["min"].Value.Trim();
            var m = 0;
            if (!string.IsNullOrEmpty(minRaw))
            {
                if (minRaw == "半")
                {
                    m = 30;
                }
                else if (minRaw == "一刻")
                {
                    m = 15;
                }
                else if (minRaw == "三刻")
                {
                    m = 45;
                }
                else
                {
                    var cleaned = minRaw.TrimEnd('分');
                    if (ParseChineseOrArabicNumber(cleaned) is { } parsedMin)
                    {
                        m = parsedMin;
                    }
                    else
                    {
                        return null;
                    }
                }
            }

            var period = cn.Groups["period"].Value;
            h = ApplyPeriod(h, period);
            if (h is >= 0 and <= 23 && m is >= 0 and <= 59)
            {
                return new TimeOnly(h, m);
            }
        }

        return null;
    }

    /// <summary>当模型未显式指定优先级时的智能兜底判别。</summary>
    public static TaskPriority InferPriority(string? title, DateOnly date, DateOnly today, TimeOnly? dueTime = null)
    {
        var text = (title ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            return TaskPriority.None;
        }

        var lower = text.ToLowerInvariant();
        string[] highKeywords =
        [
            "紧急", "ddl", "deadline", "截止", "提交", "交", "汇报", "面试", "考试", "答辩",
            "签约", "合同", "开会", "会议", "上线", "发布", "缴费", "还款", "信用卡", "就医",
            "看病", "挂号", "体检", "航班", "飞机", "高铁", "火车", "接机", "重要", "必须",
            "马上", "立刻", "软著", "验收", "评审会", "演示", "demo",
        ];

        string[] mediumKeywords =
        [
            "文档", "报告", "周报", "日报", "方案", "复习", "准备", "整理", "沟通", "联系",
            "跟进", "评审", "需求", "代码", "开发", "修复", "bug", "学习", "作业", "预约",
            "申请", "办理", "核对", "计划", "总结", "设计", "测试", "备份", "更新",
        ];

        string[] lowKeywords =
        [
            "买", "购物", "快递", "拿快递", "看电影", "电影", "逛街", "收拾", "打扫", "剪头发",
            "理发", "休闲", "随手", "有空", "以后", "游戏", "追剧", "读书", "看书", "散步",
            "运动", "跑步", "健身", "洗衣服", "浇花",
        ];

        var hasHigh = highKeywords.Any(k => lower.Contains(k, StringComparison.Ordinal));
        if (hasHigh)
        {
            return date <= today.AddDays(3) || dueTime is not null
                ? TaskPriority.High
                : TaskPriority.Medium;
        }

        var hasLow = lowKeywords.Any(k => lower.Contains(k, StringComparison.Ordinal));
        if (hasLow)
        {
            return TaskPriority.Low;
        }

        var hasMedium = mediumKeywords.Any(k => lower.Contains(k, StringComparison.Ordinal));
        if (hasMedium)
        {
            return TaskPriority.Medium;
        }

        if (date <= today.AddDays(1) && dueTime is not null)
        {
            return TaskPriority.Medium;
        }

        return date <= today ? TaskPriority.Medium : TaskPriority.Low;
    }

    private static string ExtractTrailingPeriod(string prefix)
    {
        var trimmed = prefix.TrimEnd();
        string[] periods = ["凌晨", "半夜", "清晨", "早晨", "早上", "明早", "今早", "上午", "中午", "正午", "下午", "傍晚", "晚上", "今晚", "明晚", "夜里", "夜间"];
        foreach (var p in periods)
        {
            if (trimmed.EndsWith(p, StringComparison.Ordinal))
            {
                return p;
            }
        }

        return string.Empty;
    }

    private static int ApplyPeriod(int hour, string period)
    {
        if (string.IsNullOrEmpty(period) || hour >= 13)
        {
            return hour;
        }

        return period switch
        {
            "下午" or "傍晚" when hour is >= 1 and <= 11 => hour + 12,
            "晚上" or "今晚" or "明晚" or "夜里" or "夜间" when hour is >= 1 and <= 11 => hour + 12,
            "中午" or "正午" when hour is >= 1 and <= 5 => hour + 12,
            "凌晨" or "半夜" when hour == 12 => 0,
            _ => hour,
        };
    }

    private static int? ParseChineseOrArabicNumber(string raw)
    {
        var s = raw.Trim();
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric;
        }

        if (s.Length == 0)
        {
            return null;
        }

        static int Digit(char c) => c switch
        {
            '零' or '〇' => 0,
            '一' => 1,
            '二' or '两' => 2,
            '三' => 3,
            '四' => 4,
            '五' => 5,
            '六' => 6,
            '七' => 7,
            '八' => 8,
            '九' => 9,
            _ => -1,
        };

        if (s == "十")
        {
            return 10;
        }

        if (s.Length == 1)
        {
            var d = Digit(s[0]);
            return d >= 0 ? d : null;
        }

        if (s.Length == 2 && s[0] == '零')
        {
            var d = Digit(s[1]);
            return d >= 0 ? d : null;
        }

        var tenIdx = s.IndexOf('十');
        if (tenIdx >= 0)
        {
            var tens = tenIdx == 0 ? 1 : Digit(s[0]);
            if (tens < 0)
            {
                return null;
            }

            var ones = tenIdx == s.Length - 1 ? 0 : Digit(s[tenIdx + 1]);
            if (ones < 0)
            {
                return null;
            }

            return tens * 10 + ones;
        }

        return null;
    }
}

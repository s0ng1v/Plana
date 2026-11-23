using System.ComponentModel;
using Microsoft.Extensions.AI;
using VGList.Agent.Calendar;
using VGList.App.Assistant;
using VGList.Core.Abstractions;
using VGList.Core.Dates;
using VGList.Core.Models;

namespace VGList.Agent.Tools;

/// <summary>
/// 助理只读工具集：
/// - query_tasks / list_lists 严格受隐私开关 AllowAssistantReadTasks 保护，关闭时返回「用户未授权」；
/// - today_info / resolve_date / lunar_to_solar / next_workday 离线精确计算，不靠大模型心算历法。
/// </summary>
public sealed class ReadTools
{
    public const int DefaultTaskLimit = 200;

    private readonly ITaskRepository _repository;
    private readonly CnHolidayCalendar _holidays;
    private readonly Func<bool> _allowReadTasks;
    private readonly Func<DateOnly> _todayProvider;
    private readonly Action<AssistantEvent> _emit;

    public ReadTools(
        ITaskRepository repository,
        CnHolidayCalendar holidays,
        Func<bool> allowReadTasks,
        Func<DateOnly> todayProvider,
        Action<AssistantEvent> emit)
    {
        _repository = repository;
        _holidays = holidays;
        _allowReadTasks = allowReadTasks;
        _todayProvider = todayProvider;
        _emit = emit;
    }

    public IReadOnlyList<AITool> CreateTools() =>
    [
        AIFunctionFactory.Create(QueryTasks, "query_tasks", "查询用户的任务列表（支持按日期范围、清单 Id 与是否含已完成过滤，最多返回 200 条）"),
        AIFunctionFactory.Create(ListLists, "list_lists", "查询用户的全部任务清单（返回清单 Id 与名称）"),
        AIFunctionFactory.Create(TodayInfo, "today_info", "获取今天的公历日期、星期、农历、节气、节日以及是否法定节假日/调休补班"),
        AIFunctionFactory.Create(ResolveDate, "resolve_date", "将中文自然语言日期表达（如下周三、月底、中秋节、3天后）精确解析为 yyyy-MM-dd"),
        AIFunctionFactory.Create(LunarToSolar, "lunar_to_solar", "将农历月日精确换算为下一个对应的公历日期 yyyy-MM-dd"),
        AIFunctionFactory.Create(NextWorkday, "next_workday", "计算指定日期之后的下一个中国法定工作日（考虑法定假日与调休补班，缺年份会返回 precise=false）"),
    ];

    public object QueryTasks(
        [Description("起始日期 yyyy-MM-dd，可空")] string? fromDate = null,
        [Description("截止日期 yyyy-MM-dd，可空")] string? toDate = null,
        [Description("清单 Id，可空")] string? listId = null,
        [Description("是否包含已完成任务，默认 false")] bool includeCompleted = false)
    {
        var doneLabel = BuildQueryTasksLabel(fromDate, toDate, listId, includeCompleted);
        return InvokeTracked("query_tasks", "Lana 正在翻看你的日程…", doneLabel, () =>
        {
            if (!_allowReadTasks())
            {
                return new { ok = false, error = "用户未授权助理读取任务内容，请在设置中开启「允许助理读取任务内容」。" };
            }

            DateOnly? from = null;
            DateOnly? to = null;
            if (!string.IsNullOrWhiteSpace(fromDate))
            {
                if (!ScheduleOpValidator.TryParseStrictIsoDate(fromDate, out var parsedFrom))
                {
                    return new { ok = false, error = $"fromDate「{fromDate}」格式无效，必须为 yyyy-MM-dd" };
                }

                from = parsedFrom;
            }

            if (!string.IsNullOrWhiteSpace(toDate))
            {
                if (!ScheduleOpValidator.TryParseStrictIsoDate(toDate, out var parsedTo))
                {
                    return new { ok = false, error = $"toDate「{toDate}」格式无效，必须为 yyyy-MM-dd" };
                }

                to = parsedTo;
            }

            var all = _repository.Query(new TaskQuery
            {
                From = from,
                To = to,
                ListId = string.IsNullOrWhiteSpace(listId) ? null : listId,
                IncludeCompleted = includeCompleted,
            });

            var truncated = all.Count > DefaultTaskLimit;
            var items = all.Take(DefaultTaskLimit).Select(t => new
            {
                id = t.Id,
                title = t.Title,
                date = t.Date.ToString("yyyy-MM-dd"),
                dueTime = t.DueTime?.ToString("HH:mm"),
                reminder = t.Reminder is { Enabled: true } r
                    ? new
                    {
                        offsetMinutes = r.OffsetMinutes,
                        repeatIntervalMinutes = r.RepeatIntervalMinutes,
                        repeatMaxCount = r.RepeatMaxCount,
                    }
                    : null,
                priority = t.Priority.ToString(),
                listId = t.ListId,
                isCompleted = t.IsCompleted,
                rescheduleCount = t.RescheduleCount,
                subtaskCount = t.Subtasks.Count,
                subtasksDone = t.Subtasks.Count(s => s.IsCompleted),
                contentSnippet = t.Content.Length > 120 ? t.Content[..120] + "…" : t.Content,
            }).ToList();

            return new
            {
                ok = true,
                total = all.Count,
                returned = items.Count,
                truncated,
                tasks = items,
            };
        });
    }

    public object ListLists()
    {
        return InvokeTracked("list_lists", "Lana 正在查看你的清单…", "已查看任务清单", () =>
        {
            if (!_allowReadTasks())
            {
                return new { ok = false, error = "用户未授权助理读取任务清单内容。" };
            }

            return new
            {
                ok = true,
                lists = _repository.Lists.Select(l => new { id = l.Id, name = l.Name, isSystem = l.Id == TaskList.InboxId }).ToList(),
            };
        });
    }

    public object TodayInfo()
    {
        return InvokeTracked("today_info", "Lana 正在核对今天的日期与农历…", "已核对今日历法", () =>
            _holidays.BuildTodayInfoPayload(_todayProvider()));
    }

    public object ResolveDate([Description("中文日期表达，如「下周三」「月底」「3天后」「中秋节」")] string expression)
    {
        return InvokeTracked("resolve_date", $"Lana 正在换算日期「{expression}」…", $"已解析日期「{expression}」", () =>
        {
            var today = _todayProvider();
            var parsed = ChineseDateParser.Shared.Parse($"{expression} 事项", today);
            if (parsed is null)
            {
                return new { ok = false, error = $"无法从「{expression}」解析出明确日期，请提供更具体的表达。" };
            }

            var info = _holidays.GetDayInfo(parsed.Date);
            return new
            {
                ok = true,
                date = parsed.Date.ToString("yyyy-MM-dd"),
                weekday = CnHolidayCalendar.WeekdayChinese(parsed.Date),
                matched = parsed.Matched,
                isHoliday = info.IsHoliday,
                isMakeupWorkday = info.IsMakeupWorkday,
                isWorkday = info.IsWorkday,
                precise = info.Precise,
                note = info.Note,
            };
        });
    }

    public object LunarToSolar(
        [Description("农历月份 1-12")] int lunarMonth,
        [Description("农历日期 1-30")] int lunarDay,
        [Description("可选指定公历年份；留空则返回从今天起的下一次对应公历日期")] int? year = null)
    {
        return InvokeTracked("lunar_to_solar", $"Lana 正在查农历 {lunarMonth}月{lunarDay}…", $"已换算农历 {lunarMonth}月{lunarDay}", () =>
        {
            DateOnly solar;
            bool ok;
            if (year.HasValue)
            {
                ok = CnCalendar.TryLunarToSolar(year.Value, lunarMonth, lunarDay, isLeapMonth: false, out solar);
            }
            else
            {
                ok = CnCalendar.TryFindNextLunarDate(lunarMonth, lunarDay, _todayProvider(), out solar);
            }

            if (!ok)
            {
                return new { ok = false, error = $"无效的农历日期：{lunarMonth}月{lunarDay}日" };
            }

            var info = _holidays.GetDayInfo(solar);
            return new
            {
                ok = true,
                solarDate = solar.ToString("yyyy-MM-dd"),
                weekday = CnHolidayCalendar.WeekdayChinese(solar),
                precise = true,
                holidayPrecise = info.Precise,
                holidayNote = info.Note,
            };
        });
    }

    public object NextWorkday([Description("起始日期 yyyy-MM-dd，留空则从今天起算")] string? fromDate = null)
    {
        return InvokeTracked("next_workday", "Lana 正在查法定工作日…", "已核对下一个工作日", () =>
        {
            var anchor = _todayProvider();
            if (!string.IsNullOrWhiteSpace(fromDate))
            {
                if (!ScheduleOpValidator.TryParseStrictIsoDate(fromDate, out anchor))
                {
                    return new { ok = false, error = $"fromDate「{fromDate}」格式无效，必须为 yyyy-MM-dd" };
                }
            }

            var next = _holidays.NextWorkday(anchor);
            return new
            {
                ok = true,
                fromDate = next.FromDate.ToString("yyyy-MM-dd"),
                nextWorkday = next.NextWorkday.ToString("yyyy-MM-dd"),
                weekday = CnHolidayCalendar.WeekdayChinese(next.NextWorkday),
                precise = next.Precise,
                note = next.Note,
            };
        });
    }

    private string BuildQueryTasksLabel(string? fromDate, string? toDate, string? listId, bool includeCompleted)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(fromDate) && !string.IsNullOrWhiteSpace(toDate))
        {
            parts.Add(fromDate == toDate ? FormatShortDate(fromDate) : $"{FormatShortDate(fromDate)}～{FormatShortDate(toDate)}");
        }
        else if (!string.IsNullOrWhiteSpace(fromDate))
        {
            parts.Add($"{FormatShortDate(fromDate)} 起");
        }
        else if (!string.IsNullOrWhiteSpace(toDate))
        {
            parts.Add($"截至 {FormatShortDate(toDate)}");
        }

        if (!string.IsNullOrWhiteSpace(listId))
        {
            var listName = _repository.Lists.FirstOrDefault(l => l.Id == listId)?.Name ?? listId;
            parts.Add($"「{listName}」");
        }

        if (includeCompleted)
        {
            parts.Add("含已完成");
        }

        return parts.Count == 0
            ? "已查看当前日程"
            : $"已查看日程（{string.Join(" · ", parts)}）";
    }

    private static string FormatShortDate(string iso) =>
        DateOnly.TryParse(iso, out var d) ? $"{d.Month}月{d.Day}日" : iso;

    private object InvokeTracked(string name, string runningLabel, string finishedLabel, Func<object> body)
    {
        _emit(new ToolStartedEvent(name, runningLabel));
        try
        {
            var res = body();
            _emit(new ToolFinishedEvent(name, finishedLabel, true));
            return res;
        }
        catch (Exception ex)
        {
            _emit(new ToolFinishedEvent(name, finishedLabel, false));
            return new { ok = false, error = ex.Message };
        }
    }
}

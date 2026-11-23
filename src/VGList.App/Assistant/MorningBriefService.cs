using System.Text;
using System.Text.Json;
using VGList.Core.Abstractions;
using VGList.Core.Models;

namespace VGList.App.Assistant;

/// <summary>早安提示计算结果（A6③，纯 App 层离线计算，绝不触发 Agent 加载）。</summary>
public sealed record MorningBriefInfo(
    DateOnly Date,
    int YesterdayUnfinishedCount,
    int TotalOverdueCount,
    string BannerText,
    string PromptText);

/// <summary>日常小帮手状态文档（%LOCALAPPDATA%\VGList\assistant\helpers.json）。</summary>
public sealed class HelpersStateDocument
{
    public string? LastBriefDate { get; set; }
    public string? DismissedBriefDate { get; set; }
}

/// <summary>
/// A6③ 早安提示服务：
/// 仅在存在未完成逾期任务（Date &lt; today）、用户开启开关且当日未关闭提示时返回轻量早安提醒；
/// 所有 IO 走原子写盘，解析异常自动回退为空，绝不阻塞主流程。
/// </summary>
public sealed class MorningBriefService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ITaskRepository _repository;

    public MorningBriefService(ITaskRepository repository, string? assistantDir = null)
    {
        _repository = repository;
        AssistantDirectory = string.IsNullOrWhiteSpace(assistantDir)
            ? RulesStore.DefaultAssistantDirectory
            : assistantDir;
        FilePath = Path.Combine(AssistantDirectory, "helpers.json");
    }

    public string AssistantDirectory { get; }
    public string FilePath { get; }

    public MorningBriefInfo? Evaluate(DateOnly today, bool enabled)
    {
        if (!enabled)
        {
            return null;
        }

        var todayKey = FormatDate(today);
        var state = LoadState();
        if (string.Equals(state.DismissedBriefDate, todayKey, StringComparison.Ordinal))
        {
            return null;
        }

        var yesterday = today.AddDays(-1);
        var overdue = _repository.Query(new TaskQuery
        {
            To = yesterday,
            IncludeCompleted = false,
        });

        if (overdue.Count == 0)
        {
            return null;
        }

        var yesterdayCount = overdue.Count(t => t.Date == yesterday);
        var totalCount = overdue.Count;

        var banner = yesterdayCount == totalCount
            ? $"早安 ✨ 昨天还有 {yesterdayCount} 项未完成 · 让 Lana 帮你排今天 →"
            : $"早安 ✨ 有 {totalCount} 项逾期未完成 · 让 Lana 帮你整理今天 →";

        var prompt = yesterdayCount == totalCount
            ? $"早安！昨天还有 {yesterdayCount} 项未完成的任务，帮我结合今天的日程一起重新排一下今天的安排吧～"
            : $"早安！目前有 {totalCount} 项逾期未完成的任务，帮我结合今天的日程一起梳理并重新安排今天吧～";

        if (!string.Equals(state.LastBriefDate, todayKey, StringComparison.Ordinal))
        {
            state.LastBriefDate = todayKey;
            SaveState(state);
        }

        return new MorningBriefInfo(today, yesterdayCount, totalCount, banner, prompt);
    }

    public void DismissToday(DateOnly today)
    {
        var state = LoadState();
        state.DismissedBriefDate = FormatDate(today);
        SaveState(state);
    }

    public bool IsDismissedFor(DateOnly today)
    {
        var state = LoadState();
        return string.Equals(state.DismissedBriefDate, FormatDate(today), StringComparison.Ordinal);
    }

    private HelpersStateDocument LoadState()
    {
        if (!File.Exists(FilePath))
        {
            return new HelpersStateDocument();
        }

        try
        {
            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<HelpersStateDocument>(json, JsonOpts) ?? new HelpersStateDocument();
        }
        catch
        {
            return new HelpersStateDocument();
        }
    }

    private void SaveState(HelpersStateDocument doc)
    {
        try
        {
            Directory.CreateDirectory(AssistantDirectory);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOpts), Encoding.UTF8);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            // 状态文件写盘失败不阻塞 UI
        }
    }

    private static string FormatDate(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}

using System.Globalization;
using System.Text;
using VGList.Core.Abstractions;
using VGList.Core.Models;

namespace VGList.App.Assistant;

public sealed record HabitStatsSnapshot(
    bool HasEnoughData,
    string SummaryNote,
    int CompletedLast7Days,
    int TotalCompletedAllTime,
    double HighPriorityCompletionRate,
    IReadOnlyList<(string ListName, int Count)> CompletedByList,
    IReadOnlyList<(string Weekday, int Count)> CompletedByWeekday,
    IReadOnlyList<(string Title, int RescheduleCount)> MostRescheduled);

/// <summary>
/// 离线本地习惯统计（0 Token）：
/// 当数据不足（跨度 < 14 天或累计完成 < 20 次）时不下结论，避免小样本误导。
/// </summary>
public static class LocalHabitStats
{
    public const int MinDaysSpan = 14;
    public const int MinCompletedSamples = 20;

    public static HabitStatsSnapshot Compute(ITaskRepository repository, DateOnly today)
    {
        var all = repository.Query(new TaskQuery { IncludeCompleted = true });
        var completed = all.Where(t => t.IsCompleted).ToList();
        var weekStart = today.AddDays(-6);
        var completedLast7 = completed.Count(t => t.Date >= weekStart && t.Date <= today);

        var earliestDate = all.Count > 0 ? all.Min(t => t.Date) : today;
        var spanDays = Math.Max(0, today.DayNumber - earliestDate.DayNumber);
        var hasEnoughData = spanDays >= MinDaysSpan && completed.Count >= MinCompletedSamples;

        var listNames = repository.Lists.ToDictionary(l => l.Id, l => l.Name);
        var byList = completed
            .GroupBy(t => listNames.GetValueOrDefault(t.ListId, "收集箱"))
            .Select(g => (g.Key, g.Count()))
            .OrderByDescending(x => x.Item2)
            .ToList();

        var byWeekday = completed
            .GroupBy(t => WeekdayLabel(t.Date.DayOfWeek))
            .Select(g => (g.Key, g.Count()))
            .OrderByDescending(x => x.Item2)
            .ToList();

        var highTotal = all.Count(t => t.Priority == TaskPriority.High);
        var highDone = completed.Count(t => t.Priority == TaskPriority.High);
        var highRate = highTotal == 0 ? 0.0 : (double)highDone / highTotal;

        var mostRescheduled = all
            .Where(t => t.RescheduleCount > 0)
            .OrderByDescending(t => t.RescheduleCount)
            .Take(5)
            .Select(t => (t.Title, t.RescheduleCount))
            .ToList();

        var note = hasEnoughData
            ? $"过去 7 天完成 {completedLast7} 项任务，高优完成率 {Math.Round(highRate * 100)}%"
            : $"数据还不够（需至少 {MinDaysSpan} 天记录且累计完成 {MinCompletedSamples} 项，当前 {spanDays} 天 / {completed.Count} 项），暂不下习惯结论";

        return new HabitStatsSnapshot(
            HasEnoughData: hasEnoughData,
            SummaryNote: note,
            CompletedLast7Days: completedLast7,
            TotalCompletedAllTime: completed.Count,
            HighPriorityCompletionRate: highRate,
            CompletedByList: byList,
            CompletedByWeekday: byWeekday,
            MostRescheduled: mostRescheduled);
    }

    private static string WeekdayLabel(DayOfWeek dow) => dow switch
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

/// <summary>周复盘本地文件存储（%LOCALAPPDATA%\VGList\assistant\reviews\YYYY-Www.md）。</summary>
public sealed class WeeklyReviewStore
{
    public WeeklyReviewStore(string? directory = null)
    {
        var root = string.IsNullOrWhiteSpace(directory) ? RulesStore.DefaultAssistantDirectory : directory;
        ReviewsDirectory = Path.Combine(root, "reviews");
    }

    public string ReviewsDirectory { get; }

    public static string GetIsoWeekKey(DateOnly date)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        var week = ISOWeek.GetWeekOfYear(dt);
        var year = ISOWeek.GetYear(dt);
        return $"{year}-W{week:00}";
    }

    public string GetReviewFilePath(DateOnly date) =>
        Path.Combine(ReviewsDirectory, $"{GetIsoWeekKey(date)}.md");

    public bool HasReviewForWeek(DateOnly date) => File.Exists(GetReviewFilePath(date));

    public string GenerateOfflineReview(ITaskRepository repository, DateOnly today, string? narrative = null)
    {
        var stats = LocalHabitStats.Compute(repository, today);
        var key = GetIsoWeekKey(today);
        var sb = new StringBuilder();
        sb.AppendLine($"# {key} 本周复盘");
        sb.AppendLine();
        sb.AppendLine($"> 生成于 {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine("## 本周概览");
        sb.AppendLine($"- 近 7 天完成：**{stats.CompletedLast7Days}** 项（累计完成 {stats.TotalCompletedAllTime} 项）");
        sb.AppendLine($"- 高优先级完成率：**{Math.Round(stats.HighPriorityCompletionRate * 100)}%**");
        sb.AppendLine($"- 洞察状态：{stats.SummaryNote}");
        sb.AppendLine();

        if (stats.CompletedByList.Count > 0)
        {
            sb.AppendLine("## 清单分布");
            foreach (var (name, count) in stats.CompletedByList)
            {
                sb.AppendLine($"- {name}：{count} 项");
            }

            sb.AppendLine();
        }

        if (stats.MostRescheduled.Count > 0)
        {
            sb.AppendLine("## 反复推迟事项提醒");
            foreach (var (title, count) in stats.MostRescheduled)
            {
                sb.AppendLine($"- 「{title}」已推迟 {count} 次，建议拆成 10 分钟微步骤或评估是否取消");
            }

            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(narrative))
        {
            sb.AppendLine("## 助理简评");
            sb.AppendLine(narrative.Trim());
            sb.AppendLine();
        }

        var md = sb.ToString();
        Directory.CreateDirectory(ReviewsDirectory);
        File.WriteAllText(GetReviewFilePath(today), md, Encoding.UTF8);
        return md;
    }

    public IReadOnlyList<string> ListReviews() =>
        Directory.Exists(ReviewsDirectory)
            ? Directory.GetFiles(ReviewsDirectory, "*.md").OrderByDescending(Path.GetFileName).ToList()
            : [];
}

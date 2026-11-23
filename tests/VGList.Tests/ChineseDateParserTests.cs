using VGList.Core.Dates;
using VGList.App.Services;
using VGList.Core.Models;
using VGList.Desktop.Main.Tasks;
using VGList.Desktop.Services;
using VGList.Widgets.Support;
using VGList.Widgets.Today;
using Xunit;

namespace VGList.Tests;

public sealed class ChineseDateParserTests
{
    public static IEnumerable<object[]> CorpusRows()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "date-corpus.csv");
        var lines = File.ReadAllLines(path);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            var parts = line.Split(',');
            // 允许输入中包含中文逗号，因此按标准英文逗号切分 4 列
            var input = parts[0];
            var today = string.IsNullOrEmpty(parts[1]) ? "2026-11-21" : parts[1];
            var expectedDate = parts[2];
            var expectedTitle = parts.Length > 3 ? parts[3] : string.Empty;
            yield return [i + 1, input, today, expectedDate, expectedTitle];
        }
    }

    [Theory]
    [MemberData(nameof(CorpusRows))]
    public void Corpus_MatchesExpectedDateAndCleanTitle(
        int rowNumber,
        string input,
        string todayText,
        string expectedDate,
        string expectedTitle)
    {
        var today = DateOnly.ParseExact(todayText, "yyyy-MM-dd");
        var result = ChineseDateParser.Shared.Parse(input, today);

        if (expectedDate == "NONE")
        {
            Assert.True(result is null, $"行 {rowNumber}「{input}」不应识别，但识别为 {result?.Date} / {result?.CleanTitle}");
            return;
        }

        Assert.True(result is not null, $"行 {rowNumber}「{input}」未识别出期望日期 {expectedDate}");
        Assert.Equal(DateOnly.ParseExact(expectedDate, "yyyy-MM-dd"), result!.Date);
        Assert.Equal(expectedTitle, result.CleanTitle);
    }

    [Fact]
    public void Corpus_HasOverOneHundredCases()
    {
        Assert.True(CorpusRows().Count() >= 100);
    }

    [Fact]
    public void DraftAutoFill_RespectsManualPickAndResetAndToggle()
    {
        var today = new DateOnly(2026, 11, 21);
        var enabled = true;
        var auto = new DraftDateAutoFill(today, () => enabled);

        Assert.Equal(new DateOnly(2026, 11, 25), auto.Evaluate("下周三交周报", today));

        // 用户手动选日期后不再自动覆盖
        auto.MarkUserPickedDate();
        Assert.Null(auto.Evaluate("明天开会", today));
        var (title, date) = auto.ResolveForSubmit("明天开会", new DateOnly(2026, 12, 1), today);
        Assert.Equal("明天开会", title);
        Assert.Equal(new DateOnly(2026, 12, 1), date);

        // 提交重置后恢复自动识别
        auto.Reset(today);
        var (title2, date2) = auto.ResolveForSubmit("明天开会", today, today);
        Assert.Equal("开会", title2);
        Assert.Equal(new DateOnly(2026, 11, 22), date2);

        // 开关关闭后不识别
        enabled = false;
        Assert.Null(auto.Evaluate("后天体检", today));
        var (title3, date3) = auto.ResolveForSubmit("后天体检", today, today);
        Assert.Equal("后天体检", title3);
        Assert.Equal(today, date3);
    }

    [Fact]
    public void AllEntryViewModels_AutoFillDateAndCleanTitleOnSubmit()
    {
        var today = new DateOnly(2026, 11, 21);
        var prev = AutoDetectDatePreference.IsEnabled;
        AutoDetectDatePreference.IsEnabled = true;
        try
        {
            var repo = new InMemoryTaskRepository();

            // 1. 今日小组件
            var todayVm = new TodayWidgetViewModel(repo, today);
            todayVm.DraftText = "下周三交周报";
            Assert.Equal(new DateOnly(2026, 11, 25), todayVm.DraftDate);
            todayVm.SubmitDraft();
            Assert.Contains(repo.Query(new TaskQuery()), t => t.Title == "交周报" && t.Date == new DateOnly(2026, 11, 25));

            // 2. 任务页行内添加
            var tasksVm = new TasksPageViewModel(repo, new ToastService(), today);
            tasksVm.DraftText = "月底前续费会员";
            Assert.Equal(new DateOnly(2026, 11, 30), tasksVm.DraftDate);
            tasksVm.SubmitDraft();
            Assert.Contains(repo.Query(new TaskQuery()), t => t.Title == "续费会员" && t.Date == new DateOnly(2026, 11, 30));

            // 3. 悬浮新建卡片（日历/月历/四象限共用）
            var quickVm = new QuickCreateViewModel(today);
            quickVm.Title = "后天下午3点开会";
            Assert.Equal(new DateOnly(2026, 11, 23), quickVm.TargetDate);
            var created = quickVm.Submit(repo);
            Assert.NotNull(created);
            Assert.Equal("下午3点开会", created!.Title);
            Assert.Equal(new DateOnly(2026, 11, 23), created.Date);
        }
        finally
        {
            AutoDetectDatePreference.IsEnabled = prev;
        }
    }
}

using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using VGList.Agent.Calendar;
using VGList.Agent.Clients;
using VGList.Agent.Runtime;
using VGList.App.Assistant;
using VGList.App.Models;
using VGList.App.Services;
using VGList.Core.Models;
using VGList.Data;
using Xunit;

namespace VGList.Tests;

public sealed class AssistantRulesMemoryTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 21);
    private readonly string _tempDir;

    public AssistantRulesMemoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vglist-a5-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void RuleProposalValidator_EnforcesRestDaysAndHolidayMakeup_WithoutDailyCaps()
    {
        var day2 = new DateOnly(2026, 11, 24); // 周二（普通工作日，哪怕已有 3 条也不限）
        var sunday = new DateOnly(2026, 11, 29); // 周日（休息日）
        var makeupSat = new DateOnly(2026, 10, 10); // 2026 国庆调休补班周六（视为工作日！）
        var holidayDay = new DateOnly(2026, 10, 2); // 2026 国庆法定假期

        var repo = new InMemoryTaskRepository(
        [
            new TaskItem { Id = "b1", Title = "周二1", Date = day2, Priority = TaskPriority.High },
            new TaskItem { Id = "b2", Title = "周二2", Date = day2 },
            new TaskItem { Id = "b3", Title = "周二3", Date = day2 },
        ]);
        var workList = repo.CreateList("工作");

        var rules = new UserRulesDocument
        {
            RestDays = [DayOfWeek.Sunday],
            NoWeekendOrHolidayListIds = [workList.Id],
        };

        var cal = CnHolidayCalendar.Shared;
        var validator = new RuleProposalValidator(
            repo,
            () => rules,
            d =>
            {
                var info = cal.GetDayInfo(d);
                return (info.IsHoliday, info.IsMakeupWorkday, info.HolidayName);
            });

        var items = new List<ProposalItem>
        {
            new("p-normal", ProposalActionKind.CreateTask, "新增", "普通工作日任务", "", "", new CreateTaskOp(new TaskDraft { Title = "普通工作日任务", Date = day2, Priority = TaskPriority.High })),
            new("p-sun", ProposalActionKind.CreateTask, "新增", "周日任务", "", "", new CreateTaskOp(new TaskDraft { Title = "周日任务", Date = sunday })),
            new("p-makeup", ProposalActionKind.CreateTask, "新增", "补班工作日任务", "", "", new CreateTaskOp(new TaskDraft { Title = "补班工作日任务", Date = makeupSat, ListId = workList.Id })),
            new("p-hol", ProposalActionKind.CreateTask, "新增", "国庆工作任务", "", "", new CreateTaskOp(new TaskDraft { Title = "国庆工作任务", Date = holidayDay, ListId = workList.Id })),
        };

        var violations = validator.Validate(items);

        // 已移除每日负荷上限：普通工作日无违规
        Assert.False(violations.ContainsKey("p-normal"));

        // 周日触发休息日违规
        Assert.Contains(violations["p-sun"], v => v.RuleId == "rest-day");

        // 2026-10-10 调休补班周六被正确视为工作日，无违规！
        Assert.False(violations.ContainsKey("p-makeup"));

        // 2026-10-02 国庆假期触发节假日清单保护违规！
        Assert.Contains(violations["p-hol"], v => v.RuleId == "holiday-list-guard");
    }

    [Fact]
    public void MemoryStore_RejectsSensitiveInfo_ToleratesHandEdits_AndRanksByRelevance()
    {
        var store = new MemoryStore(_tempDir);

        // 拦截敏感信息
        Assert.False(store.Add("我的银行卡号是 6222021234567890123").Success);
        Assert.False(store.Add("身份证号 110101199505121234 记住").Success);
        Assert.False(store.Add("我的登录密码: MySecret999").Success);
        Assert.False(store.Add("短信验证码 849201").Success);
        Assert.False(store.Add("API Key 是 sk-abcdefghijkl12345678").Success);
        Assert.Empty(store.LoadAll());

        // 正常写入 + 手工编辑容错
        var r1 = store.Add("周五下午固定不开会，用来深度写作", "习惯", "对话");
        Assert.True(r1.Success);
        var r2 = store.Add("偏好把健身安排在周四晚上八点", "偏好", "对话", pinned: true);
        Assert.True(r2.Success);

        // 模拟用户手工用记事本追加一条无 ID/无元数据的纯列表项
        File.AppendAllText(store.FilePath, "\n- 周一上午集中处理代码评审\n");

        var all = store.LoadAll();
        Assert.Equal(3, all.Count);
        Assert.Contains(all, m => m.Content == "周一上午集中处理代码评审");

        // 相关性排序：置顶条目优先，其次匹配「写作/周五」的条目
        var relevant = store.QueryRelevant("周五安排深度写作", Today, limit: 2);
        Assert.Equal(2, relevant.Count);
        Assert.Equal(r2.Item!.Id, relevant[0].Id); // pinned 置顶第一
        Assert.Equal(r1.Item!.Id, relevant[1].Id); // 关键词重合第二
    }

    [Fact]
    public void UserProfileStore_ProtectsHandEditedSections_AndRestoresPreviousVersion()
    {
        var store = new UserProfileStore(_tempDir);

        // 1. 首次自动更新生成画像
        var v1 = store.ApplyAutoUpdate(new Dictionary<string, string>
        {
            ["作息习惯"] = "通常上午 9 点开始工作",
            ["高效时段"] = "上午 10:00–12:00",
        });
        Assert.Contains("通常上午 9 点开始工作", v1);

        // 2. 用户手改「作息习惯」小节
        var userEdited = v1.Replace("通常上午 9 点开始工作", "我是夜猫子，晚上 10 点最专注");
        store.SaveUserEdited(userEdited);

        // 3. 再次自动更新：手改过的「作息习惯」绝不被覆盖，新建议追加到「## 助理更新建议」；未手改的「高效时段」正常更新
        var v2 = store.ApplyAutoUpdate(new Dictionary<string, string>
        {
            ["作息习惯"] = "AI 猜测你早起",
            ["高效时段"] = "上午 9:30–11:30 效率最高",
        });

        Assert.Contains("我是夜猫子，晚上 10 点最专注", v2);
        Assert.Contains("## 助理更新建议", v2);
        Assert.Contains("AI 猜测你早起", v2);
        Assert.Contains("上午 9:30–11:30 效率最高", v2);

        // 4. 恢复上一版（回退到用户手改版）
        Assert.True(store.RestorePreviousVersion());
        var restored = store.LoadMarkdown();
        Assert.Contains("我是夜猫子，晚上 10 点最专注", restored);
        Assert.DoesNotContain("## 助理更新建议", restored);
    }

    [Fact]
    public void LocalHabitStatsAndWeeklyReview_RespectsSmallSampleGuard_AndGeneratesReviewFile()
    {
        // 小样本（仅 3 天、3 条完成）→ 不下结论
        var smallRepo = new InMemoryTaskRepository(
        [
            new TaskItem { Id = "s1", Title = "T1", Date = Today, IsCompleted = true },
            new TaskItem { Id = "s2", Title = "T2", Date = Today.AddDays(-1), IsCompleted = true },
            new TaskItem { Id = "s3", Title = "T3", Date = Today.AddDays(-2), IsCompleted = true },
        ]);
        var smallStats = LocalHabitStats.Compute(smallRepo, Today);
        Assert.False(smallStats.HasEnoughData);
        Assert.Contains("数据还不够", smallStats.SummaryNote);

        // 充足样本（20 天跨度、24 条完成 + 推迟记录）
        var tasks = new List<TaskItem>();
        for (var i = 0; i < 24; i++)
        {
            tasks.Add(new TaskItem
            {
                Id = $"f{i}",
                Title = $"完成任务 {i}",
                Date = Today.AddDays(-(i % 20)),
                Priority = i % 3 == 0 ? TaskPriority.High : TaskPriority.Low,
                IsCompleted = true,
                RescheduleCount = i == 0 ? 4 : 0,
            });
        }

        var fullRepo = new InMemoryTaskRepository(tasks);
        var fullStats = LocalHabitStats.Compute(fullRepo, Today);
        Assert.True(fullStats.HasEnoughData);
        Assert.Equal(24, fullStats.TotalCompletedAllTime);
        Assert.Equal(1.0, fullStats.HighPriorityCompletionRate);
        Assert.Single(fullStats.MostRescheduled);

        var reviews = new WeeklyReviewStore(_tempDir);
        var md = reviews.GenerateOfflineReview(fullRepo, Today, "保持上午专注节奏。");
        Assert.True(reviews.HasReviewForWeek(Today));
        Assert.Contains("完成任务 0", md);
        Assert.Contains("保持上午专注节奏", md);
    }

    [Fact]
    public async Task AssistantHost_MemoryToolsRememberAndRespectToggle()
    {
        var dbPath = Path.Combine(_tempDir, "mem.db");
        using var db = SqliteDatabase.Open(dbPath);
        using var repo = new SqliteTaskRepository(db);
        var memoryStore = new MemoryStore(_tempDir);

        var script = new OneShotToolClient(
            new FunctionCallContent("m1", "remember", new Dictionary<string, object?>
            {
                ["content"] = "用户习惯把重要写作排在上午",
                ["category"] = "习惯",
            }),
            "好的，我已经记住了。");

        using var host = new AssistantHost(
            repo,
            repo,
            new DummyModelProvider(),
            new SqliteConversationStore(db),
            new FixedFactory(script),
            memoryStore: memoryStore,
            memoryEnabled: () => true,
            todayProvider: () => Today);

        var events = new List<AssistantEvent>();
        await foreach (var e in host.RunAsync(new AssistantRequest("s-mem", "记住我喜欢上午写作")))
        {
            events.Add(e);
        }

        var memEvt = Assert.Single(events.OfType<MemoryChangedEvent>());
        Assert.Equal("added", memEvt.Action);
        Assert.Contains("上午", memEvt.Text);
        Assert.Single(memoryStore.LoadAll());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed class DummyModelProvider : IModelProfileProvider
    {
        public ModelProfile? ActiveProfile => new() { Id = "p", Name = "Test", Model = "m", BaseUrl = "https://api.example.com/v1" };
        public event EventHandler? ActiveChanged { add { } remove { } }
        public bool TryResolveActive(out ResolvedModel? resolved, out string? error)
        {
            resolved = new ResolvedModel("p", "Test", null, "https://api.example.com/v1", ModelProtocol.OpenAi, "sk-1", "m", true);
            error = null;
            return true;
        }
    }

    private sealed class FixedFactory(IChatClient client) : IChatClientFactory
    {
        public IChatClient Create(ResolvedModel model) => client;
    }

    private sealed class OneShotToolClient(FunctionCallContent call, string finalReply) : IChatClient
    {
        private int _step;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _step++ == 0 ? [call] : [new TextContent(finalReply)])));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, _step++ == 0 ? [call] : [new TextContent(finalReply)]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}

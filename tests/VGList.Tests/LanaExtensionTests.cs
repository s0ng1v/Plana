using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using VGList.Agent.Clients;
using VGList.Agent.Proposals;
using VGList.Agent.Runtime;
using VGList.Agent.Tools;
using VGList.App.Assistant;
using VGList.App.Models;
using VGList.App.Services;
using VGList.Core.Abstractions;
using VGList.Core.Models;
using VGList.Data;
using VGList.Desktop.Controls;
using VGList.Desktop.Main.Assistant;
using VGList.Platform;
using VGList.Desktop.Main.Tasks;
using VGList.Desktop.Services;
using Xunit;

namespace VGList.Tests;

public sealed class LanaExtensionTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 21);
    private readonly string _tempDir;

    public LanaExtensionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vglist-adr0055-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
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

    [Theory]
    [InlineData("明天早上九点我要交软著文档", 9, 0)]
    [InlineData("早上 9:00 提交软著文档", 9, 0)]
    [InlineData("上午9点半开周会", 9, 30)]
    [InlineData("下午3点15分产品评审", 15, 15)]
    [InlineData("晚上8点健身", 20, 0)]
    [InlineData("14:45 面试候选人", 14, 45)]
    [InlineData("中午12点半聚餐", 12, 30)]
    public void TaskReminderCalculator_TryExtractClockTime_ParsesChineseAnd24h(string text, int h, int m)
    {
        var extracted = TaskReminderCalculator.TryExtractClockTime(text);
        Assert.Equal(new TimeOnly(h, m), extracted);
    }

    [Fact]
    public void TaskReminderCalculator_TryExtractClockTime_IgnoresDateWithoutTime()
    {
        Assert.Null(TaskReminderCalculator.TryExtractClockTime("明天11月22日买牛奶"));
        Assert.Null(TaskReminderCalculator.TryExtractClockTime("下周三整理周报"));
    }

    [Fact]
    public void TaskReminderCalculator_ComputesDefaultArbitraryAndRepeatingTriggers()
    {
        var taskDate = new DateOnly(2026, 11, 22);
        var dueTime = new TimeOnly(9, 0);
        var tz = TimeZoneInfo.Local;

        // 默认提前 10 分钟 → 08:50
        var defRem = TaskReminder.CreateDefault();
        var t0 = TaskReminderCalculator.ComputeNextTriggerAt(taskDate, dueTime, defRem, tz);
        Assert.NotNull(t0);
        Assert.Equal(new DateTime(2026, 11, 22, 8, 50, 0), t0.Value.LocalDateTime);

        // 任意提前量（提前 2 天 3 小时 15 分钟 = 3075 分钟）
        var arbitrary = TaskReminder.CreateDefault(offsetMinutes: 2 * 1440 + 3 * 60 + 15);
        var tArb = TaskReminderCalculator.ComputeNextTriggerAt(taskDate, dueTime, arbitrary, tz);
        Assert.Equal(new DateTime(2026, 11, 20, 5, 45, 0), tArb!.Value.LocalDateTime);
        Assert.Equal("提前 2 天 3 时 15 分", TaskReminderCalculator.FormatOffsetLabel(arbitrary.OffsetMinutes));

        // 反复提醒：每 5 分钟，最多 3 次
        var rep = TaskReminder.CreateDefault(offsetMinutes: 10, repeatIntervalMinutes: 5, repeatMaxCount: 3);
        rep.FiredCount = 1;
        rep.LastFiredAt = t0;
        var t1 = TaskReminderCalculator.ComputeNextTriggerAt(taskDate, dueTime, rep, tz);
        Assert.Equal(new DateTime(2026, 11, 22, 8, 55, 0), t1!.Value.LocalDateTime);

        rep.FiredCount = 3;
        Assert.Null(TaskReminderCalculator.ComputeNextTriggerAt(taskDate, dueTime, rep, tz));

        // 持续提醒至完成（RepeatMaxCount = 0）
        rep.RepeatMaxCount = 0;
        rep.FiredCount = 10;
        rep.LastFiredAt = t1;
        var tInf = TaskReminderCalculator.ComputeNextTriggerAt(taskDate, dueTime, rep, tz);
        Assert.Equal(new DateTime(2026, 11, 22, 9, 0, 0), tInf!.Value.LocalDateTime);
    }

    [Fact]
    public void SqliteMigrationV4_PersistsDueTimeReminderRecurrenceAndBackup_AndPreservesUndoAfterFire()
    {
        var dbPath = Path.Combine(_tempDir, "reminders.db");
        string taskId;
        string createCsId;

        using (var db = SqliteDatabase.Open(dbPath))
        using (var repo = new SqliteTaskRepository(db))
        {
            using (var rawConn = new SqliteConnection($"Data Source={dbPath}"))
            {
                rawConn.Open();
                using var verCmd = rawConn.CreateCommand();
                verCmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
                Assert.Equal(4, Convert.ToInt32(verCmd.ExecuteScalar()));
            }

            var cs = repo.Apply(
            [
                new CreateTaskOp(new TaskDraft
                {
                    Title = "提交软著文档",
                    Date = Today.AddDays(1),
                    DueTime = new TimeOnly(9, 0),
                    Reminder = TaskReminder.CreateDefault(offsetMinutes: 15, repeatIntervalMinutes: 5, repeatMaxCount: 4),
                    Priority = TaskPriority.High,
                    Recurrence = RecurrenceKind.Weekly,
                }),
            ], ChangeSource.Assistant, "新增日程");

            createCsId = cs.Id;
            taskId = cs.Entries[0].EntityId;

            // 模拟后台触发提醒：静默写库、不污染撤销栈、也不破坏原 ChangeSet 撤销
            repo.RecordReminderFired(taskId, 1, DateTimeOffset.Now);

            // JSON 与 ICS 备份均保留时刻与提醒
            var json = TaskBackupService.ExportJson(repo);
            Assert.Contains("\"dueTime\": \"09:00\"", json);
            Assert.Contains("\"offsetMinutes\": 15", json);

            var ics = TaskBackupService.ExportIcs(repo);
            Assert.Contains("DTSTART:20261122T090000", ics);
            Assert.Contains("BEGIN:VALARM", ics);
            Assert.Contains("TRIGGER:-PT15M", ics);

            var importedRepo = new InMemoryTaskRepository();
            var impRes = TaskBackupService.ImportJson(json, Today, importedRepo);
            Assert.Equal(1, impRes.ImportedCount);
            var importedTask = Assert.Single(importedRepo.Query(new TaskQuery { IncludeCompleted = true }));
            Assert.Equal(new TimeOnly(9, 0), importedTask.DueTime);
            Assert.Equal(15, importedTask.Reminder?.OffsetMinutes);
            Assert.Equal(0, importedTask.Reminder?.FiredCount);
        }

        // 重开 SQLite 验证完整持久化 + 重复任务完成继承提醒（重置 FiredCount）
        using (var db = SqliteDatabase.Open(dbPath))
        using (var repo = new SqliteTaskRepository(db))
        {
            var loaded = repo.GetById(taskId);
            Assert.NotNull(loaded);
            Assert.Equal(new TimeOnly(9, 0), loaded.DueTime);
            Assert.NotNull(loaded.Reminder);
            Assert.Equal(15, loaded.Reminder.OffsetMinutes);
            Assert.Equal(5, loaded.Reminder.RepeatIntervalMinutes);
            Assert.Equal(4, loaded.Reminder.RepeatMaxCount);
            Assert.Equal(1, loaded.Reminder.FiredCount);

            // 完成重复任务 → 生成下一周任务并继承 DueTime & Reminder（FiredCount 归零）
            repo.SetCompleted(taskId, true);
            var nextTask = repo.Query(new TaskQuery { IncludeCompleted = false }).Single();
            Assert.Equal(Today.AddDays(8), nextTask.Date);
            Assert.Equal(new TimeOnly(9, 0), nextTask.DueTime);
            Assert.NotNull(nextTask.Reminder);
            Assert.Equal(15, nextTask.Reminder.OffsetMinutes);
            Assert.Equal(0, nextTask.Reminder.FiredCount);

            // 撤销完成操作后，原助理创建的任务即便触发过提醒仍可正常撤销
            var latestUserCs = repo.Recent(5, ChangeSource.User).First();
            Assert.True(repo.Undo(latestUserCs.Id).Success);
            var undoCreate = repo.Undo(createCsId);
            Assert.True(undoCreate.Success);
            Assert.Equal(1, undoCreate.RevertedCount);
        }
    }

    [Fact]
    public void ReminderScheduler_FiresAtLeadTime_RepeatsAtInterval_StopsOnComplete_AndAvoidsSleepSpam()
    {
        var repo = new InMemoryTaskRepository();
        var taskDate = new DateOnly(2026, 11, 22);
        var task = repo.Create(new TaskDraft
        {
            Title = "提交软著文档",
            Date = taskDate,
            DueTime = new TimeOnly(9, 0),
            Reminder = TaskReminder.CreateDefault(offsetMinutes: 10, repeatIntervalMinutes: 5, repeatMaxCount: 3),
            Priority = TaskPriority.High,
        });

        var triggered = new List<ReminderTriggeredEventArgs>();
        using var scheduler = new ReminderScheduler(repo, e => triggered.Add(e));

        static DateTimeOffset At(int h, int m) =>
            new(new DateTime(2026, 11, 22, h, m, 0), TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 22, h, m, 0)));

        // 08:49 未到点
        Assert.Empty(scheduler.CheckDueReminders(At(8, 49)));

        // 08:50 首次触发
        var first = scheduler.CheckDueReminders(At(8, 50));
        Assert.Single(first);
        Assert.Equal(1, first[0].FireNumber);
        Assert.False(first[0].IsRepeat);

        // 08:51 不重复触发
        Assert.Empty(scheduler.CheckDueReminders(At(8, 51)));

        // 模拟电脑休眠到 09:20 醒来：仅补发一次（第 2 次），绝不刷屏
        var second = scheduler.CheckDueReminders(At(9, 20));
        Assert.Single(second);
        Assert.Equal(2, second[0].FireNumber);
        Assert.True(second[0].IsRepeat);

        // 09:22 仍在 5 分钟间隔内，不触发
        Assert.Empty(scheduler.CheckDueReminders(At(9, 22)));

        // 完成任务后 09:30 彻底停止提醒
        repo.SetCompleted(task.Id, true);
        Assert.Empty(scheduler.CheckDueReminders(At(9, 30)));
        Assert.Equal(2, triggered.Count);
    }

    [Fact]
    public void ProposalTools_AutoExtractsTimeDefault10MinReminder_AndAutoInfersPriority()
    {
        var repo = new InMemoryTaskRepository();
        var draft = new RunProposalDraft();
        var tools = new ProposalTools(repo, draft, validator: null, emit: _ => { }, policy: null, todayProvider: () => Today);

        // 未传 dueTime、未传 remindBeforeMinutes、未传 priority → 自动提取 09:00 + 提前 10 分钟提醒 + 高优先级
        tools.ProposeCreateTask(title: "早上 9:00 提交软著文档", date: "2026-11-22");
        Assert.Single(draft.Items);
        var createOp = Assert.IsType<CreateTaskOp>(draft.Items[0].Operation);
        Assert.Equal(new TimeOnly(9, 0), createOp.Draft.DueTime);
        Assert.Equal(TaskPriority.High, createOp.Draft.Priority);
        Assert.NotNull(createOp.Draft.Reminder);
        Assert.True(createOp.Draft.Reminder.Enabled);
        Assert.Equal(10, createOp.Draft.Reminder.OffsetMinutes);

        // 显式关闭提醒（remindBeforeMinutes = -1）与低优先级推断
        tools.ProposeCreateTask(title: "下午3点买点水果", date: "2026-11-25", remindBeforeMinutes: -1);
        var op2 = Assert.IsType<CreateTaskOp>(draft.Items[1].Operation);
        Assert.Equal(new TimeOnly(15, 0), op2.Draft.DueTime);
        Assert.Equal(TaskPriority.Low, op2.Draft.Priority);
        Assert.Null(op2.Draft.Reminder);

        // 显式自定义提前量 + 反复提醒
        tools.ProposeCreateTask(
            title: "项目评审会",
            date: "2026-11-22",
            priority: "High",
            dueTime: "14:00",
            remindBeforeMinutes: 30,
            repeatIntervalMinutes: 10,
            repeatMaxCount: 0);
        var op3 = Assert.IsType<CreateTaskOp>(draft.Items[2].Operation);
        Assert.Equal(new TimeOnly(14, 0), op3.Draft.DueTime);
        Assert.Equal(30, op3.Draft.Reminder?.OffsetMinutes);
        Assert.Equal(10, op3.Draft.Reminder?.RepeatIntervalMinutes);
        Assert.Equal(0, op3.Draft.Reminder?.RepeatMaxCount);
    }

    [Fact]
    public void ProposalCard_FormatsMonthDayDates_RemovesVerboseSubtitle_AndAppliesInlinePriorityAndReminderImmediately()
    {
        var repo = new InMemoryTaskRepository();
        var modelStore = new ModelProfileStore(new PlainSecretProtector(), Path.Combine(_tempDir, "models.json"));
        var vm = new AssistantPageViewModel(repo, modelStore, new ToastService(), () => { }, Today);

        // 图2：日期统一改为月+日（不再出现 11/22），含时刻显示 HH:mm
        var tomorrowOp = new CreateTaskOp(new TaskDraft
        {
            Title = "提交软著文档",
            Date = Today.AddDays(1),
            DueTime = new TimeOnly(9, 0),
            Reminder = TaskReminder.CreateDefault(),
            Priority = TaskPriority.High,
        });
        var proposalItem = new ProposalItem("p1", ProposalActionKind.CreateTask, "新增", "提交软著文档", "", "", tomorrowOp);
        var badge = AssistantPageViewModel.ExtractProposalBadges(proposalItem, repo, Today);

        Assert.Equal("明天 (11月22日) 09:00", badge.DateBadge);
        Assert.DoesNotContain("/", badge.DateBadge);
        Assert.Equal("高优先级", badge.PriorityBadge);
        Assert.Contains("提前 10 分钟", badge.ReminderBadge);
        Assert.Contains("08:50", badge.ReminderBadge);

        // 图3：卡片副标题为空，不展示傻瓜式引导文案
        var cardItem = new ChatProposalItemViewModel(
            actionTag: "新增",
            actionKind: "add",
            title: "提交软著文档",
            changeSummary: "",
            reason: "",
            operation: tomorrowOp,
            proposalItemId: "p1",
            dateBadgeText: badge.DateBadge,
            priorityBadgeText: badge.PriorityBadge,
            priorityTier: badge.PriorityTier,
            taskDate: badge.TaskDate,
            dueTime: badge.DueTime,
            reminder: badge.Reminder,
            reminderBadgeText: badge.ReminderBadge);

        var card = new ChatProposalCardViewModel(
            title: "已同步到日程",
            subtitle: string.Empty,
            items: [cardItem],
            applyCallback: _ => { },
            undoAllCallback: _ => { },
            undoItemsCallback: (_, _) => { });

        Assert.False(card.HasSubtitle);

        // 模拟已同步到真实仓储，图1 行内直接调节优先级与提醒（点击即生效，无需确认按钮）
        var created = repo.Create(tomorrowOp.Draft);
        card.MarkApplied("cs-1", 1, new Dictionary<string, string> { ["p1"] = created.Id }, isAutoApplied: true);
        Assert.False(card.HasSubtitle);
        Assert.True(cardItem.CanEditTaskProps);

        // 即时改优先级：High → Medium
        vm.OpenProposalPriorityMenu(card, cardItem);
        Assert.True(vm.IsProposalPriorityMenuOpen);
        vm.SelectActiveProposalPriority(TaskPriority.Medium);
        Assert.False(vm.IsProposalPriorityMenuOpen);
        Assert.Equal(TaskPriority.Medium, repo.GetById(created.Id)!.Priority);
        Assert.Equal("中优先级", cardItem.PriorityBadgeText);

        // 即时改提醒：通过 ProposalReminderEditor 改为提前 30 分钟 + 每 5 分钟反复（共 5 次）
        vm.OpenProposalReminderEditor(card, cardItem);
        Assert.True(vm.IsProposalReminderEditorOpen);
        vm.ProposalReminderEditor.SelectPresetOffset(30);
        vm.ProposalReminderEditor.SelectRepeatInterval(5);
        vm.ProposalReminderEditor.SelectRepeatMaxCount(5);

        var updatedTask = repo.GetById(created.Id)!;
        Assert.Equal(30, updatedTask.Reminder!.OffsetMinutes);
        Assert.Equal(5, updatedTask.Reminder.RepeatIntervalMinutes);
        Assert.Equal(5, updatedTask.Reminder.RepeatMaxCount);
        Assert.Contains("提前 30 分钟", cardItem.ReminderBadgeText);
        Assert.Contains("每5分×5", cardItem.ReminderBadgeText);

        // 测试 Pending 态同样即时修改 ProposalItem.Operation（点击应用时以修改后为准）
        var pendingItem = new ChatProposalItemViewModel("新增", "add", "开会", "", "", tomorrowOp, proposalItemId: "p2", taskDate: Today.AddDays(1), dueTime: new TimeOnly(9, 0));
        var pendingCard = new ChatProposalCardViewModel("待确认的日程改动", string.Empty, [pendingItem], _ => { }, _ => { }, (_, _) => { });
        vm.ApplyInlinePriority(pendingCard, pendingItem, TaskPriority.Low);
        vm.ApplyInlineReminder(pendingCard, pendingItem, new TimeOnly(10, 30), TaskReminder.CreateDefault(offsetMinutes: 60));
        var mutatedOp = Assert.IsType<CreateTaskOp>(pendingItem.Operation);
        Assert.Equal(TaskPriority.Low, mutatedOp.Draft.Priority);
        Assert.Equal(new TimeOnly(10, 30), mutatedOp.Draft.DueTime);
        Assert.Equal(60, mutatedOp.Draft.Reminder?.OffsetMinutes);
    }

    [Fact]
    public void TaskDetailAndRow_ManualReminderEditUpdatesTaskAndMiniBadgeImmediately()
    {
        var repo = new InMemoryTaskRepository();
        var task = repo.Create(new TaskDraft
        {
            Title = "手动设置提醒测试",
            Date = Today.AddDays(1),
            Priority = TaskPriority.Medium,
        });

        var detail = new MainTaskDetailViewModel(repo, task, Today, () => { }, _ => { });
        Assert.False(detail.HasReminder);

        detail.PrepareReminderEditor();
        detail.ReminderEditor.PickQuickTime("14:00");
        detail.ReminderEditor.SelectPresetOffset(20);
        detail.ReminderEditor.ToggleRepeat();

        var reloaded = repo.GetById(task.Id)!;
        Assert.Equal(new TimeOnly(14, 0), reloaded.DueTime);
        Assert.NotNull(reloaded.Reminder);
        Assert.Equal(20, reloaded.Reminder.OffsetMinutes);
        Assert.True(reloaded.Reminder.IsRepeating);
        Assert.True(detail.HasReminder);
        Assert.Contains("13:40", detail.ReminderChipLabel);

        var row = new MainTaskRowViewModel(reloaded, Today);
        Assert.True(row.HasReminder);
        Assert.Contains("13:40", row.ReminderBadgeText);
        Assert.Contains("14:00", row.DateLabel);
    }

    [Fact]
    public async Task ParallelSessionTitleGeneration_RunsConcurrentlyWithoutBlockingMainReplyOrWaitingForIt()
    {
        var dbPath = Path.Combine(_tempDir, "parallel-title.db");
        using var db = SqliteDatabase.Open(dbPath);
        using var repo = new SqliteTaskRepository(db);
        var conv = new SqliteConversationStore(db);

        var modelStore = new ModelProfileStore(new PlainSecretProtector(), Path.Combine(_tempDir, "m-par.json"));
        var profile = new ModelProfile
        {
            Name = "Test",
            PresetId = "deepseek",
            BaseUrl = "https://api.example.com/v1",
            Protocol = ModelProtocol.OpenAi,
            Model = "test-model",
            SupportsImages = true,
        };
        modelStore.SaveProfile(profile, "sk-test-key", setAsActive: true);

        // 场景 A：标题生成瞬间完成，而主回复被 gate 阻塞 → 标题应在主回复完成之前就已经更新！
        var allowMainReplyFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var titleCompletedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientFactory = new ParallelTestChatClientFactory(
            onTitleCall: async ct =>
            {
                await Task.Delay(10, ct);
                titleCompletedSignal.TrySetResult();
                return "「明早提交软著文档。」";
            },
            onMainCall: async ct =>
            {
                await allowMainReplyFinish.Task.WaitAsync(ct);
                return "好的，已经帮你安排好了！(*^▽^*)";
            });

        var host = new AssistantHost(
            repository: repo,
            schedule: repo,
            modelProvider: modelStore,
            conversationStore: conv,
            clientFactory: clientFactory,
            todayProvider: () => Today);

        using var vm = new AssistantPageViewModel(
            repository: repo,
            modelStore: modelStore,
            toast: new ToastService(),
            openModelSettings: () => { },
            today: Today,
            hostFactory: () => host);

        vm.DraftInput = "明天早上九点我要交一个软著文档，帮我记一下";
        var sendTask = vm.SendAsync();

        // 在主回复尚未放行前，标题生成就已经并行跑完并把左侧标题清洗更新完毕！
        await titleCompletedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await vm.ActiveTitleGenerationTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(sendTask.IsCompleted);
        Assert.Equal("明早提交软著文档", vm.Sessions[0].Title);

        // 放行主回复后，持久化数据库里依然保留生成的标题，绝不会被 RunAsync 覆盖回原始长文本！
        allowMainReplyFinish.TrySetResult();
        await sendTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("明早提交软著文档", vm.Sessions[0].Title);
        var persisted = conv.ListSessions(10).Single(s => s.Id == vm.Sessions[0].Id);
        Assert.Equal("明早提交软著文档", persisted.Title);

        // 场景 B：标题生成故意很慢，而主回复瞬间完成 → SendAsync 立即返回，绝不等待标题生成！
        var allowSlowTitleFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clientFactory.SetHandlers(
            onTitleCall: async ct =>
            {
                await allowSlowTitleFinish.Task.WaitAsync(ct);
                return "下周评审准备计划";
            },
            onMainCall: _ => Task.FromResult("主回复立刻完成了！"));

        vm.StartNewSession();
        vm.DraftInput = "帮我规划下周的产品评审准备计划";
        var fastSendTask = vm.SendAsync();
        await fastSendTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.ActiveTitleGenerationTask!.IsCompleted);

        allowSlowTitleFinish.TrySetResult();
        await vm.ActiveTitleGenerationTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("下周评审准备计划", vm.Sessions[0].Title);
    }

    private sealed class PlainSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public bool TryUnprotect(string ciphertext, out string plaintext)
        {
            plaintext = ciphertext;
            return true;
        }
    }

    private sealed class ParallelTestChatClientFactory : IChatClientFactory
    {
        private Func<CancellationToken, Task<string>> _onTitleCall;
        private Func<CancellationToken, Task<string>> _onMainCall;

        public ParallelTestChatClientFactory(
            Func<CancellationToken, Task<string>> onTitleCall,
            Func<CancellationToken, Task<string>> onMainCall)
        {
            _onTitleCall = onTitleCall;
            _onMainCall = onMainCall;
        }

        public void SetHandlers(
            Func<CancellationToken, Task<string>> onTitleCall,
            Func<CancellationToken, Task<string>> onMainCall)
        {
            _onTitleCall = onTitleCall;
            _onMainCall = onMainCall;
        }

        public IChatClient Create(ResolvedModel model) => new DelegateChatClient(() => _onTitleCall, () => _onMainCall);
    }

    private sealed class DelegateChatClient(
        Func<Func<CancellationToken, Task<string>>> titleHandler,
        Func<Func<CancellationToken, Task<string>>> mainHandler) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var text = await titleHandler()(cancellationToken);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var text = await mainHandler()(cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}

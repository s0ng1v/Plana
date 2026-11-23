using Microsoft.Data.Sqlite;
using VGList.App.Models;
using VGList.App.Services;
using VGList.App.Settings;
using VGList.Core.Abstractions;
using VGList.Core.Models;
using VGList.Data;
using VGList.Desktop.Main.Eisenhower;
using VGList.Desktop.Main.Settings;
using VGList.Desktop.Main.Tasks;
using VGList.Desktop.Services;
using VGList.Platform;
using VGList.Platform.Win.Win32;
using Xunit;

namespace VGList.Tests;

public sealed class P1FeaturesTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 21); // 2026-11-21 Saturday
    private readonly string _tempDir;

    public P1FeaturesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vglist-p1-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private string NewDbPath() => Path.Combine(_tempDir, $"{Guid.NewGuid():N}.db");

    // ==================== 1. 重复任务 ====================

    [Fact]
    public void RecurrenceCalculator_ComputesNextDatesAndIcsRRuleRoundtrip()
    {
        // 周五的工作日下一期跳过周末直达周一
        var friday = new DateOnly(2026, 11, 20);
        Assert.Equal(new DateOnly(2026, 11, 21), RecurrenceCalculator.NextDate(friday, RecurrenceKind.Daily));
        Assert.Equal(new DateOnly(2026, 11, 23), RecurrenceCalculator.NextDate(friday, RecurrenceKind.Weekdays));
        Assert.Equal(new DateOnly(2026, 11, 27), RecurrenceCalculator.NextDate(friday, RecurrenceKind.Weekly));
        Assert.Equal(new DateOnly(2026, 12, 20), RecurrenceCalculator.NextDate(friday, RecurrenceKind.Monthly));
        Assert.Equal(new DateOnly(2027, 11, 20), RecurrenceCalculator.NextDate(friday, RecurrenceKind.Yearly));

        // 农历每年：推进到下一个农历年（约 354~384 天）
        var lunarNext = RecurrenceCalculator.NextDate(friday, RecurrenceKind.LunarYearly);
        var diffDays = lunarNext.DayNumber - friday.DayNumber;
        Assert.InRange(diffDays, 350, 390);

        // RFC 5545 RRULE 互转无损
        RecurrenceKind[] kinds =
        [
            RecurrenceKind.Daily,
            RecurrenceKind.Weekdays,
            RecurrenceKind.Weekly,
            RecurrenceKind.Monthly,
            RecurrenceKind.Yearly,
            RecurrenceKind.LunarYearly,
        ];
        foreach (var kind in kinds)
        {
            var rrule = RecurrenceCalculator.ToIcsRRule(kind);
            Assert.False(string.IsNullOrWhiteSpace(rrule));
            Assert.Equal(kind, RecurrenceCalculator.ParseIcsRRule(rrule));
        }
    }

    [Fact]
    public void SqliteRepository_CompletingRecurringTaskSpawnsNextOccurrenceAndUndoRevertsAtomically()
    {
        var dbPath = NewDbPath();
        string origId;
        string changeSetId;

        using (var db = SqliteDatabase.Open(dbPath))
        using (var repo = new SqliteTaskRepository(db))
        {
            Assert.False(db.IsReadOnlyFutureVersion);

            var task = repo.Create(new TaskDraft
            {
                Title = "团队周会",
                Date = new DateOnly(2026, 11, 20),
                Priority = TaskPriority.Medium,
                Recurrence = RecurrenceKind.Weekly,
                Tags = ["工作", "例会"],
                Subtasks =
                [
                    new TaskSubtask { Title = "同步进度", IsCompleted = true },
                    new TaskSubtask { Title = "记录纪要", IsCompleted = false },
                ],
            });
            origId = task.Id;

            var cs = repo.Apply([new SetCompletedOp(origId, true)], ChangeSource.User, "完成周会");
            changeSetId = cs.Id;

            var all = repo.Query(new TaskQuery { IncludeCompleted = true });
            Assert.Equal(2, all.Count);

            var completedOrig = Assert.Single(all, t => t.Id == origId);
            Assert.True(completedOrig.IsCompleted);

            var next = Assert.Single(all, t => t.Id != origId);
            Assert.False(next.IsCompleted);
            Assert.Equal(new DateOnly(2026, 11, 27), next.Date);
            Assert.Equal(RecurrenceKind.Weekly, next.Recurrence);
            Assert.Equal(["工作", "例会"], next.Tags);
            Assert.Equal(2, next.Subtasks.Count);
            Assert.All(next.Subtasks, s => Assert.False(s.IsCompleted));
        }

        // 重开数据库确认持久化，再一键整批撤销应同时删掉新一期并恢复原任务未完成
        using (var db = SqliteDatabase.Open(dbPath))
        using (var repo = new SqliteTaskRepository(db))
        {
            Assert.Equal(2, repo.Query(new TaskQuery { IncludeCompleted = true }).Count);

            var undo = repo.Undo(changeSetId);
            Assert.True(undo.Success);

            var afterUndo = repo.Query(new TaskQuery { IncludeCompleted = true });
            var only = Assert.Single(afterUndo);
            Assert.Equal(origId, only.Id);
            Assert.False(only.IsCompleted);
        }
    }

    // ==================== 2. 回收站软删除 ====================

    [Fact]
    public void SqliteRepository_SoftDeleteRestorePermanentDeleteEmptyAndPurgeExpired()
    {
        var dbPath = NewDbPath();

        using var db = SqliteDatabase.Open(dbPath);
        using var repo = new SqliteTaskRepository(db);

        var t1 = repo.Create(new TaskDraft { Title = "待恢复任务", Date = Today });
        var t2 = repo.Create(new TaskDraft { Title = "待彻底删除任务", Date = Today });
        var t3 = repo.Create(new TaskDraft { Title = "过期回收站任务", Date = Today });

        // 1) Delete 进入回收站且对常规查询不可见
        var cs = repo.Apply([new DeleteTaskOp(t1.Id)], ChangeSource.User, "删除 t1");
        Assert.Null(repo.GetById(t1.Id));
        Assert.Single(repo.Query(new TaskQuery { DeletedOnly = true }), t => t.Id == t1.Id);

        // 2) Undo 立即恢复
        Assert.True(repo.Undo(cs.Id).Success);
        Assert.NotNull(repo.GetById(t1.Id));
        Assert.Empty(repo.Query(new TaskQuery { DeletedOnly = true }));

        // 3) 再次软删除 t1/t2/t3
        repo.Delete(t1.Id);
        repo.Delete(t2.Id);
        repo.Delete(t3.Id);
        Assert.Equal(3, repo.Query(new TaskQuery { DeletedOnly = true }).Count);

        // 4) RestoreFromTrash 恢复 t1
        repo.RestoreFromTrash(t1.Id);
        Assert.NotNull(repo.GetById(t1.Id));
        Assert.Equal(2, repo.Query(new TaskQuery { DeletedOnly = true }).Count);

        // 5) 手工将 t3 的 deleted_at 回拨到 35 天前，验证 schema_version=3 与 PurgeExpiredTrash(30)
        using (var rawConn = new SqliteConnection($"Data Source={dbPath}"))
        {
            rawConn.Open();
            using var verCmd = rawConn.CreateCommand();
            verCmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
            Assert.Equal(SqliteDatabase.LatestSchemaVersion, Convert.ToInt32(verCmd.ExecuteScalar()));

            using var cmd = rawConn.CreateCommand();
            cmd.CommandText = "UPDATE tasks SET deleted_at = $dt WHERE id = $id;";
            cmd.Parameters.AddWithValue("$dt", DateTimeOffset.Now.AddDays(-35).ToString("O"));
            cmd.Parameters.AddWithValue("$id", t3.Id);
            cmd.ExecuteNonQuery();
        }

        var purged = repo.PurgeExpiredTrash(30);
        Assert.Equal(1, purged);
        Assert.Single(repo.Query(new TaskQuery { DeletedOnly = true }), t => t.Id == t2.Id);

        // 6) EmptyTrash 清空剩余回收站
        var emptied = repo.EmptyTrash();
        Assert.Equal(1, emptied);
        Assert.Empty(repo.Query(new TaskQuery { DeletedOnly = true }));
        Assert.Single(repo.Query(new TaskQuery { IncludeCompleted = true }), t => t.Id == t1.Id);
    }

    [Fact]
    public void TasksPageViewModel_TrashSectionRestoreAndEmptyWork()
    {
        var repo = new InMemoryTaskRepository();
        var toast = new ToastService();
        var t1 = repo.Create(new TaskDraft { Title = "可恢复项", Date = Today });
        var t2 = repo.Create(new TaskDraft { Title = "待清空项", Date = Today });

        using var vm = new TasksPageViewModel(repo, toast, Today);
        vm.DeleteTaskWithUndo(t1);
        vm.DeleteTaskWithUndo(t2);

        Assert.Equal(2, vm.TrashCount);
        vm.SelectSection(MainSection.Trash);
        Assert.True(vm.IsTrashSelected);
        Assert.False(vm.IsInputVisible);
        Assert.Equal(2, vm.TrashedTasks.Count);

        vm.RestoreTrashedTask(vm.TrashedTasks.First(r => r.Id == t1.Id));
        Assert.Equal(1, vm.TrashCount);
        Assert.NotNull(repo.GetById(t1.Id));

        vm.BeginEmptyTrash();
        Assert.True(vm.IsConfirmingEmptyTrash);
        vm.ConfirmEmptyTrash();
        Assert.Equal(0, vm.TrashCount);
        Assert.Empty(vm.TrashedTasks);
        Assert.True(vm.IsListEmpty);
    }

    // ==================== 3. 标签 & 标签筛选器 ====================

    [Fact]
    public void TaskTagParser_ExtractsInlineTagsAndIgnoresColorsAndPureNumbers()
    {
        var (clean, tags) = TaskTagParser.ExtractInlineTags("整理周报 #工作 #汇报 #FF00AA #1234");
        Assert.Equal("整理周报 #FF00AA #1234", clean);
        Assert.Equal(["工作", "汇报"], tags);

        // 仅含标签导致标题为空时保留原文
        var (onlyTagTitle, onlyTags) = TaskTagParser.ExtractInlineTags("#工作");
        Assert.Equal("#工作", onlyTagTitle);
        Assert.Empty(onlyTags);
    }

    [Fact]
    public void TasksPageViewModel_InlineTagsSubmissionAndTagRecurrenceFiltersWork()
    {
        var repo = new InMemoryTaskRepository();
        var toast = new ToastService();
        using var vm = new TasksPageViewModel(repo, toast, Today);

        vm.DraftText = "提交季度总结 #工作 #重要";
        vm.SubmitDraft();

        var created = Assert.Single(repo.Query(new TaskQuery()));
        Assert.Equal("提交季度总结", created.Title);
        Assert.Equal(["工作", "重要"], created.Tags);

        var recTask = repo.Create(new TaskDraft
        {
            Title = "晨间复盘",
            Date = Today,
            Tags = ["生活"],
            Recurrence = RecurrenceKind.Daily,
        });

        Assert.True(vm.HasTagFilterBar);
        Assert.Contains(vm.TagFilterChips, c => c.Tag == "工作");
        Assert.Contains(vm.TagFilterChips, c => c.Tag == "生活");

        // 按 #生活 筛选
        vm.ToggleTagFilter("生活");
        Assert.Equal(1, vm.UnfinishedInViewCount);
        Assert.Equal(recTask.Id, vm.Groups.SelectMany(g => g.Tasks).Single().Id);

        // 再次点击取消筛选，然后切换仅看重复任务
        vm.ToggleTagFilter("生活");
        Assert.Equal(2, vm.UnfinishedInViewCount);
        vm.ToggleRecurringFilter();
        Assert.Equal(1, vm.UnfinishedInViewCount);
        Assert.Equal(recTask.Id, vm.Groups.SelectMany(g => g.Tasks).Single().Id);

        // 详情面板编辑标签与重复规则
        var detail = new MainTaskDetailViewModel(repo, created, Today, () => { }, _ => { });
        detail.NewTagText = "复盘";
        Assert.True(detail.AddTag());
        Assert.Contains("复盘", detail.Tags);
        detail.RemoveTag("重要");
        Assert.DoesNotContain("重要", detail.Tags);
        detail.PickRecurrence(RecurrenceKind.Monthly);
        Assert.Equal(RecurrenceKind.Monthly, repo.GetById(created.Id)!.Recurrence);
    }

    // ==================== 4. 四象限跨卡片拖拽改优先级 ====================

    [Fact]
    public void EisenhowerViewModel_MoveTaskToPriorityUpdatesQuadrantAndSupportsUndo()
    {
        var dbPath = NewDbPath();
        using var db = SqliteDatabase.Open(dbPath);
        using var repo = new SqliteTaskRepository(db);
        var toast = new ToastService();

        var task = repo.Create(new TaskDraft
        {
            Title = "准备架构评审",
            Date = Today,
            Priority = TaskPriority.None,
        });

        using var vm = new EisenhowerViewModel(repo, toast, Today);
        Assert.Single(vm.QuadrantIV.PendingTasks);
        Assert.Empty(vm.QuadrantI.PendingTasks);

        // 模拟从第四象限拖入第一象限
        var moved = vm.MoveTaskToPriority(task, TaskPriority.High);
        Assert.True(moved);
        Assert.Empty(vm.QuadrantIV.PendingTasks);
        Assert.Single(vm.QuadrantI.PendingTasks);
        Assert.Equal(TaskPriority.High, repo.GetById(task.Id)!.Priority);

        // 拖回同象限返回 false
        Assert.False(vm.MoveTaskToPriority(repo.GetById(task.Id)!, TaskPriority.High));

        // 通过 Toast 撤销回到原象限
        toast.ExecuteActionCommand.Execute(null);
        Assert.Equal(TaskPriority.None, repo.GetById(task.Id)!.Priority);
        Assert.Single(vm.QuadrantIV.PendingTasks);
        Assert.Empty(vm.QuadrantI.PendingTasks);
    }

    // ==================== 5. 数据导入 / 导出备份 ====================

    [Fact]
    public void TaskBackupService_JsonMarkdownAndIcsRoundtripWithDedupAndUndo()
    {
        var sourceRepo = new InMemoryTaskRepository();
        var workList = sourceRepo.CreateList("工作项目");
        sourceRepo.Create(new TaskDraft
        {
            Title = "季度汇报",
            Content = "第一行说明\n第二行补充",
            Date = new DateOnly(2026, 11, 25),
            Priority = TaskPriority.High,
            ListId = workList.Id,
            Recurrence = RecurrenceKind.Monthly,
            Tags = ["工作", "核心"],
            Subtasks =
            [
                new TaskSubtask { Title = "汇总数据", IsCompleted = true },
                new TaskSubtask { Title = "做 PPT", IsCompleted = false },
            ],
        });
        sourceRepo.Create(new TaskDraft
        {
            Title = "买牛奶",
            Date = new DateOnly(2026, 11, 22),
            Priority = TaskPriority.Low,
            IsCompleted = true,
            Recurrence = RecurrenceKind.LunarYearly,
            Tags = ["生活"],
        });

        var json = TaskBackupService.ExportJson(sourceRepo);
        var markdown = TaskBackupService.ExportMarkdown(sourceRepo);
        var ics = TaskBackupService.ExportIcs(sourceRepo);

        // 1) JSON 完整往返 + 去重 + 撤销
        using (var db = SqliteDatabase.Open(NewDbPath()))
        using (var target = new SqliteTaskRepository(db))
        {
            var res1 = TaskBackupService.ImportAuto("backup.json", json, Today, target);
            Assert.Equal(2, res1.ImportedCount);
            Assert.Equal(0, res1.SkippedCount);
            Assert.NotNull(res1.ChangeSetId);

            var imported = target.Query(new TaskQuery { IncludeCompleted = true });
            var q = Assert.Single(imported, t => t.Title == "季度汇报");
            Assert.Equal(TaskPriority.High, q.Priority);
            Assert.Equal(RecurrenceKind.Monthly, q.Recurrence);
            Assert.Equal(["工作", "核心"], q.Tags);
            Assert.Equal(2, q.Subtasks.Count);
            Assert.Contains("第一行说明", q.Content);

            var milk = Assert.Single(imported, t => t.Title == "买牛奶");
            Assert.True(milk.IsCompleted);
            Assert.Equal(RecurrenceKind.LunarYearly, milk.Recurrence);

            // 二次导入自动去重
            var res2 = TaskBackupService.ImportAuto("backup.json", json, Today, target);
            Assert.Equal(0, res2.ImportedCount);
            Assert.Equal(2, res2.SkippedCount);

            // 一键整批撤销导入
            Assert.True(target.Undo(res1.ChangeSetId!).Success);
            Assert.Empty(target.Query(new TaskQuery { IncludeCompleted = true }));
        }

        // 2) Markdown 往返
        var mdTarget = new InMemoryTaskRepository();
        var mdRes = TaskBackupService.ImportAuto("tasks.md", markdown, Today, mdTarget);
        Assert.Equal(2, mdRes.ImportedCount);
        var mdReport = Assert.Single(mdTarget.Query(new TaskQuery { IncludeCompleted = true }), t => t.Title == "季度汇报");
        Assert.Equal(TaskPriority.High, mdReport.Priority);
        Assert.Equal(RecurrenceKind.Monthly, mdReport.Recurrence);
        Assert.Equal(["工作", "核心"], mdReport.Tags);
        Assert.Equal(2, mdReport.Subtasks.Count);

        // 3) RFC 5545 ICS 日历往返
        Assert.Contains("BEGIN:VCALENDAR", ics);
        Assert.Contains("RRULE:FREQ=MONTHLY", ics);
        Assert.Contains("RRULE:FREQ=YEARLY;X-PLANA-LUNAR=1", ics);

        var icsTarget = new InMemoryTaskRepository();
        var icsRes = TaskBackupService.ImportAuto("calendar.ics", ics, Today, icsTarget);
        Assert.Equal(2, icsRes.ImportedCount);
        var icsReport = Assert.Single(icsTarget.Query(new TaskQuery { IncludeCompleted = true }), t => t.Title == "季度汇报");
        Assert.Equal(new DateOnly(2026, 11, 25), icsReport.Date);
        Assert.Equal(TaskPriority.High, icsReport.Priority);
        Assert.Equal(RecurrenceKind.Monthly, icsReport.Recurrence);
        Assert.Equal(["工作", "核心"], icsReport.Tags);
        Assert.Equal(2, icsReport.Subtasks.Count);
    }

    // ==================== 6. 开机自启动 & 小组件锁定 / 点击穿透 ====================

    [Fact]
    public void StartupCommandAndWidgetLockClickThrough_PersistAndSyncCorrectly()
    {
        Assert.Equal("\"C:\\Plana\\VGList.exe\" --minimized", WindowsStartupManager.BuildCommand(@"C:\Plana\VGList.exe", startMinimized: true));
        Assert.Equal("\"C:\\Plana\\VGList.exe\"", WindowsStartupManager.BuildCommand(@"C:\Plana\VGList.exe", startMinimized: false));

        var settingsPath = Path.Combine(_tempDir, "settings.json");
        var store = new SettingsStore(settingsPath);
        var repo = new InMemoryTaskRepository();
        var toast = new ToastService();

        var registryCalls = new List<(bool Enabled, bool Minimized)>();
        var coordinator = new WidgetCoordinator(
            repo,
            store,
            assistantDir: Path.Combine(_tempDir, "assistant"),
            mcpDir: Path.Combine(_tempDir, "mcp"),
            startupRegistrySync: (enabled, minimized) =>
            {
                registryCalls.Add((enabled, minimized));
                return true;
            });

        var modelStore = new ModelProfileStore(new NoopSecretProtector(), Path.Combine(_tempDir, "models.json"));
        using var vm = new SettingsPageViewModel(repo, coordinator, toast, modelStore, new ModelConnectionTester());

        vm.LaunchAtStartup = true;
        vm.StartMinimizedToTray = false;
        vm.TodayLocked = true;
        vm.TodayClickThrough = true;
        vm.MonthLocked = true;
        vm.MonthClickThrough = true;

        Assert.Equal(2, registryCalls.Count);
        Assert.Equal((true, true), registryCalls[0]);
        Assert.Equal((true, false), registryCalls[1]);

        // 从磁盘重新读取确认持久化
        var reloadedStore = new SettingsStore(settingsPath);
        var main = reloadedStore.LoadMain();
        var today = reloadedStore.LoadWidget();
        var month = reloadedStore.LoadMonth();
        Assert.True(main.LaunchAtStartup);
        Assert.False(main.StartMinimizedToTray);
        Assert.True(today.IsLocked);
        Assert.True(today.IsClickThrough);
        Assert.True(month.IsLocked);
        Assert.True(month.IsClickThrough);

        // 托盘一键解除全部小组件点击穿透
        coordinator.DisableAllClickThrough();
        Assert.False(vm.TodayClickThrough);
        Assert.False(vm.MonthClickThrough);
        Assert.False(reloadedStore.LoadWidget().IsClickThrough);
        Assert.False(reloadedStore.LoadMonth().IsClickThrough);
    }

    // ==================== 7. ADR-0054：已完成按日期倒序分组 & 月历动态周行数 ====================

    [Fact]
    public void CompletedView_GroupsByDateDescendingNewestFirstWithRelativeHeaders()
    {
        var repo = new InMemoryTaskRepository(
        [
            new TaskItem
            {
                Id = "c-old",
                Title = "更早完成的任务",
                Date = Today.AddDays(-5),
                IsCompleted = true,
                CompletedAt = new DateTimeOffset(2026, 11, 16, 15, 0, 0, TimeSpan.FromHours(8)),
                CreatedAt = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.FromHours(8)),
            },
            new TaskItem
            {
                Id = "c-yest",
                Title = "昨天完成的任务",
                Date = Today.AddDays(-1),
                IsCompleted = true,
                CompletedAt = new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.FromHours(8)),
                CreatedAt = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.FromHours(8)),
            },
            new TaskItem
            {
                Id = "c-today",
                Title = "今天完成的任务",
                Date = Today,
                IsCompleted = true,
                CompletedAt = new DateTimeOffset(2026, 11, 21, 11, 0, 0, TimeSpan.FromHours(8)),
                CreatedAt = new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.FromHours(8)),
            },
        ]);

        var toast = new ToastService();
        using var vm = new TasksPageViewModel(repo, toast, Today);
        Assert.Equal(3, vm.TotalCompletedCount);

        vm.SelectSection(MainSection.Completed);
        Assert.Equal(3, vm.Groups.Count);
        Assert.Equal("今天", vm.Groups[0].Header);
        Assert.Equal("昨天", vm.Groups[1].Header);
        Assert.Equal("11月16日 周一", vm.Groups[2].Header);
        Assert.Single(vm.Groups[0].Tasks, r => r.Id == "c-today");
        Assert.Single(vm.Groups[1].Tasks, r => r.Id == "c-yest");
        Assert.Single(vm.Groups[2].Tasks, r => r.Id == "c-old");
        Assert.False(vm.IsListEmpty);
    }

    [Fact]
    public void MonthWidget_ComputesExact4Or5Or6WeekRowsPerMonth()
    {
        var repo = new InMemoryTaskRepository();
        using var vm = new VGList.Widgets.Month.MonthWidgetViewModel(repo, Today);

        // 2026年11月：11/1 周日、30 天 → 5 行 35 格
        Assert.Equal(5, vm.WeekRows);
        Assert.Equal(35, vm.Days.Count);

        // 2026年2月：2/1 周日、28 天 → 4 行 28 格
        vm.JumpToMonth(2026, 2);
        Assert.Equal(4, vm.WeekRows);
        Assert.Equal(28, vm.Days.Count);
        Assert.All(vm.Days, d => Assert.True(d.IsCurrentMonth));

        // 2026年8月：8/1 周六、31 天 → 6 行 42 格
        vm.JumpToMonth(2026, 8);
        Assert.Equal(6, vm.WeekRows);
        Assert.Equal(42, vm.Days.Count);
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

    private sealed class NoopSecretProtector : ISecretProtector
    {
        public string Protect(string plainText) => plainText;

        public bool TryUnprotect(string cipherText, out string plainText)
        {
            plainText = cipherText;
            return true;
        }
    }
}

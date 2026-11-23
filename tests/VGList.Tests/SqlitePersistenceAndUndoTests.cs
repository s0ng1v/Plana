using Microsoft.Data.Sqlite;
using VGList.Core.Models;
using VGList.Data;
using Xunit;

namespace VGList.Tests;

public sealed class SqlitePersistenceAndUndoTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 21);
    private readonly string _tempDir;

    public SqlitePersistenceAndUndoTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vglist-sqlite-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private string NewDbPath() => Path.Combine(_tempDir, $"{Guid.NewGuid():N}.db");

    [Fact]
    public void Persistence_RestoresTasksListsSubtasksAndRescheduleCountAcrossReopen()
    {
        var dbPath = NewDbPath();
        string taskId;
        string listId;

        using (var db = SqliteDatabase.Open(dbPath))
        using (var repo = new SqliteTaskRepository(db))
        {
            var list = repo.CreateList("工作");
            listId = list.Id;
            var task = repo.Create(new TaskDraft
            {
                Title = "完成设计文档",
                Content = "首版草稿",
                Date = Today,
                Priority = TaskPriority.High,
                ListId = listId,
                Subtasks =
                [
                    new TaskSubtask { Title = "步骤一", IsCompleted = true },
                    new TaskSubtask { Title = "步骤二", IsCompleted = false },
                ],
            });
            taskId = task.Id;

            // 推迟两次（早于或等于原日期不递增，晚于原日期递增）
            task.Date = Today.AddDays(1);
            repo.Update(task);
            task.Date = Today.AddDays(3);
            repo.Update(task);
            task.Date = Today.AddDays(2); // 提前不递增
            repo.Update(task);
            repo.SetCompleted(taskId, true);
        }

        // 关闭并重新打开库
        using (var db2 = SqliteDatabase.Open(dbPath))
        using (var repo2 = new SqliteTaskRepository(db2))
        {
            Assert.Contains(repo2.Lists, l => l.Id == listId && l.Name == "工作");
            var restored = Assert.Single(repo2.Query(new TaskQuery { IncludeCompleted = true }));
            Assert.Equal(taskId, restored.Id);
            Assert.Equal("完成设计文档", restored.Title);
            Assert.Equal("首版草稿", restored.Content);
            Assert.Equal(new DateOnly(2026, 11, 23), restored.Date);
            Assert.Equal(2, restored.RescheduleCount);
            Assert.True(restored.IsCompleted);
            Assert.NotNull(restored.CompletedAt);
            Assert.Equal(2, restored.Subtasks.Count);
            Assert.True(restored.Subtasks[0].IsCompleted);
            Assert.Equal("步骤二", restored.Subtasks[1].Title);
        }
    }

    [Fact]
    public void Apply_IsAtomicAndProducesSingleUndoableChangeSet()
    {
        var dbPath = NewDbPath();
        using var db = SqliteDatabase.Open(dbPath);
        using var repo = new SqliteTaskRepository(db);

        var t1 = repo.Create(new TaskDraft { Title = "原任务", Date = Today, Priority = TaskPriority.Low });

        // 含一条无效操作 → 整体回滚，库无半成品
        var failingOps = new ScheduleOp[]
        {
            new CreateTaskOp(new TaskDraft { Title = "不应落库", Date = Today }),
            new UpdateTaskOp("non-existent-id", new TaskPatch { Title = "爆炸" }),
        };
        Assert.Throws<InvalidOperationException>(() => repo.Apply(failingOps, ChangeSource.Assistant, "测试回滚"));
        Assert.Single(repo.Query(new TaskQuery()));

        // 20 条合法操作 → 单个变更集
        var ops = new List<ScheduleOp>
        {
            new UpdateTaskOp(t1.Id, new TaskPatch
            {
                Date = Today.AddDays(2),
                Priority = TaskPriority.High,
                AppendSubtasks = [new TaskSubtask { Title = "第一步" }],
                AppendContentSection = new ContentSectionPatch("准备（助理）", "- 检查要点"),
            }),
        };
        for (var i = 0; i < 19; i++)
        {
            ops.Add(new CreateTaskOp(new TaskDraft { Title = $"批量任务 {i}", Date = Today.AddDays(1) }));
        }

        var cs = repo.Apply(ops, ChangeSource.Assistant, "批量排期");
        Assert.Equal(20, cs.Entries.Count);
        Assert.Equal(20, repo.Query(new TaskQuery()).Count);

        var updatedT1 = repo.Query(new TaskQuery()).Single(t => t.Id == t1.Id);
        Assert.Equal(Today.AddDays(2), updatedT1.Date);
        Assert.Equal(1, updatedT1.RescheduleCount);
        Assert.Contains("## 准备（助理）", updatedT1.Content);
        Assert.Single(updatedT1.Subtasks);

        // 整批撤销
        var undo = repo.Undo(cs.Id);
        Assert.True(undo.Success);
        Assert.Equal(20, undo.RevertedCount);
        Assert.Empty(undo.Skipped);

        var remaining = Assert.Single(repo.Query(new TaskQuery()));
        Assert.Equal(t1.Id, remaining.Id);
        Assert.Equal(Today, remaining.Date);
        Assert.Equal(TaskPriority.Low, remaining.Priority);
        Assert.Empty(remaining.Subtasks);
        Assert.Equal(string.Empty, remaining.Content);

        // 不可重复撤销
        var dup = repo.Undo(cs.Id);
        Assert.False(dup.Success);
    }

    [Fact]
    public void Undo_SkipsTasksModifiedLaterAndRevertsUnchangedOnes()
    {
        var dbPath = NewDbPath();
        using var db = SqliteDatabase.Open(dbPath);
        using var repo = new SqliteTaskRepository(db);

        var cs = repo.Apply(
            [
                new CreateTaskOp(new TaskDraft { Title = "任务A", Date = Today }),
                new CreateTaskOp(new TaskDraft { Title = "任务B", Date = Today }),
            ],
            ChangeSource.Assistant,
            "新建两条");

        var taskA = repo.Query(new TaskQuery()).Single(t => t.Title == "任务A");
        taskA.Title = "任务A（用户改过）";
        repo.Update(taskA);

        var undo = repo.Undo(cs.Id);
        Assert.True(undo.Success);
        Assert.Equal(1, undo.RevertedCount);
        Assert.Single(undo.Skipped);
        Assert.Equal(taskA.Id, undo.Skipped[0].EntityId);

        var left = Assert.Single(repo.Query(new TaskQuery()));
        Assert.Equal("任务A（用户改过）", left.Title);
    }

    [Fact]
    public void DeleteList_AndUndoRestoresListAndTaskOwnership()
    {
        var dbPath = NewDbPath();
        using var db = SqliteDatabase.Open(dbPath);
        using var repo = new SqliteTaskRepository(db);

        var work = repo.CreateList("项目A");
        var task = repo.Create(new TaskDraft { Title = "归属项目A", Date = Today, ListId = work.Id });

        repo.DeleteList(work.Id);
        Assert.DoesNotContain(repo.Lists, l => l.Id == work.Id);
        Assert.Equal(TaskList.InboxId, repo.Query(new TaskQuery()).Single(t => t.Id == task.Id).ListId);

        var deleteCs = repo.Recent(5).First(c => c.Label.Contains("删除清单"));
        var undo = repo.Undo(deleteCs.Id);
        Assert.True(undo.Success);
        Assert.Contains(repo.Lists, l => l.Id == work.Id && l.Name == "项目A");
        Assert.Equal(work.Id, repo.Query(new TaskQuery()).Single(t => t.Id == task.Id).ListId);
    }

    [Fact]
    public void CorruptDatabase_BacksUpAndStartsFresh()
    {
        var dbPath = NewDbPath();
        File.WriteAllText(dbPath, "this is definitely not a valid sqlite database header!!!");

        using var db = SqliteDatabase.Open(dbPath);
        Assert.True(db.RecoveredFromCorruption);
        Assert.NotNull(db.BackupCorruptPath);
        Assert.True(File.Exists(db.BackupCorruptPath));

        using var repo = new SqliteTaskRepository(db);
        var created = repo.Create(new TaskDraft { Title = "重建后可写入", Date = Today });
        Assert.Single(repo.Query(new TaskQuery()));
        Assert.Equal("重建后可写入", created.Title);
    }

    [Fact]
    public void ContentSectionPatch_AppendsAndReplacesWithoutTouchingOtherSections()
    {
        var initial = "背景说明\n\n## 我的笔记\n原文保留";
        var added = TaskPatch.ApplyContentSection(initial, "准备（助理）", "- 第一步\n- 第二步");
        Assert.Contains("原文保留", added);
        Assert.Contains("## 准备（助理）\n- 第一步\n- 第二步", added);

        var withTrailing = added + "\n\n## 结尾补充\n别动我";
        var replaced = TaskPatch.ApplyContentSection(withTrailing, "准备（助理）", "- 新提纲");
        Assert.DoesNotContain("- 第一步", replaced);
        Assert.Contains("## 准备（助理）\n- 新提纲", replaced);
        Assert.Contains("## 结尾补充\n别动我", replaced);
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
}

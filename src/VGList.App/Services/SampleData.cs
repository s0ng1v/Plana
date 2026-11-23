using VGList.Core.Abstractions;
using VGList.Core.Models;
using VGList.Data;

namespace VGList.App.Services;

/// <summary>
/// 持久化仓储初始化与按需示例数据（ADR-0061）：
/// 全新空库仅初始化内置「收集箱」清单，不自动写入演示任务；
/// 仅在设置页手动点击「重置为示例数据」或单测时才调用 CreateSeedTasks。
/// </summary>
public static class SampleData
{
    public static ITaskRepository CreateRepository() => new InMemoryTaskRepository(CreateSeedTasks());

    public static IReadOnlyList<TaskList> CreateSeedLists() =>
    [
        new TaskList { Id = TaskList.InboxId, Name = "收集箱", Color = "#3D7BE5" },
    ];

    /// <summary>打开本地 SQLite 仓储；空库仅初始化内置收集箱（0 条任务），只读或权限异常时降级到空内存仓储。</summary>
    public static (ITaskRepository Repository, IScheduleService Schedule, string? StartupNotice) OpenPersistentRepository(string? dbPath = null)
    {
        try
        {
            var db = SqliteDatabase.Open(dbPath);
            var repo = new SqliteTaskRepository(db);
            if (!repo.IsReadOnlyFutureVersion && repo.IsEmpty)
            {
                repo.SeedInitial(CreateSeedLists(), []);
            }

            string? notice = null;
            if (repo.RecoveredFromCorruption)
            {
                notice = "检测到本地数据库异常，已自动备份原文件并重新初始化";
            }
            else if (repo.IsReadOnlyFutureVersion)
            {
                notice = "数据库来自更新版本的 Plana，当前以只读模式打开";
            }

            return (repo, repo, notice);
        }
        catch
        {
            var fallback = new InMemoryTaskRepository();
            return (fallback, fallback, "本地数据目录不可写，当前以临时内存模式运行");
        }
    }

    public static IReadOnlyList<TaskItem> CreateSeedTasks()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var tomorrow = today.AddDays(1);
        var dayAfter = today.AddDays(2);
        var yesterday = today.AddDays(-1);
        var now = DateTimeOffset.Now;

        TaskItem Task(
            string title,
            DateOnly date,
            TaskPriority priority,
            bool completed,
            int minutesAgo,
            string listId = TaskList.InboxId,
            string content = "",
            IReadOnlyList<TaskSubtask>? subtasks = null) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            Content = content,
            Subtasks = subtasks?.ToList() ?? [],
            Date = date,
            Priority = priority,
            ListId = listId,
            IsCompleted = completed,
            CreatedAt = now.AddMinutes(-minutesAgo),
            CompletedAt = completed ? now.AddMinutes(-minutesAgo / 2) : null,
        };

        return
        [
            Task("可视化修改-3（后端）", today, TaskPriority.High, false, 5, content: "对齐状态同步接口与仓储变更通知。"),
            Task("整理本周会议纪要", today, TaskPriority.Medium, false, 10, subtasks:
            [
                new TaskSubtask { Title = "汇总各组周报", IsCompleted = true },
                new TaskSubtask { Title = "整理下周排期", IsCompleted = false },
                new TaskSubtask { Title = "发送参会成员", IsCompleted = false },
            ]),
            Task("回复客户邮件", today, TaskPriority.Low, false, 15),
            Task("阅读《重构》第 6 章", today, TaskPriority.None, false, 20),
            Task("提交周报", tomorrow, TaskPriority.Medium, false, 25),
            Task("预约牙医", tomorrow, TaskPriority.None, false, 30),
            Task("准备下周迭代评审演示", dayAfter, TaskPriority.High, false, 32),
            Task("回复设计评审意见", yesterday, TaskPriority.High, false, 35),
            Task("了解 Plana 的基本用法", today, TaskPriority.None, false, 38, TaskList.InboxId, "试试点击复选框完成任务，或在月历上查看全部日程。"),
            Task("把桌面小组件拖到顺手的位置", tomorrow, TaskPriority.None, false, 42, TaskList.InboxId),
            Task("校园网助手申请软著", today, TaskPriority.None, true, 40),
            Task("可视化修改-2（前端）", today, TaskPriority.None, true, 45),
            Task("可视化修改-1", yesterday, TaskPriority.None, true, 50),
            Task("备份工作资料", today, TaskPriority.Low, true, 55),
        ];
    }
}

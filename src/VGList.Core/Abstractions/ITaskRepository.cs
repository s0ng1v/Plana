using VGList.Core.Models;

namespace VGList.Core.Abstractions;

/// <summary>
/// 任务的唯一读写入口。P0 由内存实现，接入 SQLite 时替换实现，UI 不变。
/// </summary>
public interface ITaskRepository
{
    IReadOnlyList<TaskItem> Query(TaskQuery query);

    /// <summary>全部清单，收集箱始终在首位。</summary>
    IReadOnlyList<TaskList> Lists { get; }

    TaskList CreateList(string name);

    void RenameList(string id, string name);

    /// <summary>删除清单；其中的任务自动移入收集箱。收集箱本身不可删除。</summary>
    void DeleteList(string id);

    TaskItem Create(TaskDraft draft);

    void Update(TaskItem task);

    void SetCompleted(string id, bool completed);

    /// <summary>软删除任务（移入回收站，可恢复或通过撤销还原）。</summary>
    void Delete(string id);

    /// <summary>从回收站恢复任务。</summary>
    void RestoreFromTrash(string id);

    /// <summary>从回收站彻底删除单条任务（不可恢复）。</summary>
    void PermanentlyDelete(string id);

    /// <summary>清空回收站中全部任务，返回清除条数。</summary>
    int EmptyTrash();

    /// <summary>自动清理超过保留天数的回收站任务。</summary>
    int PurgeExpiredTrash(int retentionDays = 30);

    /// <summary>返回当前全部未删除任务使用过的去重标签列表（按频次降序）。</summary>
    IReadOnlyList<string> GetAllTags();

    /// <summary>静默记录提醒触发次数与时间（不进入用户撤销栈，不影响 UI 焦点）。</summary>
    void RecordReminderFired(string id, int firedCount, DateTimeOffset firedAt);

    event EventHandler<TaskChangedEventArgs>? Changed;

    event EventHandler? ListsChanged;
}

public static class TaskRepositoryExtensions
{
    public static TaskItem? GetById(this ITaskRepository repository, string id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : repository.Query(new TaskQuery { IncludeCompleted = true }).FirstOrDefault(t => t.Id == id);
}

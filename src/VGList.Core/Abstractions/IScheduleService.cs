using VGList.Core.Models;

namespace VGList.Core.Abstractions;

/// <summary>
/// 统一批量写入与整批撤销入口。任何通过本服务提交的写操作都是原子的，并形成单个可撤销变更集。
/// </summary>
public interface IScheduleService
{
    ChangeSet Apply(IReadOnlyList<ScheduleOp> ops, ChangeSource source, string label);

    UndoResult Undo(string changeSetId);

    /// <summary>局部撤销变更集中的指定实体（支持单条撤销与多选撤销；全部条目撤销后自动标记变更集已撤销）。</summary>
    UndoResult UndoEntries(string changeSetId, IReadOnlyCollection<string> entityIds);

    /// <summary>撤销最近一次未撤销的指定来源变更集（在限定时间窗口内）。</summary>
    UndoResult? UndoLatest(IReadOnlyCollection<ChangeSource> sources, TimeSpan within);

    IReadOnlyList<ChangeSet> Recent(int count, ChangeSource? source = null);

    /// <summary>将任务重置为给定示例集（作为一个可撤销的 User 变更集）。</summary>
    ChangeSet ReplaceAllTasks(IEnumerable<TaskItem> replacement, string label = "重置为示例数据");

    event EventHandler<ChangeSet>? ChangeSetApplied;
}

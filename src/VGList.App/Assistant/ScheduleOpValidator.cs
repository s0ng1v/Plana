using VGList.Core.Abstractions;
using VGList.Core.Models;

namespace VGList.App.Assistant;

/// <summary>
/// 日程变更操作严格参数校验器（A3 提案工具与 A7 MCP 共用）：
/// 确保任务 Id 真实存在、标题非空、日期有效、单批提案条数不超上限，非法参数返回明确中文错误供模型自纠。
/// </summary>
public static class ScheduleOpValidator
{
    public const int MaxProposalItems = 50;

    public static (bool IsValid, string? Error) Validate(ScheduleOp op, ITaskRepository repository)
    {
        switch (op)
        {
            case CreateTaskOp create:
                if (string.IsNullOrWhiteSpace(create.Draft.Title))
                {
                    return (false, "任务标题不能为空");
                }

                if (create.Draft.Title.Trim().Length > 200)
                {
                    return (false, "任务标题过长（上限 200 字）");
                }

                if (create.Draft.Date.Year is < 1901 or > 2100)
                {
                    return (false, $"日期「{create.Draft.Date:yyyy-MM-dd}」超出允许范围（1901–2100）");
                }

                if (!string.IsNullOrWhiteSpace(create.Draft.ListId) &&
                    create.Draft.ListId != TaskList.InboxId &&
                    !repository.Lists.Any(l => l.Id == create.Draft.ListId))
                {
                    return (false, $"目标清单 Id「{create.Draft.ListId}」不存在，请先调用 list_lists 查询或创建清单");
                }

                return (true, null);

            case UpdateTaskOp update:
                if (string.IsNullOrWhiteSpace(update.TaskId))
                {
                    return (false, "必须提供有效的任务 Id");
                }

                var existing = repository.GetById(update.TaskId);
                if (existing is null)
                {
                    return (false, $"任务 Id「{update.TaskId}」不存在，请先调用 query_tasks 获取真实 Id，不得编造");
                }

                if (update.Patch.Title is not null && string.IsNullOrWhiteSpace(update.Patch.Title))
                {
                    return (false, "修改后的任务标题不能为空");
                }

                if (update.Patch.Date is { } newDate && newDate.Year is < 1901 or > 2100)
                {
                    return (false, $"日期「{newDate:yyyy-MM-dd}」超出允许范围（1901–2100）");
                }

                if (update.Patch.ListId is { } listId &&
                    listId != TaskList.InboxId &&
                    !repository.Lists.Any(l => l.Id == listId))
                {
                    return (false, $"目标清单 Id「{listId}」不存在");
                }

                return (true, null);

            case SetCompletedOp complete:
                if (string.IsNullOrWhiteSpace(complete.TaskId) || repository.GetById(complete.TaskId) is null)
                {
                    return (false, $"任务 Id「{complete.TaskId}」不存在");
                }

                return (true, null);

            case DeleteTaskOp delete:
                if (string.IsNullOrWhiteSpace(delete.TaskId) || repository.GetById(delete.TaskId) is null)
                {
                    return (false, $"任务 Id「{delete.TaskId}」不存在");
                }

                return (true, null);

            case CreateListOp createList:
                if (string.IsNullOrWhiteSpace(createList.Name))
                {
                    return (false, "清单名称不能为空");
                }

                if (createList.Name.Trim().Length > 40)
                {
                    return (false, "清单名称过长（上限 40 字）");
                }

                return (true, null);

            default:
                return (false, "不支持的操作类型");
        }
    }

    public static bool TryParseStrictIsoDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date);
}

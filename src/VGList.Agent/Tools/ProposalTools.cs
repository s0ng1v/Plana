using System.ComponentModel;
using Microsoft.Extensions.AI;
using VGList.Agent.Proposals;
using VGList.App.Assistant;
using VGList.Core.Abstractions;
using VGList.Core.Models;

namespace VGList.Agent.Tools;

/// <summary>
/// 提案工具集（安全红线：绝不直接写仓储！仅向当前运行的 RunProposalDraft 追加并通过 IProposalValidator 校验回灌）。
/// </summary>
public sealed class ProposalTools
{
    private readonly ITaskRepository _repository;
    private readonly RunProposalDraft _draft;
    private readonly IProposalValidator? _validator;
    private readonly Action<AssistantEvent> _emit;
    private readonly PermissionPolicy? _policy;
    private readonly Func<DateOnly> _todayProvider;

    public ProposalTools(
        ITaskRepository repository,
        RunProposalDraft draft,
        IProposalValidator? validator,
        Action<AssistantEvent> emit,
        PermissionPolicy? policy = null,
        Func<DateOnly>? todayProvider = null)
    {
        _repository = repository;
        _draft = draft;
        _validator = validator;
        _emit = emit;
        _policy = policy;
        _todayProvider = todayProvider ?? (() => DateOnly.FromDateTime(DateTime.Today));
    }

    public IReadOnlyList<AITool> CreateTools() =>
    [
        AIFunctionFactory.Create(ProposeCreateTask, "propose_create_task", "新建一条日程任务（务必自动判别 priority；若有具体时刻请传 dueTime，默认提前 10 分钟提醒，可按需调整提前量与反复提醒）"),
        AIFunctionFactory.Create(ProposeUpdateTask, "propose_update_task", "修改已有日程任务（改期/改时刻/调整提醒与反复提醒/改优先级/改标题/归类清单/追加子待办）"),
        AIFunctionFactory.Create(ProposeCompleteTask, "propose_complete_task", "标记任务已完成或取消完成（默认直接生效并支持撤销）"),
        AIFunctionFactory.Create(ProposeDeleteTask, "propose_delete_task", "删除已有任务（默认需用户点击确认；若用户在设置中开启了自动删除则会直接执行并支持撤销）"),
        AIFunctionFactory.Create(ProposeCreateList, "propose_create_list", "新建一个自定义任务清单"),
        AIFunctionFactory.Create(RemoveProposalItem, "remove_proposal_item", "从当前提案草稿中移除某条已添加的建议（用于根据规则违规自纠替换）"),
    ];

    public object ProposeCreateTask(
        [Description("任务标题")] string title,
        [Description("任务日期，严格格式 yyyy-MM-dd")] string date,
        [Description("自动判别后的优先级：High / Medium / Low / None")] string? priority = null,
        [Description("目标清单 Id，默认 inbox")] string? listId = null,
        [Description("可选任务详情描述")] string? content = null,
        [Description("可选子待办标题数组")] string[]? subtasks = null,
        [Description("向用户展示的简短建议理由")] string? reason = null,
        [Description("可选具体时刻 24 小时制 HH:mm（如 09:00、15:30）")] string? dueTime = null,
        [Description("可选提前提醒分钟数：0=准时，10=提前10分钟（有时刻时默认10），可为任意正整数；传 -1 表示不提醒")] int? remindBeforeMinutes = null,
        [Description("可选反复提醒间隔（分钟，>0 开启反复提醒，例如 5、10、15）")] int? repeatIntervalMinutes = null,
        [Description("可选反复提醒总次数上限（含首次；0 表示持续提醒直至任务完成，默认 3）")] int? repeatMaxCount = null)
    {
        return Track("propose_create_task", $"Lana 正在添加「{title}」…", $"新建任务「{title}」", () =>
        {
            if (!ScheduleOpValidator.TryParseStrictIsoDate(date, out var parsedDate))
            {
                return new { ok = false, error = $"日期「{date}」无效，必须使用 yyyy-MM-dd 格式。" };
            }

            TimeOnly? parsedDueTime = null;
            if (!string.IsNullOrWhiteSpace(dueTime))
            {
                if (!TaskReminderCalculator.TryParseTimeOnly(dueTime, out var dt))
                {
                    return new { ok = false, error = $"时刻「{dueTime}」格式无效，请使用 24 小时制 HH:mm（例如 09:00）。" };
                }

                parsedDueTime = dt;
            }
            else
            {
                parsedDueTime = TaskReminderCalculator.TryExtractClockTime(title);
            }

            TaskPriority parsedPriority;
            if (string.IsNullOrWhiteSpace(priority))
            {
                parsedPriority = TaskReminderCalculator.InferPriority(title, parsedDate, _todayProvider(), parsedDueTime);
            }
            else if (!TryParsePriority(priority, out parsedPriority))
            {
                return new { ok = false, error = $"优先级「{priority}」无效，仅允许 None / Low / Medium / High。" };
            }

            TaskReminder? reminder = null;
            if (remindBeforeMinutes != -1)
            {
                var shouldArm = remindBeforeMinutes is >= 0 ||
                                repeatIntervalMinutes is > 0 ||
                                parsedDueTime is not null;
                if (shouldArm)
                {
                    var offset = remindBeforeMinutes is >= 0 ? remindBeforeMinutes.Value : TaskReminder.DefaultOffsetMinutes;
                    var repInt = repeatIntervalMinutes is > 0 ? repeatIntervalMinutes : null;
                    var repMax = repeatMaxCount is >= 0 ? repeatMaxCount.Value : TaskReminder.DefaultRepeatMaxCount;
                    reminder = TaskReminder.CreateDefault(offset, repInt, repMax);
                }
            }

            var targetList = string.IsNullOrWhiteSpace(listId) ? TaskList.InboxId : listId.Trim();
            var draft = new TaskDraft
            {
                Title = title.Trim(),
                Date = parsedDate,
                DueTime = parsedDueTime,
                Reminder = reminder,
                Priority = parsedPriority,
                ListId = targetList,
                Content = content?.Trim() ?? string.Empty,
                Subtasks = subtasks?
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => new TaskSubtask { Title = s.Trim() })
                    .ToList(),
            };

            var op = new CreateTaskOp(draft);
            var (valid, err) = ScheduleOpValidator.Validate(op, _repository);
            if (!valid)
            {
                return new { ok = false, error = err };
            }

            var summaryParts = new List<string>
            {
                parsedDueTime is { } pt ? $"{parsedDate:M月d日} {pt:HH:mm}" : $"{parsedDate:M月d日}",
                FormatPriority(parsedPriority),
            };
            if (reminder is { Enabled: true })
            {
                summaryParts.Add(TaskReminderCalculator.FormatOffsetLabel(reminder.OffsetMinutes) + "提醒");
            }

            var item = new ProposalItem(
                Id: Guid.NewGuid().ToString("N")[..12],
                Kind: ProposalActionKind.CreateTask,
                ActionTag: "新增",
                Title: draft.Title,
                ChangeSummary: string.Join(" · ", summaryParts),
                Reason: string.IsNullOrWhiteSpace(reason) ? "根据对话内容建议创建" : reason.Trim(),
                Operation: op);

            return AddAndValidate(item);
        });
    }

    public object ProposeUpdateTask(
        [Description("必须为 query_tasks 返回的真实任务 Id")] string taskId,
        [Description("新日期 yyyy-MM-dd，不修改则留空")] string? newDate = null,
        [Description("新标题，不修改则留空")] string? newTitle = null,
        [Description("新优先级 None/Low/Medium/High，不修改则留空")] string? newPriority = null,
        [Description("新清单 Id，不修改则留空")] string? newListId = null,
        [Description("追加的子待办标题列表，不追加则留空")] string[]? appendSubtasks = null,
        [Description("向用户展示的简短理由")] string? reason = null,
        [Description("新时刻 HH:mm（传 clear 可清除时刻），不修改则留空")] string? newDueTime = null,
        [Description("修改提前提醒分钟数（>=0 开启并设置提前量；-1 关闭提醒），不修改则留空")] int? remindBeforeMinutes = null,
        [Description("修改反复提醒间隔（分钟；>0 开启，0 关闭反复提醒），不修改则留空")] int? repeatIntervalMinutes = null,
        [Description("修改反复提醒总次数上限（0=持续直至完成），不修改则留空")] int? repeatMaxCount = null)
    {
        return Track("propose_update_task", "Lana 正在调整日程安排…", "调整日程任务", () =>
        {
            var existing = _repository.GetById(taskId);
            if (existing is null)
            {
                return new { ok = false, error = $"任务 Id「{taskId}」不存在，请先调用 query_tasks 获取真实 Id。" };
            }

            DateOnly? parsedDate = null;
            if (!string.IsNullOrWhiteSpace(newDate))
            {
                if (!ScheduleOpValidator.TryParseStrictIsoDate(newDate, out var d))
                {
                    return new { ok = false, error = $"newDate「{newDate}」格式无效，必须为 yyyy-MM-dd。" };
                }

                parsedDate = d;
            }

            TimeOnly? parsedDueTime = null;
            var clearDueTime = false;
            if (!string.IsNullOrWhiteSpace(newDueTime))
            {
                var lowered = newDueTime.Trim().ToLowerInvariant();
                if (lowered is "clear" or "none" or "无")
                {
                    clearDueTime = true;
                }
                else if (TaskReminderCalculator.TryParseTimeOnly(newDueTime, out var dt))
                {
                    parsedDueTime = dt;
                }
                else
                {
                    return new { ok = false, error = $"newDueTime「{newDueTime}」格式无效，请使用 HH:mm 或 clear。" };
                }
            }

            TaskPriority? parsedPriority = null;
            if (!string.IsNullOrWhiteSpace(newPriority))
            {
                if (!TryParsePriority(newPriority, out var p))
                {
                    return new { ok = false, error = $"newPriority「{newPriority}」无效，仅允许 None / Low / Medium / High。" };
                }

                parsedPriority = p;
            }

            TaskReminder? updatedReminder = null;
            var clearReminder = false;
            if (remindBeforeMinutes == -1)
            {
                clearReminder = true;
            }
            else if (remindBeforeMinutes is >= 0 || repeatIntervalMinutes is not null || repeatMaxCount is not null)
            {
                updatedReminder = existing.Reminder?.Clone() ?? TaskReminder.CreateDefault();
                updatedReminder.Enabled = true;
                if (remindBeforeMinutes is >= 0)
                {
                    updatedReminder.OffsetMinutes = remindBeforeMinutes.Value;
                    updatedReminder.AbsoluteAt = null;
                }

                if (repeatIntervalMinutes is { } ri)
                {
                    updatedReminder.RepeatIntervalMinutes = ri > 0 ? ri : null;
                }

                if (repeatMaxCount is { } rm)
                {
                    updatedReminder.RepeatMaxCount = Math.Max(0, rm);
                }

                updatedReminder.ResetTriggerState();
            }
            else if (parsedDueTime is not null && existing.Reminder is null)
            {
                updatedReminder = TaskReminder.CreateDefault();
            }

            var subs = appendSubtasks?
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => new TaskSubtask { Title = s.Trim() })
                .ToList();

            var patch = new TaskPatch
            {
                Title = string.IsNullOrWhiteSpace(newTitle) ? null : newTitle.Trim(),
                Date = parsedDate,
                DueTime = parsedDueTime,
                ClearDueTime = clearDueTime,
                Reminder = updatedReminder,
                ClearReminder = clearReminder,
                Priority = parsedPriority,
                ListId = string.IsNullOrWhiteSpace(newListId) ? null : newListId.Trim(),
                AppendSubtasks = subs is { Count: > 0 } ? subs : null,
            };

            var hasReminderPatch = patch.DueTime is not null || patch.ClearDueTime || patch.Reminder is not null || patch.ClearReminder;
            if (patch.Title is null && patch.Date is null && patch.Priority is null && patch.ListId is null && patch.AppendSubtasks is null && !hasReminderPatch)
            {
                return new { ok = false, error = "至少需要提供一项要修改的字段。" };
            }

            var op = new UpdateTaskOp(taskId, patch);
            var (valid, err) = ScheduleOpValidator.Validate(op, _repository);
            if (!valid)
            {
                return new { ok = false, error = err };
            }

            // 准确归类权限种类：仅追加子待办 → AppendSubtasks；仅移动清单 → CategorizeList；否则 UpdateTitleOrDate
            var onlySubtasks = patch.AppendSubtasks is { Count: > 0 }
                && patch.Title is null && patch.Date is null && patch.Priority is null && patch.ListId is null && !hasReminderPatch;
            var onlyList = patch.ListId is not null
                && patch.Title is null && patch.Date is null && patch.Priority is null && patch.AppendSubtasks is null && !hasReminderPatch;

            var kind = onlySubtasks ? ProposalActionKind.AppendSubtasks
                : onlyList ? ProposalActionKind.CategorizeList
                : ProposalActionKind.UpdateTitleOrDate;

            var actionTag = patch.Date is not null ? "改期"
                : onlySubtasks ? "拆步骤"
                : onlyList ? "归类"
                : "修改";

            var parts = new List<string>();
            if (patch.Date is { } nd) parts.Add($"{existing.Date:M月d日} → {nd:M月d日}");
            if (patch.DueTime is { } ntTime) parts.Add($"时刻 {ntTime:HH:mm}");
            else if (patch.ClearDueTime) parts.Add("清除时刻");
            if (patch.ClearReminder) parts.Add("关闭提醒");
            else if (patch.Reminder is { Enabled: true } nr) parts.Add(TaskReminderCalculator.FormatOffsetLabel(nr.OffsetMinutes) + "提醒");
            if (patch.Title is { } nt) parts.Add($"改名「{nt}」");
            if (patch.Priority is { } np) parts.Add($"优先级 → {FormatPriority(np)}");
            if (patch.ListId is { } nl) parts.Add($"移至清单「{_repository.Lists.FirstOrDefault(l => l.Id == nl)?.Name ?? nl}」");
            if (patch.AppendSubtasks is { Count: > 0 } sList) parts.Add($"追加 {sList.Count} 个子待办");

            var item = new ProposalItem(
                Id: Guid.NewGuid().ToString("N")[..12],
                Kind: kind,
                ActionTag: actionTag,
                Title: existing.Title,
                ChangeSummary: string.Join(" · ", parts),
                Reason: string.IsNullOrWhiteSpace(reason) ? "优化日程安排" : reason.Trim(),
                Operation: op);

            return AddAndValidate(item);
        });
    }

    public object ProposeCompleteTask(
        [Description("真实任务 Id")] string taskId,
        [Description("true=标记完成，false=取消完成")] bool isCompleted = true,
        [Description("简短理由")] string? reason = null)
    {
        return Track("propose_complete_task", "Lana 正在更新任务状态…", isCompleted ? "标记任务已完成" : "恢复任务未完成", () =>
        {
            var existing = _repository.GetById(taskId);
            if (existing is null)
            {
                return new { ok = false, error = $"任务 Id「{taskId}」不存在。" };
            }

            var op = new SetCompletedOp(taskId, isCompleted);
            var item = new ProposalItem(
                Id: Guid.NewGuid().ToString("N")[..12],
                Kind: ProposalActionKind.CompleteTask,
                ActionTag: isCompleted ? "完成" : "重开",
                Title: existing.Title,
                ChangeSummary: isCompleted ? "标记为已完成" : "恢复为未完成",
                Reason: string.IsNullOrWhiteSpace(reason) ? "更新任务进度" : reason.Trim(),
                Operation: op);

            return AddAndValidate(item);
        });
    }

    public object ProposeDeleteTask(
        [Description("真实任务 Id")] string taskId,
        [Description("删除理由")] string? reason = null)
    {
        return Track("propose_delete_task", "Lana 正在准备删除确认…", "准备删除任务", () =>
        {
            var existing = _repository.GetById(taskId);
            if (existing is null)
            {
                return new { ok = false, error = $"任务 Id「{taskId}」不存在。" };
            }

            var op = new DeleteTaskOp(taskId);
            var item = new ProposalItem(
                Id: Guid.NewGuid().ToString("N")[..12],
                Kind: ProposalActionKind.DeleteTask,
                ActionTag: "删除",
                Title: existing.Title,
                ChangeSummary: $"将永久删除（{existing.Date:M月d日}，应用后可撤销）",
                Reason: string.IsNullOrWhiteSpace(reason) ? "清理冗余任务" : reason.Trim(),
                Operation: op);

            return AddAndValidate(item);
        });
    }

    public object ProposeCreateList(
        [Description("新清单名称")] string name,
        [Description("简短理由")] string? reason = null)
    {
        return Track("propose_create_list", $"Lana 正在创建清单「{name}」…", $"新建清单「{name}」", () =>
        {
            var op = new CreateListOp(name.Trim());
            var (valid, err) = ScheduleOpValidator.Validate(op, _repository);
            if (!valid)
            {
                return new { ok = false, error = err };
            }

            var item = new ProposalItem(
                Id: Guid.NewGuid().ToString("N")[..12],
                Kind: ProposalActionKind.CreateList,
                ActionTag: "新建清单",
                Title: op.Name,
                ChangeSummary: "新建自定义清单",
                Reason: string.IsNullOrWhiteSpace(reason) ? "归类相关事项" : reason.Trim(),
                Operation: op);

            return AddAndValidate(item);
        });
    }

    public object RemoveProposalItem([Description("要从当前提案草稿中移除的 itemId")] string itemId)
    {
        var removed = _draft.RemoveById(itemId);
        return new { ok = removed, message = removed ? "已移除该提案条目，可继续添加修正后的方案。" : $"未找到条目 {itemId}" };
    }

    private object AddAndValidate(ProposalItem item)
    {
        if (!_draft.TryAdd(item, out var addErr))
        {
            return new { ok = false, error = addErr };
        }

        if (_validator is null)
        {
            var autoNoVal = _policy?.Evaluate(item.Kind) == PermissionLevel.Auto;
            return new
            {
                ok = true,
                itemId = item.Id,
                willAutoApply = autoNoVal,
                executionHint = autoNoVal
                    ? "该项将随本轮回复自动写入日程（用户随时可撤销），回复时直接告知已处理好即可，不要说「请确认后生效」。"
                    : "该项需要用户在下方卡片点击「确认执行」后才会生效。",
                violations = Array.Empty<object>(),
            };
        }

        var map = _validator.Validate(_draft.Items);
        _draft.AttachViolations(map);

        if (map.TryGetValue(item.Id, out var itemViolations) && itemViolations.Count > 0)
        {
            var canCorrect = _draft.SelfCorrectionRoundsUsed < RunProposalDraft.MaxSelfCorrectionRounds;
            if (canCorrect)
            {
                _draft.IncrementCorrectionRound();
                return new
                {
                    ok = true,
                    itemId = item.Id,
                    willAutoApply = false,
                    needsCorrection = true,
                    remainingCorrectionRounds = RunProposalDraft.MaxSelfCorrectionRounds - _draft.SelfCorrectionRoundsUsed,
                    violations = itemViolations.Select(v => new { ruleId = v.RuleId, message = v.Message }).ToList(),
                    instruction = "该条建议触发了用户规则限制，请优先调用 remove_proposal_item 移除并重新提出合规安排；若确需保留请在回复中说明原因交由用户裁决。",
                };
            }

            return new
            {
                ok = true,
                itemId = item.Id,
                willAutoApply = false,
                needsCorrection = false,
                violations = itemViolations.Select(v => new { ruleId = v.RuleId, message = v.Message }).ToList(),
                instruction = "自纠次数已达上限，将附带违规提醒交付用户手动裁决。",
            };
        }

        var willAuto = _policy?.Evaluate(item.Kind) == PermissionLevel.Auto;
        return new
        {
            ok = true,
            itemId = item.Id,
            willAutoApply = willAuto,
            executionHint = willAuto
                ? "该项将随本轮回复自动写入日程（用户随时可撤销），回复时直接告知已处理好即可，不要说「请确认后生效」。"
                : "该项需要用户在下方卡片点击「确认执行」后才会生效。",
            violations = Array.Empty<object>(),
        };
    }

    private object Track(string name, string runningLabel, string finishedLabel, Func<object> body)
    {
        _emit(new ToolStartedEvent(name, runningLabel));
        try
        {
            var result = body();
            _emit(new ToolFinishedEvent(name, finishedLabel, true));
            return result;
        }
        catch (Exception ex)
        {
            _emit(new ToolFinishedEvent(name, finishedLabel, false));
            return new { ok = false, error = ex.Message };
        }
    }

    private static bool TryParsePriority(string? raw, out TaskPriority priority)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            priority = TaskPriority.None;
            return true;
        }

        switch (raw.Trim().ToLowerInvariant())
        {
            case "none" or "无":
                priority = TaskPriority.None;
                return true;
            case "low" or "低":
                priority = TaskPriority.Low;
                return true;
            case "medium" or "中":
                priority = TaskPriority.Medium;
                return true;
            case "high" or "高":
                priority = TaskPriority.High;
                return true;
            default:
                priority = TaskPriority.None;
                return false;
        }
    }

    private static string FormatPriority(TaskPriority p) => p switch
    {
        TaskPriority.High => "高优先级",
        TaskPriority.Medium => "中优先级",
        TaskPriority.Low => "低优先级",
        _ => "无优先级",
    };
}

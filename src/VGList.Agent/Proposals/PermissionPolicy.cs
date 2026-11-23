using VGList.App.Assistant;

namespace VGList.Agent.Proposals;

/// <summary>
/// 权限分级策略（ADR-0048）：
/// - 默认除 DeleteTask 为 Ask 外，新增/修改/改期/拆子待办/归类/完成/建清单均为 Auto（应用后仍支持单条/多选/整批撤销）；
/// - 所有开关（包含删除任务权限）均尊重用户在「设置 → 助理与记忆」中的配置。
/// </summary>
public sealed class PermissionPolicy
{
    private readonly Func<AssistantPermissionConfig> _configProvider;

    public PermissionPolicy(Func<AssistantPermissionConfig> configProvider, Action<string>? warnLogger = null)
    {
        _configProvider = configProvider;
        _ = warnLogger;
    }

    public PermissionLevel Evaluate(ProposalActionKind kind)
    {
        var cfg = _configProvider() ?? AssistantPermissionConfig.Default;

        var isAuto = kind switch
        {
            ProposalActionKind.AppendSubtasks => cfg.AutoAppendSubtasks,
            ProposalActionKind.CategorizeList => cfg.AutoCategorizeList,
            ProposalActionKind.CreateTask => cfg.AutoCreateTask,
            ProposalActionKind.UpdateTitleOrDate => cfg.AutoUpdateTask,
            ProposalActionKind.CompleteTask => cfg.AutoCompleteTask,
            ProposalActionKind.CreateList => cfg.AutoCreateList,
            ProposalActionKind.DeleteTask => cfg.AutoDeleteTask,
            _ => false,
        };

        return isAuto ? PermissionLevel.Auto : PermissionLevel.Ask;
    }

    public bool CanAutoApplyAll(IReadOnlyList<ProposalItem> items)
    {
        if (items.Count == 0)
        {
            return false;
        }

        foreach (var item in items)
        {
            if (Evaluate(item.Kind) != PermissionLevel.Auto)
            {
                return false;
            }

            if (item.Violations is { Count: > 0 })
            {
                return false;
            }
        }

        return true;
    }
}

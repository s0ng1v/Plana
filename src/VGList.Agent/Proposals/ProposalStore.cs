using System.Collections.Concurrent;
using System.Text.Json;
using VGList.App.Assistant;
using VGList.Core.Abstractions;
using VGList.Core.Models;

namespace VGList.Agent.Proposals;

/// <summary>单次运行期间的提案草稿盒（纯内存，不触达任何业务仓储）。</summary>
public sealed class RunProposalDraft
{
    public const int MaxSelfCorrectionRounds = 2;

    private readonly List<ProposalItem> _items = [];

    public IReadOnlyList<ProposalItem> Items => _items;

    public int SelfCorrectionRoundsUsed { get; private set; }

    public bool TryAdd(ProposalItem item, out string? error)
    {
        if (_items.Count >= ScheduleOpValidator.MaxProposalItems)
        {
            error = $"单次提案最多包含 {ScheduleOpValidator.MaxProposalItems} 条变更，请合并或精简后重试。";
            return false;
        }

        _items.Add(item);
        error = null;
        return true;
    }

    public bool RemoveById(string itemId) => _items.RemoveAll(i => i.Id == itemId) > 0;

    public void Clear() => _items.Clear();

    public void IncrementCorrectionRound() => SelfCorrectionRoundsUsed++;

    public void AttachViolations(IReadOnlyDictionary<string, IReadOnlyList<ProposalViolation>> map)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (map.TryGetValue(_items[i].Id, out var violations) && violations.Count > 0)
            {
                _items[i] = _items[i] with { Violations = violations };
            }
            else if (_items[i].Violations is not null)
            {
                _items[i] = _items[i] with { Violations = null };
            }
        }
    }

    public Proposal Seal(string sessionId, string? title = null, string? summary = null) =>
        new(
            Id: Guid.NewGuid().ToString("N"),
            SessionId: sessionId,
            Title: string.IsNullOrWhiteSpace(title) ? "日程调整建议" : title,
            Summary: string.IsNullOrWhiteSpace(summary) ? $"共 {_items.Count} 项建议改动" : summary,
            Items: _items.ToList(),
            Status: ProposalStatus.Pending);
}

/// <summary>提案存储与二次校验应用器：应用前重新校验目标仍存在，不存在的条目跳过并明确告知。</summary>
public sealed class ProposalApplier
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ITaskRepository _repository;
    private readonly IScheduleService _schedule;
    private readonly IConversationStore? _conversationStore;
    private readonly ConcurrentDictionary<string, Proposal> _liveProposals = new();

    public ProposalApplier(
        ITaskRepository repository,
        IScheduleService schedule,
        IConversationStore? conversationStore = null)
    {
        _repository = repository;
        _schedule = schedule;
        _conversationStore = conversationStore;
    }

    public void Register(Proposal proposal)
    {
        _liveProposals[proposal.Id] = proposal;
        _conversationStore?.SaveProposal(new ProposalRecord(
            proposal.Id,
            proposal.SessionId,
            SerializeSummary(proposal),
            proposal.Status,
            proposal.AppliedChangeSetId,
            DateTimeOffset.Now));
    }

    public Proposal? Find(string proposalId) =>
        _liveProposals.TryGetValue(proposalId, out var p) ? p : null;

    /// <summary>在提案仍处于 Pending 态时，就地替换某条目的 ScheduleOp（如用户卡片内即时调节优先级或提醒）。</summary>
    public bool UpdateItemOperation(string proposalId, string itemId, ScheduleOp updatedOperation)
    {
        if (!_liveProposals.TryGetValue(proposalId, out var proposal) ||
            proposal.Status != ProposalStatus.Pending)
        {
            return false;
        }

        var items = proposal.Items.ToList();
        var idx = items.FindIndex(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
        if (idx < 0)
        {
            return false;
        }

        items[idx] = items[idx] with { Operation = updatedOperation };
        _liveProposals[proposalId] = proposal with { Items = items };
        return true;
    }

    public ProposalApplyResult Apply(string proposalId, IReadOnlyCollection<string>? selectedItemIds, bool isAutoApplied = false)
    {
        if (!_liveProposals.TryGetValue(proposalId, out var proposal))
        {
            return new ProposalApplyResult(false, null, 0, [], "提案不存在或已过期");
        }

        if (proposal.Status is ProposalStatus.Applied or ProposalStatus.AutoApplied)
        {
            return new ProposalApplyResult(false, proposal.AppliedChangeSetId, 0, [], "该提案已应用，请勿重复提交");
        }

        if (proposal.Status == ProposalStatus.Discarded)
        {
            return new ProposalApplyResult(false, null, 0, [], "该提案已放弃");
        }

        var selectedSet = selectedItemIds is null
            ? null
            : new HashSet<string>(selectedItemIds, StringComparer.Ordinal);

        var chosen = proposal.Items
            .Where(i => selectedSet is null || selectedSet.Contains(i.Id))
            .ToList();

        if (chosen.Count == 0)
        {
            return new ProposalApplyResult(false, null, 0, [], "未勾选任何要应用的条目");
        }

        // 应用前二次校验（防止用户期间已删除或改动目标任务）
        var ops = new List<ScheduleOp>();
        var appliedItems = new List<ProposalItem>();
        var skipped = new List<string>();
        foreach (var item in chosen)
        {
            var (valid, err) = ScheduleOpValidator.Validate(item.Operation, _repository);
            if (!valid)
            {
                skipped.Add($"「{item.Title}」已跳过：{err}");
                continue;
            }

            ops.Add(item.Operation);
            appliedItems.Add(item);
        }

        if (ops.Count == 0)
        {
            return new ProposalApplyResult(false, null, 0, skipped, "所选条目的目标任务已不存在，未写入任何改动");
        }

        try
        {
            var changeSet = _schedule.Apply(ops, ChangeSource.Assistant, proposal.Title);
            var entityMap = BuildAppliedEntityMap(appliedItems, changeSet);
            var status = isAutoApplied ? ProposalStatus.AutoApplied : ProposalStatus.Applied;
            var updated = proposal with { Status = status, AppliedChangeSetId = changeSet.Id };
            _liveProposals[proposalId] = updated;
            _conversationStore?.UpdateProposalStatus(proposalId, status, changeSet.Id);
            return new ProposalApplyResult(true, changeSet.Id, ops.Count, skipped, AppliedEntityMap: entityMap);
        }
        catch (Exception ex)
        {
            return new ProposalApplyResult(false, null, 0, skipped, $"应用提案失败（已自动回滚）：{ex.Message}");
        }
    }

    private static Dictionary<string, string> BuildAppliedEntityMap(
        List<ProposalItem> appliedItems,
        ChangeSet changeSet)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (changeSet.Entries.Count == appliedItems.Count)
        {
            for (var i = 0; i < appliedItems.Count; i++)
            {
                map[appliedItems[i].Id] = changeSet.Entries[i].EntityId;
            }

            return map;
        }

        // 兜底匹配（如某条 SetCompletedOp 无实际状态变化而未生成 entry）
        var entryCursor = 0;
        foreach (var item in appliedItems)
        {
            var directId = item.Operation switch
            {
                UpdateTaskOp u => u.TaskId,
                SetCompletedOp s => s.TaskId,
                DeleteTaskOp d => d.TaskId,
                _ => null,
            };

            if (!string.IsNullOrEmpty(directId))
            {
                map[item.Id] = directId;
                if (entryCursor < changeSet.Entries.Count && changeSet.Entries[entryCursor].EntityId == directId)
                {
                    entryCursor++;
                }
            }
            else if (entryCursor < changeSet.Entries.Count)
            {
                map[item.Id] = changeSet.Entries[entryCursor++].EntityId;
            }
        }

        return map;
    }

    public void Discard(string proposalId)
    {
        if (_liveProposals.TryGetValue(proposalId, out var proposal))
        {
            _liveProposals[proposalId] = proposal with { Status = ProposalStatus.Discarded };
        }

        _conversationStore?.UpdateProposalStatus(proposalId, ProposalStatus.Discarded, null);
    }

    public void MarkUndoneByChangeSet(string changeSetId)
    {
        foreach (var kv in _liveProposals)
        {
            if (kv.Value.AppliedChangeSetId == changeSetId)
            {
                _liveProposals[kv.Key] = kv.Value with { Status = ProposalStatus.Undone };
                _conversationStore?.UpdateProposalStatus(kv.Key, ProposalStatus.Undone, changeSetId);
            }
        }
    }

    private static string SerializeSummary(Proposal proposal) =>
        JsonSerializer.Serialize(new
        {
            proposal.Id,
            proposal.Title,
            proposal.Summary,
            Items = proposal.Items.Select(i => new { i.Id, i.ActionTag, i.Title, i.ChangeSummary, i.Reason }),
        }, JsonOpts);
}

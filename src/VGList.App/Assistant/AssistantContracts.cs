using VGList.Core.Abstractions;
using VGList.Core.Models;

namespace VGList.App.Assistant;

/// <summary>场景化上下文（A6 拆任务、晨间简报等传入焦点任务或场景名）。</summary>
public sealed record AssistantContext(string? Scene = null, string? FocusTaskId = null, string? ExtraHint = null);

/// <summary>单次助理对话请求。</summary>
public sealed record AssistantRequest(
    string SessionId,
    string Text,
    IReadOnlyList<AttachmentRef>? Attachments = null,
    AssistantContext? Context = null);

/// <summary>提案条目的操作权限分级：Delete 恒为 Ask，不可被配置改为 Auto。</summary>
public enum PermissionLevel
{
    Ask,
    Auto,
}

/// <summary>权限分类（对应不同提案写入语义）。</summary>
public enum ProposalActionKind
{
    CreateTask,
    UpdateTitleOrDate,
    AppendSubtasks,
    CategorizeList,
    CompleteTask,
    DeleteTask,
    CreateList,
}

/// <summary>助理权限配置（ADR-0048：默认可自动执行新增与修改日程，仅删除任务默认需确认；全部可在设置中调整）。</summary>
public sealed class AssistantPermissionConfig
{
    public bool AutoAppendSubtasks { get; set; } = true;
    public bool AutoCategorizeList { get; set; } = true;
    public bool AutoCreateTask { get; set; } = true;
    public bool AutoUpdateTask { get; set; } = true;
    public bool AutoCompleteTask { get; set; } = true;
    public bool AutoCreateList { get; set; } = true;

    /// <summary>删除任务默认需用户确认，用户也可在设置中显式开启自动执行（依然支持单条/整批撤销）。</summary>
    public bool AutoDeleteTask { get; set; }

    public static AssistantPermissionConfig Default => new();
}

/// <summary>规则校验违规条目（由 IProposalValidator 生成，供模型自纠或交付用户裁决）。</summary>
public sealed record ProposalViolation(string RuleId, string Message, bool HardBlock = false);

/// <summary>提案中的单条变更建议（未应用前绝不触达仓储）。</summary>
public sealed record ProposalItem(
    string Id,
    ProposalActionKind Kind,
    string ActionTag,
    string Title,
    string ChangeSummary,
    string Reason,
    ScheduleOp Operation,
    IReadOnlyList<ProposalViolation>? Violations = null);

/// <summary>一次助理运行生成的完整日程变更提案。</summary>
public sealed record Proposal(
    string Id,
    string SessionId,
    string Title,
    string Summary,
    IReadOnlyList<ProposalItem> Items,
    ProposalStatus Status = ProposalStatus.Pending,
    string? AppliedChangeSetId = null);

// ===== 助理运行流式事件（UI 只消费事件，不碰模型客户端） =====

public abstract record AssistantEvent;

public sealed record TextDeltaEvent(string Delta) : AssistantEvent;

public sealed record ToolStartedEvent(string ToolName, string HumanLabel) : AssistantEvent;

public sealed record ToolFinishedEvent(string ToolName, string HumanLabel, bool Succeeded) : AssistantEvent;

public sealed record ProposalReadyEvent(Proposal Proposal) : AssistantEvent;

public sealed record AutoAppliedEvent(
    string ProposalId,
    string ChangeSetId,
    string Summary,
    int AppliedCount,
    Proposal? Proposal = null,
    IReadOnlyDictionary<string, string>? AppliedEntityMap = null) : AssistantEvent;

public sealed record MemoryChangedEvent(string MemoryId, string Action, string Text) : AssistantEvent;

public sealed record ProfileNicknameChangedEvent(string Nickname) : AssistantEvent;

public sealed record WarningEvent(string Message) : AssistantEvent;

public sealed record ErrorEvent(string UserMessage, bool Retryable) : AssistantEvent;

public sealed record DoneEvent(string SessionId, string FullText) : AssistantEvent;

// ===== 规则校验与上下文扩展点（供 A5 规则 / 记忆 / 画像接入） =====

/// <summary>提案规则校验器扩展点：校验全部条目并返回按 ItemId 归组的违规列表。</summary>
public interface IProposalValidator
{
    IReadOnlyDictionary<string, IReadOnlyList<ProposalViolation>> Validate(IReadOnlyList<ProposalItem> items);
}

/// <summary>系统提示动态上下文贡献者（A5 用于注入我的规则、用户画像与命中记忆）。</summary>
public interface IContextContributor
{
    string SectionName { get; }

    /// <summary>返回要注入系统提示的段落文本；返回 null/空表示本轮无内容。</summary>
    string? BuildContext(string userMessage, DateOnly today);
}

/// <summary>额外工具提供者扩展点（A5/A6 追加记忆、规则等工具）。</summary>
public interface IAssistantToolProvider
{
    IReadOnlyList<object> CreateTools(string sessionId);
}

/// <summary>提案应用结果（附带 ProposalItemId → EntityId 映射，供单条/多选撤销）。</summary>
public sealed record ProposalApplyResult(
    bool Success,
    string? ChangeSetId,
    int AppliedCount,
    IReadOnlyList<string> SkippedReasons,
    string? ErrorMessage = null,
    IReadOnlyDictionary<string, string>? AppliedEntityMap = null);

/// <summary>助理内核唯一对外入口（UI 通过此接口驱动对话、提案应用与细粒度撤销，严守常驻态懒加载）。</summary>
public interface IAssistantHost : IDisposable
{
    IReadOnlyList<SessionSummary> Sessions { get; }

    SessionSummary CreateSession(string title = "新对话");

    IReadOnlyList<ConversationMessageRecord> LoadSessionMessages(string sessionId);

    void DeleteSession(string sessionId);

    /// <summary>截断指定会话中超出 keepCount 的后续持久化消息（重新生成 / 编辑历史消息）。</summary>
    void TruncateSessionMessages(string sessionId, int keepCount);

    /// <summary>更新指定会话的标题并持久化。</summary>
    void UpdateSessionTitle(string sessionId, string title);

    /// <summary>独立于主对话流并行生成简洁会话标题（互不阻塞）。</summary>
    Task<string?> GenerateSessionTitleAsync(string sessionId, string firstUserMessage, CancellationToken ct = default);

    /// <summary>在待确认提案中就地修改某条目的操作（如用户在卡片里直接调节优先级或提醒，无需确认按钮）。</summary>
    bool UpdateProposalItemOperation(string proposalId, string itemId, ScheduleOp updatedOperation);

    IAsyncEnumerable<AssistantEvent> RunAsync(AssistantRequest request, CancellationToken ct = default);

    Task<ProposalApplyResult> ApplyProposalAsync(
        string proposalId,
        IReadOnlyCollection<string> selectedItemIds,
        CancellationToken ct = default);

    void DiscardProposal(string proposalId);

    Task<UndoResult> UndoAsync(string changeSetId);

    /// <summary>局部撤销单条或多条实体改动。</summary>
    Task<UndoResult> UndoEntriesAsync(string changeSetId, IReadOnlyCollection<string> entityIds);

    Task<UndoResult?> UndoLastAsync(ChangeSource[] sources, TimeSpan within);
}

namespace VGList.Core.Models;

/// <summary>附件元信息引用（不落库原始内容，仅保留名称/路径/大小/类型）。</summary>
public sealed record AttachmentRef(string Name, string FullPath, long SizeBytes, string KindTag);

public enum ProposalStatus
{
    Pending,
    Applied,
    AutoApplied,
    Discarded,
    Undone,
}

public sealed record SessionSummary(string Id, string Title, DateTimeOffset UpdatedAt);

public sealed record ConversationMessageRecord(
    string Id,
    string SessionId,
    int Seq,
    string Role,
    string Text,
    string? ModelTag,
    IReadOnlyList<AttachmentRef>? Attachments,
    DateTimeOffset CreatedAt);

public sealed record ProposalRecord(
    string Id,
    string SessionId,
    string Json,
    ProposalStatus Status,
    string? AppliedChangeSetId,
    DateTimeOffset CreatedAt);

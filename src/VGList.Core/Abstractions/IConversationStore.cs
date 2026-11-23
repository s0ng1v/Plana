using VGList.Core.Models;

namespace VGList.Core.Abstractions;

/// <summary>助理对话会话、消息与提案记录持久化接口（保留最近 30 个会话，附件原文不落库）。</summary>
public interface IConversationStore
{
    IReadOnlyList<SessionSummary> ListSessions(int limit = 30);
    SessionSummary CreateSession(string? id = null, string title = "新对话");
    void UpdateSessionTitle(string sessionId, string title);
    void DeleteSession(string sessionId);

    IReadOnlyList<ConversationMessageRecord> LoadMessages(string sessionId);
    void AppendMessage(ConversationMessageRecord message);

    /// <summary>保留指定会话最早的 keepCount 条消息，删除其余后续记录（用于重新生成与编辑历史消息）。</summary>
    void TruncateMessages(string sessionId, int keepCount);

    void SaveProposal(ProposalRecord record);
    ProposalRecord? GetProposal(string proposalId);
    IReadOnlyList<ProposalRecord> LoadSessionProposals(string sessionId);
    void UpdateProposalStatus(string proposalId, ProposalStatus status, string? appliedChangeSetId);
}

using System.Text.Json;

namespace VGList.App.Mcp;

/// <summary>A7 本地 MCP 管道协议数据契约（换行分隔 JSON，版本字段 v = 1）。</summary>
public static class McpProtocol
{
    public const int CurrentVersion = 1;
}

public sealed record McpPipeRequest(
    int V,
    string Id,
    string ClientName,
    string Tool,
    JsonElement? Args);

public sealed record McpPipeError(
    string Code,
    string Message);

public sealed record McpPipeResponse(
    int V,
    string Id,
    bool Ok,
    object? Result = null,
    McpPipeError? Error = null);

/// <summary>隐私安全访问记录：只记时间、客户端、工具、条数、结果与变更集 Id，绝不记录任务标题或正文。</summary>
public sealed record McpAuditEntry(
    DateTimeOffset Time,
    string Client,
    string Tool,
    int Count,
    string Result,
    string? ChangeSetId = null);

public sealed record McpPendingRequestItem(
    string ActionTag,
    string Title,
    string Detail);

public sealed record McpPendingConfirmation(
    string Id,
    string ClientName,
    string Tool,
    string Summary,
    IReadOnlyList<McpPendingRequestItem> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed class McpAppliedEventArgs(
    string clientName,
    string tool,
    string label,
    int count,
    string changeSetId) : EventArgs
{
    public string ClientName { get; } = clientName;
    public string Tool { get; } = tool;
    public string Label { get; } = label;
    public int Count { get; } = count;
    public string ChangeSetId { get; } = changeSetId;
}

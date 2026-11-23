using System.Text;

namespace VGList.Core.Models;

/// <summary>变更来源，用于审计与按来源筛选可撤销变更集。</summary>
public enum ChangeSource
{
    User,
    Assistant,
    Mcp,
    System,
}

public enum ChangeEntryKind
{
    TaskCreated,
    TaskUpdated,
    TaskDeleted,
    ListCreated,
    ListRenamed,
    ListDeleted,
}

public sealed record ChangeEntry(
    ChangeEntryKind Kind,
    string EntityId,
    string? BeforeJson,
    string? AfterJson);

public sealed record ChangeSet(
    string Id,
    ChangeSource Source,
    string Label,
    DateTimeOffset At,
    IReadOnlyList<ChangeEntry> Entries,
    DateTimeOffset? UndoneAt = null);

/// <summary>在任务描述中按二级标题替换或追加小节，保留其余原文。</summary>
public sealed record ContentSectionPatch(string Heading, string Markdown);

/// <summary>局部更新任务的补丁；null 字段表示不修改。</summary>
public sealed record TaskPatch
{
    public string? Title { get; init; }

    public string? Content { get; init; }

    public DateOnly? Date { get; init; }

    public TimeOnly? DueTime { get; init; }

    public bool ClearDueTime { get; init; }

    public TaskReminder? Reminder { get; init; }

    public bool ClearReminder { get; init; }

    public TaskPriority? Priority { get; init; }

    public string? ListId { get; init; }

    /// <summary>整体替换标签列表。</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>修改重复规则。</summary>
    public RecurrenceKind? Recurrence { get; init; }

    /// <summary>整体替换子待办集合。</summary>
    public IReadOnlyList<TaskSubtask>? Subtasks { get; init; }

    /// <summary>在现有子待办末尾追加新条目。</summary>
    public IReadOnlyList<TaskSubtask>? AppendSubtasks { get; init; }

    /// <summary>按二级标题替换或追加描述小节。</summary>
    public ContentSectionPatch? AppendContentSection { get; init; }

    /// <summary>
    /// 若原文已有同名二级标题（## Heading），替换到下一个二级标题或结尾；否则追加到末尾。
    /// 其余原文逐字保留。
    /// </summary>
    public static string ApplyContentSection(string existing, string heading, string markdown)
    {
        var normalizedHeading = heading.Trim();
        var body = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var headerLine = $"## {normalizedHeading}";
        var replacementBlock = string.IsNullOrEmpty(body) ? headerLine : $"{headerLine}\n{body}";

        if (string.IsNullOrEmpty(existing))
        {
            return replacementBlock;
        }

        var normalized = existing.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var startIdx = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (string.Equals(lines[i].Trim(), headerLine, StringComparison.Ordinal))
            {
                startIdx = i;
                break;
            }
        }

        if (startIdx < 0)
        {
            var trimmedEnd = normalized.TrimEnd('\n');
            return string.IsNullOrEmpty(trimmedEnd)
                ? replacementBlock
                : $"{trimmedEnd}\n\n{replacementBlock}";
        }

        var endIdx = lines.Length;
        for (var i = startIdx + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                endIdx = i;
                break;
            }
        }

        var sb = new StringBuilder();
        for (var i = 0; i < startIdx; i++)
        {
            sb.Append(lines[i]).Append('\n');
        }

        sb.Append(replacementBlock);

        if (endIdx < lines.Length)
        {
            sb.Append("\n\n");
            for (var i = endIdx; i < lines.Length; i++)
            {
                sb.Append(lines[i]);
                if (i < lines.Length - 1)
                {
                    sb.Append('\n');
                }
            }
        }

        return sb.ToString();
    }
}

/// <summary>声明式日程操作，供助理提案、MCP 与批量变更共用。</summary>
public abstract record ScheduleOp;

public sealed record CreateTaskOp(TaskDraft Draft) : ScheduleOp;

public sealed record UpdateTaskOp(string TaskId, TaskPatch Patch) : ScheduleOp;

public sealed record SetCompletedOp(string TaskId, bool Completed) : ScheduleOp;

public sealed record DeleteTaskOp(string TaskId) : ScheduleOp;

public sealed record CreateListOp(string Name) : ScheduleOp;

public sealed record UndoSkippedItem(string EntityId, string Reason);

public sealed record UndoResult(
    bool Success,
    int RevertedCount,
    IReadOnlyList<UndoSkippedItem> Skipped,
    string? Error = null);

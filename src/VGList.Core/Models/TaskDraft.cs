namespace VGList.Core.Models;

/// <summary>新建任务时提交的最小字段集合。</summary>
public sealed record TaskDraft
{
    public required string Title { get; init; }

    public required DateOnly Date { get; init; }

    public TimeOnly? DueTime { get; init; }

    public TaskReminder? Reminder { get; init; }

    public string Content { get; init; } = string.Empty;

    public IReadOnlyList<TaskSubtask>? Subtasks { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    public RecurrenceKind Recurrence { get; init; } = RecurrenceKind.None;

    public TaskPriority Priority { get; init; } = TaskPriority.None;

    public string ListId { get; init; } = TaskList.InboxId;

    public bool IsCompleted { get; init; }
}

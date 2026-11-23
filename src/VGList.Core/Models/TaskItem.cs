namespace VGList.Core.Models;

/// <summary>一条任务（支持子待办、多标签、重复规则与软删除回收站）。</summary>
public sealed class TaskItem
{
    public required string Id { get; init; }

    public required string Title { get; set; }

    public string Content { get; set; } = string.Empty;

    public List<TaskSubtask> Subtasks { get; set; } = [];

    public List<string> Tags { get; set; } = [];

    public RecurrenceKind Recurrence { get; set; } = RecurrenceKind.None;

    public DateOnly Date { get; set; }

    /// <summary>可选具体时刻（HH:mm），用于精确定时与提前提醒计算。</summary>
    public TimeOnly? DueTime { get; set; }

    /// <summary>可选提醒配置（支持提前任意时间与反复提醒）。</summary>
    public TaskReminder? Reminder { get; set; }

    public TaskPriority Priority { get; set; }

    public string ListId { get; set; } = TaskList.InboxId;

    public bool IsCompleted { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>软删除时间：非空表示位于回收站中，可恢复或彻底清除。</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public bool IsDeleted => DeletedAt is not null;

    /// <summary>被推迟次数：当日期被改为晚于原日期时递增。</summary>
    public int RescheduleCount { get; set; }

    /// <summary>创建深拷贝，避免调用方绕过仓储直接篡改内部实例。</summary>
    public TaskItem Clone() => new()
    {
        Id = Id,
        Title = Title,
        Content = Content,
        Subtasks = Subtasks
            .Select(s => new TaskSubtask
            {
                Id = s.Id,
                Title = s.Title,
                IsCompleted = s.IsCompleted,
            })
            .ToList(),
        Tags = [.. Tags],
        Recurrence = Recurrence,
        Date = Date,
        DueTime = DueTime,
        Reminder = Reminder?.Clone(),
        Priority = Priority,
        ListId = ListId,
        IsCompleted = IsCompleted,
        CreatedAt = CreatedAt,
        CompletedAt = CompletedAt,
        DeletedAt = DeletedAt,
        RescheduleCount = RescheduleCount,
    };
}

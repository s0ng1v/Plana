namespace VGList.Core.Models;

/// <summary>任务内部的一条可勾选子待办事项（Checklist）。</summary>
public sealed class TaskSubtask
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public required string Title { get; set; }

    public bool IsCompleted { get; set; }
}

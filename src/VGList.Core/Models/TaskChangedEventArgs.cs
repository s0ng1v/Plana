namespace VGList.Core.Models;

public enum TaskChangeKind
{
    Created,
    Updated,
    CompletedChanged,
    Deleted,
}

public sealed class TaskChangedEventArgs(TaskChangeKind kind, string taskId) : EventArgs
{
    public TaskChangeKind Kind { get; } = kind;

    public string TaskId { get; } = taskId;
}

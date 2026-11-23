using CommunityToolkit.Mvvm.ComponentModel;
using VGList.Core.Models;
using VGList.Widgets.Support;

namespace VGList.Desktop.Main.Tasks;

public enum MainSection
{
    Today,
    Next7Days,
    Inbox,
    List,
    Completed,
    Trash,
}

public enum MainPage
{
    Tasks,
    Eisenhower,
    Calendar,
    Assistant,
    Settings,
}

public enum TaskSortMode
{
    Default,
    Priority,
    Date,
}

/// <summary>主窗口任务行视图模型（在任务页与四象限中复用）。</summary>
public sealed partial class MainTaskRowViewModel : ObservableObject
{
    private readonly DateOnly _today;

    public MainTaskRowViewModel(TaskItem task, DateOnly today, bool showListTag = true)
    {
        Task = task;
        _today = today;
        ShowListTag = showListTag;
        var baseDate = DateLabels.Describe(task.Date, today);
        DateLabel = task.DueTime is { } dt ? $"{baseDate} {dt:HH:mm}" : baseDate;
    }

    public TaskItem Task { get; }

    public string Id => Task.Id;

    public string Title => Task.Title;

    public bool IsCompleted => Task.IsCompleted;

    public bool IsOverdue => !Task.IsCompleted && Task.Date < _today;

    public bool IsDueToday => Task.Date == _today;

    public string DateLabel { get; }

    public string ListName => TaskListCatalog.NameOf(Task.ListId);

    public bool ShowListTag { get; }

    public int SubtasksTotal => Task.Subtasks.Count;

    public int SubtasksCompleted => Task.Subtasks.Count(s => s.IsCompleted);

    public bool HasSubtasks => SubtasksTotal > 0;

    public string SubtasksTooltip => HasSubtasks ? $"待办进度 {SubtasksCompleted}/{SubtasksTotal}" : string.Empty;

    public bool HasDescription => !HasSubtasks && !string.IsNullOrWhiteSpace(Task.Content);

    public IReadOnlyList<string> Tags => Task.Tags;

    public IReadOnlyList<string> VisibleTags => Task.Tags.Count <= 2 ? Task.Tags : Task.Tags.Take(2).ToList();

    public bool HasTags => Task.Tags.Count > 0;

    public bool HasExtraTags => Task.Tags.Count > 2;

    public string ExtraTagCountText => Task.Tags.Count > 2 ? $"+{Task.Tags.Count - 2}" : string.Empty;

    public bool HasRecurrence => Task.Recurrence != RecurrenceKind.None;

    public string RecurrenceBadgeText => RecurrenceCalculator.FormatShortBadge(Task.Recurrence);

    public string RecurrenceTooltip => HasRecurrence ? $"重复：{RecurrenceCalculator.FormatLabel(Task.Recurrence)}" : string.Empty;

    public bool HasReminder => !Task.IsCompleted && Task.Reminder is { Enabled: true };

    public string ReminderBadgeText => HasReminder
        ? TaskReminderCalculator.FormatRowMiniBadge(Task.Date, Task.DueTime, Task.Reminder)
        : string.Empty;

    public string ReminderTooltip => HasReminder
        ? $"提醒：{TaskReminderCalculator.FormatPillBadge(Task.Date, Task.DueTime, Task.Reminder, _today)}"
        : string.Empty;

    public string DeletedLabel => Task.DeletedAt is { } deletedAt
        ? $"删除于 {deletedAt.LocalDateTime:M月d日 HH:mm}"
        : string.Empty;

    public bool IsHigh => Task.Priority == TaskPriority.High;

    public bool IsMedium => Task.Priority == TaskPriority.Medium;

    public bool IsLow => Task.Priority == TaskPriority.Low;

    public bool IsNone => Task.Priority == TaskPriority.None;

    public string QuickDateTooltip => Task.Date == _today ? "改期到明天" : "改期到今天";

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isCompleting;

    public bool IsCheckVisible => IsCompleted || IsCompleting;

    public bool IsDoneVisual => IsCompleted || IsCompleting;

    partial void OnIsCompletingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCheckVisible));
        OnPropertyChanged(nameof(IsDoneVisual));
    }
}

/// <summary>可折叠的任务日期分组。</summary>
public sealed partial class MainTaskGroupViewModel : ObservableObject
{
    public MainTaskGroupViewModel(
        string key,
        string header,
        IReadOnlyList<MainTaskRowViewModel> tasks,
        bool isOverdueGroup = false,
        bool isExpanded = true)
    {
        Key = key;
        Header = header;
        Tasks = tasks;
        CountLabel = tasks.Count.ToString();
        IsOverdueGroup = isOverdueGroup;
        _isExpanded = isExpanded;
    }

    public string Key { get; }

    public string Header { get; }

    public string CountLabel { get; }

    public bool IsOverdueGroup { get; }

    public IReadOnlyList<MainTaskRowViewModel> Tasks { get; }

    [ObservableProperty]
    private bool _isExpanded;
}

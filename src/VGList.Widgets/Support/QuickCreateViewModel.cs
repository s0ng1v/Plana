using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VGList.App.Services;
using VGList.Core.Abstractions;
using VGList.Core.Models;

namespace VGList.Widgets.Support;

/// <summary>单条子待办草稿/编辑项，用于悬浮新建卡片与详情面板。</summary>
public sealed partial class QuickSubtaskItem : ObservableObject
{
    public QuickSubtaskItem(string title = "", bool isCompleted = false, string? id = null)
    {
        Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id;
        _title = title;
        _isCompleted = isCompleted;
    }

    public string Id { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isCompleted;
}

/// <summary>多行文本描述 ↔ 可勾选子待办清单互转工具。</summary>
public static class ChecklistConverter
{
    public static List<QuickSubtaskItem> LinesToSubtasks(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [new QuickSubtaskItem()];
        }

        var items = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .Select(line => new QuickSubtaskItem(line))
            .ToList();

        return items.Count > 0 ? items : [new QuickSubtaskItem()];
    }

    public static string SubtasksToLines(IEnumerable<QuickSubtaskItem> items) =>
        string.Join(
            '\n',
            items
                .Select(i => i.Title?.Trim() ?? string.Empty)
                .Where(t => !string.IsNullOrEmpty(t)));
}

/// <summary>
/// 统一任务悬浮新建/编辑卡片视图模型（日历格、月历小组件、四象限通用）：
/// 支持新建与就地编辑两种模式、目标日期、右上角优先级圆点（Tab/点击切换）、多行描述 ↔ 待办清单互转、饼状进度与清单选择。
/// </summary>
public sealed partial class QuickCreateViewModel : ObservableObject
{
    private readonly DateOnly _today;
    private readonly DraftDateAutoFill _dateAutoFill;
    private ITaskRepository? _editRepository;
    private bool _isLoading;

    public QuickCreateViewModel(DateOnly today)
    {
        _today = today;
        _targetDate = today;
        _dateAutoFill = new DraftDateAutoFill(today, () => AutoDetectDatePreference.IsEnabled);
    }

    public ObservableCollection<QuickSubtaskItem> Subtasks { get; } = [];

    public TaskItem? EditingTask { get; private set; }

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private DateOnly _targetDate;

    [ObservableProperty]
    private TaskPriority _priority = TaskPriority.None;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isChecklistMode;

    [ObservableProperty]
    private string _listId = TaskList.InboxId;

    [ObservableProperty]
    private RecurrenceKind _recurrence = RecurrenceKind.None;

    public bool HasRecurrence => Recurrence != RecurrenceKind.None;

    public string RecurrenceLabel => HasRecurrence ? RecurrenceCalculator.FormatShortBadge(Recurrence) : "重复";

    public bool IsCreateMode => !IsEditMode;

    public string DateDisplayLabel
    {
        get
        {
            var abs = $"{TargetDate.Month}月{TargetDate.Day}日";
            var rel = DateLabels.Describe(TargetDate, _today);
            if (rel is "今天" or "明天" or "昨天" or "后天")
            {
                return $"{abs}, {rel}";
            }

            if (TargetDate > _today && TargetDate <= _today.AddDays(6))
            {
                return $"{abs}, {DateLabels.Weekday(TargetDate)}";
            }

            return $"{abs} {DateLabels.Weekday(TargetDate)}";
        }
    }

    public bool IsHigh => Priority == TaskPriority.High;
    public bool IsMedium => Priority == TaskPriority.Medium;
    public bool IsLow => Priority == TaskPriority.Low;
    public bool IsNone => Priority == TaskPriority.None;

    public string PriorityLabel => Priority switch
    {
        TaskPriority.High => "高优先级",
        TaskPriority.Medium => "中优先级",
        TaskPriority.Low => "低优先级",
        _ => "无优先级",
    };

    public string ListName => TaskListCatalog.NameOf(ListId);

    public int SubtasksTotal => Subtasks.Count(s => !string.IsNullOrWhiteSpace(s.Title));
    public int SubtasksCompleted => Subtasks.Count(s => !string.IsNullOrWhiteSpace(s.Title) && s.IsCompleted);
    public bool HasSubtasksProgress => IsChecklistMode && SubtasksTotal > 0;

    partial void OnIsEditModeChanged(bool value) => OnPropertyChanged(nameof(IsCreateMode));

    partial void OnTargetDateChanged(DateOnly value)
    {
        OnPropertyChanged(nameof(DateDisplayLabel));
        SyncEditToRepository();
    }

    partial void OnPriorityChanged(TaskPriority value)
    {
        OnPropertyChanged(nameof(IsHigh));
        OnPropertyChanged(nameof(IsMedium));
        OnPropertyChanged(nameof(IsLow));
        OnPropertyChanged(nameof(IsNone));
        OnPropertyChanged(nameof(PriorityLabel));
        SyncEditToRepository();
    }

    partial void OnListIdChanged(string value)
    {
        OnPropertyChanged(nameof(ListName));
        SyncEditToRepository();
    }

    partial void OnRecurrenceChanged(RecurrenceKind value)
    {
        OnPropertyChanged(nameof(HasRecurrence));
        OnPropertyChanged(nameof(RecurrenceLabel));
        SyncEditToRepository();
    }

    partial void OnTitleChanged(string value)
    {
        if (!_isLoading && !IsEditMode && _dateAutoFill.Evaluate(value, _today) is { } detected && detected != TargetDate)
        {
            TargetDate = detected;
        }

        SyncEditToRepository();
    }

    public void PickManualDate(DateOnly date)
    {
        _dateAutoFill.MarkUserPickedDate();
        TargetDate = date;
    }

    partial void OnDescriptionChanged(string value) => SyncEditToRepository();

    partial void OnIsChecklistModeChanged(bool value) => NotifyProgress();

    public void Reset(DateOnly targetDate, TaskPriority priority = TaskPriority.None, string listId = TaskList.InboxId)
    {
        _isLoading = true;
        try
        {
            EditingTask = null;
            _editRepository = null;
            IsEditMode = false;
            IsCompleted = false;
            _dateAutoFill.Reset(targetDate);
            TargetDate = targetDate;
            Priority = priority;
            ListId = listId;
            Recurrence = RecurrenceKind.None;
            Title = string.Empty;
            Description = string.Empty;
            Subtasks.Clear();
            IsChecklistMode = false;
        }
        finally
        {
            _isLoading = false;
        }

        NotifyProgress();
    }

    public void LoadTask(TaskItem task, ITaskRepository repository)
    {
        _isLoading = true;
        try
        {
            EditingTask = task;
            _editRepository = repository;
            IsEditMode = true;
            IsCompleted = task.IsCompleted;
            TargetDate = task.Date;
            Priority = task.Priority;
            ListId = task.ListId;
            Recurrence = task.Recurrence;
            Title = task.Title;
            Subtasks.Clear();

            if (task.Subtasks.Count > 0)
            {
                Description = string.Empty;
                foreach (var s in task.Subtasks)
                {
                    var item = new QuickSubtaskItem(s.Title, s.IsCompleted, s.Id);
                    AttachSubtask(item);
                    Subtasks.Add(item);
                }

                IsChecklistMode = true;
            }
            else
            {
                Description = task.Content;
                IsChecklistMode = false;
            }
        }
        finally
        {
            _isLoading = false;
        }

        NotifyProgress();
    }

    public void ToggleCompleted()
    {
        if (!IsEditMode || EditingTask is null || _editRepository is null)
        {
            return;
        }

        IsCompleted = !IsCompleted;
        EditingTask.IsCompleted = IsCompleted;
        _editRepository.SetCompleted(EditingTask.Id, IsCompleted);
    }

    public void CyclePriority(bool reverse = false) =>
        Priority = Priority switch
        {
            TaskPriority.None => reverse ? TaskPriority.Low : TaskPriority.High,
            TaskPriority.High => reverse ? TaskPriority.None : TaskPriority.Medium,
            TaskPriority.Medium => reverse ? TaskPriority.High : TaskPriority.Low,
            _ => reverse ? TaskPriority.Medium : TaskPriority.None,
        };

    public void ToggleList()
    {
        var ids = TaskListCatalog.Items.Select(l => l.Id).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var index = ids.IndexOf(ListId);
        ListId = ids[(index + 1) % ids.Count];
    }

    public void CycleRecurrence() =>
        Recurrence = Recurrence switch
        {
            RecurrenceKind.None => RecurrenceKind.Daily,
            RecurrenceKind.Daily => RecurrenceKind.Weekdays,
            RecurrenceKind.Weekdays => RecurrenceKind.Weekly,
            RecurrenceKind.Weekly => RecurrenceKind.Monthly,
            RecurrenceKind.Monthly => RecurrenceKind.Yearly,
            RecurrenceKind.Yearly => RecurrenceKind.LunarYearly,
            _ => RecurrenceKind.None,
        };

    public void ToggleChecklistMode()
    {
        if (!IsChecklistMode)
        {
            var items = ChecklistConverter.LinesToSubtasks(Description);
            _isLoading = true;
            try
            {
                Subtasks.Clear();
                foreach (var item in items)
                {
                    AttachSubtask(item);
                    Subtasks.Add(item);
                }

                Description = string.Empty;
                IsChecklistMode = true;
            }
            finally
            {
                _isLoading = false;
            }
        }
        else
        {
            var lines = ChecklistConverter.SubtasksToLines(Subtasks);
            _isLoading = true;
            try
            {
                Subtasks.Clear();
                IsChecklistMode = false;
                Description = lines;
            }
            finally
            {
                _isLoading = false;
            }
        }

        NotifyProgress();
        SyncEditToRepository();
    }

    public QuickSubtaskItem AddSubtaskAfter(QuickSubtaskItem? current = null)
    {
        var item = new QuickSubtaskItem();
        AttachSubtask(item);
        if (current is not null)
        {
            var idx = Subtasks.IndexOf(current);
            if (idx >= 0 && idx < Subtasks.Count - 1)
            {
                Subtasks.Insert(idx + 1, item);
                NotifyProgress();
                SyncEditToRepository();
                return item;
            }
        }

        Subtasks.Add(item);
        NotifyProgress();
        SyncEditToRepository();
        return item;
    }

    public QuickSubtaskItem? RemoveSubtask(QuickSubtaskItem item)
    {
        var idx = Subtasks.IndexOf(item);
        if (idx < 0)
        {
            return null;
        }

        if (Subtasks.Count <= 1)
        {
            Subtasks.Clear();
            IsChecklistMode = false;
            NotifyProgress();
            SyncEditToRepository();
            return null;
        }

        Subtasks.RemoveAt(idx);
        NotifyProgress();
        SyncEditToRepository();
        var nextIdx = Math.Clamp(idx - 1, 0, Subtasks.Count - 1);
        return Subtasks[nextIdx];
    }

    public TaskItem? Submit(ITaskRepository repository)
    {
        if (IsEditMode)
        {
            SyncEditToRepository();
            return EditingTask;
        }

        var raw = Title?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        var (tagClean, inlineTags) = TaskTagParser.ExtractInlineTags(raw);
        var (trimmed, resolvedDate) = _dateAutoFill.ResolveForSubmit(tagClean, TargetDate, _today);

        var subtasks = IsChecklistMode
            ? Subtasks
                .Where(s => !string.IsNullOrWhiteSpace(s.Title))
                .Select(s => new TaskSubtask
                {
                    Id = s.Id,
                    Title = s.Title.Trim(),
                    IsCompleted = s.IsCompleted,
                })
                .ToList()
            : [];

        var created = repository.Create(new TaskDraft
        {
            Title = trimmed,
            Content = IsChecklistMode ? string.Empty : (Description?.Trim() ?? string.Empty),
            Subtasks = subtasks,
            Tags = inlineTags,
            Recurrence = Recurrence,
            Date = resolvedDate,
            Priority = Priority,
            ListId = ListId,
        });

        Reset(TargetDate, TaskPriority.None, ListId);
        return created;
    }

    private void AttachSubtask(QuickSubtaskItem item) =>
        item.PropertyChanged += (_, _) =>
        {
            NotifyProgress();
            SyncEditToRepository();
        };

    private void SyncEditToRepository()
    {
        if (_isLoading || !IsEditMode || EditingTask is null || _editRepository is null)
        {
            return;
        }

        var trimmedTitle = Title?.Trim();
        if (!string.IsNullOrEmpty(trimmedTitle))
        {
            EditingTask.Title = trimmedTitle;
        }

        EditingTask.Date = TargetDate;
        EditingTask.Priority = Priority;
        EditingTask.ListId = ListId;
        EditingTask.Recurrence = Recurrence;
        if (IsChecklistMode)
        {
            EditingTask.Content = string.Empty;
            EditingTask.Subtasks = Subtasks
                .Where(s => !string.IsNullOrWhiteSpace(s.Title))
                .Select(s => new TaskSubtask
                {
                    Id = s.Id,
                    Title = s.Title.Trim(),
                    IsCompleted = s.IsCompleted,
                })
                .ToList();
        }
        else
        {
            EditingTask.Subtasks = [];
            EditingTask.Content = Description?.Trim() ?? string.Empty;
        }

        _editRepository.Update(EditingTask);
    }

    private void NotifyProgress()
    {
        OnPropertyChanged(nameof(SubtasksTotal));
        OnPropertyChanged(nameof(SubtasksCompleted));
        OnPropertyChanged(nameof(HasSubtasksProgress));
    }
}

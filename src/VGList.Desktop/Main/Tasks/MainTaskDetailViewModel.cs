using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VGList.Core.Abstractions;
using VGList.Core.Models;
using VGList.Desktop.Controls;
using VGList.Widgets.Support;

namespace VGList.Desktop.Main.Tasks;

/// <summary>详情面板重复规则下拉项。</summary>
public sealed record RecurrencePickItem(RecurrenceKind Kind, string Label, bool IsSelected);

/// <summary>主窗口任务详情面板视图模型（在任务页、四象限与日历抽屉中复用）。</summary>
public sealed partial class MainTaskDetailViewModel : ObservableObject
{
    private readonly ITaskRepository _repository;
    private readonly DateOnly _today;
    private readonly Action<TaskItem> _delete;
    private bool _syncing;
    private bool _updatingSubtasks;
    private bool _isChecklistMode;

    public MainTaskDetailViewModel(
        ITaskRepository repository,
        TaskItem task,
        DateOnly today,
        Action close,
        Action<TaskItem> delete)
    {
        _repository = repository;
        _today = today;
        _delete = delete;
        Task = task;
        _title = task.Title;
        _content = task.Content;
        _isChecklistMode = task.Subtasks.Count > 0;
        DatePicker = new DatePickerViewModel(today, task.Date, PickDate);
        ReminderEditor = new ReminderEditorViewModel(OnReminderEdited);
        ReminderEditor.Load(task.Date, task.DueTime, task.Reminder, today);
        RebuildSubtasks(task.Subtasks);

        ToggleCompletedCommand = new RelayCommand(ToggleCompleted);
        ToggleCalendarCommand = new RelayCommand(ToggleCalendar);
        TogglePriorityMenuCommand = new RelayCommand(() => IsPriorityMenuOpen = !IsPriorityMenuOpen);
        SetPriorityHighCommand = new RelayCommand(() => SetPriority(TaskPriority.High));
        SetPriorityMediumCommand = new RelayCommand(() => SetPriority(TaskPriority.Medium));
        SetPriorityLowCommand = new RelayCommand(() => SetPriority(TaskPriority.Low));
        SetPriorityNoneCommand = new RelayCommand(() => SetPriority(TaskPriority.None));
        CloseCommand = new RelayCommand(close);
        DeleteCommand = new RelayCommand(() => _delete(Task));
        ToggleListMenuCommand = new RelayCommand(() => IsListMenuOpen = !IsListMenuOpen);
        ToggleRecurrenceMenuCommand = new RelayCommand(() => IsRecurrenceMenuOpen = !IsRecurrenceMenuOpen);
        RebuildListOptions();
        RebuildRecurrenceOptions();
        RebuildTags(task.Tags);
    }

    public TaskItem Task { get; private set; }

    public DatePickerViewModel DatePicker { get; }

    public ReminderEditorViewModel ReminderEditor { get; }

    public ObservableCollection<QuickSubtaskItem> Subtasks { get; } = [];

    public ObservableCollection<string> Tags { get; } = [];

    public ObservableCollection<RecurrencePickItem> RecurrenceOptions { get; } = [];

    /// <summary>由 View 写入，避免外部同步覆盖正在输入的文本。</summary>
    public bool IsTitleFocused { get; set; }

    public bool IsContentFocused { get; set; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private string _newTagText = string.Empty;

    [ObservableProperty]
    private bool _isCalendarOpen;

    [ObservableProperty]
    private bool _isPriorityMenuOpen;

    [ObservableProperty]
    private bool _isListMenuOpen;

    [ObservableProperty]
    private bool _isRecurrenceMenuOpen;

    [ObservableProperty]
    private bool _isReminderMenuOpen;

    public bool HasAnyOverlay => IsCalendarOpen || IsPriorityMenuOpen || IsListMenuOpen || IsRecurrenceMenuOpen || IsReminderMenuOpen;

    public bool HasTags => Tags.Count > 0;

    public RecurrenceKind Recurrence => Task.Recurrence;

    public bool HasRecurrence => Task.Recurrence != RecurrenceKind.None;

    public string RecurrenceLabel => RecurrenceCalculator.FormatLabel(Task.Recurrence);

    public bool HasReminder => Task.Reminder is { Enabled: true };

    public string ReminderChipLabel => HasReminder
        ? TaskReminderCalculator.FormatRowMiniBadge(Task.Date, Task.DueTime, Task.Reminder)
        : "提醒";

    public string ReminderTooltip => HasReminder
        ? TaskReminderCalculator.FormatPillBadge(Task.Date, Task.DueTime, Task.Reminder, _today)
        : "设置日程时刻与 Windows 通知提醒";

    public bool IsChecklistMode
    {
        get => _isChecklistMode;
        private set
        {
            if (SetProperty(ref _isChecklistMode, value))
            {
                OnPropertyChanged(nameof(ContentMetaLabel));
                OnPropertyChanged(nameof(HasContentMeta));
            }
        }
    }

    public int SubtasksTotal => Task.Subtasks.Count;
    public int SubtasksCompleted => Task.Subtasks.Count(s => s.IsCompleted);
    public bool HasSubtasks => SubtasksTotal > 0;

    partial void OnIsCalendarOpenChanged(bool value) => OnPropertyChanged(nameof(HasAnyOverlay));
    partial void OnIsPriorityMenuOpenChanged(bool value) => OnPropertyChanged(nameof(HasAnyOverlay));
    partial void OnIsListMenuOpenChanged(bool value) => OnPropertyChanged(nameof(HasAnyOverlay));
    partial void OnIsRecurrenceMenuOpenChanged(bool value) => OnPropertyChanged(nameof(HasAnyOverlay));
    partial void OnIsReminderMenuOpenChanged(bool value) => OnPropertyChanged(nameof(HasAnyOverlay));

    public bool IsCompleted => Task.IsCompleted;

    public bool IsOverdue => !Task.IsCompleted && Task.Date < _today;

    public bool IsHigh => Task.Priority == TaskPriority.High;
    public bool IsMedium => Task.Priority == TaskPriority.Medium;
    public bool IsLow => Task.Priority == TaskPriority.Low;
    public bool IsNone => Task.Priority == TaskPriority.None;

    public string PriorityText => Task.Priority switch
    {
        TaskPriority.High => "高优先级",
        TaskPriority.Medium => "中优先级",
        TaskPriority.Low => "低优先级",
        _ => "无优先级",
    };

    public string DateLabel
    {
        get
        {
            var absolute = $"{Task.Date.Month}月{Task.Date.Day}日";
            var relative = DateLabels.Describe(Task.Date, _today);
            var baseText = relative == absolute ? absolute : $"{absolute}, {relative}";
            return Task.DueTime is { } dt ? $"{baseText} {dt:HH:mm}" : baseText;
        }
    }

    public string ListName => TaskListCatalog.NameOf(Task.ListId);

    public ObservableCollection<ListPickItem> ListOptions { get; } = [];

    public string CreatedLabel => $"创建于 {Task.CreatedAt.LocalDateTime:M月d日 HH:mm}";

    public string ContentMetaLabel
    {
        get
        {
            if (IsChecklistMode)
            {
                return SubtasksTotal == 0 ? string.Empty : $"待办进度 {SubtasksCompleted}/{SubtasksTotal}";
            }

            return string.IsNullOrWhiteSpace(Content)
                ? string.Empty
                : $"{Content.Trim().Length} 字";
        }
    }

    public bool HasContentMeta => !string.IsNullOrEmpty(ContentMetaLabel);

    public IRelayCommand ToggleCompletedCommand { get; }
    public IRelayCommand ToggleCalendarCommand { get; }
    public IRelayCommand TogglePriorityMenuCommand { get; }
    public IRelayCommand SetPriorityHighCommand { get; }
    public IRelayCommand SetPriorityMediumCommand { get; }
    public IRelayCommand SetPriorityLowCommand { get; }
    public IRelayCommand SetPriorityNoneCommand { get; }
    public IRelayCommand CloseCommand { get; }
    public IRelayCommand DeleteCommand { get; }
    public IRelayCommand ToggleListMenuCommand { get; }
    public IRelayCommand ToggleRecurrenceMenuCommand { get; }

    partial void OnTitleChanged(string value)
    {
        if (_syncing)
        {
            return;
        }

        var title = value?.Trim() ?? string.Empty;
        if (title.Length == 0 || string.Equals(title, Task.Title, StringComparison.Ordinal))
        {
            return;
        }

        Task.Title = title;
        _repository.Update(Task);
    }

    partial void OnContentChanged(string value)
    {
        OnPropertyChanged(nameof(ContentMetaLabel));
        OnPropertyChanged(nameof(HasContentMeta));

        if (_syncing)
        {
            return;
        }

        var content = value ?? string.Empty;
        if (string.Equals(content, Task.Content, StringComparison.Ordinal))
        {
            return;
        }

        Task.Content = content;
        _repository.Update(Task);
    }

    public void CommitTitle()
    {
        _syncing = true;
        try
        {
            Title = string.IsNullOrWhiteSpace(Title) ? Task.Title : Title.Trim();
        }
        finally
        {
            _syncing = false;
        }
    }

    public void SyncFromTask(TaskItem task)
    {
        var dateChanged = task.Date != Task.Date;
        Task = task;

        _syncing = true;
        try
        {
            if (!IsTitleFocused && !string.Equals(Title, task.Title, StringComparison.Ordinal))
            {
                Title = task.Title;
            }

            if (!IsContentFocused && !string.Equals(Content, task.Content, StringComparison.Ordinal))
            {
                Content = task.Content;
            }
        }
        finally
        {
            _syncing = false;
        }

        RefreshComputed();
        if (dateChanged)
        {
            DatePicker.SyncSelectedDate(task.Date);
        }

        if (!_updatingSubtasks)
        {
            var hasSubtasks = task.Subtasks.Count > 0;
            if (hasSubtasks && !IsChecklistMode)
            {
                IsChecklistMode = true;
            }

            if (IsChecklistMode)
            {
                RebuildSubtasks(task.Subtasks);
            }
        }
    }

    public void ToggleChecklistMode()
    {
        if (!IsChecklistMode)
        {
            var items = ChecklistConverter.LinesToSubtasks(Content);
            _syncing = true;
            try
            {
                Content = string.Empty;
                Task.Content = string.Empty;
            }
            finally
            {
                _syncing = false;
            }

            Subtasks.Clear();
            foreach (var item in items)
            {
                AttachSubtask(item);
                Subtasks.Add(item);
            }

            IsChecklistMode = true;
            SyncSubtasksToRepository();
        }
        else
        {
            var text = ChecklistConverter.SubtasksToLines(Subtasks);
            Subtasks.Clear();
            IsChecklistMode = false;
            Task.Subtasks = [];
            _syncing = true;
            try
            {
                Content = text;
                Task.Content = text;
            }
            finally
            {
                _syncing = false;
            }

            _updatingSubtasks = true;
            try
            {
                _repository.Update(Task);
            }
            finally
            {
                _updatingSubtasks = false;
            }

            RefreshComputed();
        }
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
                SyncSubtasksToRepository();
                return item;
            }
        }

        Subtasks.Add(item);
        SyncSubtasksToRepository();
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
            Task.Subtasks = [];
            SyncSubtasksToRepository();
            return null;
        }

        Subtasks.RemoveAt(idx);
        SyncSubtasksToRepository();
        return Subtasks[Math.Clamp(idx - 1, 0, Subtasks.Count - 1)];
    }

    private void RebuildSubtasks(IEnumerable<TaskSubtask> source)
    {
        Subtasks.Clear();
        foreach (var s in source)
        {
            var vm = new QuickSubtaskItem(s.Title, s.IsCompleted, s.Id);
            AttachSubtask(vm);
            Subtasks.Add(vm);
        }
    }

    private void AttachSubtask(QuickSubtaskItem item) =>
        item.PropertyChanged += (_, _) => SyncSubtasksToRepository();

    private void SyncSubtasksToRepository()
    {
        Task.Subtasks = Subtasks
            .Where(s => !string.IsNullOrWhiteSpace(s.Title))
            .Select(s => new TaskSubtask
            {
                Id = s.Id,
                Title = s.Title.Trim(),
                IsCompleted = s.IsCompleted,
            })
            .ToList();

        _updatingSubtasks = true;
        try
        {
            _repository.Update(Task);
        }
        finally
        {
            _updatingSubtasks = false;
        }

        RefreshComputed();
    }

    public bool CloseOpenPopups()
    {
        if (IsCalendarOpen)
        {
            IsCalendarOpen = false;
            return true;
        }

        if (IsPriorityMenuOpen)
        {
            IsPriorityMenuOpen = false;
            return true;
        }

        if (IsListMenuOpen)
        {
            IsListMenuOpen = false;
            return true;
        }

        if (IsRecurrenceMenuOpen)
        {
            IsRecurrenceMenuOpen = false;
            return true;
        }

        if (IsReminderMenuOpen)
        {
            IsReminderMenuOpen = false;
            return true;
        }

        return false;
    }

    public void PrepareReminderEditor() =>
        ReminderEditor.Load(Task.Date, Task.DueTime, Task.Reminder, _today);

    private void OnReminderEdited(TimeOnly? dueTime, TaskReminder? reminder)
    {
        Task.DueTime = dueTime;
        Task.Reminder = reminder?.Clone();
        _repository.Update(Task);
        RefreshComputed();
    }

    public void PickRecurrence(RecurrenceKind kind)
    {
        if (Task.Recurrence != kind)
        {
            Task.Recurrence = kind;
            _repository.Update(Task);
            RefreshComputed();
        }

        IsRecurrenceMenuOpen = false;
    }

    public bool AddTag(string? raw = null)
    {
        var candidate = TaskTagParser.CleanSingle(raw ?? NewTagText);
        NewTagText = string.Empty;
        if (candidate is null)
        {
            return false;
        }

        if (Task.Tags.Any(t => string.Equals(t, candidate, StringComparison.OrdinalIgnoreCase)) ||
            Task.Tags.Count >= TaskTagParser.MaxTagsPerTask)
        {
            return false;
        }

        Task.Tags.Add(candidate);
        _repository.Update(Task);
        RebuildTags(Task.Tags);
        return true;
    }

    public void RemoveTag(string tag)
    {
        var idx = Task.Tags.FindIndex(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
        {
            return;
        }

        Task.Tags.RemoveAt(idx);
        _repository.Update(Task);
        RebuildTags(Task.Tags);
    }

    public void RemoveLastTag()
    {
        if (Task.Tags.Count == 0)
        {
            return;
        }

        Task.Tags.RemoveAt(Task.Tags.Count - 1);
        _repository.Update(Task);
        RebuildTags(Task.Tags);
    }

    private void RebuildTags(IEnumerable<string> source)
    {
        Tags.Clear();
        foreach (var tag in TaskTagParser.Normalize(source))
        {
            Tags.Add(tag);
        }

        OnPropertyChanged(nameof(HasTags));
    }

    private void RebuildRecurrenceOptions()
    {
        RecurrenceOptions.Clear();
        RecurrenceKind[] all =
        [
            RecurrenceKind.None,
            RecurrenceKind.Daily,
            RecurrenceKind.Weekdays,
            RecurrenceKind.Weekly,
            RecurrenceKind.Monthly,
            RecurrenceKind.Yearly,
            RecurrenceKind.LunarYearly,
        ];

        foreach (var kind in all)
        {
            RecurrenceOptions.Add(new RecurrencePickItem(
                kind,
                RecurrenceCalculator.FormatLabel(kind),
                Task.Recurrence == kind));
        }
    }

    public void PickList(string listId)
    {
        if (!string.Equals(Task.ListId, listId, StringComparison.Ordinal))
        {
            Task.ListId = listId;
            _repository.Update(Task);
            RefreshComputed();
        }

        IsListMenuOpen = false;
    }

    private void RebuildListOptions()
    {
        ListOptions.Clear();
        foreach (var list in _repository.Lists)
        {
            ListOptions.Add(new ListPickItem(
                list.Id,
                list.Name,
                list.Color,
                string.Equals(list.Id, Task.ListId, StringComparison.Ordinal)));
        }
    }

    private void PickDate(DateOnly date)
    {
        if (Task.Date != date)
        {
            Task.Date = date;
            _repository.Update(Task);
        }

        IsCalendarOpen = false;
        RefreshComputed();
    }

    private void ToggleCompleted()
    {
        _repository.SetCompleted(Task.Id, !Task.IsCompleted);
        RefreshComputed();
    }

    private void SetPriority(TaskPriority priority)
    {
        IsPriorityMenuOpen = false;
        if (Task.Priority == priority)
        {
            return;
        }

        Task.Priority = priority;
        _repository.Update(Task);
        RefreshComputed();
    }

    private void ToggleCalendar()
    {
        if (!IsCalendarOpen)
        {
            DatePicker.SyncSelectedDate(Task.Date);
        }

        IsCalendarOpen = !IsCalendarOpen;
    }

    private void RefreshComputed()
    {
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(IsOverdue));
        OnPropertyChanged(nameof(IsHigh));
        OnPropertyChanged(nameof(IsMedium));
        OnPropertyChanged(nameof(IsLow));
        OnPropertyChanged(nameof(IsNone));
        OnPropertyChanged(nameof(PriorityText));
        OnPropertyChanged(nameof(DateLabel));
        OnPropertyChanged(nameof(ListName));
        OnPropertyChanged(nameof(Recurrence));
        OnPropertyChanged(nameof(HasRecurrence));
        OnPropertyChanged(nameof(RecurrenceLabel));
        OnPropertyChanged(nameof(HasReminder));
        OnPropertyChanged(nameof(ReminderChipLabel));
        OnPropertyChanged(nameof(ReminderTooltip));
        RebuildListOptions();
        RebuildRecurrenceOptions();
        RebuildTags(Task.Tags);
        OnPropertyChanged(nameof(ContentMetaLabel));
        OnPropertyChanged(nameof(HasContentMeta));
        OnPropertyChanged(nameof(SubtasksTotal));
        OnPropertyChanged(nameof(SubtasksCompleted));
        OnPropertyChanged(nameof(HasSubtasks));
    }
}

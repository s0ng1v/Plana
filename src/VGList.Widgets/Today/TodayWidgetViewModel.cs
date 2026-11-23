using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VGList.App.Assistant;
using VGList.App.Services;
using VGList.Core.Abstractions;
using VGList.Core.Models;
using VGList.Widgets.Support;

namespace VGList.Widgets.Today;

/// <summary>小组件当前展示的时间范围。</summary>
public enum WidgetViewMode
{
    All,
    Today,
    Tomorrow,
    Next7Days,
}

/// <summary>
/// 今日日程小组件的全部状态：草稿、优先级、日期、视图、已完成分组。
/// 唯一数据入口是 ITaskRepository，所有窗口共享同一实例。
/// </summary>
public sealed partial class TodayWidgetViewModel : ObservableObject, IDisposable
{
    private readonly ITaskRepository _repository;
    private readonly IScheduleService? _schedule;
    private readonly DraftDateAutoFill _dateAutoFill;
    private readonly MorningBriefService? _briefService;
    private readonly Func<bool>? _morningBriefEnabled;
    private bool _disposed;

    /// <summary>本次编辑中用户是否按过 Tab 主动选过优先级；决定占位是提示语还是优先级名。</summary>
    private bool _priorityPicked;

    public TodayWidgetViewModel(
        ITaskRepository repository,
        DateOnly? today = null,
        MorningBriefService? briefService = null,
        Func<bool>? morningBriefEnabled = null)
    {
        _repository = repository;
        _schedule = repository as IScheduleService;
        _briefService = briefService;
        _morningBriefEnabled = morningBriefEnabled;
        Today = today ?? DateOnly.FromDateTime(DateTime.Today);
        visibleMonth = new DateTime(Today.Year, Today.Month, 1);
        DraftDate = Today;
        _dateAutoFill = new DraftDateAutoFill(Today, () => AutoDetectDatePreference.IsEnabled);

        SubmitCommand = new RelayCommand(SubmitDraft);
        ToggleCompletedGroupCommand = new RelayCommand(ToggleCompletedGroup);
        ToggleCalendarCommand = new RelayCommand(ToggleCalendar);
        NextMonthCommand = new RelayCommand(() => ShiftMonth(1));
        PreviousMonthCommand = new RelayCommand(() => ShiftMonth(-1));
        ClearDateCommand = new RelayCommand(ClearDate);
        PostponeOverdueCommand = new RelayCommand(PostponeOverdue);

        _repository.Changed += OnRepositoryChanged;
        Refresh();
        BuildCalendar();
        RefreshMorningBrief();
    }

    public event EventHandler<MorningBriefInfo>? MorningBriefActivated;

    [ObservableProperty]
    private MorningBriefInfo? _morningBrief;

    public bool HasMorningBrief => MorningBrief is not null;

    public string MorningBriefText => MorningBrief?.BannerText ?? string.Empty;

    partial void OnMorningBriefChanged(MorningBriefInfo? value)
    {
        OnPropertyChanged(nameof(HasMorningBrief));
        OnPropertyChanged(nameof(MorningBriefText));
    }

    public void RefreshMorningBrief(DateOnly? currentDate = null)
    {
        if (_briefService is null)
        {
            MorningBrief = null;
            return;
        }

        var enabled = _morningBriefEnabled?.Invoke() ?? true;
        MorningBrief = _briefService.Evaluate(currentDate ?? Today, enabled);
    }

    public void OpenMorningBrief()
    {
        if (MorningBrief is not { } info)
        {
            return;
        }

        _briefService?.DismissToday(info.Date);
        MorningBrief = null;
        MorningBriefActivated?.Invoke(this, info);
    }

    public void DismissMorningBrief()
    {
        var date = MorningBrief?.Date ?? Today;
        _briefService?.DismissToday(date);
        MorningBrief = null;
    }

    public DateOnly Today { get; }

    public ObservableCollection<TaskRowViewModel> Tasks { get; } = [];

    public ObservableCollection<TaskGroupViewModel> Groups { get; } = [];

    public ObservableCollection<TaskRowViewModel> CompletedTasks { get; } = [];

    public ObservableCollection<DayCellViewModel> Days { get; } = [];

    [ObservableProperty]
    private string draftText = string.Empty;

    [ObservableProperty]
    private TaskPriority draftPriority = TaskPriority.None;

    [ObservableProperty]
    private DateOnly draftDate;

    [ObservableProperty]
    private bool isEditing;

    [ObservableProperty]
    private bool isCalendarOpen;

    [ObservableProperty]
    private bool completedExpanded;

    [ObservableProperty]
    private DateTime visibleMonth;

    [ObservableProperty]
    private WidgetViewMode activeView = WidgetViewMode.Today;

    [ObservableProperty]
    private bool showCompleted = true;

    public bool IsHigh => DraftPriority == TaskPriority.High;

    public bool IsMedium => DraftPriority == TaskPriority.Medium;

    public bool IsLow => DraftPriority == TaskPriority.Low;

    public bool IsNone => DraftPriority == TaskPriority.None;

    /// <summary>占位当前显示的是提示语（而非优先级名），此时用中性色。</summary>
    public bool IsPriorityHint => !_priorityPicked;

    public bool IsAllView => ActiveView == WidgetViewMode.All;

    public bool IsTodayView => ActiveView == WidgetViewMode.Today;

    public bool IsTomorrowView => ActiveView == WidgetViewMode.Tomorrow;

    public bool IsNext7DaysView => ActiveView == WidgetViewMode.Next7Days;

    public string TitleLabel => ActiveView switch
    {
        WidgetViewMode.All => "所有",
        WidgetViewMode.Tomorrow => "明天",
        WidgetViewMode.Next7Days => "最近7天",
        _ => "今天",
    };

    public bool HasPendingTasks => Tasks.Count > 0;

    public bool HasCompleted => CompletedTasks.Count > 0;

    public bool ShowCompletedSection => ShowCompleted && HasCompleted;

    public bool IsCompletedListVisible => CompletedExpanded && ShowCompletedSection;

    public string CompletedCount => CompletedTasks.Count.ToString();

    public string DateLabel => DateLabels.Describe(DraftDate, Today);

    /// <summary>录入区占位文案：空闲时提示添加任务；聚焦后先提示用 Tab 切换，按过 Tab 才报出当前优先级。</summary>
    public string PlaceholderText
    {
        get
        {
            if (!IsEditing || !string.IsNullOrEmpty(DraftText))
            {
                return "添加任务";
            }

            if (!_priorityPicked)
            {
                return "按Tab切换优先级";
            }

            return DraftPriority switch
            {
                TaskPriority.Low => "低优先级",
                TaskPriority.Medium => "中优先级",
                TaskPriority.High => "高优先级",
                _ => "无优先级",
            };
        }
    }

    public string MonthTitle => $"{VisibleMonth.Year}年{VisibleMonth.Month}月";

    public IRelayCommand SubmitCommand { get; }

    public IRelayCommand ToggleCompletedGroupCommand { get; }

    public IRelayCommand ToggleCalendarCommand { get; }

    public IRelayCommand NextMonthCommand { get; }

    public IRelayCommand PreviousMonthCommand { get; }

    public IRelayCommand ClearDateCommand { get; }

    public IRelayCommand PostponeOverdueCommand { get; }

    partial void OnDraftPriorityChanged(TaskPriority value)
    {
        OnPropertyChanged(nameof(IsHigh));
        OnPropertyChanged(nameof(IsMedium));
        OnPropertyChanged(nameof(IsLow));
        OnPropertyChanged(nameof(IsNone));
        OnPropertyChanged(nameof(PlaceholderText));
    }

    partial void OnDraftTextChanged(string value)
    {
        OnPropertyChanged(nameof(PlaceholderText));
        if (_dateAutoFill.Evaluate(value, Today) is { } detected && detected != DraftDate)
        {
            DraftDate = detected;
        }
    }

    partial void OnDraftDateChanged(DateOnly value)
    {
        OnPropertyChanged(nameof(DateLabel));
        BuildCalendar();
    }

    partial void OnVisibleMonthChanged(DateTime value)
    {
        OnPropertyChanged(nameof(MonthTitle));
        BuildCalendar();
    }

    partial void OnIsEditingChanged(bool value)
    {
        // 每次进出编辑态都重新开始：占位先提示按 Tab，而不是直接报出默认优先级。
        _priorityPicked = false;

        if (!value)
        {
            IsCalendarOpen = false;
            // 失焦即还原：不再保留草稿优先级，下次点击重新从低优先级开始。
            DraftPriority = TaskPriority.None;
        }
        else if (DraftPriority == TaskPriority.None)
        {
            // 点击进入编辑态即默认低优先级（只体现在边框上）。
            DraftPriority = TaskPriority.Low;
        }

        OnPropertyChanged(nameof(PlaceholderText));
        OnPropertyChanged(nameof(IsPriorityHint));
    }

    partial void OnActiveViewChanged(WidgetViewMode value)
    {
        OnPropertyChanged(nameof(IsAllView));
        OnPropertyChanged(nameof(IsTodayView));
        OnPropertyChanged(nameof(IsTomorrowView));
        OnPropertyChanged(nameof(IsNext7DaysView));
        OnPropertyChanged(nameof(TitleLabel));
        Refresh();
    }

    partial void OnShowCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCompletedSection));
        OnPropertyChanged(nameof(IsCompletedListVisible));
    }

    partial void OnCompletedExpandedChanged(bool value) => OnPropertyChanged(nameof(IsCompletedListVisible));

    public void SetPriority(TaskPriority priority) => DraftPriority = priority;

    public void CyclePriority(bool reverse = false)
    {
        // 本次编辑的第一次 Tab 直接从循环起点开始：正向「高」，反向「无」；之后才按顺序循环。
        var first = !_priorityPicked;
        _priorityPicked = true;
        OnPropertyChanged(nameof(IsPriorityHint));

        DraftPriority = first
            ? (reverse ? TaskPriority.None : TaskPriority.High)
            : DraftPriority switch
            {
                TaskPriority.None => reverse ? TaskPriority.Low : TaskPriority.High,
                TaskPriority.High => reverse ? TaskPriority.None : TaskPriority.Medium,
                TaskPriority.Medium => reverse ? TaskPriority.High : TaskPriority.Low,
                _ => reverse ? TaskPriority.Medium : TaskPriority.None,
            };
    }

    public void SetView(WidgetViewMode view) => ActiveView = view;

    public void SubmitDraft()
    {
        var raw = DraftText?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return;
        }

        var (tagClean, inlineTags) = TaskTagParser.ExtractInlineTags(raw);
        var (title, date) = _dateAutoFill.ResolveForSubmit(tagClean, DraftDate, Today);
        _repository.Create(new TaskDraft
        {
            Title = title,
            Date = date,
            Priority = DraftPriority,
            ListId = TaskList.InboxId,
            Tags = inlineTags,
        });

        var resetDate = _dateAutoFill.UserPickedDate ? DraftDate : Today;
        _dateAutoFill.Reset(Today);
        DraftDate = resetDate;
        DraftText = string.Empty;
        DraftPriority = IsEditing ? TaskPriority.Low : TaskPriority.None;
        _priorityPicked = false;
        OnPropertyChanged(nameof(PlaceholderText));
        OnPropertyChanged(nameof(IsPriorityHint));
        Refresh();
    }

    public void ToggleTask(TaskItem task) => _repository.SetCompleted(task.Id, !task.IsCompleted);

    public void ToggleCompletedGroup() => CompletedExpanded = !CompletedExpanded;

    public void ToggleCalendar() => IsCalendarOpen = !IsCalendarOpen;

    public void PickDate(DateOnly date)
    {
        _dateAutoFill.MarkUserPickedDate();
        DraftDate = date;
        VisibleMonth = new DateTime(date.Year, date.Month, 1);
        IsCalendarOpen = false;
    }

    /// <summary>日历内点选：只更新草稿并高亮，等「确定」再关闭。</summary>
    public void SelectDate(DateOnly date)
    {
        _dateAutoFill.MarkUserPickedDate();
        DraftDate = date;
        if (date.Year != VisibleMonth.Year || date.Month != VisibleMonth.Month)
        {
            VisibleMonth = new DateTime(date.Year, date.Month, 1);
        }
    }

    public void CloseCalendar() => IsCalendarOpen = false;

    public void ShowMonth(DateOnly date) => VisibleMonth = new DateTime(date.Year, date.Month, 1);

    public TaskDetailViewModel CreateDetail(TaskItem task) => new(_repository, task, Today);

    public void ClearDate()
    {
        _dateAutoFill.Reset(Today);
        DraftDate = Today;
        IsCalendarOpen = false;
    }

    public void ShiftMonth(int delta) => VisibleMonth = VisibleMonth.AddMonths(delta);

    public void EndEditing() => IsEditing = false;

    private void OnRepositoryChanged(object? sender, TaskChangedEventArgs e) => UiDispatcher.PostIfNeeded(() =>
    {
        if (_disposed)
        {
            return;
        }

        Refresh();
        RefreshMorningBrief();
    });

    /// <summary>一键将当前全部未完成的逾期任务顺延到今天（原子可撤销）。</summary>
    public void PostponeOverdue()
    {
        var overdueTasks = _repository
            .Query(new TaskQuery { To = Today.AddDays(-1), IncludeCompleted = false })
            .ToList();
        if (overdueTasks.Count == 0)
        {
            return;
        }

        if (_schedule is not null)
        {
            var ops = overdueTasks
                .Select(task => (ScheduleOp)new UpdateTaskOp(task.Id, new TaskPatch { Date = Today }))
                .ToList();
            _schedule.Apply(ops, ChangeSource.User, $"顺延 {overdueTasks.Count} 条逾期任务到今天");
            return;
        }

        foreach (var task in overdueTasks)
        {
            task.Date = Today;
            _repository.Update(task);
        }
    }

    private void Refresh()
    {
        var query = ActiveView switch
        {
            WidgetViewMode.All => new TaskQuery(),
            WidgetViewMode.Tomorrow => TaskQuery.ForDate(Today.AddDays(1)),
            WidgetViewMode.Next7Days => new TaskQuery { To = Today.AddDays(6) },
            _ => new TaskQuery { To = Today },
        };

        var items = _repository.Query(query);

        Tasks.Clear();
        Groups.Clear();
        CompletedTasks.Clear();

        var pending = items.Where(t => !t.IsCompleted).ToList();
        var completedSource = ActiveView switch
        {
            WidgetViewMode.Today => items.Where(t => t.IsCompleted && t.Date == Today),
            WidgetViewMode.Next7Days => items.Where(t => t.IsCompleted && t.Date >= Today && t.Date <= Today.AddDays(6)),
            _ => items.Where(t => t.IsCompleted),
        };
        foreach (var item in completedSource)
        {
            CompletedTasks.Add(new TaskRowViewModel(item, Today, ToggleTask));
        }

        if (ActiveView == WidgetViewMode.All)
        {
            // 所有视图：今天固定最上，其后按与今天的距离排列（未来在前、过去的在后）。
            var ordered = pending
                .GroupBy(t => t.Date)
                .OrderBy(g => g.Key == Today ? 0 : g.Key > Today ? 1 : 2)
                .ThenBy(g => g.Key == Today ? 0 : Math.Abs(g.Key.DayNumber - Today.DayNumber));

            foreach (var group in ordered)
            {
                var rows = group.Select(t => new TaskRowViewModel(t, Today, ToggleTask)).ToList();
                Groups.Add(new TaskGroupViewModel(DateLabels.DescribeHeader(group.Key, Today), true, rows));
                foreach (var row in rows)
                {
                    Tasks.Add(row);
                }
            }
        }
        else if (ActiveView is WidgetViewMode.Today or WidgetViewMode.Next7Days)
        {
            var overdue = pending
                .Where(t => t.Date < Today)
                .OrderBy(t => t.Date)
                .ThenByDescending(t => t.Priority)
                .ThenByDescending(t => t.CreatedAt)
                .ToList();
            var current = pending.Where(t => t.Date >= Today).ToList();

            if (overdue.Count > 0)
            {
                var overdueRows = overdue.Select(t => new TaskRowViewModel(t, Today, ToggleTask)).ToList();
                Groups.Add(new TaskGroupViewModel("已过期", true, overdueRows, isOverdueGroup: true));
                foreach (var row in overdueRows)
                {
                    Tasks.Add(row);
                }

                if (current.Count > 0)
                {
                    var currentRows = current.Select(t => new TaskRowViewModel(t, Today, ToggleTask)).ToList();
                    var currentHeader = ActiveView == WidgetViewMode.Today ? "今天" : "最近7天";
                    Groups.Add(new TaskGroupViewModel(currentHeader, true, currentRows));
                    foreach (var row in currentRows)
                    {
                        Tasks.Add(row);
                    }
                }
            }
            else
            {
                var rows = current.Select(t => new TaskRowViewModel(t, Today, ToggleTask)).ToList();
                Groups.Add(new TaskGroupViewModel(string.Empty, false, rows));
                foreach (var row in rows)
                {
                    Tasks.Add(row);
                }
            }
        }
        else
        {
            var rows = pending.Select(t => new TaskRowViewModel(t, Today, ToggleTask)).ToList();
            Groups.Add(new TaskGroupViewModel(string.Empty, false, rows));
            foreach (var row in rows)
            {
                Tasks.Add(row);
            }
        }

        OnPropertyChanged(nameof(HasPendingTasks));
        OnPropertyChanged(nameof(HasCompleted));
        OnPropertyChanged(nameof(ShowCompletedSection));
        OnPropertyChanged(nameof(IsCompletedListVisible));
        OnPropertyChanged(nameof(CompletedCount));
    }

    private void BuildCalendar()
    {
        Days.Clear();

        var first = new DateOnly(VisibleMonth.Year, VisibleMonth.Month, 1);
        var offset = (int)first.DayOfWeek;
        var start = first.AddDays(-offset);

        for (var i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            Days.Add(new DayCellViewModel(
                date,
                isOutside: date.Month != first.Month,
                isToday: date == Today,
                isSelected: date == DraftDate,
                SelectDate));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _repository.Changed -= OnRepositoryChanged;
    }
}

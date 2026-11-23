using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VGList.App.Services;
using VGList.Core.Abstractions;
using VGList.Core.Dates;
using VGList.Core.Models;
using VGList.Desktop.Main.Tasks;
using VGList.Desktop.Services;
using VGList.Widgets.Support;

namespace VGList.Desktop.Main.Calendar;

public enum CalendarViewKind
{
    Month,
    Week,
}

public interface ICalendarDateTarget
{
    DateOnly Date { get; }
    bool IsDropTarget { get; set; }
}

/// <summary>日历网格/周列中的任务条模型。</summary>
public sealed partial class CalendarTaskItemViewModel : ObservableObject
{
    public CalendarTaskItemViewModel(TaskItem task)
    {
        Task = task;
        Title = task.Title;
        ListName = TaskListCatalog.NameOf(task.ListId);
        HasDescription = !string.IsNullOrWhiteSpace(task.Content);
        DescriptionPreview = task.Content.Replace('\r', ' ').Replace('\n', ' ').Trim();
        IsCompleted = task.IsCompleted;
        IsHigh = !task.IsCompleted && task.Priority == TaskPriority.High;
        IsMedium = !task.IsCompleted && task.Priority == TaskPriority.Medium;
        IsLow = !task.IsCompleted && task.Priority == TaskPriority.Low;
        IsNone = !task.IsCompleted && task.Priority == TaskPriority.None;
    }

    public TaskItem Task { get; }
    public string Id => Task.Id;
    public string Title { get; }
    public string ListName { get; }
    public bool HasDescription { get; }
    public string DescriptionPreview { get; }
    public int SubtasksTotal => Task.Subtasks.Count;
    public int SubtasksCompleted => Task.Subtasks.Count(s => s.IsCompleted);
    public bool HasSubtasks => SubtasksTotal > 0;
    public string SubtasksTooltip => HasSubtasks ? $"待办进度 {SubtasksCompleted}/{SubtasksTotal}" : string.Empty;
    public bool IsCompleted { get; }
    public bool IsHigh { get; }
    public bool IsMedium { get; }
    public bool IsLow { get; }
    public bool IsNone { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>日历月视图单元格（42 格）。</summary>
public sealed partial class CalendarMonthCellViewModel : ObservableObject, ICalendarDateTarget
{
    private readonly IReadOnlyList<TaskItem> _allTasks;

    public CalendarMonthCellViewModel(
        DateOnly date,
        bool isCurrentMonth,
        DateOnly today,
        int maxChips,
        IReadOnlyList<TaskItem> tasks)
    {
        Date = date;
        IsCurrentMonth = isCurrentMonth;
        IsOutside = !isCurrentMonth;
        IsToday = date == today;
        DayLabel = date.Day == 1 ? $"{date.Month}月1日" : date.Day.ToString();
        IsFirstDay = date.Day == 1;

        var lunar = LunarCalendar.Describe(date);
        LunarLabel = lunar.Text;
        IsFestival = lunar.Kind == CalendarLabelKind.Festival;
        IsSolarTerm = lunar.Kind == CalendarLabelKind.SolarTerm;

        _allTasks = tasks;
        UpdateVisibleTasks(maxChips);
    }

    public DateOnly Date { get; }
    public bool IsCurrentMonth { get; }
    public bool IsOutside { get; }
    public bool IsToday { get; }
    public bool IsFirstDay { get; }
    public string DayLabel { get; }
    public string LunarLabel { get; }
    public bool IsFestival { get; }
    public bool IsSolarTerm { get; }
    public IReadOnlyList<TaskItem> AllTasks => _allTasks;
    public IReadOnlyList<CalendarTaskItemViewModel> VisibleTasks { get; private set; } = [];
    public bool HasOverflow { get; private set; }
    public string OverflowLabel { get; private set; } = string.Empty;
    public double ContentOpacity => IsCurrentMonth ? 1.0 : 0.45;

    [ObservableProperty]
    private bool _isDropTarget;

    public void UpdateVisibleTasks(int maxChips)
    {
        var capacity = Math.Max(1, maxChips);
        if (_allTasks.Count <= capacity)
        {
            VisibleTasks = _allTasks.Select(t => new CalendarTaskItemViewModel(t)).ToList();
            HasOverflow = false;
            OverflowLabel = string.Empty;
        }
        else
        {
            var visibleCount = Math.Max(1, capacity - 1);
            VisibleTasks = _allTasks.Take(visibleCount).Select(t => new CalendarTaskItemViewModel(t)).ToList();
            HasOverflow = true;
            OverflowLabel = $"+{_allTasks.Count - visibleCount} 更多";
        }

        OnPropertyChanged(nameof(VisibleTasks));
        OnPropertyChanged(nameof(HasOverflow));
        OnPropertyChanged(nameof(OverflowLabel));
    }
}

/// <summary>日历周视图中的一天列。</summary>
public sealed partial class CalendarWeekDayViewModel : ObservableObject, ICalendarDateTarget
{
    public CalendarWeekDayViewModel(DateOnly date, DateOnly today, IReadOnlyList<TaskItem> tasks)
    {
        Date = date;
        IsToday = date == today;
        WeekdayLabel = DateLabels.Weekday(date);
        DayNumber = date.Day.ToString();

        var lunar = LunarCalendar.Describe(date);
        LunarLabel = lunar.Text;
        IsFestival = lunar.Kind is CalendarLabelKind.Festival or CalendarLabelKind.SolarTerm;

        Tasks = tasks.Select(t => new CalendarTaskItemViewModel(t)).ToList();
        IsEmpty = Tasks.Count == 0;
        CountLabel = Tasks.Count.ToString();
        HasTasks = Tasks.Count > 0;
    }

    public DateOnly Date { get; }
    public bool IsToday { get; }
    public string WeekdayLabel { get; }
    public string DayNumber { get; }
    public string LunarLabel { get; }
    public bool IsFestival { get; }
    public IReadOnlyList<CalendarTaskItemViewModel> Tasks { get; }
    public bool IsEmpty { get; }
    public bool HasTasks { get; }
    public string CountLabel { get; }

    [ObservableProperty]
    private bool _isDropTarget;
}

/// <summary>主窗口日历页：月视图 / 周视图、点击新建、指针拖拽改期与详情抽屉联动。</summary>
public sealed partial class CalendarPageViewModel : ObservableObject, IDisposable
{
    private readonly ITaskRepository _repository;
    private readonly ToastService _toast;
    private readonly WidgetCoordinator _coordinator;
    private readonly DateOnly _today;
    private DateOnly _anchorDate;
    private string? _selectedTaskId;
    private int _maxChipsPerCell = 3;
    private bool _disposed;

    public CalendarPageViewModel(
        ITaskRepository repository,
        ToastService toast,
        WidgetCoordinator coordinator,
        DateOnly today)
    {
        _repository = repository;
        _toast = toast;
        _coordinator = coordinator;
        _today = today;
        _anchorDate = today;
        _showCompleted = false;
        _quickAddDate = today;
        QuickCreate = new QuickCreateViewModel(today);

        _repository.Changed += OnRepositoryChanged;
        Refresh();
    }

    public QuickCreateViewModel QuickCreate { get; }

    public ObservableCollection<CalendarMonthCellViewModel> MonthCells { get; } = [];
    public ObservableCollection<CalendarWeekDayViewModel> WeekColumns { get; } = [];
    public ObservableCollection<CalendarTaskItemViewModel> OverflowTasks { get; } = [];

    [ObservableProperty]
    private CalendarViewKind _viewKind = CalendarViewKind.Month;

    [ObservableProperty]
    private int _weekRows = 5;

    [ObservableProperty]
    private bool _showCompleted;

    [ObservableProperty]
    private string _periodTitle = string.Empty;

    [ObservableProperty]
    private MainTaskDetailViewModel? _detail;

    [ObservableProperty]
    private bool _isQuickAddOpen;

    [ObservableProperty]
    private DateOnly _quickAddDate;

    [ObservableProperty]
    private string _quickAddText = string.Empty;

    [ObservableProperty]
    private TaskPriority _quickAddPriority = TaskPriority.None;

    [ObservableProperty]
    private bool _isOverflowOpen;

    [ObservableProperty]
    private DateOnly _overflowDate;

    [ObservableProperty]
    private string _overflowTitle = string.Empty;

    public bool IsMonthView => ViewKind == CalendarViewKind.Month;
    public bool IsWeekView => ViewKind == CalendarViewKind.Week;
    public bool HasSelection => Detail is not null;
    public bool HasAnyOverlay => IsQuickAddOpen || IsOverflowOpen;

    partial void OnIsQuickAddOpenChanged(bool value) => OnPropertyChanged(nameof(HasAnyOverlay));
    partial void OnIsOverflowOpenChanged(bool value) => OnPropertyChanged(nameof(HasAnyOverlay));

    public void CloseOverlays()
    {
        IsQuickAddOpen = false;
        IsOverflowOpen = false;
        if (_selectedTaskId is not null || Detail is not null)
        {
            _selectedTaskId = null;
            Detail = null;
            SyncSelection();
        }
    }

    public string QuickAddTitle => $"新建任务 · {QuickAddDate.Month}月{QuickAddDate.Day}日 {DateLabels.Weekday(QuickAddDate)}";
    public bool IsQuickAddHigh => QuickAddPriority == TaskPriority.High;
    public bool IsQuickAddMedium => QuickAddPriority == TaskPriority.Medium;
    public bool IsQuickAddLow => QuickAddPriority == TaskPriority.Low;
    public string QuickAddPriorityLabel => QuickAddPriority switch
    {
        TaskPriority.High => "高优先级",
        TaskPriority.Medium => "中优先级",
        TaskPriority.Low => "低优先级",
        _ => "无优先级",
    };

    public event EventHandler? ViewAnimatedRefresh;

    [RelayCommand]
    private void SwitchToMonth()
    {
        if (ViewKind == CalendarViewKind.Month)
        {
            return;
        }

        ViewKind = CalendarViewKind.Month;
    }

    [RelayCommand]
    private void SwitchToWeek()
    {
        if (ViewKind == CalendarViewKind.Week)
        {
            return;
        }

        ViewKind = CalendarViewKind.Week;
    }

    [RelayCommand]
    private void PreviousPeriod()
    {
        _anchorDate = ViewKind == CalendarViewKind.Month
            ? new DateOnly(_anchorDate.Year, _anchorDate.Month, 1).AddMonths(-1)
            : _anchorDate.AddDays(-7);
        Refresh();
        ViewAnimatedRefresh?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void NextPeriod()
    {
        _anchorDate = ViewKind == CalendarViewKind.Month
            ? new DateOnly(_anchorDate.Year, _anchorDate.Month, 1).AddMonths(1)
            : _anchorDate.AddDays(7);
        Refresh();
        ViewAnimatedRefresh?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void GoToday()
    {
        _anchorDate = _today;
        Refresh();
        ViewAnimatedRefresh?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleShowCompleted() => ShowCompleted = !ShowCompleted;

    [RelayCommand]
    private void CycleQuickAddPriority()
    {
        QuickAddPriority = QuickAddPriority switch
        {
            TaskPriority.None => TaskPriority.High,
            TaskPriority.High => TaskPriority.Medium,
            TaskPriority.Medium => TaskPriority.Low,
            _ => TaskPriority.None,
        };
    }

    public void OpenQuickAdd(DateOnly date)
    {
        IsOverflowOpen = false;
        _selectedTaskId = null;
        Detail = null;
        QuickAddDate = date;
        QuickAddPriority = TaskPriority.None;
        QuickAddText = string.Empty;
        QuickCreate.Reset(date);
        IsQuickAddOpen = true;
        SyncSelection();
    }

    public void OpenEditTask(TaskItem task)
    {
        IsOverflowOpen = false;
        _selectedTaskId = task.Id;
        QuickCreate.LoadTask(task, _repository);
        IsQuickAddOpen = true;
        SyncSelection();
    }

    public void DeleteEditingTask()
    {
        if (QuickCreate.EditingTask is { } task)
        {
            CloseOverlays();
            DeleteTaskWithUndo(task);
        }
    }

    public bool SubmitQuickAdd()
    {
        if (QuickCreate.IsEditMode)
        {
            QuickCreate.Submit(_repository);
            CloseOverlays();
            return true;
        }

        if (!string.IsNullOrWhiteSpace(QuickCreate.Title))
        {
            if (QuickCreate.Submit(_repository) is null)
            {
                return false;
            }

            IsQuickAddOpen = false;
            return true;
        }

        var trimmed = QuickAddText?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        _repository.Create(new TaskDraft
        {
            Title = trimmed,
            Date = QuickAddDate,
            Priority = QuickAddPriority,
            ListId = TaskList.InboxId,
        });

        QuickAddText = string.Empty;
        QuickAddPriority = TaskPriority.None;
        IsQuickAddOpen = false;
        return true;
    }

    public void OpenOverflow(DateOnly date)
    {
        IsQuickAddOpen = false;
        OverflowDate = date;
        OverflowTitle = $"{date.Month}月{date.Day}日 {DateLabels.Weekday(date)}";
        RefreshOverflow();
        IsOverflowOpen = true;
    }

    public void SelectTask(TaskItem task)
    {
        if (string.Equals(_selectedTaskId, task.Id, StringComparison.Ordinal) && Detail is not null)
        {
            return;
        }

        _selectedTaskId = task.Id;
        Detail = new MainTaskDetailViewModel(_repository, task, _today, ClearSelection, DeleteTaskWithUndo);
        SyncSelection();
    }

    public void ClearSelection()
    {
        _selectedTaskId = null;
        Detail = null;
        SyncSelection();
    }

    public void ToggleTask(TaskItem task) =>
        _repository.SetCompleted(task.Id, !task.IsCompleted);

    public void RescheduleTask(TaskItem task, DateOnly targetDate)
    {
        if (task.Date == targetDate)
        {
            return;
        }

        var oldDate = task.Date;
        task.Date = targetDate;
        _repository.Update(task);

        _toast.Show(
            $"已将「{task.Title}」改期到 {DateLabels.Describe(targetDate, _today)}",
            () =>
            {
                task.Date = oldDate;
                _repository.Update(task);
            });
    }

    public void UpdateMonthChipCapacity(int maxChips)
    {
        var clamped = Math.Clamp(maxChips, 1, 6);
        if (_maxChipsPerCell == clamped)
        {
            return;
        }

        _maxChipsPerCell = clamped;
        foreach (var cell in MonthCells)
        {
            cell.UpdateVisibleTasks(clamped);
        }

        SyncSelection();
    }

    public void SetDropTarget(DateOnly? targetDate)
    {
        foreach (var cell in MonthCells)
        {
            cell.IsDropTarget = targetDate is { } d && cell.Date == d;
        }

        foreach (var col in WeekColumns)
        {
            col.IsDropTarget = targetDate is { } d && col.Date == d;
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

    partial void OnViewKindChanged(CalendarViewKind value)
    {
        OnPropertyChanged(nameof(IsMonthView));
        OnPropertyChanged(nameof(IsWeekView));
        Refresh();
        ViewAnimatedRefresh?.Invoke(this, EventArgs.Empty);
    }

    partial void OnShowCompletedChanged(bool value)
    {
        _coordinator.SetCalendarShowCompleted(value);
        Refresh();
    }

    partial void OnDetailChanged(MainTaskDetailViewModel? value) =>
        OnPropertyChanged(nameof(HasSelection));

    partial void OnQuickAddDateChanged(DateOnly value) =>
        OnPropertyChanged(nameof(QuickAddTitle));

    partial void OnQuickAddPriorityChanged(TaskPriority value)
    {
        OnPropertyChanged(nameof(IsQuickAddHigh));
        OnPropertyChanged(nameof(IsQuickAddMedium));
        OnPropertyChanged(nameof(IsQuickAddLow));
        OnPropertyChanged(nameof(QuickAddPriorityLabel));
    }

    private void DeleteTaskWithUndo(TaskItem task)
    {
        var snapshot = task.Clone();

        if (string.Equals(_selectedTaskId, task.Id, StringComparison.Ordinal))
        {
            ClearSelection();
        }

        if (_repository is IScheduleService schedule)
        {
            var cs = schedule.Apply([new DeleteTaskOp(task.Id)], ChangeSource.User, $"删除「{snapshot.Title}」");
            _toast.Show($"已删除「{snapshot.Title}」", () => schedule.Undo(cs.Id));
            return;
        }

        _repository.Delete(task.Id);
        _toast.Show(
            $"已删除「{snapshot.Title}」",
            () =>
            {
                if (_repository is InMemoryTaskRepository memory)
                {
                    memory.Restore(snapshot);
                }
            });
    }

    private void OnRepositoryChanged(object? sender, TaskChangedEventArgs e) => UiDispatcher.PostIfNeeded(() =>
    {
        if (_disposed)
        {
            return;
        }

        Refresh();
        if (IsOverflowOpen)
        {
            RefreshOverflow();
        }

        if (Detail is not { } detail)
        {
            return;
        }

        var current = _repository.Query(new TaskQuery()).FirstOrDefault(t => t.Id == detail.Task.Id);
        if (current is null)
        {
            ClearSelection();
        }
        else
        {
            detail.SyncFromTask(current);
        }
    });

    private void Refresh()
    {
        if (ViewKind == CalendarViewKind.Month)
        {
            BuildMonth();
        }
        else
        {
            BuildWeek();
        }

        SyncSelection();
    }

    private void BuildMonth()
    {
        MonthCells.Clear();
        var first = new DateOnly(_anchorDate.Year, _anchorDate.Month, 1);
        var leadingDays = (int)first.DayOfWeek;
        var daysInMonth = DateTime.DaysInMonth(first.Year, first.Month);
        var rows = (leadingDays + daysInMonth + 6) / 7;
        var totalCells = rows * 7;
        WeekRows = rows;

        var start = first.AddDays(-leadingDays);
        var end = start.AddDays(totalCells - 1);

        PeriodTitle = $"{first.Year}年{first.Month}月";

        var byDate = QueryRangeGrouped(start, end);
        for (var i = 0; i < totalCells; i++)
        {
            var date = start.AddDays(i);
            byDate.TryGetValue(date, out var tasks);
            MonthCells.Add(new CalendarMonthCellViewModel(
                date,
                date.Month == first.Month && date.Year == first.Year,
                _today,
                _maxChipsPerCell,
                tasks ?? Array.Empty<TaskItem>()));
        }
    }

    private void BuildWeek()
    {
        WeekColumns.Clear();
        var start = _anchorDate.AddDays(-(int)_anchorDate.DayOfWeek);
        var end = start.AddDays(6);

        var weekNum = ISOWeek.GetWeekOfYear(start.AddDays(3).ToDateTime(TimeOnly.MinValue));
        PeriodTitle = $"{start.Year}年{start.Month}月 · 第{weekNum}周";

        var byDate = QueryRangeGrouped(start, end);
        for (var i = 0; i < 7; i++)
        {
            var date = start.AddDays(i);
            byDate.TryGetValue(date, out var tasks);
            WeekColumns.Add(new CalendarWeekDayViewModel(
                date,
                _today,
                tasks ?? Array.Empty<TaskItem>()));
        }
    }

    private Dictionary<DateOnly, IReadOnlyList<TaskItem>> QueryRangeGrouped(DateOnly start, DateOnly end) =>
        _repository
            .Query(new TaskQuery { From = start, To = end, IncludeCompleted = ShowCompleted })
            .GroupBy(t => t.Date)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<TaskItem>)g
                    .OrderBy(t => t.IsCompleted)
                    .ThenByDescending(t => t.Priority)
                    .ThenByDescending(t => t.CreatedAt)
                    .ToList());

    private void RefreshOverflow()
    {
        OverflowTasks.Clear();
        var items = _repository
            .Query(new TaskQuery { Date = OverflowDate, IncludeCompleted = ShowCompleted })
            .OrderBy(t => t.IsCompleted)
            .ThenByDescending(t => t.Priority)
            .ThenByDescending(t => t.CreatedAt);

        foreach (var item in items)
        {
            OverflowTasks.Add(new CalendarTaskItemViewModel(item));
        }
    }

    private void SyncSelection()
    {
        var allChips = MonthCells.SelectMany(c => c.VisibleTasks)
            .Concat(WeekColumns.SelectMany(w => w.Tasks))
            .Concat(OverflowTasks);

        foreach (var chip in allChips)
        {
            chip.IsSelected = _selectedTaskId is not null &&
                              string.Equals(chip.Id, _selectedTaskId, StringComparison.Ordinal);
        }
    }
}

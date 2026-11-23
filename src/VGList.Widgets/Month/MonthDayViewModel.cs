using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VGList.Core.Dates;
using VGList.Core.Models;
using VGList.Widgets.Support;

namespace VGList.Widgets.Month;

/// <summary>月历格内显示的一条任务芯片。</summary>
public sealed class MonthTaskChipViewModel
{
    public MonthTaskChipViewModel(TaskItem task)
    {
        Task = task;
        Title = task.Title;
        IsCompleted = task.IsCompleted;
        IsHigh = !task.IsCompleted && task.Priority == TaskPriority.High;
        IsMedium = !task.IsCompleted && task.Priority == TaskPriority.Medium;
        IsLow = !task.IsCompleted && task.Priority == TaskPriority.Low;
        IsNone = !task.IsCompleted && task.Priority == TaskPriority.None;

        var priorityLabel = task.Priority switch
        {
            TaskPriority.High => "高优先级",
            TaskPriority.Medium => "中优先级",
            TaskPriority.Low => "低优先级",
            _ => "无优先级",
        };
        TooltipText = task.IsCompleted
            ? $"{task.Title}（已完成）"
            : $"{task.Title} · {priorityLabel}";
    }

    public TaskItem Task { get; }

    public string Title { get; }

    public bool IsCompleted { get; }

    public bool IsHigh { get; }

    public bool IsMedium { get; }

    public bool IsLow { get; }

    public bool IsNone { get; }

    public int SubtasksTotal => Task.Subtasks.Count;

    public int SubtasksCompleted => Task.Subtasks.Count(s => s.IsCompleted);

    public bool HasSubtasks => SubtasksTotal > 0;

    public string TooltipText { get; }
}

/// <summary>当日日程浮层内的一行任务，支持直接勾选和点击查看详情。</summary>
public sealed class MonthAgendaTaskViewModel
{
    public MonthAgendaTaskViewModel(
        TaskItem task,
        Action<TaskItem> toggle,
        Action<TaskItem> openDetail,
        Action<TaskItem>? delete = null)
    {
        Task = task;
        Title = task.Title;
        IsCompleted = task.IsCompleted;
        IsHigh = task.Priority == TaskPriority.High;
        IsMedium = task.Priority == TaskPriority.Medium;
        IsLow = task.Priority == TaskPriority.Low;
        ListName = TaskListCatalog.NameOf(task.ListId);
        HasDescription = !string.IsNullOrWhiteSpace(task.Content);

        ToggleCommand = new RelayCommand(() => toggle(Task));
        OpenDetailCommand = new RelayCommand(() => openDetail(Task));
        DeleteCommand = new RelayCommand(() => delete?.Invoke(Task));
    }

    public TaskItem Task { get; }

    public string Title { get; }

    public bool IsCompleted { get; }

    public bool IsHigh { get; }

    public bool IsMedium { get; }

    public bool IsLow { get; }

    public string ListName { get; }

    public bool HasDescription { get; }

    public int SubtasksTotal => Task.Subtasks.Count;

    public int SubtasksCompleted => Task.Subtasks.Count(s => s.IsCompleted);

    public bool HasSubtasks => SubtasksTotal > 0;

    public IRelayCommand ToggleCommand { get; }

    public IRelayCommand OpenDetailCommand { get; }

    public IRelayCommand DeleteCommand { get; }
}

/// <summary>年月选择器中的一个月份格。</summary>
public sealed class MonthPickerItemViewModel
{
    public MonthPickerItemViewModel(
        int year,
        int month,
        bool isSelectedMonth,
        bool isCurrentMonth,
        Action<int, int> pick)
    {
        Year = year;
        Month = month;
        Label = $"{month}月";
        IsSelectedMonth = isSelectedMonth;
        IsCurrentMonth = isCurrentMonth;
        PickCommand = new RelayCommand(() => pick(Year, Month));
    }

    public int Year { get; }

    public int Month { get; }

    public string Label { get; }

    public bool IsSelectedMonth { get; }

    public bool IsCurrentMonth { get; }

    public IRelayCommand PickCommand { get; }
}

/// <summary>月历网格里的一格：日期、农历/节气/节日与自适应多条任务芯片。</summary>
public sealed partial class MonthDayViewModel : ObservableObject
{
    private readonly IReadOnlyList<TaskItem> _tasks;

    public MonthDayViewModel(
        DateOnly date,
        bool isCurrentMonth,
        DateOnly today,
        bool isSelected,
        bool showLunar,
        int maxChips,
        IReadOnlyList<TaskItem> tasks)
    {
        Date = date;
        IsCurrentMonth = isCurrentMonth;
        IsToday = date == today;
        IsWeekend = date.DayOfWeek is DayOfWeek.Sunday or DayOfWeek.Saturday;
        _isSelected = isSelected;
        _tasks = tasks;

        DayLabel = date.Day == 1 ? $"{date.Month}月1日" : date.Day.ToString();
        IsFirstDay = date.Day == 1;

        var label = LunarCalendar.Describe(date);
        LunarLabel = showLunar ? label.Text : string.Empty;
        IsFestival = showLunar && label.Kind == CalendarLabelKind.Festival;
        IsSolarTerm = showLunar && label.Kind == CalendarLabelKind.SolarTerm;

        UpdateVisibleChips(maxChips);
    }

    public DateOnly Date { get; }

    public bool IsCurrentMonth { get; }

    public bool IsOutside => !IsCurrentMonth;

    public bool IsToday { get; }

    public bool IsFirstDay { get; }

    public bool IsWeekend { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string DayLabel { get; }

    public string LunarLabel { get; }

    public bool IsFestival { get; }

    public bool IsSolarTerm { get; }

    public bool HasTasks => _tasks.Count > 0;

    public IReadOnlyList<MonthTaskChipViewModel> FullWidthChips { get; private set; } = [];

    public MonthTaskChipViewModel? BottomChip { get; private set; }

    public bool HasBottomChip => BottomChip is not null;

    public bool HasOverflow { get; private set; }

    public string OverflowLabel { get; private set; } = string.Empty;

    /// <summary>非当前月整块内容淡化，格线不受影响。</summary>
    public double ContentOpacity => IsCurrentMonth ? 1 : 0.45;

    public void UpdateVisibleChips(int maxChips)
    {
        var capacity = Math.Max(1, maxChips);
        if (_tasks.Count == 0)
        {
            FullWidthChips = [];
            BottomChip = null;
            HasOverflow = false;
            OverflowLabel = string.Empty;
        }
        else if (_tasks.Count <= capacity)
        {
            FullWidthChips = _tasks.Select(t => new MonthTaskChipViewModel(t)).ToList();
            BottomChip = null;
            HasOverflow = false;
            OverflowLabel = string.Empty;
        }
        else
        {
            FullWidthChips = _tasks.Take(capacity - 1).Select(t => new MonthTaskChipViewModel(t)).ToList();
            BottomChip = new MonthTaskChipViewModel(_tasks[capacity - 1]);
            HasOverflow = true;
            OverflowLabel = $"+{_tasks.Count - capacity}";
        }

        OnPropertyChanged(nameof(FullWidthChips));
        OnPropertyChanged(nameof(BottomChip));
        OnPropertyChanged(nameof(HasBottomChip));
        OnPropertyChanged(nameof(HasOverflow));
        OnPropertyChanged(nameof(OverflowLabel));
    }
}

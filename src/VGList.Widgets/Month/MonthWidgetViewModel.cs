using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VGList.Core.Abstractions;
using VGList.Core.Dates;
using VGList.Core.Models;
using VGList.Widgets.Support;
using VGList.Widgets.Today;

namespace VGList.Widgets.Month;

/// <summary>
/// 月历小组件的全部状态：42 个格子、自适应多行芯片、当日日程浮层、年月跳转、快速新建与仓储实时同步。
/// </summary>
public sealed partial class MonthWidgetViewModel : ObservableObject, IDisposable
{
    private readonly ITaskRepository _repository;
    private int _maxChipsPerCell = 2;
    private bool _disposed;

    public MonthWidgetViewModel(ITaskRepository repository, DateOnly? today = null)
    {
        _repository = repository;
        Today = today ?? DateOnly.FromDateTime(DateTime.Today);
        visibleMonth = new DateTime(Today.Year, Today.Month, 1);
        pickerYear = Today.Year;
        agendaDate = Today;
        quickAddTargetDate = Today;
        QuickCreate = new QuickCreateViewModel(Today);

        _repository.Changed += OnRepositoryChanged;
        BuildDays();
        BuildPickerMonths();
    }

    public DateOnly Today { get; }

    public QuickCreateViewModel QuickCreate { get; }

    public ObservableCollection<MonthDayViewModel> Days { get; } = [];

    public ObservableCollection<MonthAgendaTaskViewModel> AgendaTasks { get; } = [];

    public ObservableCollection<MonthPickerItemViewModel> PickerMonths { get; } = [];

    [ObservableProperty]
    private DateTime visibleMonth;

    [ObservableProperty]
    private int weekRows = 5;

    [ObservableProperty]
    private DateOnly? selectedDate;

    [ObservableProperty]
    private bool showCompleted = true;

    [ObservableProperty]
    private bool showLunar = true;

    [ObservableProperty]
    private int pickerYear;

    [ObservableProperty]
    private bool isMonthPickerOpen;

    [ObservableProperty]
    private bool isAgendaOpen;

    [ObservableProperty]
    private bool isAddOpen;

    [ObservableProperty]
    private bool isViewMenuOpen;

    [ObservableProperty]
    private bool isMoreMenuOpen;

    public bool HasAnyOverlay =>
        IsMonthPickerOpen || IsAgendaOpen || IsAddOpen || IsViewMenuOpen || IsMoreMenuOpen;

    [ObservableProperty]
    private DateOnly agendaDate;

    [ObservableProperty]
    private string agendaDraftText = string.Empty;

    [ObservableProperty]
    private TaskPriority agendaDraftPriority = TaskPriority.None;

    [ObservableProperty]
    private DateOnly quickAddTargetDate;

    [ObservableProperty]
    private TaskPriority quickAddPriority = TaskPriority.None;

    public string MonthTitle => $"{VisibleMonth.Year}年{VisibleMonth.Month}月";

    public bool IsCurrentMonth => VisibleMonth.Year == Today.Year && VisibleMonth.Month == Today.Month;

    public string AgendaTitle
    {
        get
        {
            var baseTitle = $"{AgendaDate.Month}月{AgendaDate.Day}日 {DateLabels.Weekday(AgendaDate)}";
            var rel = DateLabels.Describe(AgendaDate, Today);
            return rel is "今天" or "明天" or "昨天" ? $"{baseTitle} · {rel}" : baseTitle;
        }
    }

    public string AgendaLunar
    {
        get
        {
            var label = LunarCalendar.Describe(AgendaDate);
            return label.Text;
        }
    }

    public bool IsAgendaFestival =>
        LunarCalendar.Describe(AgendaDate).Kind is CalendarLabelKind.Festival or CalendarLabelKind.SolarTerm;

    public bool HasAgendaTasks => AgendaTasks.Count > 0;

    public bool IsAgendaEmpty => AgendaTasks.Count == 0;

    public string AgendaPlaceholder => AgendaDraftPriority switch
    {
        TaskPriority.High => $"添加到 {AgendaDate.Month}月{AgendaDate.Day}日（高优先级）",
        TaskPriority.Medium => $"添加到 {AgendaDate.Month}月{AgendaDate.Day}日（中优先级）",
        TaskPriority.Low => $"添加到 {AgendaDate.Month}月{AgendaDate.Day}日（低优先级）",
        _ => $"添加到 {AgendaDate.Month}月{AgendaDate.Day}日 (Tab 切优先级)",
    };

    public bool IsAgendaHigh => AgendaDraftPriority == TaskPriority.High;
    public bool IsAgendaMedium => AgendaDraftPriority == TaskPriority.Medium;
    public bool IsAgendaLow => AgendaDraftPriority == TaskPriority.Low;

    public string QuickAddDateLabel => $"添加到 {DateLabels.Describe(QuickAddTargetDate, Today)}";

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

    public event EventHandler<TaskItem>? DetailRequested;

    partial void OnVisibleMonthChanged(DateTime value)
    {
        OnPropertyChanged(nameof(MonthTitle));
        OnPropertyChanged(nameof(IsCurrentMonth));
        PickerYear = value.Year;
        BuildDays();
        BuildPickerMonths();
    }

    partial void OnSelectedDateChanged(DateOnly? value) => RefreshDaySelection();

    /// <summary>选中高亮只跟随当日日程浮层：浮层打开时高亮锚点日期，关闭后立即消失。</summary>
    private void RefreshDaySelection()
    {
        var selected = IsAgendaOpen ? SelectedDate : null;
        foreach (var day in Days)
        {
            day.IsSelected = selected is { } date && day.Date == date;
        }

        if (SelectedDate is { } target)
        {
            QuickAddTargetDate = target;
        }
    }

    partial void OnShowCompletedChanged(bool value)
    {
        BuildDays();
        if (IsAgendaOpen)
        {
            RefreshAgenda();
        }
    }

    partial void OnShowLunarChanged(bool value) => BuildDays();

    partial void OnPickerYearChanged(int value) => BuildPickerMonths();

    partial void OnIsMonthPickerOpenChanged(bool value) => OnPropertyChanged(nameof(HasAnyOverlay));

    partial void OnIsAgendaOpenChanged(bool value)
    {
        RefreshDaySelection();
        OnPropertyChanged(nameof(HasAnyOverlay));
    }

    partial void OnIsAddOpenChanged(bool value) => OnPropertyChanged(nameof(HasAnyOverlay));

    partial void OnIsViewMenuOpenChanged(bool value) => OnPropertyChanged(nameof(HasAnyOverlay));

    partial void OnIsMoreMenuOpenChanged(bool value) => OnPropertyChanged(nameof(HasAnyOverlay));

    public void CloseAllOverlays()
    {
        IsMonthPickerOpen = false;
        IsAgendaOpen = false;
        IsAddOpen = false;
        IsViewMenuOpen = false;
        IsMoreMenuOpen = false;
    }

    partial void OnAgendaDateChanged(DateOnly value)
    {
        OnPropertyChanged(nameof(AgendaTitle));
        OnPropertyChanged(nameof(AgendaLunar));
        OnPropertyChanged(nameof(IsAgendaFestival));
        OnPropertyChanged(nameof(AgendaPlaceholder));
        RefreshAgenda();
    }

    partial void OnAgendaDraftPriorityChanged(TaskPriority value)
    {
        OnPropertyChanged(nameof(AgendaPlaceholder));
        OnPropertyChanged(nameof(IsAgendaHigh));
        OnPropertyChanged(nameof(IsAgendaMedium));
        OnPropertyChanged(nameof(IsAgendaLow));
    }

    partial void OnQuickAddTargetDateChanged(DateOnly value) =>
        OnPropertyChanged(nameof(QuickAddDateLabel));

    partial void OnQuickAddPriorityChanged(TaskPriority value)
    {
        OnPropertyChanged(nameof(IsQuickAddHigh));
        OnPropertyChanged(nameof(IsQuickAddMedium));
        OnPropertyChanged(nameof(IsQuickAddLow));
        OnPropertyChanged(nameof(QuickAddPriorityLabel));
    }

    public void UpdateChipCapacity(int maxChips)
    {
        var clamped = Math.Clamp(maxChips, 1, 5);
        if (_maxChipsPerCell == clamped)
        {
            return;
        }

        _maxChipsPerCell = clamped;
        foreach (var day in Days)
        {
            day.UpdateVisibleChips(clamped);
        }
    }

    /// <summary>单元格固定纵向开销：底边格线 1 + 上下内边距 4+4 + 日号行 24 + 芯片上间距 2。</summary>
    private const double CellChromeHeight = 35;

    /// <summary>相邻芯片行的纵向步距：芯片高 16 + 行距 2。</summary>
    private const double ChipRowPitch = 18;

    private const double ChipHeight = 16;

    /// <summary>按格子高度换算可完整显示的芯片行数，保证最后一条不会被底边格线裁切。</summary>
    public static int ComputeChipCapacity(double cellHeight)
    {
        if (cellHeight <= CellChromeHeight)
        {
            return 1;
        }

        var chipArea = cellHeight - CellChromeHeight;
        var rows = 1 + (int)Math.Floor((chipArea - ChipHeight) / ChipRowPitch);
        return Math.Clamp(rows, 1, 5);
    }

    public void ShiftMonth(int delta) => VisibleMonth = VisibleMonth.AddMonths(delta);

    /// <summary>回到今天所在月份；已在本月时返回 false（不重播动画），同时选中今天。</summary>
    public bool GoToToday()
    {
        SelectedDate = Today;
        if (IsCurrentMonth)
        {
            return false;
        }

        VisibleMonth = new DateTime(Today.Year, Today.Month, 1);
        return true;
    }

    public void ShiftPickerYear(int delta) => PickerYear = Math.Clamp(PickerYear + delta, 1901, 2100);

    public bool JumpToMonth(int year, int month)
    {
        IsMonthPickerOpen = false;
        var target = new DateTime(year, month, 1);
        if (target == VisibleMonth)
        {
            return false;
        }

        VisibleMonth = target;
        return true;
    }

    public void OpenAgenda(DateOnly date)
    {
        SelectedDate = date;
        AgendaDate = date;
        QuickCreate.Reset(date);
        RefreshAgenda();
        IsAgendaOpen = true;
    }

    public bool SubmitQuickCreate() => QuickCreate.Submit(_repository) is not null;

    public void PrepareQuickAdd()
    {
        QuickAddTargetDate = SelectedDate ?? Today;
        QuickAddPriority = TaskPriority.None;
    }

    public void CycleQuickAddDate()
    {
        if (QuickAddTargetDate == Today)
        {
            QuickAddTargetDate = Today.AddDays(1);
        }
        else if (QuickAddTargetDate == Today.AddDays(1) && SelectedDate is { } sel && sel != Today && sel != Today.AddDays(1))
        {
            QuickAddTargetDate = sel;
        }
        else
        {
            QuickAddTargetDate = Today;
        }
    }

    public void CycleQuickAddPriority(bool reverse = false) =>
        QuickAddPriority = CyclePriorityValue(QuickAddPriority, reverse);

    public void CycleAgendaPriority(bool reverse = false) =>
        AgendaDraftPriority = CyclePriorityValue(AgendaDraftPriority, reverse);

    /// <summary>快速新建：使用指定日期与优先级，写入共享仓储。</summary>
    public bool CreateTask(string title, DateOnly? targetDate = null, TaskPriority? priority = null)
    {
        var trimmed = title?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        _repository.Create(new TaskDraft
        {
            Title = trimmed,
            Date = targetDate ?? QuickAddTargetDate,
            Priority = priority ?? QuickAddPriority,
            ListId = TaskList.InboxId,
        });

        QuickAddPriority = TaskPriority.None;
        return true;
    }

    public bool SubmitAgendaDraft()
    {
        var trimmed = AgendaDraftText?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        _repository.Create(new TaskDraft
        {
            Title = trimmed,
            Date = AgendaDate,
            Priority = AgendaDraftPriority,
            ListId = TaskList.InboxId,
        });

        AgendaDraftText = string.Empty;
        AgendaDraftPriority = TaskPriority.None;
        return true;
    }

    public TaskDetailViewModel CreateDetail(TaskItem task) => new(_repository, task, Today);

    private static TaskPriority CyclePriorityValue(TaskPriority current, bool reverse) => current switch
    {
        TaskPriority.None => reverse ? TaskPriority.Low : TaskPriority.High,
        TaskPriority.High => reverse ? TaskPriority.None : TaskPriority.Medium,
        TaskPriority.Medium => reverse ? TaskPriority.High : TaskPriority.Low,
        _ => reverse ? TaskPriority.Medium : TaskPriority.None,
    };

    private void ToggleTask(TaskItem task) => _repository.SetCompleted(task.Id, !task.IsCompleted);

    private void DeleteAgendaTask(TaskItem task) => _repository.Delete(task.Id);

    private void OnRepositoryChanged(object? sender, TaskChangedEventArgs e) => UiDispatcher.PostIfNeeded(() =>
    {
        if (_disposed)
        {
            return;
        }

        BuildDays();
        if (IsAgendaOpen)
        {
            RefreshAgenda();
        }
    });

    private void BuildDays()
    {
        Days.Clear();

        var first = new DateOnly(VisibleMonth.Year, VisibleMonth.Month, 1);
        var leadingDays = (int)first.DayOfWeek;
        var daysInMonth = DateTime.DaysInMonth(first.Year, first.Month);
        var rows = (leadingDays + daysInMonth + 6) / 7;
        var totalCells = rows * 7;
        WeekRows = rows;

        var start = first.AddDays(-leadingDays);
        var end = start.AddDays(totalCells - 1);

        var tasksByDate = _repository
            .Query(new TaskQuery { From = start, To = end, IncludeCompleted = ShowCompleted })
            .GroupBy(t => t.Date)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<TaskItem>)g
                    .OrderBy(t => t.IsCompleted)
                    .ThenByDescending(t => t.Priority)
                    .ThenByDescending(t => t.CreatedAt)
                    .ToList());

        for (var i = 0; i < totalCells; i++)
        {
            var date = start.AddDays(i);
            tasksByDate.TryGetValue(date, out var tasks);

            Days.Add(new MonthDayViewModel(
                date,
                date.Year == first.Year && date.Month == first.Month,
                Today,
                IsAgendaOpen && SelectedDate is { } sel && date == sel,
                ShowLunar,
                _maxChipsPerCell,
                tasks ?? Array.Empty<TaskItem>()));
        }
    }

    private void RefreshAgenda()
    {
        AgendaTasks.Clear();
        var items = _repository
            .Query(new TaskQuery { Date = AgendaDate, IncludeCompleted = true })
            .OrderBy(t => t.IsCompleted)
            .ThenByDescending(t => t.Priority)
            .ThenByDescending(t => t.CreatedAt);

        foreach (var item in items)
        {
            AgendaTasks.Add(new MonthAgendaTaskViewModel(
                item,
                ToggleTask,
                task => DetailRequested?.Invoke(this, task),
                DeleteAgendaTask));
        }

        OnPropertyChanged(nameof(HasAgendaTasks));
        OnPropertyChanged(nameof(IsAgendaEmpty));
    }

    private void BuildPickerMonths()
    {
        PickerMonths.Clear();
        for (var m = 1; m <= 12; m++)
        {
            var isSelected = PickerYear == VisibleMonth.Year && m == VisibleMonth.Month;
            var isCurrent = PickerYear == Today.Year && m == Today.Month;
            PickerMonths.Add(new MonthPickerItemViewModel(
                PickerYear,
                m,
                isSelected,
                isCurrent,
                (y, month) => JumpToMonth(y, month)));
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

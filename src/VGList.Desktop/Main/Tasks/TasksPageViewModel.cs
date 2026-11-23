using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VGList.App.Assistant;
using VGList.App.Services;
using VGList.Core.Abstractions;
using VGList.Core.Models;
using VGList.Desktop.Controls;
using VGList.Desktop.Services;
using VGList.Widgets.Support;

namespace VGList.Desktop.Main.Tasks;

/// <summary>标签筛选条中的单个标签胶囊项。</summary>
public sealed record TagFilterChipViewModel(string Tag, bool IsSelected);

/// <summary>任务页视图模型：智能视图、排序、标签与重复筛选、回收站、分组折叠、行内增强录入、顺延与详情联动。</summary>
public sealed partial class TasksPageViewModel : ObservableObject, IDisposable
{
    private readonly ITaskRepository _repository;
    private readonly IScheduleService? _schedule;
    private readonly ToastService _toast;
    private readonly DateOnly _today;
    private readonly DraftDateAutoFill _dateAutoFill;
    private readonly MorningBriefService? _briefService;
    private readonly Func<bool>? _shouldShowFallbackBrief;
    private readonly Action<MorningBriefInfo>? _onOpenMorningBrief;
    private readonly HashSet<string> _collapsedGroups = [];
    private string? _selectedTaskId;
    private bool _disposed;

    /// <summary>本次录入中用户是否按过 Tab 主动选过优先级；决定占位是提示语还是优先级名。</summary>
    private bool _priorityPicked;

    public TasksPageViewModel(
        ITaskRepository repository,
        ToastService toast,
        DateOnly today,
        MorningBriefService? briefService = null,
        Func<bool>? shouldShowFallbackBrief = null,
        Action<MorningBriefInfo>? onOpenMorningBrief = null)
    {
        _repository = repository;
        _schedule = repository as IScheduleService;
        _toast = toast;
        _today = today;
        _briefService = briefService;
        _shouldShowFallbackBrief = shouldShowFallbackBrief;
        _onOpenMorningBrief = onOpenMorningBrief;
        _draftDate = today;
        _dateAutoFill = new DraftDateAutoFill(today, () => AutoDetectDatePreference.IsEnabled);
        DraftDatePicker = new DatePickerViewModel(today, today, PickDraftDate);

        _repository.Changed += OnRepositoryChanged;
        _repository.ListsChanged += OnListsChanged;
        RebuildDraftListOptions();
        Refresh();
        RefreshMorningBrief();
    }

    [ObservableProperty]
    private MorningBriefInfo? _morningBrief;

    public bool ShowMorningBrief => MorningBrief is not null && Section == MainSection.Today;

    public string MorningBriefText => MorningBrief?.BannerText ?? string.Empty;

    partial void OnMorningBriefChanged(MorningBriefInfo? value)
    {
        OnPropertyChanged(nameof(ShowMorningBrief));
        OnPropertyChanged(nameof(MorningBriefText));
    }

    public void RefreshMorningBrief(DateOnly? currentDate = null)
    {
        if (_briefService is null || !(_shouldShowFallbackBrief?.Invoke() ?? false))
        {
            MorningBrief = null;
            return;
        }

        MorningBrief = _briefService.Evaluate(currentDate ?? DateOnly.FromDateTime(DateTime.Today), enabled: true);
    }

    public void OpenMorningBrief()
    {
        if (MorningBrief is not { } info)
        {
            return;
        }

        _briefService?.DismissToday(info.Date);
        MorningBrief = null;
        _onOpenMorningBrief?.Invoke(info);
    }

    public void DismissMorningBrief()
    {
        var date = MorningBrief?.Date ?? DateOnly.FromDateTime(DateTime.Today);
        _briefService?.DismissToday(date);
        MorningBrief = null;
    }

    public event EventHandler? ViewAnimatedRefresh;
    public event EventHandler? FocusAddRequested;

    public ObservableCollection<MainTaskGroupViewModel> Groups { get; } = [];
    public ObservableCollection<MainTaskRowViewModel> CompletedTasks { get; } = [];
    public ObservableCollection<MainTaskRowViewModel> TrashedTasks { get; } = [];
    public ObservableCollection<TagFilterChipViewModel> TagFilterChips { get; } = [];
    public DatePickerViewModel DraftDatePicker { get; }

    [ObservableProperty]
    private MainSection _section = MainSection.Today;

    [ObservableProperty]
    private TaskSortMode _sortMode = TaskSortMode.Default;

    [ObservableProperty]
    private string _draftText = string.Empty;

    [ObservableProperty]
    private TaskPriority _draftPriority = TaskPriority.None;

    [ObservableProperty]
    private DateOnly _draftDate;

    [ObservableProperty]
    private string _draftListId = TaskList.InboxId;

    [ObservableProperty]
    private bool _isDraftFocused;

    [ObservableProperty]
    private bool _isDraftCalendarOpen;

    [ObservableProperty]
    private bool _isDraftListMenuOpen;

    [ObservableProperty]
    private bool _isSortMenuOpen;

    [ObservableProperty]
    private int _todayCount;

    [ObservableProperty]
    private int _next7DaysCount;

    [ObservableProperty]
    private int _inboxCount;

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private int _totalCompletedCount;

    [ObservableProperty]
    private int _trashCount;

    [ObservableProperty]
    private bool _isConfirmingEmptyTrash;

    [ObservableProperty]
    private string? _selectedTagFilter;

    [ObservableProperty]
    private bool _filterRecurringOnly;

    [ObservableProperty]
    private int _unfinishedInViewCount;

    [ObservableProperty]
    private bool _isCompletedExpanded;

    [ObservableProperty]
    private bool _showCompletedSection;

    [ObservableProperty]
    private bool _isListEmpty;

    [ObservableProperty]
    private MainTaskDetailViewModel? _detail;

    public bool HasSelection => Detail is not null;

    public bool HasAnyOverlay => IsSortMenuOpen || IsDraftCalendarOpen || IsDraftListMenuOpen;

    partial void OnIsSortMenuOpenChanged(bool value) => OnPropertyChanged(nameof(HasAnyOverlay));

    partial void OnIsDraftCalendarOpenChanged(bool value) => OnPropertyChanged(nameof(HasAnyOverlay));

    partial void OnIsDraftListMenuOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(HasAnyOverlay));
        OnPropertyChanged(nameof(ShowAddOptions));
    }

    public void CloseOverlays()
    {
        IsSortMenuOpen = false;
        IsDraftCalendarOpen = false;
        IsDraftListMenuOpen = false;
    }

    public bool IsInputVisible => Section != MainSection.Completed && Section != MainSection.Trash;

    public bool IsTrashSelected => Section == MainSection.Trash;

    public bool IsNormalListVisible => Section != MainSection.Trash;

    public bool HasTrashedTasks => TrashedTasks.Count > 0;

    public bool ShowEmptyTrashButton => HasTrashedTasks && !IsConfirmingEmptyTrash;

    partial void OnIsConfirmingEmptyTrashChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyTrashButton));

    public bool HasActiveFilter => !string.IsNullOrEmpty(SelectedTagFilter) || FilterRecurringOnly;

    public bool IsAllFilterSelected => !HasActiveFilter;

    public bool HasTagFilterBar => Section != MainSection.Trash && (TagFilterChips.Count > 0 || HasActiveFilter);

    /// <summary>录入框右侧的日期 / 优先级选项只在编辑态显示。</summary>
    public bool ShowAddOptions => IsDraftFocused || IsDraftCalendarOpen || IsDraftListMenuOpen;

    public string DraftDateLabel => DateLabels.Describe(DraftDate, _today);
    public string DraftListName =>
        _repository.Lists.FirstOrDefault(l => string.Equals(l.Id, DraftListId, StringComparison.Ordinal))?.Name
        ?? TaskListCatalog.NameOf(DraftListId);
    public bool IsNonInboxDraftList => !string.Equals(DraftListId, TaskList.InboxId, StringComparison.Ordinal);

    /// <summary>待创建任务的目标清单下拉项。</summary>
    public ObservableCollection<ListPickItem> DraftListOptions { get; } = [];

    private string? _cachedDotColor;
    private IBrush? _cachedDotBrush;

    public IBrush DraftListDotBrush
    {
        get
        {
            var color = _repository.Lists.FirstOrDefault(l => l.Id == DraftListId)?.Color;
            var resolved = string.IsNullOrEmpty(color) ? "#E46B92" : color;
            if (_cachedDotBrush is not null && string.Equals(_cachedDotColor, resolved, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedDotBrush;
            }

            _cachedDotColor = resolved;
            _cachedDotBrush = new SolidColorBrush(Color.Parse(resolved));
            return _cachedDotBrush;
        }
    }

    /// <summary>当前选中的自建清单；仅在 Section == List 时生效。</summary>
    public string CurrentListId { get; private set; } = TaskList.WelcomeId;

    public bool IsDraftHigh => DraftPriority == TaskPriority.High;
    public bool IsDraftMedium => DraftPriority == TaskPriority.Medium;
    public bool IsDraftLow => DraftPriority == TaskPriority.Low;
    public bool IsDraftNone => DraftPriority == TaskPriority.None;

    /// <summary>占位当前显示的是提示语（而非优先级名），此时用中性色。</summary>
    public bool IsDraftPriorityHint => IsDraftFocused && !_priorityPicked;

    /// <summary>录入框占位文案：未聚焦提示添加任务；聚焦后先提示按 Tab 切换，按过 Tab 才报出当前优先级。</summary>
    public string DraftPlaceholder
    {
        get
        {
            if (!IsDraftFocused || !string.IsNullOrEmpty(DraftText))
            {
                return "添加任务…";
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

    public string DraftPriorityLabel => DraftPriority switch
    {
        TaskPriority.High => "高",
        TaskPriority.Medium => "中",
        TaskPriority.Low => "低",
        _ => "优先级",
    };

    public string SortModeLabel => SortMode switch
    {
        TaskSortMode.Priority => "按优先级",
        TaskSortMode.Date => "按日期",
        _ => "默认排序",
    };

    public bool IsSortDefault => SortMode == TaskSortMode.Default;
    public bool IsSortPriority => SortMode == TaskSortMode.Priority;
    public bool IsSortDate => SortMode == TaskSortMode.Date;

    public string HeaderTitle => Section switch
    {
        MainSection.Next7Days => "最近7天",
        MainSection.Inbox => "收集箱",
        MainSection.List => TaskListCatalog.NameOf(CurrentListId),
        MainSection.Completed => "已完成",
        MainSection.Trash => "回收站",
        _ => "今天",
    };

    public string EmptyTitle
    {
        get
        {
            if (Section == MainSection.Trash)
            {
                return "回收站是空的";
            }

            if (HasActiveFilter)
            {
                return "没有符合筛选条件的任务";
            }

            return Section switch
            {
                MainSection.Next7Days => "最近 7 天没有待办",
                MainSection.Inbox => "收集箱已清空",
                MainSection.List => $"「{TaskListCatalog.NameOf(CurrentListId)}」清单暂无待办",
                MainSection.Completed => "还没有已完成的任务",
                _ => "今日待办已全部完成",
            };
        }
    }

    public string EmptySubtitle
    {
        get
        {
            if (Section == MainSection.Trash)
            {
                return "删除的任务会在此保留 30 天，随时可以恢复或彻底清除";
            }

            if (HasActiveFilter)
            {
                return "点击筛选栏「全部」即可恢复完整列表";
            }

            return Section == MainSection.Completed
                ? "勾选完成的任务会自动归档到这里"
                : "享受专注与留白，或者按 Ctrl+N 记录一条新想法（支持 #标签）";
        }
    }

    public bool IsCompletedToggleEnabled => Section != MainSection.Completed && Section != MainSection.Trash;
    public bool IsTodaySelected => Section == MainSection.Today;
    public bool IsNext7DaysSelected => Section == MainSection.Next7Days;
    public bool IsInboxSelected => Section == MainSection.Inbox;
    public bool IsCompletedSelected => Section == MainSection.Completed;

    public void ToggleTagFilter(string tag)
    {
        var normalized = TaskTagParser.CleanSingle(tag);
        if (normalized is null)
        {
            return;
        }

        SelectedTagFilter = string.Equals(SelectedTagFilter, normalized, StringComparison.OrdinalIgnoreCase)
            ? null
            : normalized;
    }

    public void ToggleRecurringFilter() => FilterRecurringOnly = !FilterRecurringOnly;

    public void ClearFilters()
    {
        SelectedTagFilter = null;
        FilterRecurringOnly = false;
    }

    public void RestoreTrashedTask(MainTaskRowViewModel row)
    {
        _repository.RestoreFromTrash(row.Id);
        _toast.Show($"已恢复「{row.Title}」");
    }

    public void PermanentlyDeleteTrashedTask(MainTaskRowViewModel row)
    {
        _repository.PermanentlyDelete(row.Id);
        _toast.Show($"已彻底删除「{row.Title}」");
    }

    public void BeginEmptyTrash() => IsConfirmingEmptyTrash = true;

    public void CancelEmptyTrash() => IsConfirmingEmptyTrash = false;

    public void ConfirmEmptyTrash()
    {
        IsConfirmingEmptyTrash = false;
        var count = _repository.EmptyTrash();
        if (count > 0)
        {
            _toast.Show($"已清空回收站（{count} 项）");
        }
    }

    [RelayCommand]
    private void FocusAdd() => FocusAddRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void CycleDraftPriority()
    {
        // 与每日小组件一致：本次录入的第一次 Tab 直接从「高」开始，之后按 高→中→低→无→高 循环。
        if (!_priorityPicked)
        {
            _priorityPicked = true;
            DraftPriority = TaskPriority.High;
            return;
        }

        DraftPriority = DraftPriority switch
        {
            TaskPriority.None => TaskPriority.High,
            TaskPriority.High => TaskPriority.Medium,
            TaskPriority.Medium => TaskPriority.Low,
            _ => TaskPriority.None,
        };
    }

    [RelayCommand]
    private void ToggleDraftListMenu() => IsDraftListMenuOpen = !IsDraftListMenuOpen;

    /// <summary>从清单下拉中选定目标清单。</summary>
    public void PickDraftList(string listId)
    {
        DraftListId = listId;
        IsDraftListMenuOpen = false;
    }

    private void RebuildDraftListOptions()
    {
        DraftListOptions.Clear();
        foreach (var list in _repository.Lists)
        {
            DraftListOptions.Add(new ListPickItem(list.Id, list.Name, list.Color, list.Id == DraftListId));
        }
    }

    [RelayCommand]
    private void ToggleDraftCalendar()
    {
        if (!IsDraftCalendarOpen)
        {
            DraftDatePicker.SyncSelectedDate(DraftDate);
        }

        IsDraftCalendarOpen = !IsDraftCalendarOpen;
    }

    [RelayCommand]
    private void SetSortMode(TaskSortMode mode)
    {
        SortMode = mode;
        IsSortMenuOpen = false;
    }

    public void SelectSection(MainSection section)
    {
        if (Section == section)
        {
            return;
        }

        Section = section;
    }

    public void SubmitDraft()
    {
        var raw = DraftText?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return;
        }

        var (tagClean, inlineTags) = TaskTagParser.ExtractInlineTags(raw);
        var (title, resolvedDate) = _dateAutoFill.ResolveForSubmit(tagClean, DraftDate, _today);
        _repository.Create(new TaskDraft
        {
            Title = title,
            Date = resolvedDate,
            Priority = DraftPriority,
            ListId = DraftListId,
            Tags = inlineTags,
        });

        var resetDate = _dateAutoFill.UserPickedDate ? DraftDate : _today;
        _dateAutoFill.Reset(_today);
        DraftDate = resetDate;
        DraftText = string.Empty;
        // 提交后仍处于录入态则继续从低优先级开始（只体现在边框上），否则还原为无优先级。
        DraftPriority = IsDraftFocused ? TaskPriority.Low : TaskPriority.None;
        _priorityPicked = false;
    }

    public void SelectTask(MainTaskRowViewModel row)
    {
        if (string.Equals(_selectedTaskId, row.Id, StringComparison.Ordinal) && Detail is not null)
        {
            return;
        }

        _selectedTaskId = row.Id;
        Detail = new MainTaskDetailViewModel(_repository, row.Task, _today, ClearSelection, DeleteTaskWithUndo);
        SyncSelection();
    }

    public void ClearSelection()
    {
        _selectedTaskId = null;
        Detail = null;
        SyncSelection();
    }

    public void ToggleTask(MainTaskRowViewModel row) =>
        _repository.SetCompleted(row.Id, !row.IsCompleted);

    public void QuickReschedule(MainTaskRowViewModel row)
    {
        var oldDate = row.Task.Date;
        var target = oldDate == _today ? _today.AddDays(1) : _today;
        row.Task.Date = target;
        _repository.Update(row.Task);

        _toast.Show(
            $"已改期到 {DateLabels.Describe(target, _today)}",
            () =>
            {
                row.Task.Date = oldDate;
                _repository.Update(row.Task);
            });
    }

    public void QuickCyclePriority(MainTaskRowViewModel row)
    {
        row.Task.Priority = row.Task.Priority switch
        {
            TaskPriority.None => TaskPriority.Low,
            TaskPriority.Low => TaskPriority.Medium,
            TaskPriority.Medium => TaskPriority.High,
            _ => TaskPriority.None,
        };
        _repository.Update(row.Task);
    }

    public void DeleteTaskWithUndo(TaskItem task)
    {
        var snapshot = task.Clone();

        if (string.Equals(_selectedTaskId, task.Id, StringComparison.Ordinal))
        {
            ClearSelection();
        }

        if (_schedule is not null)
        {
            var cs = _schedule.Apply([new DeleteTaskOp(task.Id)], ChangeSource.User, $"删除「{snapshot.Title}」");
            _toast.Show($"已移至回收站「{snapshot.Title}」", () => _schedule.Undo(cs.Id));
            return;
        }

        _repository.Delete(task.Id);
        _toast.Show(
            $"已移至回收站「{snapshot.Title}」",
            () => _repository.RestoreFromTrash(snapshot.Id));
    }

    public void ToggleGroup(MainTaskGroupViewModel group)
    {
        group.IsExpanded = !group.IsExpanded;
        if (group.IsExpanded)
        {
            _collapsedGroups.Remove(group.Key);
        }
        else
        {
            _collapsedGroups.Add(group.Key);
        }
    }

    public void PostponeOverdue()
    {
        var overdueTasks = Groups
            .Where(g => g.IsOverdueGroup)
            .SelectMany(g => g.Tasks)
            .Select(r => (Task: r.Task, OriginalDate: r.Task.Date))
            .ToList();

        if (overdueTasks.Count == 0)
        {
            return;
        }

        if (_schedule is not null)
        {
            var ops = overdueTasks
                .Select(item => (ScheduleOp)new UpdateTaskOp(item.Task.Id, new TaskPatch { Date = _today }))
                .ToList();
            var cs = _schedule.Apply(ops, ChangeSource.User, $"顺延 {overdueTasks.Count} 条逾期任务到今天");
            _toast.Show($"已将 {overdueTasks.Count} 条逾期任务顺延到今天", () => _schedule.Undo(cs.Id));
            return;
        }

        foreach (var item in overdueTasks)
        {
            item.Task.Date = _today;
            _repository.Update(item.Task);
        }

        _toast.Show(
            $"已将 {overdueTasks.Count} 条逾期任务顺延到今天",
            () =>
            {
                foreach (var item in overdueTasks)
                {
                    item.Task.Date = item.OriginalDate;
                    _repository.Update(item.Task);
                }
            });
    }

    public void ToggleCompletedGroup()
    {
        if (!IsCompletedToggleEnabled)
        {
            return;
        }

        IsCompletedExpanded = !IsCompletedExpanded;
    }

    public void RefreshAll() => Refresh();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _repository.Changed -= OnRepositoryChanged;
        _repository.ListsChanged -= OnListsChanged;
    }

    /// <summary>选中任意自建清单（由侧栏或外部导航调用）。</summary>
    public void SelectList(string listId)
    {
        CurrentListId = listId;
        OnPropertyChanged(nameof(CurrentListId));
        DraftListId = listId;

        if (Section == MainSection.List)
        {
            Refresh();
            NotifyListState();
        }
        else
        {
            Section = MainSection.List;
        }
    }

    public void RefreshAfterListsChanged() => OnListsChanged(this, EventArgs.Empty);

    private void OnListsChanged(object? sender, EventArgs e) => UiDispatcher.PostIfNeeded(() =>
    {
        if (_disposed)
        {
            return;
        }

        if (Section == MainSection.List && _repository.Lists.All(l => l.Id != CurrentListId))
        {
            SelectSection(MainSection.Inbox);
            return;
        }

        RebuildDraftListOptions();
        OnPropertyChanged(nameof(DraftListDotBrush));
        NotifyListState();
        Refresh();
    });

    private void NotifyListState()
    {
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(ShowMorningBrief));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(DraftListName));
        OnPropertyChanged(nameof(IsInboxSelected));
        OnPropertyChanged(nameof(IsCompletedSelected));
    }

    partial void OnSectionChanged(MainSection value)
    {
        ClearSelection();
        IsConfirmingEmptyTrash = false;
        IsCompletedExpanded = value == MainSection.Completed;
        DraftListId = value == MainSection.List ? CurrentListId : TaskList.InboxId;
        _dateAutoFill.Reset(_today);
        DraftDate = _today;
        Refresh();

        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(ShowMorningBrief));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptySubtitle));
        OnPropertyChanged(nameof(IsInputVisible));
        OnPropertyChanged(nameof(IsCompletedToggleEnabled));
        OnPropertyChanged(nameof(IsTodaySelected));
        OnPropertyChanged(nameof(IsNext7DaysSelected));
        OnPropertyChanged(nameof(IsInboxSelected));
        OnPropertyChanged(nameof(IsCompletedSelected));
        OnPropertyChanged(nameof(IsTrashSelected));
        OnPropertyChanged(nameof(IsNormalListVisible));
        OnPropertyChanged(nameof(HasTagFilterBar));

        ViewAnimatedRefresh?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedTagFilterChanged(string? value)
    {
        OnPropertyChanged(nameof(HasActiveFilter));
        OnPropertyChanged(nameof(IsAllFilterSelected));
        OnPropertyChanged(nameof(HasTagFilterBar));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptySubtitle));
        Refresh();
    }

    partial void OnFilterRecurringOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(HasActiveFilter));
        OnPropertyChanged(nameof(IsAllFilterSelected));
        OnPropertyChanged(nameof(HasTagFilterBar));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptySubtitle));
        Refresh();
    }

    partial void OnSortModeChanged(TaskSortMode value)
    {
        OnPropertyChanged(nameof(SortModeLabel));
        OnPropertyChanged(nameof(IsSortDefault));
        OnPropertyChanged(nameof(IsSortPriority));
        OnPropertyChanged(nameof(IsSortDate));
        Refresh();
    }

    partial void OnDraftPriorityChanged(TaskPriority value)
    {
        OnPropertyChanged(nameof(IsDraftHigh));
        OnPropertyChanged(nameof(IsDraftMedium));
        OnPropertyChanged(nameof(IsDraftLow));
        OnPropertyChanged(nameof(IsDraftNone));
        OnPropertyChanged(nameof(DraftPriorityLabel));
        OnPropertyChanged(nameof(DraftPlaceholder));
    }

    partial void OnDraftTextChanged(string value)
    {
        OnPropertyChanged(nameof(DraftPlaceholder));
        if (_dateAutoFill.Evaluate(value, _today) is { } detected && detected != DraftDate)
        {
            DraftDate = detected;
        }
    }

    partial void OnDraftDateChanged(DateOnly value) => OnPropertyChanged(nameof(DraftDateLabel));

    partial void OnDraftListIdChanged(string value)
    {
        OnPropertyChanged(nameof(DraftListName));
        OnPropertyChanged(nameof(IsNonInboxDraftList));
        OnPropertyChanged(nameof(DraftListDotBrush));
        RebuildDraftListOptions();
    }

    partial void OnIsDraftFocusedChanged(bool value)
    {
        // 每次进出录入态都重新开始：占位先提示按 Tab，而不是直接报出默认优先级。
        _priorityPicked = false;

        if (!value)
        {
            // 失焦即还原：不再保留草稿优先级，下次聚焦重新从低优先级开始。
            DraftPriority = TaskPriority.None;
        }
        else if (DraftPriority == TaskPriority.None)
        {
            // 聚焦即默认低优先级（只体现在边框与占位上）。
            DraftPriority = TaskPriority.Low;
        }

        OnPropertyChanged(nameof(ShowAddOptions));
        OnPropertyChanged(nameof(DraftPlaceholder));
        OnPropertyChanged(nameof(IsDraftPriorityHint));
    }

    partial void OnDetailChanged(MainTaskDetailViewModel? value) =>
        OnPropertyChanged(nameof(HasSelection));

    private void PickDraftDate(DateOnly date)
    {
        _dateAutoFill.MarkUserPickedDate();
        DraftDate = date;
        IsDraftCalendarOpen = false;
    }

    private void OnRepositoryChanged(object? sender, TaskChangedEventArgs e) => UiDispatcher.PostIfNeeded(() =>
    {
        if (_disposed)
        {
            return;
        }

        Refresh();
        RefreshMorningBrief();

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
        var all = _repository.Query(new TaskQuery());
        var trashed = _repository.Query(new TaskQuery { DeletedOnly = true });
        UpdateCounts(all, trashed);
        RebuildTagFilterChips();

        Groups.Clear();
        CompletedTasks.Clear();
        TrashedTasks.Clear();

        if (Section == MainSection.Trash)
        {
            foreach (var task in trashed)
            {
                TrashedTasks.Add(new MainTaskRowViewModel(task, _today));
            }

            UnfinishedInViewCount = TrashedTasks.Count;
            ShowCompletedSection = false;
            IsListEmpty = TrashedTasks.Count == 0;
            OnPropertyChanged(nameof(HasTrashedTasks));
            OnPropertyChanged(nameof(ShowEmptyTrashButton));
            OnPropertyChanged(nameof(HasTagFilterBar));
            return;
        }

        OnPropertyChanged(nameof(HasTrashedTasks));
        OnPropertyChanged(nameof(ShowEmptyTrashButton));

        if (Section == MainSection.Completed)
        {
            var filteredCompleted = ApplyFilters(all.Where(t => t.IsCompleted)).ToList();
            AddCompletedDateGroups(filteredCompleted);
            UnfinishedInViewCount = filteredCompleted.Count;
            ShowCompletedSection = false;
            IsListEmpty = Groups.Count == 0;
            SyncSelection();
            return;
        }

        var source = ApplyFilters(QueryForSection()).ToList();
        var pending = source.Where(t => !t.IsCompleted).ToList();
        UnfinishedInViewCount = pending.Count;

        if (SortMode == TaskSortMode.Priority)
        {
            AddPriorityGroups(pending);
        }
        else
        {
            AddDateGroups(pending);
        }

        var completed = Section switch
        {
            MainSection.Today => source.Where(t => t.IsCompleted && t.Date == _today),
            MainSection.Next7Days => source.Where(t =>
                t.IsCompleted && t.Date >= _today && t.Date <= _today.AddDays(6)),
            _ => source.Where(t => t.IsCompleted),
        };
        AddCompleted(completed);

        ShowCompletedSection = CompletedTasks.Count > 0;
        IsListEmpty = Groups.Count == 0 && CompletedTasks.Count == 0;
        SyncSelection();
    }

    private IEnumerable<TaskItem> ApplyFilters(IEnumerable<TaskItem> source)
    {
        var result = source;
        if (!string.IsNullOrWhiteSpace(SelectedTagFilter))
        {
            var targetTag = SelectedTagFilter.Trim();
            result = result.Where(t => t.Tags.Any(tag => string.Equals(tag, targetTag, StringComparison.OrdinalIgnoreCase)));
        }

        if (FilterRecurringOnly)
        {
            result = result.Where(t => t.Recurrence != RecurrenceKind.None);
        }

        return result;
    }

    private void RebuildTagFilterChips()
    {
        var tags = _repository.GetAllTags();
        TagFilterChips.Clear();
        foreach (var tag in tags)
        {
            var isSelected = !string.IsNullOrEmpty(SelectedTagFilter) &&
                             string.Equals(SelectedTagFilter, tag, StringComparison.OrdinalIgnoreCase);
            TagFilterChips.Add(new TagFilterChipViewModel(tag, isSelected));
        }

        OnPropertyChanged(nameof(HasTagFilterBar));
    }

    private void AddDateGroups(List<TaskItem> pending)
    {
        var overdue = pending
            .Where(t => t.Date < _today)
            .OrderBy(t => t.Date)
            .ThenByDescending(t => t.Priority)
            .ThenByDescending(t => t.CreatedAt)
            .ToList();
        if (overdue.Count > 0)
        {
            const string key = "overdue";
            Groups.Add(new MainTaskGroupViewModel(
                key,
                "已过期",
                BuildRows(overdue),
                isOverdueGroup: true,
                isExpanded: !_collapsedGroups.Contains(key)));
        }

        var future = pending.Where(t => t.Date >= _today);
        var ordered = SortMode == TaskSortMode.Date
            ? future.OrderBy(t => t.Date).ThenByDescending(t => t.CreatedAt)
            : future.OrderBy(t => t.Date).ThenByDescending(t => t.Priority).ThenByDescending(t => t.CreatedAt);

        foreach (var group in ordered.GroupBy(t => t.Date))
        {
            var key = group.Key.ToString("yyyy-MM-dd");
            Groups.Add(new MainTaskGroupViewModel(
                key,
                DateLabels.DescribeHeader(group.Key, _today),
                BuildRows(group),
                isExpanded: !_collapsedGroups.Contains(key)));
        }
    }

    /// <summary>「已完成」视图按完成日期（缺失时回退任务日期）降序分组：今天 → 昨天 → 前天 → 更早日期。</summary>
    private void AddCompletedDateGroups(List<TaskItem> completed)
    {
        var grouped = completed
            .GroupBy(t => t.CompletedAt is { } doneAt
                ? DateOnly.FromDateTime(doneAt.LocalDateTime)
                : t.Date)
            .OrderByDescending(g => g.Key);

        foreach (var group in grouped)
        {
            var key = $"completed-{group.Key:yyyy-MM-dd}";
            var ordered = group
                .OrderByDescending(t => t.CompletedAt ?? t.CreatedAt)
                .ToList();
            Groups.Add(new MainTaskGroupViewModel(
                key,
                DateLabels.DescribeHeader(group.Key, _today),
                BuildRows(ordered),
                isExpanded: !_collapsedGroups.Contains(key)));
        }
    }

    private void AddPriorityGroups(List<TaskItem> pending)
    {
        var tiers = new (TaskPriority Priority, string Key, string Title)[]
        {
            (TaskPriority.High, "prio-high", "高优先级"),
            (TaskPriority.Medium, "prio-med", "中优先级"),
            (TaskPriority.Low, "prio-low", "低优先级"),
            (TaskPriority.None, "prio-none", "无优先级"),
        };

        foreach (var (priority, key, title) in tiers)
        {
            var items = pending
                .Where(t => t.Priority == priority)
                .OrderBy(t => t.Date)
                .ThenByDescending(t => t.CreatedAt)
                .ToList();
            if (items.Count > 0)
            {
                Groups.Add(new MainTaskGroupViewModel(
                    key,
                    title,
                    BuildRows(items),
                    isExpanded: !_collapsedGroups.Contains(key)));
            }
        }
    }

    private IReadOnlyList<TaskItem> QueryForSection() => _repository.Query(Section switch
    {
        MainSection.Today => new TaskQuery { To = _today },
        MainSection.Next7Days => new TaskQuery { To = _today.AddDays(6) },
        MainSection.Inbox => new TaskQuery { ListId = TaskList.InboxId },
        MainSection.List => new TaskQuery { ListId = CurrentListId },
        _ => new TaskQuery(),
    });

    private List<MainTaskRowViewModel> BuildRows(IEnumerable<TaskItem> tasks) =>
        tasks.Select(task => new MainTaskRowViewModel(task, _today)).ToList();

    private void AddCompleted(IEnumerable<TaskItem> tasks)
    {
        foreach (var task in tasks.OrderByDescending(t => t.CompletedAt ?? t.CreatedAt))
        {
            CompletedTasks.Add(new MainTaskRowViewModel(task, _today));
        }

        CompletedCount = CompletedTasks.Count;
    }

    private void UpdateCounts(IReadOnlyList<TaskItem> all, IReadOnlyList<TaskItem> trashed)
    {
        var unfinished = all.Where(t => !t.IsCompleted).ToList();
        TodayCount = unfinished.Count(t => t.Date <= _today);
        Next7DaysCount = unfinished.Count(t => t.Date <= _today.AddDays(6));
        InboxCount = unfinished.Count(t => t.ListId == TaskList.InboxId);
        TotalCompletedCount = all.Count(t => t.IsCompleted);
        TrashCount = trashed.Count;
    }

    private void SyncSelection()
    {
        foreach (var row in Groups.SelectMany(g => g.Tasks).Concat(CompletedTasks))
        {
            row.IsSelected = _selectedTaskId is not null &&
                             string.Equals(row.Id, _selectedTaskId, StringComparison.Ordinal);
        }
    }
}

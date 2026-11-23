using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VGList.App.Services;
using VGList.Core.Abstractions;
using VGList.Core.Models;
using VGList.Desktop.Main.Tasks;
using VGList.Desktop.Services;
using VGList.Widgets.Support;

namespace VGList.Desktop.Main.Eisenhower;

/// <summary>单个象限卡片模型，严格按 TaskPriority 一对一映射。</summary>
public sealed partial class QuadrantViewModel : ObservableObject
{
    private readonly Action<TaskPriority, string> _createTask;

    public QuadrantViewModel(
        TaskPriority priority,
        string romanNumeral,
        string title,
        string subtitle,
        Action<TaskPriority, string> createTask)
    {
        Priority = priority;
        RomanNumeral = romanNumeral;
        Title = title;
        Subtitle = subtitle;
        _createTask = createTask;

        ToggleAddingCommand = new RelayCommand(() => IsAdding = !IsAdding);
        ToggleCompletedCommand = new RelayCommand(() => IsCompletedExpanded = !IsCompletedExpanded);
    }

    public TaskPriority Priority { get; }
    public string RomanNumeral { get; }
    public string Title { get; }
    public string Subtitle { get; }

    public bool IsHigh => Priority == TaskPriority.High;
    public bool IsMedium => Priority == TaskPriority.Medium;
    public bool IsLow => Priority == TaskPriority.Low;
    public bool IsNone => Priority == TaskPriority.None;

    public ObservableCollection<MainTaskRowViewModel> PendingTasks { get; } = [];
    public ObservableCollection<MainTaskRowViewModel> CompletedTasks { get; } = [];

    [ObservableProperty]
    private int _pendingCount;

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private bool _hasCompleted;

    [ObservableProperty]
    private bool _isCompletedExpanded;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isAdding;

    [ObservableProperty]
    private bool _isDropTarget;

    [ObservableProperty]
    private string _draftText = string.Empty;

    public string AddPlaceholder => $"添加到「{Title}」… Enter 保存";

    public IRelayCommand ToggleAddingCommand { get; }
    public IRelayCommand ToggleCompletedCommand { get; }

    public void SubmitDraft()
    {
        var trimmed = DraftText?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        _createTask(Priority, trimmed);
        DraftText = string.Empty;
    }

    public void UpdateTasks(
        IEnumerable<MainTaskRowViewModel> pending,
        IEnumerable<MainTaskRowViewModel> completed)
    {
        PendingTasks.Clear();
        foreach (var item in pending)
        {
            PendingTasks.Add(item);
        }

        CompletedTasks.Clear();
        foreach (var item in completed)
        {
            CompletedTasks.Add(item);
        }

        PendingCount = PendingTasks.Count;
        CompletedCount = CompletedTasks.Count;
        HasCompleted = CompletedCount > 0;
        IsEmpty = PendingCount == 0 && CompletedCount == 0;
    }
}

/// <summary>四象限视图模型：2×2 卡片、全部/仅今天切换、卡片内快速新建、与详情抽屉联动。</summary>
public sealed partial class EisenhowerViewModel : ObservableObject, IDisposable
{
    private readonly ITaskRepository _repository;
    private readonly ToastService _toast;
    private readonly DateOnly _today;
    private string? _selectedTaskId;
    private bool _disposed;

    public EisenhowerViewModel(ITaskRepository repository, ToastService toast, DateOnly today)
    {
        _repository = repository;
        _toast = toast;
        _today = today;

        QuadrantI = new QuadrantViewModel(TaskPriority.High, "I", "重要且紧急", string.Empty, CreateInQuadrant);
        QuadrantII = new QuadrantViewModel(TaskPriority.Medium, "II", "重要不紧急", string.Empty, CreateInQuadrant);
        QuadrantIII = new QuadrantViewModel(TaskPriority.Low, "III", "不重要但紧急", string.Empty, CreateInQuadrant);
        QuadrantIV = new QuadrantViewModel(TaskPriority.None, "IV", "不重要不紧急", string.Empty, CreateInQuadrant);
        QuickCreate = new QuickCreateViewModel(today);

        _repository.Changed += OnRepositoryChanged;
        Refresh();
    }

    public QuadrantViewModel QuadrantI { get; }
    public QuadrantViewModel QuadrantII { get; }
    public QuadrantViewModel QuadrantIII { get; }
    public QuadrantViewModel QuadrantIV { get; }
    public QuickCreateViewModel QuickCreate { get; }

    [ObservableProperty]
    private bool _isQuickAddOpen;

    [ObservableProperty]
    private bool _todayOnly;

    public bool IsAllScope => !TodayOnly;

    [RelayCommand]
    private void SetAllScope() => TodayOnly = false;

    [RelayCommand]
    private void SetTodayScope() => TodayOnly = true;

    public void OpenQuickCreate(TaskPriority priority)
    {
        _selectedTaskId = null;
        QuickCreate.Reset(_today, priority);
        IsQuickAddOpen = true;
        SyncSelection();
    }

    public void OpenEditTask(MainTaskRowViewModel row)
    {
        _selectedTaskId = row.Id;
        QuickCreate.LoadTask(row.Task, _repository);
        IsQuickAddOpen = true;
        SyncSelection();
    }

    public bool SubmitQuickCreate()
    {
        if (QuickCreate.IsEditMode)
        {
            QuickCreate.Submit(_repository);
            CloseQuickCreate();
            return true;
        }

        if (QuickCreate.Submit(_repository) is null)
        {
            return false;
        }

        IsQuickAddOpen = false;
        return true;
    }

    public void CloseQuickCreate()
    {
        IsQuickAddOpen = false;
        _selectedTaskId = null;
        SyncSelection();
    }

    public void DeleteEditingTask()
    {
        if (QuickCreate.EditingTask is { } task)
        {
            CloseQuickCreate();
            DeleteTaskWithUndo(task);
        }
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

    public void SetDropTarget(TaskPriority? target)
    {
        QuadrantI.IsDropTarget = target == TaskPriority.High;
        QuadrantII.IsDropTarget = target == TaskPriority.Medium;
        QuadrantIII.IsDropTarget = target == TaskPriority.Low;
        QuadrantIV.IsDropTarget = target == TaskPriority.None;
    }

    /// <summary>P1：四象限跨卡片拖拽改优先级，原子提交并支持一键撤销。</summary>
    public bool MoveTaskToPriority(TaskItem task, TaskPriority targetPriority)
    {
        if (task.Priority == targetPriority)
        {
            return false;
        }

        var oldPriority = task.Priority;
        var targetName = targetPriority switch
        {
            TaskPriority.High => "重要且紧急",
            TaskPriority.Medium => "重要不紧急",
            TaskPriority.Low => "不重要但紧急",
            _ => "不重要不紧急",
        };

        if (_repository is IScheduleService schedule)
        {
            var cs = schedule.Apply(
                [new UpdateTaskOp(task.Id, new TaskPatch { Priority = targetPriority })],
                ChangeSource.User,
                $"调整「{task.Title}」到{targetName}");
            _toast.Show($"已移至「{targetName}」", () => schedule.Undo(cs.Id));
            return true;
        }

        task.Priority = targetPriority;
        _repository.Update(task);
        _toast.Show(
            $"已移至「{targetName}」",
            () =>
            {
                task.Priority = oldPriority;
                _repository.Update(task);
            });
        return true;
    }

    public void DeleteTaskWithUndo(TaskItem task)
    {
        var snapshot = task.Clone();

        if (string.Equals(_selectedTaskId, task.Id, StringComparison.Ordinal))
        {
            _selectedTaskId = null;
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _repository.Changed -= OnRepositoryChanged;
    }

    partial void OnTodayOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAllScope));
        Refresh();
    }

    private void CreateInQuadrant(TaskPriority priority, string title)
    {
        _repository.Create(new TaskDraft
        {
            Title = title,
            Date = _today,
            Priority = priority,
            ListId = TaskList.InboxId,
        });
    }

    private void OnRepositoryChanged(object? sender, TaskChangedEventArgs e) => UiDispatcher.PostIfNeeded(() =>
    {
        if (_disposed)
        {
            return;
        }

        Refresh();

        // 编辑对象被外部删除时收起悬浮卡，避免继续写回已不存在的任务。
        if (QuickCreate.EditingTask is { } editing &&
            !_repository.Query(new TaskQuery()).Any(t => t.Id == editing.Id))
        {
            CloseQuickCreate();
        }
    });

    private void Refresh()
    {
        var all = _repository.Query(TodayOnly ? new TaskQuery { To = _today } : new TaskQuery());

        PopulateQuadrant(QuadrantI, all, TaskPriority.High);
        PopulateQuadrant(QuadrantII, all, TaskPriority.Medium);
        PopulateQuadrant(QuadrantIII, all, TaskPriority.Low);
        PopulateQuadrant(QuadrantIV, all, TaskPriority.None);

        SyncSelection();
    }

    private void PopulateQuadrant(QuadrantViewModel quadrant, IReadOnlyList<TaskItem> source, TaskPriority priority)
    {
        var pending = source
            .Where(t => !t.IsCompleted && t.Priority == priority)
            .OrderBy(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .Select(t => new MainTaskRowViewModel(t, _today, showListTag: false));

        var completed = source
            .Where(t => t.IsCompleted && t.Priority == priority)
            .OrderByDescending(t => t.CompletedAt ?? t.CreatedAt)
            .Select(t => new MainTaskRowViewModel(t, _today, showListTag: false));

        quadrant.UpdateTasks(pending, completed);
    }

    private void SyncSelection()
    {
        var allRows = QuadrantI.PendingTasks.Concat(QuadrantI.CompletedTasks)
            .Concat(QuadrantII.PendingTasks).Concat(QuadrantII.CompletedTasks)
            .Concat(QuadrantIII.PendingTasks).Concat(QuadrantIII.CompletedTasks)
            .Concat(QuadrantIV.PendingTasks).Concat(QuadrantIV.CompletedTasks);

        foreach (var row in allRows)
        {
            row.IsSelected = _selectedTaskId is not null &&
                             string.Equals(row.Id, _selectedTaskId, StringComparison.Ordinal);
        }
    }
}

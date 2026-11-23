using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VGList.Core.Models;
using VGList.Widgets.Support;

namespace VGList.Widgets.Today;

/// <summary>任务行的只读投影。勾选动作回交给父 ViewModel，行本身不碰仓储。</summary>
public sealed partial class TaskRowViewModel : ObservableObject
{
    private readonly Action<TaskItem> _toggle;
    private readonly DateOnly _today;

    public TaskRowViewModel(TaskItem task, DateOnly today, Action<TaskItem> toggle)
    {
        Task = task;
        _today = today;
        _toggle = toggle;
        ToggleCommand = new RelayCommand(() => _toggle(Task));
    }

    public TaskItem Task { get; }

    public string Title => Task.Title;

    public bool IsCompleted => Task.IsCompleted;

    public bool IsOverdue => !IsCompleted && Task.Date < _today;

    public string DateLabel => DateLabels.Describe(Task.Date, _today);

    public bool IsHigh => Task.Priority == TaskPriority.High;

    public bool IsMedium => Task.Priority == TaskPriority.Medium;

    public bool IsLow => Task.Priority == TaskPriority.Low;

    public bool IsNone => Task.Priority == TaskPriority.None;

    public int SubtasksTotal => Task.Subtasks.Count;

    public int SubtasksCompleted => Task.Subtasks.Count(s => s.IsCompleted);

    public bool HasSubtasks => SubtasksTotal > 0;

    /// <summary>勾选动画进行中：勾先出现、标题划掉，随后整行收进已完成。</summary>
    [ObservableProperty]
    private bool isCompleting;

    public bool IsCheckVisible => IsCompleted || IsCompleting;

    public bool IsDoneVisual => IsCompleted || IsCompleting;

    public IRelayCommand ToggleCommand { get; }

    partial void OnIsCompletingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCheckVisible));
        OnPropertyChanged(nameof(IsDoneVisual));
    }
}

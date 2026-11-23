using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VGList.Core.Abstractions;
using VGList.Core.Models;
using VGList.Widgets.Support;

namespace VGList.Widgets.Today;

/// <summary>任务详情面板：标题、Markdown 内容、优先级与完成状态，改动即时写回仓储。</summary>
public sealed partial class TaskDetailViewModel : ObservableObject
{
    private readonly ITaskRepository _repository;
    private readonly TaskItem _task;
    private readonly DateOnly _today;

    public TaskDetailViewModel(ITaskRepository repository, TaskItem task, DateOnly today)
    {
        _repository = repository;
        _task = task;
        _today = today;
        title = task.Title;
        content = task.Content;

        ToggleCompletedCommand = new RelayCommand(ToggleCompleted);
        CyclePriorityCommand = new RelayCommand(CyclePriority);
    }

    public IReadOnlyList<SlashMenuItem> SlashItems => SlashMenuItem.All;

    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private string content;

    public bool IsCompleted => _task.IsCompleted;

    public bool IsHigh => _task.Priority == TaskPriority.High;

    public bool IsMedium => _task.Priority == TaskPriority.Medium;

    public bool IsLow => _task.Priority == TaskPriority.Low;

    public bool IsNone => _task.Priority == TaskPriority.None;

    public string PriorityLabel => _task.Priority switch
    {
        TaskPriority.High => "优先级：高（点击切换）",
        TaskPriority.Medium => "优先级：中（点击切换）",
        TaskPriority.Low => "优先级：低（点击切换）",
        _ => "优先级：无（点击切换）",
    };

    public string DateLabel
    {
        get
        {
            var absolute = $"{_task.Date.Month}月{_task.Date.Day}日";
            var relative = DateLabels.Describe(_task.Date, _today);
            return relative == absolute ? absolute : $"{absolute}, {relative}";
        }
    }

    public IRelayCommand ToggleCompletedCommand { get; }

    public IRelayCommand CyclePriorityCommand { get; }

    partial void OnTitleChanged(string value)
    {
        var title = value?.Trim() ?? string.Empty;
        if (title.Length == 0 || string.Equals(title, _task.Title, StringComparison.Ordinal))
        {
            return;
        }

        _task.Title = title;
        _repository.Update(_task);
    }

    partial void OnContentChanged(string value)
    {
        var content = value ?? string.Empty;
        if (string.Equals(content, _task.Content, StringComparison.Ordinal))
        {
            return;
        }

        _task.Content = content;
        _repository.Update(_task);
    }

    public void ToggleCompleted()
    {
        var next = !_task.IsCompleted;
        _repository.SetCompleted(_task.Id, next);

        // SQLite 仓储会更新数据库中的副本，这里同步当前实例，界面才能立即打钩
        _task.IsCompleted = next;
        _task.CompletedAt = next ? DateTimeOffset.Now : null;
        OnPropertyChanged(nameof(IsCompleted));
    }

    public void CyclePriority()
    {
        _task.Priority = _task.Priority switch
        {
            TaskPriority.None => TaskPriority.Low,
            TaskPriority.Low => TaskPriority.Medium,
            TaskPriority.Medium => TaskPriority.High,
            _ => TaskPriority.None,
        };

        _repository.Update(_task);
        OnPropertyChanged(nameof(IsHigh));
        OnPropertyChanged(nameof(IsMedium));
        OnPropertyChanged(nameof(IsLow));
        OnPropertyChanged(nameof(IsNone));
        OnPropertyChanged(nameof(PriorityLabel));
    }
}

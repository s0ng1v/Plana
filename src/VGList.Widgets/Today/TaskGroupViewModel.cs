namespace VGList.Widgets.Today;

/// <summary>今日小组件任务列表分组（「已过期」/「所有」视图按日期分组；无逾期时今天视图仅保留单组隐藏标题）。</summary>
public sealed class TaskGroupViewModel
{
    public TaskGroupViewModel(
        string header,
        bool showHeader,
        IReadOnlyList<TaskRowViewModel> tasks,
        bool isOverdueGroup = false)
    {
        Header = header;
        IsHeaderVisible = showHeader;
        Tasks = tasks;
        IsOverdueGroup = isOverdueGroup;
    }

    public string Header { get; }

    public bool IsHeaderVisible { get; }

    public bool IsOverdueGroup { get; }

    public string CountLabel => Tasks.Count.ToString();

    public IReadOnlyList<TaskRowViewModel> Tasks { get; }
}

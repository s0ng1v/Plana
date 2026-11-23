namespace VGList.Core.Models;

/// <summary>
/// 清单名称与颜色的只读解析入口：由仓储在清单增删改时同步刷新，
/// 供任务行、打卡卡片、月历等没有仓储引用的视图模型使用。
/// </summary>
public static class TaskListCatalog
{
    private static IReadOnlyList<TaskList> _items =
    [
        new() { Id = TaskList.InboxId, Name = "收集箱", Color = "#8F7BE5" },
        new() { Id = TaskList.WelcomeId, Name = "欢迎", Color = "#5FC7A2" },
    ];

    public static IReadOnlyList<TaskList> Items => _items;

    public static string NameOf(string? listId) =>
        _items.FirstOrDefault(l => l.Id == listId)?.Name ?? "收集箱";

    public static void Replace(IEnumerable<TaskList> lists) =>
        _items = lists.Select(l => new TaskList { Id = l.Id, Name = l.Name, Color = l.Color }).ToList();
}

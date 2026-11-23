namespace VGList.Core.Models;

/// <summary>清单。P0 只区分收集箱与自建清单。</summary>
public sealed class TaskList
{
    public const string InboxId = "inbox";

    /// <summary>P0 内置的示例清单，用于验证「清单」视图与任务归属。</summary>
    public const string WelcomeId = "welcome";

    public required string Id { get; init; }

    public required string Name { get; set; }

    /// <summary>侧栏与详情菜单里的圆点颜色（十六进制）。</summary>
    public string Color { get; set; } = "#8F7BE5";
}

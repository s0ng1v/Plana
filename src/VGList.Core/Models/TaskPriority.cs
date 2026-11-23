namespace VGList.Core.Models;

/// <summary>
/// 任务优先级。数值同时决定四象限归属：
/// 高=重要且紧急，中=重要不紧急，低=不重要但紧急，无=不重要不紧急。
/// </summary>
public enum TaskPriority
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

using Avalonia;
using Avalonia.Threading;

namespace VGList.Widgets.Support;

/// <summary>
/// 统一 UI 线程封送助手（ADR-0057）：
/// 确保来自后台线程（AssistantHost、MCP 管道、ReminderScheduler 等）的仓储/存储变更安全切回 Avalonia UI 线程执行，
/// 防止跨线程分配 SolidColorBrush 或并发篡改 ObservableCollection 导致渲染崩溃与假死；
/// 在无 Application.Current 的单元测试或已在 UI 线程上时同步执行，保持单测确定性。
/// </summary>
public static class UiDispatcher
{
    public static void PostIfNeeded(Action action)
    {
        if (Application.Current is not null && !Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(action);
        }
        else
        {
            action();
        }
    }
}

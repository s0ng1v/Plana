namespace VGList.Platform;

/// <summary>系统通知服务。点击回调可能为空实现（例如 macOS 的脚本通知无法回传点击）。</summary>
public interface INotificationService : IDisposable
{
    event Action<string?>? NotificationClicked;

    bool Show(string title, string body, string? taskId = null);
}

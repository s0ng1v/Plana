namespace VGList.Platform;

/// <summary>当前用户级开机自启动。</summary>
public interface IStartupManager
{
    bool TrySetEnabled(bool enabled, bool startMinimized);
}

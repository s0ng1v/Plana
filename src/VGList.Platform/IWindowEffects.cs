namespace VGList.Platform;

/// <summary>窗口外观与桌面挂件行为。Windows 走 Win32/DWM，macOS 走原生窗口或安全降级。</summary>
public interface IWindowEffects
{
    bool TryDisableDwmChrome(IntPtr windowHandle);

    bool TrySetToolWindow(IntPtr windowHandle);

    bool TrySetClickThrough(IntPtr windowHandle, bool enabled);
}

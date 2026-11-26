using VGList.Platform;

namespace VGList.Platform.Mac;

/// <summary>
/// macOS 窗口外观。DWM 圆角和工具窗口样式不适用；点击穿透暂未接入 NSWindow，
/// 统一返回 false，由界面层保持可点击状态。
/// </summary>
public sealed class MacWindowEffects : IWindowEffects
{
    public bool TryDisableDwmChrome(IntPtr windowHandle) => false;

    public bool TrySetToolWindow(IntPtr windowHandle) => false;

    public bool TrySetClickThrough(IntPtr windowHandle, bool enabled) => false;

    public bool TrySetKeepOnDesktop(IntPtr windowHandle, bool enabled) => false;
}

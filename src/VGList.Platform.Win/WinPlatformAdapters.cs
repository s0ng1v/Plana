using System.IO.Pipes;
using VGList.Platform;
using VGList.Platform.Win.Win32;

namespace VGList.Platform.Win;

/// <summary>把现有 Win32 静态工具适配为跨平台接口，供 Desktop 启动时注册。</summary>
public sealed class WinWindowEffects : IWindowEffects
{
    public bool TryDisableDwmChrome(IntPtr windowHandle) => WindowEffects.TryDisableDwmChrome(windowHandle);

    public bool TrySetToolWindow(IntPtr windowHandle) => WindowEffects.TrySetToolWindow(windowHandle);

    public bool TrySetClickThrough(IntPtr windowHandle, bool enabled) =>
        WindowEffects.TrySetClickThrough(windowHandle, enabled);
}

public sealed class WinStartupManager : IStartupManager
{
    public bool TrySetEnabled(bool enabled, bool startMinimized) =>
        WindowsStartupManager.TrySetEnabled(enabled, startMinimized);
}

public sealed class WinMcpPlatform : IMcpPlatform
{
    public string GetCurrentUserPipeName() => WindowsMcpPlatform.GetCurrentUserPipeName();

    public NamedPipeServerStream CreateCurrentUserOnlyServerPipe(string pipeName, int maxInstances = 8) =>
        WindowsMcpPlatform.CreateCurrentUserOnlyServerPipe(pipeName, maxInstances);

    public void FlashTaskbarOnce(IntPtr windowHandle) => WindowsMcpPlatform.FlashTaskbarOnce(windowHandle);
}

public sealed class WinClipboardReader : IClipboardReader
{
    public bool TryGetFilePaths(out string[] paths) => WinClipboardHelper.TryGetFilePaths(out paths);

    public bool TryGetImageBytes(out byte[] bytes, out bool isPng) =>
        WinClipboardHelper.TryGetImageBytes(out bytes, out isPng);
}

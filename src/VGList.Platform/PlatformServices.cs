using System.IO.Pipes;

namespace VGList.Platform;

/// <summary>
/// 平台服务的进程级注册点。桌面启动时由 PlatformFactory 注入具体实现；
/// 未注入时使用安全降级实现，保证单元测试和工具进程不会因缺少平台而崩溃。
/// </summary>
public static class PlatformServices
{
    public static IWindowEffects WindowEffects { get; private set; } = new NoopWindowEffects();

    public static INotificationService Notifications { get; private set; } = new NoopNotificationService();

    public static IStartupManager Startup { get; private set; } = new NoopStartupManager();

    public static IMcpPlatform Mcp { get; private set; } = new NoopMcpPlatform();

    public static IClipboardReader Clipboard { get; private set; } = new NoopClipboardReader();

    public static ISecretProtector Secrets { get; private set; } = new NoopSecretProtector();

    public static void Configure(
        IWindowEffects windowEffects,
        INotificationService notifications,
        IStartupManager startup,
        IMcpPlatform mcp,
        IClipboardReader clipboard,
        ISecretProtector secrets)
    {
        WindowEffects = windowEffects;
        Notifications = notifications;
        Startup = startup;
        Mcp = mcp;
        Clipboard = clipboard;
        Secrets = secrets;
    }
}

internal sealed class NoopWindowEffects : IWindowEffects
{
    public bool TryDisableDwmChrome(IntPtr windowHandle) => false;

    public bool TrySetToolWindow(IntPtr windowHandle) => false;

    public bool TrySetClickThrough(IntPtr windowHandle, bool enabled) => false;

    public bool TrySetKeepOnDesktop(IntPtr windowHandle, bool enabled) => false;
}

internal sealed class NoopNotificationService : INotificationService
{
    public event Action<string?>? NotificationClicked
    {
        add { }
        remove { }
    }

    public bool Show(string title, string body, string? taskId = null) => false;

    public void Dispose()
    {
    }
}

internal sealed class NoopStartupManager : IStartupManager
{
    public bool TrySetEnabled(bool enabled, bool startMinimized) => false;
}

internal sealed class NoopMcpPlatform : IMcpPlatform
{
    public string GetCurrentUserPipeName() => McpPipeName.ForCurrentUser();

    public NamedPipeServerStream CreateCurrentUserOnlyServerPipe(string pipeName, int maxInstances = 8) =>
        new(
            pipeName,
            PipeDirection.InOut,
            maxInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

    public void FlashTaskbarOnce(IntPtr windowHandle)
    {
    }
}

internal sealed class NoopClipboardReader : IClipboardReader
{
    public bool TryGetFilePaths(out string[] paths)
    {
        paths = [];
        return false;
    }

    public bool TryGetImageBytes(out byte[] bytes, out bool isPng)
    {
        bytes = [];
        isPng = false;
        return false;
    }
}

internal sealed class NoopSecretProtector : ISecretProtector
{
    public string Protect(string plainText) => plainText;

    public bool TryUnprotect(string cipherText, out string plainText)
    {
        plainText = cipherText;
        return true;
    }
}

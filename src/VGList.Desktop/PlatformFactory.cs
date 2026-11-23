using VGList.Desktop.Services;
using VGList.Platform;
#if WINDOWS
using VGList.Platform.Win;
using VGList.Platform.Win.Win32;
#else
using VGList.Platform.Mac;
#endif

namespace VGList.Desktop;

/// <summary>按目标平台注册窗口、通知、自启、MCP、剪贴板与密钥保护实现。</summary>
internal static class PlatformFactory
{
    public static void Configure()
    {
#if WINDOWS
        PlatformServices.Configure(
            new WinWindowEffects(),
            new WindowsNotificationService(),
            new WinStartupManager(),
            new WinMcpPlatform(),
            new WinClipboardReader(),
            new WindowsSecretProtector());
#else
        PlatformServices.Configure(
            new MacWindowEffects(),
            new MacNotificationService(),
            new MacStartupManager(),
            new MacMcpPlatform(),
            new MacClipboardReader(),
            new MacSecretProtector());
#endif
    }
}

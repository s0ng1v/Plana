using VGList.Platform;

namespace VGList.Platform.Mac;

/// <summary>macOS 首版暂不写 LaunchAgent，避免未签名应用被系统拦截；界面会收到 false。</summary>
public sealed class MacStartupManager : IStartupManager
{
    public bool TrySetEnabled(bool enabled, bool startMinimized) => false;
}

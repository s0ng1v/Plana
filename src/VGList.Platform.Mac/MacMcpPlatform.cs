using System.IO.Pipes;
using VGList.Platform;

namespace VGList.Platform.Mac;

/// <summary>.NET 命名管道在 macOS 上由 Unix Domain Socket 实现，管道名与 MCP 客户端保持一致。</summary>
public sealed class MacMcpPlatform : IMcpPlatform
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
        // macOS 没有任务栏闪烁；Dock 图标提醒需要 NSApplication，首版先跳过。
    }
}

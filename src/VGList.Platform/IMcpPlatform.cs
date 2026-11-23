using System.IO.Pipes;

namespace VGList.Platform;

/// <summary>MCP 本地传输与后台提醒的平台能力。</summary>
public interface IMcpPlatform
{
    string GetCurrentUserPipeName();

    NamedPipeServerStream CreateCurrentUserOnlyServerPipe(string pipeName, int maxInstances = 8);

    void FlashTaskbarOnce(IntPtr windowHandle);
}

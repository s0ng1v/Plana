using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using VGList.Platform;

namespace VGList.Platform.Win.Win32;

/// <summary>
/// A7 本地 MCP 命名管道 Windows 安全封装与后台提醒：
/// - 命名管道仅授权当前登录 Windows 用户 SID（拒绝 Everyone / 其他本机账户）；
/// - 主窗口在后台收到外部 AI 确认请求时轻轻闪烁任务栏图标一次。
/// </summary>
public static class WindowsMcpPlatform
{
    private const uint FlashwTray = 0x00000002;
    private const uint FlashwTimerNoFg = 0x0000000C;

    public static string GetCurrentUserPipeName() => McpPipeName.ForCurrentUser();

    public static NamedPipeServerStream CreateCurrentUserOnlyServerPipe(string pipeName, int maxInstances = 8)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var userSid = identity.User ?? throw new InvalidOperationException("无法获取当前 Windows 用户 SID。");

        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            userSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    public static void FlashTaskbarOnce(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = FlashwTray | FlashwTimerNoFg,
            uCount = 1,
            dwTimeout = 0,
        };
        _ = FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);
}

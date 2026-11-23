using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using VGList.Platform;

namespace VGList.Platform.Win.Win32;

/// <summary>
/// Windows 原生通知服务（ADR-0055）：
/// 使用纯 Win32 Shell_NotifyIconW (NIF_INFO) 在 Windows 10/11 操作中心弹出原生系统通知，
/// 零 NuGet / WinRT AUMID 注册依赖，未打包 .exe 与 --minimized 后台常驻模式均可稳定弹出。
/// </summary>
public sealed class WindowsNotificationService : INotificationService
{
    private const uint WM_APP = 0x8000;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_TIMER = 0x0113;
    private const uint WM_TRAY_CALLBACK = WM_APP + 0x51;
    private const uint WM_SHOW_NEXT = WM_APP + 0x52;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;
    private const uint NOTIFYICON_VERSION_4 = 4;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint NIIF_INFO = 0x00000001;
    private const uint NIIF_USER = 0x00000004;
    private const uint NIIF_LARGE_ICON = 0x00000020;
    private const uint NIIF_RESPECT_QUIET_TIME = 0x00000080;

    private const uint NIN_BALLOONTIMEOUT = 0x0404;
    private const uint NIN_BALLOONUSERCLICK = 0x0405;

    private const uint WS_OVERLAPPED = 0x00000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;

    private const uint NotifyIconId = 0x504C414E; // 'PLAN'
    private const nuint CleanupTimerId = 1;
    private const uint CleanupDelayMs = 15_000;

    private readonly Lock _gate = new();
    private readonly ConcurrentQueue<(string Title, string Body, string? TaskId)> _queue = new();
    private readonly ManualResetEventSlim _windowReady = new(false);

    private Thread? _pumpThread;
    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _iconAdded;
    private string? _lastTaskId;
    private WndProcDelegate? _wndProcDelegate;
    private bool _disposed;

    /// <summary>用户点击 Windows 通知横幅时触发（参数为关联 TaskId，可能为空）。</summary>
    public event Action<string?>? NotificationClicked;

    public bool Show(string title, string body, string? taskId = null)
    {
        if (!OperatingSystem.IsWindows() || _disposed)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        var safeTitle = Truncate(string.IsNullOrWhiteSpace(title) ? "Plana 提醒" : title.Trim(), 63);
        var safeBody = Truncate(string.IsNullOrWhiteSpace(body) ? safeTitle : body.Trim(), 255);

        EnsurePumpThreadStarted();
        if (!_windowReady.Wait(TimeSpan.FromSeconds(2)) || _hwnd == IntPtr.Zero)
        {
            return false;
        }

        _queue.Enqueue((safeTitle, safeBody, taskId));
        return PostMessageW(_hwnd, WM_SHOW_NEXT, IntPtr.Zero, IntPtr.Zero);
    }

    private void EnsurePumpThreadStarted()
    {
        lock (_gate)
        {
            if (_disposed || _pumpThread is not null)
            {
                return;
            }

            _pumpThread = new Thread(RunMessageLoop)
            {
                IsBackground = true,
                Name = "Plana.WinNotifyHost",
            };
            _pumpThread.Start();
        }
    }

    private void RunMessageLoop()
    {
        var className = $"PlanaNotifyHost_{Environment.ProcessId}_{Guid.NewGuid():N}";
        var hInstance = GetModuleHandleW(null);
        _wndProcDelegate = WndProc;

        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = hInstance,
            lpszClassName = className,
        };

        if (RegisterClassExW(ref wc) == 0)
        {
            _windowReady.Set();
            return;
        }

        try
        {
            _hwnd = CreateWindowExW(
                WS_EX_TOOLWINDOW,
                className,
                "Plana",
                WS_OVERLAPPED,
                0,
                0,
                0,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                hInstance,
                IntPtr.Zero);

            _hIcon = LoadAppIcon();
            _windowReady.Set();

            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
        }
        finally
        {
            RemoveNotifyIcon();
            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }

            _hwnd = IntPtr.Zero;
            UnregisterClassW(className, hInstance);
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_SHOW_NEXT:
                DrainQueue(hWnd);
                return IntPtr.Zero;

            case WM_TRAY_CALLBACK:
            {
                var evt = unchecked((uint)lParam.ToInt64()) & 0xFFFF;
                if (evt == NIN_BALLOONUSERCLICK)
                {
                    var clickedId = _lastTaskId;
                    try
                    {
                        NotificationClicked?.Invoke(clickedId);
                    }
                    catch
                    {
                    }
                }
                else if (evt == NIN_BALLOONTIMEOUT)
                {
                    RemoveNotifyIcon();
                }

                return IntPtr.Zero;
            }

            case WM_TIMER:
                if (wParam == (IntPtr)CleanupTimerId)
                {
                    KillTimer(hWnd, CleanupTimerId);
                    RemoveNotifyIcon();
                }

                return IntPtr.Zero;

            case WM_CLOSE:
                KillTimer(hWnd, CleanupTimerId);
                RemoveNotifyIcon();
                DestroyWindow(hWnd);
                return IntPtr.Zero;

            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void DrainQueue(IntPtr hWnd)
    {
        while (_queue.TryDequeue(out var item))
        {
            _lastTaskId = item.TaskId;
            PushBalloon(hWnd, item.Title, item.Body);
        }
    }

    private void PushBalloon(IntPtr hWnd, string title, string body)
    {
        var hasCustomIcon = _hIcon != IntPtr.Zero;
        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = hWnd,
            uID = NotifyIconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_INFO,
            uCallbackMessage = WM_TRAY_CALLBACK,
            hIcon = _hIcon,
            szTip = "Plana 提醒",
            szInfoTitle = title,
            szInfo = body,
            dwInfoFlags = (hasCustomIcon ? (NIIF_USER | NIIF_LARGE_ICON) : NIIF_INFO) | NIIF_RESPECT_QUIET_TIME,
            hBalloonIcon = hasCustomIcon ? _hIcon : IntPtr.Zero,
        };

        if (!_iconAdded)
        {
            if (Shell_NotifyIconW(NIM_ADD, ref nid))
            {
                _iconAdded = true;
                var ver = new NOTIFYICONDATAW
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                    hWnd = hWnd,
                    uID = NotifyIconId,
                    uVersion = NOTIFYICON_VERSION_4,
                };
                Shell_NotifyIconW(NIM_SETVERSION, ref ver);
            }
        }
        else
        {
            Shell_NotifyIconW(NIM_MODIFY, ref nid);
        }

        KillTimer(hWnd, CleanupTimerId);
        SetTimer(hWnd, CleanupTimerId, CleanupDelayMs, IntPtr.Zero);
    }

    private void RemoveNotifyIcon()
    {
        if (!_iconAdded || _hwnd == IntPtr.Zero)
        {
            return;
        }

        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hwnd,
            uID = NotifyIconId,
        };
        Shell_NotifyIconW(NIM_DELETE, ref nid);
        _iconAdded = false;
    }

    private static IntPtr LoadAppIcon()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                var sb = new StringBuilder(exePath, 260);
                ushort idx = 0;
                var hIcon = ExtractAssociatedIconW(IntPtr.Zero, sb, ref idx);
                if (hIcon != IntPtr.Zero)
                {
                    return hIcon;
                }
            }
        }
        catch
        {
        }

        return IntPtr.Zero;
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..(maxLen - 1)] + "…";

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        var hwnd = _hwnd;
        if (hwnd != IntPtr.Zero)
        {
            PostMessageW(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        try
        {
            _pumpThread?.Join(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        _windowReady.Dispose();
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractAssociatedIconW(IntPtr hInst, StringBuilder lpIconPath, ref ushort lpiIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nuint SetTimer(IntPtr hWnd, nuint nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool KillTimer(IntPtr hWnd, nuint uIDEvent);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);
}

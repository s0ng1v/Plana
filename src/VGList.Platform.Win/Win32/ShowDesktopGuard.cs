using System.Text;

namespace VGList.Platform.Win.Win32;

/// <summary>
/// Windows「显示桌面」（Win+D / 任务栏右下角按钮）桌面小组件可见性守护（ADR-0073）：
/// 不修改小组件的父/所有者窗口（避免 ADR-0024 中桌面图标层拦截点击或破坏透明渲染），
/// 仅在存在追踪窗口时挂接外部进程前后台与最小化事件钩子；当桌面宿主（Progman / WorkerW / SHELLDLL_DefView）
/// 被置前压过小组件或小组件被意外最小化时，立即将其无焦点恢复到桌面宿主正上方，
/// 关闭设置开关或关闭全部小组件后立刻卸载钩子，零常驻轮询开销。
/// </summary>
public static class ShowDesktopGuard
{
    private static readonly Lock Gate = new();
    private static readonly HashSet<IntPtr> TrackedWindows = [];
    // 保留静态强引用，防止原生回调期间委托被 GC 回收
    private static readonly NativeMethods.WinEventDelegate WinEventCallback = OnWinEvent;

    private static IntPtr _foregroundHook;
    private static IntPtr _minimizeHook;
    private static IntPtr _desktopSwitchHook;
    private static IntPtr _explorerReorderHook;
    private static Timer? _recheckTimer;
    private static int _pendingRecheckStage;

    public static bool SetEnabled(IntPtr windowHandle, bool enabled)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero)
        {
            return false;
        }

        TrySetExcludedFromPeek(windowHandle, enabled);

        lock (Gate)
        {
            if (enabled)
            {
                TrackedWindows.Add(windowHandle);
                EnsureHooksLocked();
            }
            else
            {
                TrackedWindows.Remove(windowHandle);
                if (TrackedWindows.Count == 0)
                {
                    RemoveHooksLocked();
                }
            }
        }

        if (enabled)
        {
            EnsureWidgetsAboveDesktop();
        }

        return true;
    }

    /// <summary>判断前景或重排事件窗口类名是否属于 Windows 外壳或桌面宿主。</summary>
    public static bool IsShellOrDesktopClass(string? className) =>
        string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(className, "SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(className, "SysListView32", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(className, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(className, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(className, "TrayShowDesktopButtonWClass", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(className, "MSTaskSwWClass", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(className, "MSTaskListWClass", StringComparison.OrdinalIgnoreCase);

    /// <summary>判断紧邻桌面宿主上方的窗口在选取 Z 序插入点时是否应跳过（不可见、UWP Cloak 或外壳自身窗口）。</summary>
    public static bool ShouldSkipAsInsertAfter(bool isVisible, bool isCloaked, bool isShellOrDesktop) =>
        !isVisible || isCloaked || isShellOrDesktop;

    /// <summary>
    /// 计算 SetWindowPos 的 hWndInsertAfter 目标：
    /// - 若桌面宿主的上一窗口就是自身，说明已在正上方无需调整（返回 targetHwnd）；
    /// - 若桌面宿主已在非置顶带最顶（prevOfDesktop 为空或为 TOPMOST 窗口），返回 HWND_TOP(0) 放入非置顶带顶部；
    /// - 否则插入 prevOfDesktop 之后、桌面宿主正上方，保证不反向遮挡普通应用窗口。
    /// </summary>
    public static IntPtr ResolveInsertAfter(IntPtr targetHwnd, IntPtr prevOfDesktop, bool prevIsTopmost)
    {
        if (prevOfDesktop == targetHwnd)
        {
            return targetHwnd;
        }

        if (prevOfDesktop == IntPtr.Zero || prevIsTopmost)
        {
            return IntPtr.Zero;
        }

        return prevOfDesktop;
    }

    private static void EnsureHooksLocked()
    {
        const uint externalFlags = NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS;
        const uint anyProcessFlags = NativeMethods.WINEVENT_OUTOFCONTEXT;

        if (_foregroundHook == IntPtr.Zero)
        {
            _foregroundHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                WinEventCallback,
                0,
                0,
                externalFlags);
        }

        if (_minimizeHook == IntPtr.Zero)
        {
            _minimizeHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_MINIMIZESTART,
                NativeMethods.EVENT_SYSTEM_MINIMIZESTART,
                IntPtr.Zero,
                WinEventCallback,
                0,
                0,
                anyProcessFlags);
        }

        if (_desktopSwitchHook == IntPtr.Zero)
        {
            _desktopSwitchHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_DESKTOPSWITCH,
                NativeMethods.EVENT_SYSTEM_DESKTOPSWITCH,
                IntPtr.Zero,
                WinEventCallback,
                0,
                0,
                anyProcessFlags);
        }

        if (_explorerReorderHook == IntPtr.Zero)
        {
            var desktopHost = FindDesktopHostWindow();
            if (desktopHost != IntPtr.Zero)
            {
                _ = NativeMethods.GetWindowThreadProcessId(desktopHost, out var explorerPid);
                if (explorerPid != 0)
                {
                    _explorerReorderHook = NativeMethods.SetWinEventHook(
                        NativeMethods.EVENT_OBJECT_REORDER,
                        NativeMethods.EVENT_OBJECT_REORDER,
                        IntPtr.Zero,
                        WinEventCallback,
                        explorerPid,
                        0,
                        anyProcessFlags);
                }
            }
        }
    }

    private static void RemoveHooksLocked()
    {
        if (_foregroundHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }

        if (_minimizeHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_minimizeHook);
            _minimizeHook = IntPtr.Zero;
        }

        if (_desktopSwitchHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_desktopSwitchHook);
            _desktopSwitchHook = IntPtr.Zero;
        }

        if (_explorerReorderHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_explorerReorderHook);
            _explorerReorderHook = IntPtr.Zero;
        }

        _pendingRecheckStage = 0;
        _recheckTimer?.Dispose();
        _recheckTimer = null;
    }

    private static void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
        {
            var cls = GetWindowClassName(hwnd);
            if (!IsShellOrDesktopClass(cls))
            {
                return;
            }
        }
        else if (eventType == NativeMethods.EVENT_OBJECT_REORDER)
        {
            if (idObject != NativeMethods.OBJID_WINDOW)
            {
                return;
            }

            if (hwnd != IntPtr.Zero && !IsShellOrDesktopClass(GetWindowClassName(hwnd)))
            {
                return;
            }
        }

        EnsureWidgetsAboveDesktop();
        ScheduleDelayedRechecks();
    }

    private static void ScheduleDelayedRechecks()
    {
        lock (Gate)
        {
            if (TrackedWindows.Count == 0)
            {
                return;
            }

            _pendingRecheckStage = 1;
            _recheckTimer ??= new Timer(OnRecheckTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
            _recheckTimer.Change(50, Timeout.Infinite);
        }
    }

    private static void OnRecheckTimerElapsed(object? state)
    {
        EnsureWidgetsAboveDesktop();

        lock (Gate)
        {
            if (TrackedWindows.Count == 0 || _recheckTimer is null)
            {
                _pendingRecheckStage = 0;
                return;
            }

            if (_pendingRecheckStage == 1)
            {
                _pendingRecheckStage = 2;
                _recheckTimer.Change(130, Timeout.Infinite);
            }
            else if (_pendingRecheckStage == 2)
            {
                _pendingRecheckStage = 3;
                _recheckTimer.Change(180, Timeout.Infinite);
            }
            else
            {
                _pendingRecheckStage = 0;
            }
        }
    }

    private static void EnsureWidgetsAboveDesktop()
    {
        IntPtr[] snapshot;
        lock (Gate)
        {
            if (TrackedWindows.Count == 0)
            {
                return;
            }

            snapshot = [.. TrackedWindows];
        }

        var shellHost = FindDesktopHostWindow();
        if (shellHost == IntPtr.Zero)
        {
            return;
        }

        var shellRoot = NativeMethods.GetShellWindow();
        var desktopHost = shellRoot != IntPtr.Zero &&
                          shellRoot != shellHost &&
                          NativeMethods.IsWindowVisible(shellRoot) &&
                          IsWindowAbove(shellRoot, shellHost)
            ? shellRoot
            : shellHost;

        List<IntPtr>? deadHandles = null;

        foreach (var hwnd in snapshot)
        {
            if (!NativeMethods.IsWindow(hwnd))
            {
                deadHandles ??= [];
                deadHandles.Add(hwnd);
                continue;
            }

            if (!NativeMethods.IsWindowVisible(hwnd))
            {
                continue;
            }

            var wasIconic = NativeMethods.IsIconic(hwnd);
            if (wasIconic)
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);
                if (NativeMethods.IsIconic(hwnd))
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                }
            }

            if (wasIconic || IsWindowAbove(desktopHost, hwnd))
            {
                var insertAfter = FindInsertAfterAboveDesktop(hwnd, desktopHost);
                if (insertAfter != hwnd)
                {
                    NativeMethods.SetWindowPos(
                        hwnd,
                        insertAfter,
                        0,
                        0,
                        0,
                        0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

                    if (insertAfter != IntPtr.Zero && IsWindowAbove(desktopHost, hwnd))
                    {
                        NativeMethods.SetWindowPos(
                            hwnd,
                            IntPtr.Zero,
                            0,
                            0,
                            0,
                            0,
                            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                    }
                }
            }
        }

        if (deadHandles is { Count: > 0 })
        {
            lock (Gate)
            {
                foreach (var dead in deadHandles)
                {
                    TrackedWindows.Remove(dead);
                }

                if (TrackedWindows.Count == 0)
                {
                    RemoveHooksLocked();
                }
            }
        }
    }

    private static IntPtr FindInsertAfterAboveDesktop(IntPtr targetHwnd, IntPtr desktopHost)
    {
        var cur = desktopHost;
        for (var i = 0; i < 4096 && cur != IntPtr.Zero; i++)
        {
            cur = NativeMethods.GetWindow(cur, NativeMethods.GW_HWNDPREV);
            if (cur == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (cur == targetHwnd)
            {
                return targetHwnd;
            }

            var isVisible = NativeMethods.IsWindowVisible(cur);
            var isCloaked = IsWindowCloaked(cur);
            var isShell = IsShellOrDesktopClass(GetWindowClassName(cur));
            if (ShouldSkipAsInsertAfter(isVisible, isCloaked, isShell))
            {
                continue;
            }

            var isTopmost = (((long)NativeMethods.GetWindowLongPtr(cur, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOPMOST) != 0);
            return isTopmost ? IntPtr.Zero : cur;
        }

        return IntPtr.Zero;
    }

    private static bool IsWindowCloaked(IntPtr hwnd)
    {
        try
        {
            return NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 &&
                   cloaked != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static void TrySetExcludedFromPeek(IntPtr hwnd, bool excluded)
    {
        var value = excluded ? 1 : 0;
        try
        {
            NativeMethods.DwmSetWindowAttribute(
                hwnd,
                NativeMethods.DWMWA_EXCLUDED_FROM_PEEK,
                ref value,
                sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    private static IntPtr FindDesktopHostWindow()
    {
        var shell = NativeMethods.GetShellWindow();
        if (shell != IntPtr.Zero &&
            NativeMethods.FindWindowExW(shell, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
        {
            return shell;
        }

        var progman = NativeMethods.FindWindowW("Progman", null);
        if (progman != IntPtr.Zero &&
            NativeMethods.FindWindowExW(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
        {
            return progman;
        }

        var found = IntPtr.Zero;
        NativeMethods.EnumWindows((h, _) =>
        {
            if (NativeMethods.FindWindowExW(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                found = h;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        if (found != IntPtr.Zero)
        {
            return found;
        }

        return shell != IntPtr.Zero ? shell : progman;
    }

    private static bool IsWindowAbove(IntPtr candidateAbove, IntPtr reference)
    {
        var cur = reference;
        for (var i = 0; i < 4096 && cur != IntPtr.Zero; i++)
        {
            cur = NativeMethods.GetWindow(cur, NativeMethods.GW_HWNDPREV);
            if (cur == candidateAbove)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(128);
        var len = NativeMethods.GetClassNameW(hwnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString() : string.Empty;
    }
}

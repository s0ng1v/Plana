namespace VGList.Platform.Win;

/// <summary>DWM 窗口外观。Win11 生效，其他系统返回 false 且不影响功能。</summary>
public static class WindowEffects
{
    public static bool TryEnableRoundedCorners(IntPtr windowHandle)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var preference = NativeMethods.DWMWCP_ROUND;
        try
        {
            return NativeMethods.DwmSetWindowAttribute(
                windowHandle,
                NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                ref preference,
                sizeof(int)) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// 关闭 Win11 DWM 自动追加的 8px 圆角裁切与 1px 系统浅白描边（专用于自带大圆角透明画布的桌面小组件，
    /// 避免 DWM 8px 圆角框超出小组件 16px/10px 圆角卡片在四角露出半透明偏白残影）。
    /// </summary>
    public static bool TryDisableDwmChrome(IntPtr windowHandle)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var corner = NativeMethods.DWMWCP_DONOTROUND;
            var r1 = NativeMethods.DwmSetWindowAttribute(
                windowHandle,
                NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                ref corner,
                sizeof(int));

            var borderNone = NativeMethods.DWMWA_COLOR_NONE;
            var r2 = NativeMethods.DwmSetWindowAttribute(
                windowHandle,
                NativeMethods.DWMWA_BORDER_COLOR,
                ref borderNone,
                sizeof(int));

            return r1 == 0 || r2 == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static bool TryEnableDarkTitleBar(IntPtr windowHandle)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var enabled = 1;
        try
        {
            return NativeMethods.DwmSetWindowAttribute(
                windowHandle,
                NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref enabled,
                sizeof(int)) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// 把窗口标记为工具窗口：任务栏与 Alt+Tab 都不再把它当作独立程序展示。
    /// 小组件是桌面挂件，不应出现在程序切换列表里。
    /// </summary>
    public static bool TrySetToolWindow(IntPtr windowHandle)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var style = (long)NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GWL_EXSTYLE);
            if ((style & NativeMethods.WS_EX_TOOLWINDOW) != 0)
            {
                return true;
            }

            NativeMethods.SetWindowLongPtr(
                windowHandle,
                NativeMethods.GWL_EXSTYLE,
                (IntPtr)(style | NativeMethods.WS_EX_TOOLWINDOW));

            NativeMethods.SetWindowPos(
                windowHandle,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_FRAMECHANGED);

            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// 切换桌面小组件的鼠标点击穿透（WS_EX_TRANSPARENT）：开启后鼠标点击直接穿过窗口落到桌面或下层应用。
    /// 需从主窗口设置页或系统托盘菜单解除。
    /// </summary>
    public static bool TrySetClickThrough(IntPtr windowHandle, bool enabled)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var style = (long)NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GWL_EXSTYLE);
            var next = enabled
                ? style | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED
                : style & ~NativeMethods.WS_EX_TRANSPARENT;

            if (next != style)
            {
                NativeMethods.SetWindowLongPtr(
                    windowHandle,
                    NativeMethods.GWL_EXSTYLE,
                    (IntPtr)next);

                NativeMethods.SetWindowPos(
                    windowHandle,
                    IntPtr.Zero,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE |
                    NativeMethods.SWP_FRAMECHANGED);
            }

            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// 控制窗口是否在 Windows「显示桌面」（Win+D / 右下角按钮）时保持位于桌面宿主之上（ADR-0073）。
    /// </summary>
    public static bool TrySetKeepOnDesktop(IntPtr windowHandle, bool enabled) =>
        Win32.ShowDesktopGuard.SetEnabled(windowHandle, enabled);
}

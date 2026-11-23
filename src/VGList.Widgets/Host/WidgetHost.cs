using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using VGList.App.Settings;
using VGList.Platform;

namespace VGList.Widgets.Host;

/// <summary>设置文件中的槽位：各小组件独立记忆位置与尺寸。</summary>
public enum WidgetSlot
{
    Today,
    Month,
}

/// <summary>
/// 所有小组件共用的宿主行为：无边框拖动、自由缩放、层级、位置与尺寸记忆。
/// 只做窗口行为，不碰业务数据。
/// </summary>
public sealed class WidgetHost
{
    private readonly Window _window;
    private readonly SettingsStore _store;
    private readonly double _baseWidth;
    private readonly double _baseHeight;
    private readonly WidgetSlot _slot;
    private readonly WidgetSettings _settings;
    private readonly WidgetSettings? _sibling;
    private readonly double _siblingBaseWidth;
    private readonly bool _hasStoredPosition;
    private bool _applyingSize;

    public WidgetHost(
        Window window,
        SettingsStore store,
        double baseWidth,
        double baseHeight,
        WidgetSlot slot = WidgetSlot.Today,
        WidgetSettings? siblingSettings = null,
        double siblingBaseWidth = 0)
    {
        _window = window;
        _store = store;
        _baseWidth = baseWidth;
        _baseHeight = baseHeight;
        _slot = slot;
        _settings = slot == WidgetSlot.Month ? store.LoadMonth() : store.LoadWidget();
        _hasStoredPosition = _settings.X is not null && _settings.Y is not null;
        _sibling = siblingSettings;
        _siblingBaseWidth = siblingBaseWidth;
    }

    public WidgetSettings Settings => _settings;

    public bool IsLocked => _settings.IsLocked;

    public bool IsClickThrough => _settings.IsClickThrough;

    public void Attach()
    {
        ApplySize(_settings.Width ?? _baseWidth, _settings.Height ?? _baseHeight);

        if (_settings.X is { } x && _settings.Y is { } y)
        {
            _window.Position = new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
        }

        _window.Opened += OnOpened;
        _window.PositionChanged += OnPositionChanged;
        _window.SizeChanged += OnWindowSizeChanged;
        _window.Closing += OnClosing;
    }

    public void BeginDrag(PointerPressedEventArgs e)
    {
        if (_settings.IsLocked)
        {
            return;
        }

        if (e.GetCurrentPoint(_window).Properties.IsLeftButtonPressed)
        {
            _window.BeginMoveDrag(e);
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var handle = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        // 透明无边框小组件由 Avalonia 自身绘制圆角与细描边；禁用 DWM 8px 圆角框与系统描边，避免四角偏白残影。
        PlatformServices.WindowEffects.TryDisableDwmChrome(handle);

        // 小组件是桌面挂件：从任务栏与 Alt+Tab 隐藏，不占用「程序」位置。
        PlatformServices.WindowEffects.TrySetToolWindow(handle);

        if (_settings.IsClickThrough)
        {
            PlatformServices.WindowEffects.TrySetClickThrough(handle, true);
        }

        // 先摆位再挂到 WorkerW：窗口一旦成为壁纸层的子窗口，再设置 Position 就不生效了。
        // 首次运行没有记忆位置时，窗口会落在系统默认位置，必须显式摆到右上角。
        // 注意不能读 _settings.X/Y：窗口首次显示时 PositionChanged 会先把系统默认位置写进去。
        if (!_hasStoredPosition || !IsPositionOnAnyScreen(_window.Position))
        {
            PositionDefaultTopRight();
        }

        // P0 不挂 WorkerW：桌面图标窗口会拦掉所有点击，小组件就不可交互了（见 ADR-0024）。
        _window.Topmost = _settings.IsTopmost || _settings.Layer == WidgetLayer.AlwaysOnTop;
    }

    private void PositionDefaultTopRight()
    {
        var screen = _window.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var gap = (int)Math.Round(32 * scale);
        var pixelWidth = (int)Math.Round(_window.Width * scale);
        var x = area.X + area.Width - pixelWidth - gap;

        // 今日组件也在右上角时，月历改停在它左侧，间距 16，避免首次启动完全重叠。
        if (_sibling is not null && IsAtDefaultTopRight(_sibling, area, gap, scale))
        {
            x -= (int)Math.Round((_siblingBaseWidth + 16) * scale);
        }

        _window.Position = new PixelPoint(x, area.Y + gap);
    }

    private bool IsAtDefaultTopRight(WidgetSettings sibling, PixelRect area, int gap, double scale)
    {
        if (sibling.X is null || sibling.Y is null)
        {
            return true;
        }

        var defaultX = area.X + area.Width - (sibling.Width ?? _siblingBaseWidth) * scale - gap;
        var defaultY = area.Y + gap;
        var tolerance = 24 * scale;
        return Math.Abs(sibling.X.Value - defaultX) <= tolerance &&
               Math.Abs(sibling.Y.Value - defaultY) <= tolerance;
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        _settings.X = e.Point.X;
        _settings.Y = e.Point.Y;
    }

    /// <summary>自由缩放：记录窗口宽高，字体与版式不随窗口缩小而缩放。</summary>
    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_applyingSize)
        {
            return;
        }

        _settings.Width = Math.Round(_window.Bounds.Width);
        _settings.Height = Math.Round(_window.Bounds.Height);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e) => Save();

    public void SaveSettings() => Save();

    /// <summary>「置于顶层」开关。</summary>
    public void SetTopmost(bool value)
    {
        _settings.IsTopmost = value;
        _window.Topmost = value || _settings.Layer == WidgetLayer.AlwaysOnTop;
        Save();
    }

    /// <summary>「锁定位置与尺寸」开关：阻止误拖动与误缩放。</summary>
    public void SetLocked(bool value)
    {
        _settings.IsLocked = value;
        Save();
    }

    /// <summary>「鼠标点击穿透」开关：动态切换 Win32 WS_EX_TRANSPARENT。</summary>
    public void SetClickThrough(bool value)
    {
        _settings.IsClickThrough = value;
        var handle = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            PlatformServices.WindowEffects.TrySetClickThrough(handle, value);
        }

        Save();
    }

    /// <summary>恢复默认尺寸与工作区右上角默认位置。</summary>
    public void ResetToDefault()
    {
        ApplySize(_baseWidth, _baseHeight);
        PositionDefaultTopRight();
        _settings.Width = _baseWidth;
        _settings.Height = _baseHeight;
        _settings.X = _window.Position.X;
        _settings.Y = _window.Position.Y;
        Save();
    }

    private void Save()
    {
        if (_slot == WidgetSlot.Month)
        {
            _store.SaveMonth(_settings);
        }
        else
        {
            _store.SaveWidget(_settings);
        }
    }

    private void ApplySize(double width, double height)
    {
        _applyingSize = true;
        _window.Width = Math.Clamp(width, _window.MinWidth, _window.MaxWidth);
        _window.Height = Math.Clamp(height, _window.MinHeight, _window.MaxHeight);
        _applyingSize = false;
    }

    private bool IsPositionOnAnyScreen(PixelPoint position)
    {
        foreach (var screen in _window.Screens.All)
        {
            var area = screen.Bounds;
            if (position.X >= area.X &&
                position.X < area.X + area.Width &&
                position.Y >= area.Y &&
                position.Y < area.Y + area.Height)
            {
                return true;
            }
        }

        return false;
    }
}

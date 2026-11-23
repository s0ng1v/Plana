namespace VGList.App.Settings;

/// <summary>小组件与桌面的层级关系。WorkerW 挂载失败时降级为始终置顶。</summary>
public enum WidgetLayer
{
    Desktop,
    AlwaysOnTop,
    Normal,
}

/// <summary>小组件窗口的持久化设置。位置为屏幕坐标，宽高为逻辑像素。</summary>
public sealed class WidgetSettings
{
    public double? X { get; set; }

    public double? Y { get; set; }

    /// <summary>窗口宽度；为空时使用默认 331。</summary>
    public double? Width { get; set; }

    /// <summary>窗口高度；为空时使用默认 454。</summary>
    public double? Height { get; set; }

    public WidgetLayer Layer { get; set; } = WidgetLayer.Desktop;

    /// <summary>整体不透明度 0.4–1.0。</summary>
    public double Opacity { get; set; } = 1.0;

    public bool IsTopmost { get; set; }

    public bool ShowInput { get; set; } = true;

    public bool ShowCompleted { get; set; } = true;

    public bool ShowLunar { get; set; } = true;

    /// <summary>P1：锁定小组件位置与尺寸，防止误拖动与误缩放。</summary>
    public bool IsLocked { get; set; }

    /// <summary>P1：小组件鼠标点击穿透（WS_EX_TRANSPARENT），可在设置页或托盘菜单随时解除。</summary>
    public bool IsClickThrough { get; set; }

    public WidgetSettings Clone() => new()
    {
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        Layer = Layer,
        Opacity = Opacity,
        IsTopmost = IsTopmost,
        ShowInput = ShowInput,
        ShowCompleted = ShowCompleted,
        ShowLunar = ShowLunar,
        IsLocked = IsLocked,
        IsClickThrough = IsClickThrough,
    };
}

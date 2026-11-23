using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using VGList.Desktop.Services;

namespace VGList.Desktop.Services;

/// <summary>
/// 通用「出现即渐入」附加属性：
/// 任意控件挂载 RevealMotion.OffsetY 后，在首次附着或 IsVisible 从 False 变为 True 时，
/// 自动播放短而顺滑的透明淡入 + 轻微位移过渡（严格遵守 MotionHelper.ReduceMotion）。
/// </summary>
public static class RevealMotion
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(RevealMotion));

    public static readonly AttachedProperty<double> OffsetYProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("OffsetY", typeof(RevealMotion), defaultValue: 6.0);

    public static readonly AttachedProperty<int> DurationMsProperty =
        AvaloniaProperty.RegisterAttached<Control, int>("DurationMs", typeof(RevealMotion), defaultValue: 180);

    static RevealMotion()
    {
        EnabledProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.NewValue is true)
            {
                control.AttachedToVisualTree += OnAttached;
                control.PropertyChanged += OnControlPropertyChanged;
            }
            else
            {
                control.AttachedToVisualTree -= OnAttached;
                control.PropertyChanged -= OnControlPropertyChanged;
            }
        });
    }

    public static bool GetEnabled(Control element) => element.GetValue(EnabledProperty);
    public static void SetEnabled(Control element, bool value) => element.SetValue(EnabledProperty, value);

    public static double GetOffsetY(Control element) => element.GetValue(OffsetYProperty);
    public static void SetOffsetY(Control element, double value) => element.SetValue(OffsetYProperty, value);

    public static int GetDurationMs(Control element) => element.GetValue(DurationMsProperty);
    public static void SetDurationMs(Control element, int value) => element.SetValue(DurationMsProperty, value);

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control { IsVisible: true } control)
        {
            Play(control);
        }
    }

    private static void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty &&
            e.NewValue is true &&
            sender is Control control &&
            control.IsAttachedToVisualTree())
        {
            Play(control);
        }
    }

    private static void Play(Control control) =>
        MotionHelper.PlayEnter(control, offsetX: 0, offsetY: GetOffsetY(control), durationMs: GetDurationMs(control));
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using VGList.App.Settings;

namespace VGList.Widgets.Today;

/// <summary>独立的设置窗口：实色面板、可拖动，改动即时回传给小组件。</summary>
public partial class WidgetSettingsWindow : Window
{
    private const double OpacityMin = 40;
    private const double OpacityMax = 100;
    private const double KnobSize = 11;

    private readonly Border _opacitySlider;
    private readonly Border _opacityFill;
    private readonly Ellipse _opacityKnob;
    private readonly Border _showInputRow;
    private readonly Border _showLunarRow;
    private readonly CheckBox _topmostToggle;
    private readonly CheckBox _showInputToggle;
    private readonly CheckBox _showLunarToggle;
    private readonly CheckBox _showCompletedToggle;
    private bool _loading;
    private bool _sliderDragging;
    private double _opacityValue = 100;

    public WidgetSettingsWindow(WidgetSettings settings, bool isMonthWidget = false)
    {
        InitializeComponent();

        _opacitySlider = Find<Border>("OpacitySlider");
        _opacityFill = Find<Border>("OpacityFill");
        _opacityKnob = Find<Ellipse>("OpacityKnob");
        _showInputRow = Find<Border>("ShowInputRow");
        _showLunarRow = Find<Border>("ShowLunarRow");
        _topmostToggle = Find<CheckBox>("TopmostToggle");
        _showInputToggle = Find<CheckBox>("ShowInputToggle");
        _showLunarToggle = Find<CheckBox>("ShowLunarToggle");
        _showCompletedToggle = Find<CheckBox>("ShowCompletedToggle");

        _showInputRow.IsVisible = !isMonthWidget;
        _showLunarRow.IsVisible = isMonthWidget;

        _loading = true;

        var opacity = Math.Clamp(settings.Opacity, 0.4, 1.0) * 100;
        SetOpacity(opacity);
        _topmostToggle.IsChecked = settings.IsTopmost;
        _showInputToggle.IsChecked = settings.ShowInput;
        _showLunarToggle.IsChecked = settings.ShowLunar;
        _showCompletedToggle.IsChecked = settings.ShowCompleted;

        _loading = false;

        // 首次布局后才知道轨道宽度，此时再摆放小球。
        _opacitySlider.SizeChanged += (_, _) => SetOpacity(_opacityValue);

        _topmostToggle.PropertyChanged += (_, e) => RaiseToggle(e, ToggleButton.IsCheckedProperty, TopmostChanged);
        _showInputToggle.PropertyChanged += (_, e) => RaiseToggle(e, ToggleButton.IsCheckedProperty, ShowInputChanged);
        _showLunarToggle.PropertyChanged += (_, e) => RaiseToggle(e, ToggleButton.IsCheckedProperty, ShowLunarChanged);
        _showCompletedToggle.PropertyChanged += (_, e) => RaiseToggle(e, ToggleButton.IsCheckedProperty, ShowCompletedChanged);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    public event EventHandler<double>? OpacityChanged;

    public event EventHandler? OpacityCommitted;

    public event EventHandler<bool>? TopmostChanged;

    public event EventHandler<bool>? ShowInputChanged;

    public event EventHandler<bool>? ShowLunarChanged;

    public event EventHandler<bool>? ShowCompletedChanged;

    private void RaiseToggle(AvaloniaPropertyChangedEventArgs e, AvaloniaProperty property, EventHandler<bool>? handler)
    {
        if (_loading || e.Property != property)
        {
            return;
        }

        handler?.Invoke(this, (bool)(e.NewValue ?? false));
    }

    private T Find<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new InvalidOperationException($"{name} 未在 XAML 中定义。");

    private void OnDragAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnClosePressed(object? sender, PointerPressedEventArgs e)
    {
        Close();
        e.Handled = true;
    }

    // ---------- 不透明度滑块 ----------

    private void OnOpacitySliderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_opacitySlider).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _sliderDragging = true;
        ApplyOpacityFromPointer(e.GetPosition(_opacitySlider).X);
        e.Handled = true;
    }

    private void OnOpacitySliderMoved(object? sender, PointerEventArgs e)
    {
        if (!_sliderDragging)
        {
            return;
        }

        ApplyOpacityFromPointer(e.GetPosition(_opacitySlider).X);
        e.Handled = true;
    }

    private void OnOpacitySliderReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_sliderDragging)
        {
            return;
        }

        _sliderDragging = false;
        OpacityCommitted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    /// <summary>按指针位置换算不透明度并更新轨道与小球。</summary>
    private void ApplyOpacityFromPointer(double x)
    {
        var width = _opacitySlider.Bounds.Width;
        if (width <= 0)
        {
            return;
        }

        var travel = Math.Max(1, width - KnobSize);
        var ratio = Math.Clamp((x - KnobSize / 2) / travel, 0, 1);
        SetOpacity(OpacityMin + ratio * (OpacityMax - OpacityMin));
    }

    private void SetOpacity(double value)
    {
        _opacityValue = value;

        var width = _opacitySlider.Bounds.Width;
        if (width <= 0)
        {
            return;
        }

        var travel = Math.Max(1, width - KnobSize);
        var ratio = Math.Clamp((value - OpacityMin) / (OpacityMax - OpacityMin), 0, 1);
        var offset = ratio * travel;

        _opacityFill.Width = offset + KnobSize / 2;
        _opacityKnob.Margin = new Thickness(offset, 0, 0, 0);

        if (_loading)
        {
            return;
        }

        OpacityChanged?.Invoke(this, Math.Clamp(value / 100.0, 0.4, 1.0));
    }
}

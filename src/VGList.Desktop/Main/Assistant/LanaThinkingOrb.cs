using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VGList.Desktop.Services;

namespace VGList.Desktop.Main.Assistant;

/// <summary>
/// Lana 纯文字柔和粉紫流光思考态控件（ADR-0048）：
/// - 低饱和莫兰迪樱粉 ↔ 柔雾薰紫 ↔ 珍珠微光丝滑循环流动，温和护眼不刺眼；
/// - 使用 Font.Display（Anthropic Serif / Noto Serif SC 思源宋体）；
/// - 仅在可见、挂树且未开启「减少动效」时运行 60fps 计时器，隐藏或卸载立即停表零常驻开销。
/// </summary>
public sealed class LanaThinkingOrb : Control
{
    // 低饱和柔和同色系粉紫丝绸调色板（饱和度 25%–36%，白底耐看柔和）
    private static readonly Color[] Palette =
    [
        Color.Parse("#FFC2829F"), // 柔雾樱粉
        Color.Parse("#FFA684BC"), // 浅丁香紫
        Color.Parse("#FF968AC4"), // 柔雾薰衣草
        Color.Parse("#FFD1A2BC"), // 珍珠粉光晕
    ];

    private static readonly string[] ThinkingPhrases =
    [
        "Lana 正在思考…",
        "让 Lana 想想…",
        "Lana 正在整理思绪…",
        "Lana 琢磨一下哦…",
        "稍等一下，Lana 正在构思…",
        "Lana 正在理清头绪…",
    ];

    private static readonly string[] AfterToolPhrases =
    [
        "Lana 正在整理结果…",
        "快好啦，让 Lana 组织一下回复…",
        "信息都拿到啦，Lana 正在整理…",
    ];

    private static int _phraseCursor;

    public static readonly StyledProperty<string> StatusTextProperty =
        AvaloniaProperty.Register<LanaThinkingOrb, string>(nameof(StatusText), "Lana 正在思考…");

    public static readonly StyledProperty<double> TextFontSizeProperty =
        AvaloniaProperty.Register<LanaThinkingOrb, double>(nameof(TextFontSize), 13.5);

    private DispatcherTimer? _timer;
    private long _startTickMs;
    private double _phase;

    static LanaThinkingOrb()
    {
        AffectsRender<LanaThinkingOrb>(StatusTextProperty, TextFontSizeProperty);
        AffectsMeasure<LanaThinkingOrb>(StatusTextProperty, TextFontSizeProperty);
    }

    public LanaThinkingOrb()
    {
        Height = 24;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        IsHitTestVisible = false;

        AttachedToVisualTree += (_, _) => UpdateTimerState();
        DetachedFromVisualTree += (_, _) => StopTimer();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                UpdateTimerState();
            }
        };
    }

    public string StatusText
    {
        get => GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public double TextFontSize
    {
        get => GetValue(TextFontSizeProperty);
        set => SetValue(TextFontSizeProperty, value);
    }

    public static string PickThinkingPhrase()
    {
        var idx = Interlocked.Increment(ref _phraseCursor);
        return ThinkingPhrases[Math.Abs(idx) % ThinkingPhrases.Length];
    }

    public static string PickAfterToolPhrase()
    {
        var idx = Interlocked.Increment(ref _phraseCursor);
        return AfterToolPhrases[Math.Abs(idx) % AfterToolPhrases.Length];
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var ft = CreateFormattedText(StatusText, Brushes.Gray);
        var width = Math.Ceiling(ft.Width) + 4;
        return new Size(Math.Min(width, double.IsInfinity(availableSize.Width) ? width : availableSize.Width), Math.Max(24, Math.Ceiling(ft.Height) + 4));
    }

    public override void Render(DrawingContext context)
    {
        // 舒缓丝滑的粉紫循环流光（步调从容、低饱和柔光，不闪烁不刺眼）
        var shift = MotionHelper.ReduceMotion ? 0.0 : (_phase * 0.24) % 1.0;

        var stops = new GradientStops();
        const int sampleCount = 7;
        for (var i = 0; i < sampleCount; i++)
        {
            var pos = i / (double)(sampleCount - 1);
            // 宽波长（0.55）确保整句文字色调和谐连贯，避免两字一变色的花哨感
            var sampleT = (pos * 0.55 - shift) % 1.0;
            if (sampleT < 0) sampleT += 1.0;
            stops.Add(new GradientStop(SamplePalette(sampleT), pos));
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = stops,
        };

        var ft = CreateFormattedText(StatusText, brush);
        var y = Math.Max(0, (Bounds.Height - ft.Height) * 0.5);
        context.DrawText(ft, new Point(0, y));
    }

    private FormattedText CreateFormattedText(string text, IBrush brush)
    {
        var fontFamily = this.TryFindResource("Font.Display", ActualThemeVariant, out var res) && res is FontFamily ff
            ? ff
            : FontFamily.Default;
        var typeface = new Typeface(fontFamily, FontStyle.Normal, FontWeight.Medium);
        return new FormattedText(
            string.IsNullOrEmpty(text) ? "Lana 正在思考…" : text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            TextFontSize,
            brush);
    }

    private static Color SamplePalette(double t)
    {
        var scaled = t * Palette.Length;
        var idx = (int)Math.Floor(scaled) % Palette.Length;
        var next = (idx + 1) % Palette.Length;
        var frac = scaled - Math.Floor(scaled);
        var smooth = 0.5 - 0.5 * Math.Cos(frac * Math.PI);
        var a = Palette[idx];
        var b = Palette[next];
        return Color.FromArgb(
            255,
            (byte)(a.R + (b.R - a.R) * smooth),
            (byte)(a.G + (b.G - a.G) * smooth),
            (byte)(a.B + (b.B - a.B) * smooth));
    }

    private void UpdateTimerState()
    {
        if (IsVisible && this.IsAttachedToVisualTree() && !MotionHelper.ReduceMotion)
        {
            _startTickMs = Environment.TickCount64;
            _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnTick);
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
        }
        else
        {
            StopTimer();
            InvalidateVisual();
        }
    }

    private void StopTimer() => _timer?.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        if (!IsVisible || !this.IsAttachedToVisualTree() || MotionHelper.ReduceMotion)
        {
            StopTimer();
            return;
        }

        _phase = (Environment.TickCount64 - _startTickMs) / 1000.0;
        InvalidateVisual();
    }
}

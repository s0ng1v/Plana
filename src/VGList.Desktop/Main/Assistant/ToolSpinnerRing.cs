using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VGList.Desktop.Services;

namespace VGList.Desktop.Main.Assistant;

/// <summary>
/// 轻量矢量转圈指示器（ADR-0058）：
/// 用于助理工具调用执行期间在胶囊左侧平滑旋转；
/// 仅在可见、已挂树且未开启「减少动效」时运行 60fps 计时器，完成或隐藏后立即停表零常驻开销。
/// </summary>
public sealed class ToolSpinnerRing : Control
{
    private DispatcherTimer? _timer;
    private long _startTickMs;
    private double _angleDeg;

    public ToolSpinnerRing()
    {
        Width = 13;
        Height = 13;
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

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width > 0 ? Bounds.Width : 13;
        var h = Bounds.Height > 0 ? Bounds.Height : 13;
        var size = Math.Min(w, h);
        var stroke = 1.6;
        var radius = Math.Max(2.0, (size - stroke) * 0.5);
        var center = new Point(w * 0.5, h * 0.5);

        var trackColor = ResolveColor("Main.Accent.Border", Color.FromRgb(235, 198, 212));
        var accentBrush = ResolveBrush("Main.Accent") ?? Brushes.DeepPink;

        // 柔和底圈轨道
        var trackPen = new Pen(new SolidColorBrush(Color.FromArgb(115, trackColor.R, trackColor.G, trackColor.B)), stroke);
        context.DrawEllipse(null, trackPen, center, radius, radius);

        // 225° 旋转弧线（带圆头端点）
        var startDeg = MotionHelper.ReduceMotion ? -90.0 : (_angleDeg - 90.0);
        const double sweepDeg = 225.0;
        var startRad = startDeg * Math.PI / 180.0;
        var endRad = (startDeg + sweepDeg) * Math.PI / 180.0;

        var startPt = new Point(
            center.X + radius * Math.Cos(startRad),
            center.Y + radius * Math.Sin(startRad));
        var endPt = new Point(
            center.X + radius * Math.Cos(endRad),
            center.Y + radius * Math.Sin(endRad));

        var geo = new StreamGeometry();
        using (var gctx = geo.Open())
        {
            gctx.BeginFigure(startPt, isFilled: false);
            gctx.ArcTo(
                endPt,
                new Size(radius, radius),
                rotationAngle: 0,
                isLargeArc: true,
                SweepDirection.Clockwise);
            gctx.EndFigure(isClosed: false);
        }

        var arcPen = new Pen(accentBrush, stroke, lineCap: PenLineCap.Round);
        context.DrawGeometry(null, arcPen, geo);
    }

    private void UpdateTimerState()
    {
        if (IsVisible && this.IsAttachedToVisualTree() && !MotionHelper.ReduceMotion)
        {
            StartTimer();
        }
        else
        {
            StopTimer();
        }
    }

    private void StartTimer()
    {
        if (_timer is not null)
        {
            return;
        }

        _startTickMs = Environment.TickCount64;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnTick);
        _timer.Start();
    }

    private void StopTimer()
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Stop();
        _timer = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!IsVisible || !this.IsAttachedToVisualTree() || MotionHelper.ReduceMotion)
        {
            StopTimer();
            return;
        }

        var elapsedSec = (Environment.TickCount64 - _startTickMs) / 1000.0;
        // 每秒旋转约 1.25 圈（450°/s），柔和流畅不晕眼
        _angleDeg = (elapsedSec * 450.0) % 360.0;
        InvalidateVisual();
    }

    private IBrush? ResolveBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var val) ? val as IBrush : null;

    private Color ResolveColor(string key, Color fallback) =>
        ResolveBrush(key) is ISolidColorBrush solid ? solid.Color : fallback;
}

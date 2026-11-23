using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VGList.Widgets.Support;

/// <summary>子待办完成度饼状图小图标（空心圆环 + 顺时针实心进度扇形）。</summary>
public sealed class SubtaskPieControl : Control
{
    public static readonly StyledProperty<int> CompletedCountProperty =
        AvaloniaProperty.Register<SubtaskPieControl, int>(nameof(CompletedCount));

    public static readonly StyledProperty<int> TotalCountProperty =
        AvaloniaProperty.Register<SubtaskPieControl, int>(nameof(TotalCount));

    public static readonly StyledProperty<IBrush?> RingBrushProperty =
        AvaloniaProperty.Register<SubtaskPieControl, IBrush?>(nameof(RingBrush));

    public static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<SubtaskPieControl, IBrush?>(nameof(FillBrush));

    private static readonly IBrush DefaultBrush = new SolidColorBrush(Color.Parse("#8A9099"));

    static SubtaskPieControl()
    {
        AffectsRender<SubtaskPieControl>(CompletedCountProperty, TotalCountProperty, RingBrushProperty, FillBrushProperty);
    }

    public SubtaskPieControl()
    {
        Width = 13;
        Height = 13;
        IsHitTestVisible = false;
    }

    public int CompletedCount
    {
        get => GetValue(CompletedCountProperty);
        set => SetValue(CompletedCountProperty, value);
    }

    public int TotalCount
    {
        get => GetValue(TotalCountProperty);
        set => SetValue(TotalCountProperty, value);
    }

    public IBrush? RingBrush
    {
        get => GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    public IBrush? FillBrush
    {
        get => GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (TotalCount <= 0)
        {
            return;
        }

        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 2)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2.0, Bounds.Height / 2.0);
        var radius = Math.Max(2.0, size / 2.0 - 1.1);
        var strokeBrush = RingBrush ?? DefaultBrush;
        var fillBrush = FillBrush ?? strokeBrush;
        var ratio = Math.Clamp((double)CompletedCount / TotalCount, 0.0, 1.0);

        context.DrawEllipse(null, new Pen(strokeBrush, 1.15), center, radius, radius);

        if (ratio >= 0.999)
        {
            context.DrawEllipse(fillBrush, null, center, radius, radius);
            return;
        }

        if (ratio <= 0.001)
        {
            return;
        }

        var sectorRadius = Math.Max(1.0, radius - 0.15);
        var startPoint = new Point(center.X, center.Y - sectorRadius);
        var endAngle = -Math.PI / 2.0 + ratio * 2.0 * Math.PI;
        var endPoint = new Point(
            center.X + sectorRadius * Math.Cos(endAngle),
            center.Y + sectorRadius * Math.Sin(endAngle));

        var geometry = new StreamGeometry();
        using (var gc = geometry.Open())
        {
            gc.BeginFigure(center, isFilled: true);
            gc.LineTo(startPoint);
            gc.ArcTo(
                endPoint,
                new Size(sectorRadius, sectorRadius),
                0,
                isLargeArc: ratio > 0.5,
                SweepDirection.Clockwise);
            gc.EndFigure(isClosed: true);
        }

        context.DrawGeometry(fillBrush, null, geometry);
    }
}

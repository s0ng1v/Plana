using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace VGList.Desktop.Services;

/// <summary>主窗口统一动效工具，使用合成属性过渡并自动尊重「减少动效」开关。</summary>
public static class MotionHelper
{
    private sealed class AnimationState
    {
        public int Token;
    }

    private static readonly ConditionalWeakTable<Visual, AnimationState> States = new();

    private static bool _reduceMotion;

    public static bool ReduceMotion
    {
        get => _reduceMotion;
        set
        {
            if (_reduceMotion == value)
            {
                return;
            }

            _reduceMotion = value;
            ReducedMotionChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>「减少动效」开关变化时通知自绘动效控件立即停表或恢复。</summary>
    public static event EventHandler? ReducedMotionChanged;

    public static void PlayEnter(Visual control, double offsetX = 0, double offsetY = 8, int durationMs = 180)
    {
        var state = States.GetOrCreateValue(control);
        var token = ++state.Token;

        if (ReduceMotion)
        {
            control.Transitions = null;
            control.Opacity = 1;
            control.RenderTransform = null;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(durationMs);

        control.Transitions = null;
        control.Opacity = 0;
        control.RenderTransform = TransformOperations.Parse($"translate({offsetX:0.##}px, {offsetY:0.##}px)");

        Dispatcher.UIThread.Post(
            () =>
            {
                if (state.Token != token)
                {
                    return;
                }

                control.Transitions =
                [
                    new DoubleTransition
                    {
                        Property = Visual.OpacityProperty,
                        Duration = duration,
                        Easing = new CubicEaseOut(),
                    },
                    new TransformOperationsTransition
                    {
                        Property = Visual.RenderTransformProperty,
                        Duration = duration,
                        Easing = new CubicEaseOut(),
                    },
                ];
                control.Opacity = 1;
                control.RenderTransform = TransformOperations.Parse("translate(0px, 0px)");
            },
            DispatcherPriority.Render);
    }
}

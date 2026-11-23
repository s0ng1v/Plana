using Avalonia;

namespace VGList.Desktop.Main.Assistant;

/// <summary>对话页活体 Lana 的飞行转场类型。</summary>
public enum LanaFlightKind
{
    /// <summary>即时贴合锚点（历史会话切换、滚动、缩放或开启减少动效）。</summary>
    None,

    /// <summary>首轮开聊：从居中欢迎主视觉（108×108）丝滑飞向首条助理回复左侧（42×42）。</summary>
    FirstTurn,

    /// <summary>多轮接续：从上一条助理回复左侧平滑滑落到最新助理回复左侧（42×42）。</summary>
    NextTurn,

    /// <summary>新建对话：从回复左侧轻盈浮回居中欢迎主视觉（108×108）。</summary>
    ReturnHome,
}

/// <summary>对话页活体 Lana 锚点变更事件参数。</summary>
public sealed class LanaStageTransitionEventArgs(LanaFlightKind kind, ChatMessageViewModel? targetAssistantMessage) : EventArgs
{
    public LanaFlightKind Kind { get; } = kind;
    public ChatMessageViewModel? TargetAssistantMessage { get; } = targetAssistantMessage;
}

/// <summary>
/// 对话页唯一活体 Lana 舞台转场的纯函数运动学引擎（可完整单测）：
/// - 复刻参考项目 `FIRST_TURN_MOVE_EASING = cubic-bezier(0.77, 0, 0.175, 1)` 520ms 首轮丝滑曲线；
/// - 多轮滑落沿左侧槽位垂直柔和下落并自动夹紧超视口起点，避免跨屏高速穿行；
/// - 滚出视口判定用于自动暂停渲染以保持低能耗。
/// </summary>
public static class LanaStageMotion
{
    public const double HeroSize = 108.0;
    public const double MessageAvatarSize = 42.0;

    public const double FirstTurnDurationMs = 520.0;
    public const double NextTurnDurationMs = 440.0;
    public const double ReturnHomeDurationMs = 420.0;

    public static double GetDurationMs(LanaFlightKind kind) => kind switch
    {
        LanaFlightKind.FirstTurn => FirstTurnDurationMs,
        LanaFlightKind.NextTurn => NextTurnDurationMs,
        LanaFlightKind.ReturnHome => ReturnHomeDurationMs,
        _ => 0.0,
    };

    public static double GetLandingBounceImpulse(LanaFlightKind kind) => kind switch
    {
        LanaFlightKind.FirstTurn => -1.15,
        LanaFlightKind.NextTurn => -1.05,
        LanaFlightKind.ReturnHome => -0.85,
        _ => 0.0,
    };

    public static double EvaluateEase(LanaFlightKind kind, double t)
    {
        var clamped = Math.Clamp(t, 0.0, 1.0);
        return kind switch
        {
            LanaFlightKind.FirstTurn => EvaluateCubicBezier(clamped, 0.77, 0.0, 0.175, 1.0),
            LanaFlightKind.NextTurn => EvaluateCubicBezier(clamped, 0.65, 0.0, 0.25, 1.0),
            LanaFlightKind.ReturnHome => EvaluateCubicBezier(clamped, 0.22, 1.0, 0.36, 1.0),
            _ => clamped,
        };
    }

    /// <summary>
    /// 当上一轮回复因长对话已滚出可视区域时，将飞行起点纵坐标柔和夹紧到视口边缘外侧，
    /// 使活体 Lana 从边缘自然滑入而不是超高速横穿整屏。
    /// </summary>
    public static Rect ClampFlightOriginToViewport(Rect origin, double stageHeight)
    {
        if (stageHeight <= 0)
        {
            return origin;
        }

        const double topEdgeBuffer = 56.0;
        const double bottomEdgeBuffer = 16.0;
        var clampedY = Math.Clamp(origin.Y, -topEdgeBuffer, stageHeight + bottomEdgeBuffer);
        return new Rect(origin.X, clampedY, origin.Width, origin.Height);
    }

    /// <summary>按当前帧进度与实时目标锚点计算活体 Lana 的插值矩形（支持飞行途中目标随滚动动态修正）。</summary>
    public static Rect InterpolateFlightRect(Rect from, Rect to, double normalizedTime, LanaFlightKind kind)
    {
        if (kind == LanaFlightKind.None)
        {
            return to;
        }

        var t = Math.Clamp(normalizedTime, 0.0, 1.0);
        if (t <= 0.0)
        {
            return from;
        }

        if (t >= 1.0)
        {
            return to;
        }

        var p = EvaluateEase(kind, t);
        var x = from.X + (to.X - from.X) * p;
        var y = from.Y + (to.Y - from.Y) * p;
        var w = from.Width + (to.Width - from.Width) * p;
        var h = from.Height + (to.Height - from.Height) * p;

        // 柔和浮空微弧：首轮/返航有轻盈上浮感，多轮滑落仅向左微偏（绝不侵入右侧消息正文区）
        var arc = Math.Sin(p * Math.PI);
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);

        switch (kind)
        {
            case LanaFlightKind.FirstTurn:
                y -= arc * Math.Min(16.0, dist * 0.045);
                break;
            case LanaFlightKind.NextTurn:
                x -= arc * Math.Min(5.0, Math.Abs(dy) * 0.02);
                break;
            case LanaFlightKind.ReturnHome:
                y -= arc * Math.Min(14.0, dist * 0.04);
                break;
        }

        return new Rect(x, y, w, h);
    }

    public static bool IsRectInViewport(Rect rect, double stageHeight, double margin = 24.0) =>
        stageHeight > 0 && rect.Bottom >= -margin && rect.Top <= stageHeight + margin;

    /// <summary>标准 CSS cubic-bezier(x1, y1, x2, y2) 求值器（牛顿迭代 + 二分兜底，精度 &lt; 1e-6）。</summary>
    public static double EvaluateCubicBezier(double x, double x1, double y1, double x2, double y2)
    {
        if (x <= 0.0) return 0.0;
        if (x >= 1.0) return 1.0;

        static double Sample(double u, double p1, double p2)
        {
            var inv = 1.0 - u;
            return 3.0 * inv * inv * u * p1 + 3.0 * inv * u * u * p2 + u * u * u;
        }

        static double SampleDerivative(double u, double p1, double p2)
        {
            var inv = 1.0 - u;
            return 3.0 * inv * inv * p1 + 6.0 * inv * u * (p2 - p1) + 3.0 * u * u * (1.0 - p2);
        }

        var u = x;
        for (var i = 0; i < 6; i++)
        {
            var err = Sample(u, x1, x2) - x;
            if (Math.Abs(err) < 1e-6)
            {
                return Sample(u, y1, y2);
            }

            var d = SampleDerivative(u, x1, x2);
            if (Math.Abs(d) < 1e-6)
            {
                break;
            }

            u = Math.Clamp(u - err / d, 0.0, 1.0);
        }

        double lo = 0.0, hi = 1.0;
        u = x;
        for (var i = 0; i < 16; i++)
        {
            var val = Sample(u, x1, x2);
            if (Math.Abs(val - x) < 1e-6)
            {
                break;
            }

            if (val < x) lo = u;
            else hi = u;
            u = 0.5 * (lo + hi);
        }

        return Sample(u, y1, y2);
    }
}

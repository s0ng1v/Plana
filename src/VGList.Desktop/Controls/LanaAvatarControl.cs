using System;
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using VGList.Desktop.Services;

namespace VGList.Desktop.Controls;

/// <summary>
/// Lana 视觉状态枚举（可放在 Plana.Core 或 Plana.App 中以保持与 Agent 程序集完全解耦）。
/// </summary>
public enum LanaVisualState
{
    /// <summary>非 Lana 页面：安静休息闭眼，低饱和低频省电。</summary>
    Sleep,
    /// <summary>打开 Lana 页面：唤醒待机，自然眨眼与轻柔呼吸。</summary>
    Idle,
    /// <summary>点击输入框聚焦：微前倾专注 + 边界内柔光脉冲。</summary>
    Listening,
    /// <summary>流式回复/Agent 规划中：本体微缩至 0.84x + 边界内流光拖尾环绕。</summary>
    Thinking,
    /// <summary>回复完成：轻快双跳庆祝，约 2 秒后平滑回落至 Idle。</summary>
    Happy
}

/// <summary>
/// Lana 渲染模式：侧栏完整动效 vs. 对话列表零开销静态缓存小头像。
/// </summary>
public enum LanaRenderMode
{
    /// <summary>完整参数化实时动效（支持悬停反馈、状态连续形变、自适应降频）。</summary>
    Interactive,
    /// <summary>静态缓存小头像模式（零动画循环、全局 SKImage 位图缓存，适合数百条消息列表高速滚动）。</summary>
    StaticAvatar
}

/// <summary>
/// 纯 SkiaSharp 参数化渲染的 Lana 助手控件（适用于 .NET 10 + Avalonia 12.1.6）。
/// 不依赖任何 Agent 程序集，可安全放置于 Desktop UI 层，完全保持懒加载。
/// </summary>
public sealed class LanaAvatarControl : Control
{
    public static readonly StyledProperty<LanaVisualState> StateProperty =
        AvaloniaProperty.Register<LanaAvatarControl, LanaVisualState>(nameof(State), LanaVisualState.Sleep);

    public static readonly StyledProperty<LanaRenderMode> RenderModeProperty =
        AvaloniaProperty.Register<LanaAvatarControl, LanaRenderMode>(nameof(RenderMode), LanaRenderMode.Interactive);

    /// <summary>
    /// 是否启用悬停短反馈（Sleep 时触发浅醒探头 WakePeek；Idle 时触发单眼 WinkGreet；工作态不打断）。
    /// </summary>
    public static readonly StyledProperty<bool> EnableHoverFeedbackProperty =
        AvaloniaProperty.Register<LanaAvatarControl, bool>(nameof(EnableHoverFeedback), true);

    /// <summary>
    /// 进入 Happy 状态后是否在 2 秒后自动平滑回落至 Idle。
    /// </summary>
    public static readonly StyledProperty<bool> AutoReturnFromHappyProperty =
        AvaloniaProperty.Register<LanaAvatarControl, bool>(nameof(AutoReturnFromHappy), true);

    /// <summary>
    /// 是否在顶层窗口范围内追踪鼠标视线（默认关闭；仅对话舞台 Lana 启用，侧边栏 Lana 关闭；Sleep / 不可见 / 减少动效时零开销）。
    /// </summary>
    public static readonly StyledProperty<bool> TrackWindowPointerProperty =
        AvaloniaProperty.Register<LanaAvatarControl, bool>(nameof(TrackWindowPointer), false);

    public LanaVisualState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public LanaRenderMode RenderMode
    {
        get => GetValue(RenderModeProperty);
        set => SetValue(RenderModeProperty, value);
    }

    public bool EnableHoverFeedback
    {
        get => GetValue(EnableHoverFeedbackProperty);
        set => SetValue(EnableHoverFeedbackProperty, value);
    }

    public bool AutoReturnFromHappy
    {
        get => GetValue(AutoReturnFromHappyProperty);
        set => SetValue(AutoReturnFromHappyProperty, value);
    }

    public bool TrackWindowPointer
    {
        get => GetValue(TrackWindowPointerProperty);
        set => SetValue(TrackWindowPointerProperty, value);
    }

    /// <summary>
    /// 当 Happy 2 秒双跳完成并自动切回 Idle 时触发，方便 ViewModel 同步状态。
    /// </summary>
    public event EventHandler? HappyCompleted;

    // 静态小头像全局位图缓存：key = (状态, 物理像素尺寸)
    private static readonly ConcurrentDictionary<(LanaVisualState State, int PixelSize), SKImage> StaticCache = new();
    private const int MaxStaticCacheEntries = 24;

    private TopLevel? _topLevel;
    private bool _isAttached;
    private bool _frameRequested;
    private bool _isSleepConverged;
    private TimeSpan _lastFrameTime;
    private double _elapsedSeconds;
    private double _lastDrawTimestamp;

    // 连续参数化骨骼状态（单套参数插值，彻底消除状态切换突变）
    private RigState _rig = RigState.Sleep;
    private double _floatPhase;
    private double _breathePhase;
    private double _orbitAngle;

    private double _lookX;
    private double _lookY;
    private double _targetLookX;
    private double _targetLookY;

    private double _squash;
    private double _squashVel;
    private double _blinkTimer = 2.4;
    private double _blinkPhase;

    private double _happyRemaining;
    private double _happyElapsed;
    private bool _isPointerHovered;

    static LanaAvatarControl()
    {
        AffectsRender<LanaAvatarControl>(StateProperty, RenderModeProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StateProperty)
        {
            var next = change.GetNewValue<LanaVisualState>();
            _isSleepConverged = false;
            if (next == LanaVisualState.Happy)
            {
                _happyRemaining = 2.0;
                _happyElapsed = 0.0;
                _squashVel = -1.6;
            }
            else
            {
                _happyRemaining = 0.0;
                _squashVel = 0.0;
            }

            if (CanRunAnimationLoop)
                EnsureAnimationLoop();
            else
                InvalidateVisual();
        }
        else if (change.Property == RenderModeProperty)
        {
            _isSleepConverged = false;
            if (CanRunAnimationLoop)
                EnsureAnimationLoop();
            else
                InvalidateVisual();
        }
        else if (change.Property == IsVisibleProperty)
        {
            if (IsVisible)
            {
                _lastFrameTime = TimeSpan.Zero;
                if (CanRunAnimationLoop)
                    EnsureAnimationLoop();
                else
                    InvalidateVisual();
            }
            else
            {
                _isPointerHovered = false;
            }
        }
        else if (change.Property == TrackWindowPointerProperty && !TrackWindowPointer && !_isPointerHovered)
        {
            _targetLookX = 0;
            _targetLookY = 0;
        }
    }

    /// <summary>「减少动效」开启时退化为静态缓存绘制，零动画循环。</summary>
    private bool AnimationsEnabled => RenderMode == LanaRenderMode.Interactive && !MotionHelper.ReduceMotion;

    /// <summary>仅当控件挂树、允许动效且实际可见时运行帧循环（隐藏页 / 滚出视口零渲染开销）。</summary>
    private bool CanRunAnimationLoop => _isAttached && AnimationsEnabled && IsEffectivelyVisible && _topLevel is not null;

    /// <summary>
    /// 根据鼠标相对 Lana 中心的位移计算单位圆内平滑视线向量（径向 tanh 饱和，360° 柔和追随且永不越界）。
    /// </summary>
    public static (double LookX, double LookY) ComputeGazeVector(double dx, double dy, double sensitivityRadius = 240.0)
    {
        var dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < 1e-4)
            return (0.0, 0.0);

        var radius = Math.Max(1.0, sensitivityRadius);
        var mag = Math.Tanh(dist / radius);
        return ((dx / dist) * mag, (dy / dist) * mag);
    }

    /// <summary>祖先可见性恢复或滚动回视口时显式唤醒帧循环。</summary>
    public void ResumeIfVisible()
    {
        if (!CanRunAnimationLoop)
            return;

        if (_isSleepConverged && ResolveEffectiveState() == EffectiveState.Sleep)
            return;

        _lastFrameTime = TimeSpan.Zero;
        EnsureAnimationLoop();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        _isSleepConverged = false;
        _topLevel = TopLevel.GetTopLevel(this);
        _lastFrameTime = TimeSpan.Zero;
        _rig = GetPreset(ResolveEffectiveState());
        MotionHelper.ReducedMotionChanged += OnReducedMotionChanged;
        EffectiveViewportChanged += OnEffectiveViewportChanged;

        if (_topLevel is not null)
        {
            _topLevel.AddHandler(PointerMovedEvent, OnTopLevelPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            _topLevel.AddHandler(PointerExitedEvent, OnTopLevelPointerExited, RoutingStrategies.Direct);
        }

        if (CanRunAnimationLoop)
            EnsureAnimationLoop();
        else
            InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        MotionHelper.ReducedMotionChanged -= OnReducedMotionChanged;
        EffectiveViewportChanged -= OnEffectiveViewportChanged;
        if (_topLevel is not null)
        {
            _topLevel.RemoveHandler(PointerMovedEvent, OnTopLevelPointerMoved);
            _topLevel.RemoveHandler(PointerExitedEvent, OnTopLevelPointerExited);
        }

        _isAttached = false;
        _frameRequested = false;
        _isPointerHovered = false;
        _topLevel = null;
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        if (!CanRunAnimationLoop)
            return;

        if (_isSleepConverged && ResolveEffectiveState() == EffectiveState.Sleep)
            return;

        EnsureAnimationLoop();
    }

    private void OnTopLevelPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!TrackWindowPointer || !CanRunAnimationLoop || _topLevel is null)
            return;

        if (State == LanaVisualState.Sleep && !_isPointerHovered)
            return;

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var center = this.TranslatePoint(new Point(Bounds.Width * 0.5, Bounds.Height * 0.5), _topLevel);
        if (center is null)
            return;

        var pt = e.GetPosition(_topLevel);
        var radius = Math.Clamp(Math.Min(Bounds.Width, Bounds.Height) * 2.2, 160.0, 260.0);
        (_targetLookX, _targetLookY) = ComputeGazeVector(pt.X - center.Value.X, pt.Y - center.Value.Y, radius);
    }

    private void OnTopLevelPointerExited(object? sender, PointerEventArgs e)
    {
        if (!TrackWindowPointer)
            return;

        _targetLookX = 0;
        _targetLookY = 0;
    }

    private void OnReducedMotionChanged(object? sender, EventArgs e)
    {
        _frameRequested = false;
        if (CanRunAnimationLoop)
        {
            EnsureAnimationLoop();
        }
        else
        {
            InvalidateVisual();
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (!CanRunAnimationLoop || !EnableHoverFeedback)
            return;

        _isPointerHovered = true;
        _isSleepConverged = false;
        if (State is LanaVisualState.Sleep or LanaVisualState.Idle)
            _squashVel = -1.0;

        EnsureAnimationLoop();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _isPointerHovered = false;
        if (!TrackWindowPointer)
        {
            _targetLookX = 0;
            _targetLookY = 0;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        // 视线跟随统一由 TrackWindowPointer 控制；未开启时（如侧边栏）不随鼠标转动眼神或身体
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (CanRunAnimationLoop)
            _squashVel = -2.2;
    }

    /// <summary>
    /// 外部可主动触发一次轻快弹跳（例如点击发送、飞翔落位或日程变更成功时）。
    /// </summary>
    public void TriggerBounce(double impulse = -2.2)
    {
        if (!CanRunAnimationLoop)
            return;
        _isSleepConverged = false;
        _squashVel = impulse;
        EnsureAnimationLoop();
    }

    private void EnsureAnimationLoop()
    {
        if (_frameRequested || !CanRunAnimationLoop || _topLevel is null)
            return;

        _frameRequested = true;
        _topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan timestamp)
    {
        _frameRequested = false;
        if (!CanRunAnimationLoop)
            return;

        double dt = _lastFrameTime == TimeSpan.Zero
            ? 1.0 / 60.0
            : Math.Clamp((timestamp - _lastFrameTime).TotalSeconds, 0.001, 0.05);
        _lastFrameTime = timestamp;
        _elapsedSeconds += dt;

        // 1. Happy 状态 2 秒自动回落至 Idle
        if (State == LanaVisualState.Happy && AutoReturnFromHappy)
        {
            _happyElapsed += dt;
            _happyRemaining = Math.Max(0.0, _happyRemaining - dt);
            if (_happyRemaining <= 0.0)
            {
                SetCurrentValue(StateProperty, LanaVisualState.Idle);
                HappyCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        // 2. 计算当前有效目标态并执行指数阻尼插值
        var eff = ResolveEffectiveState();
        var target = GetPreset(eff);
        double lambda = eff is EffectiveState.WakePeek or EffectiveState.WinkGreet ? 10.5 : 8.5;
        bool converged = _rig.DampToward(target, lambda, dt);

        // 3. 弹簧挤压拉伸物理
        double force = -150.0 * _squash - 11.5 * _squashVel;
        _squashVel += force * dt;
        _squash += _squashVel * dt;

        // 4. 连续相位累加（严禁 sin(t * speed)，彻底避免状态切换抖动）
        _floatPhase += dt * _rig.FloatSpeed;
        _breathePhase += dt * (_rig.FloatSpeed * 1.05);
        _orbitAngle += dt * (2.6 + Math.Sin(_elapsedSeconds * 2.2) * 0.65);

        // 5. 视线目标平滑追随
        double gx = _targetLookX;
        double gy = _targetLookY;
        switch (eff)
        {
            case EffectiveState.Thinking:
                gx = 0.42 + Math.Sin(_elapsedSeconds * 1.6) * 0.22;
                gy = -0.32 + Math.Cos(_elapsedSeconds * 1.2) * 0.14;
                break;
            case EffectiveState.Sleep:
                gx = 0;
                gy = 0.1;
                break;
            case EffectiveState.WakePeek:
                gx = 0.28 + _targetLookX * 0.35;
                gy = -0.12 + _targetLookY * 0.25;
                break;
            case EffectiveState.WinkGreet:
                gx = TrackWindowPointer ? _targetLookX * 0.6 : 0.15;
                gy = TrackWindowPointer ? _targetLookY * 0.55 : -0.08;
                break;
            case EffectiveState.Happy:
                gx = _targetLookX * 0.35;
                gy = _targetLookY * 0.25;
                break;
        }
        _lookX = Damp(_lookX, gx, 8.2, dt);
        _lookY = Damp(_lookY, gy, 8.2, dt);

        // 6. 自然眨眼
        _blinkTimer -= dt;
        if (_blinkTimer <= 0.0)
        {
            _blinkPhase += dt * 11.5;
            if (_blinkPhase >= Math.PI)
            {
                _blinkPhase = 0.0;
                _blinkTimer = 2.3 + Random.Shared.NextDouble() * 2.3;
            }
        }

        // 7. 自适应省电：Sleep 收敛后补绘最后一帧并彻底停止动画循环（离开 Lana 页时零常驻开销）；
        //    悬停 / 状态切换会通过 EnsureAnimationLoop 重新唤醒。
        bool lowPowerSleep = eff == EffectiveState.Sleep && converged && Math.Abs(_squash) < 0.005 && Math.Abs(_squashVel) < 0.005;
        if (lowPowerSleep)
        {
            _isSleepConverged = true;
            if (_elapsedSeconds - _lastDrawTimestamp >= 1.0 / 15.0)
            {
                _lastDrawTimestamp = _elapsedSeconds;
                InvalidateVisual();
            }

            return;
        }

        _isSleepConverged = false;
        InvalidateVisual();
        EnsureAnimationLoop();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        if (!AnimationsEnabled)
        {
            double dpiScale = _topLevel?.RenderScaling ?? 1.0;
            context.Custom(new StaticAvatarDrawOperation(new Rect(Bounds.Size), State, dpiScale));
            return;
        }

        double hopY = 0.0;
        double hopSquash = 0.0;
        if (State == LanaVisualState.Happy)
        {
            const double hopDur = 0.52;
            if (_happyElapsed < hopDur * 2.0)
            {
                double localT = (_happyElapsed % hopDur) / hopDur;
                double arc = Math.Sin(localT * Math.PI);
                hopY = -arc * 15.0;
                hopSquash = (arc - 0.35) * 0.42;
            }
        }

        var snapshot = new RenderSnapshot(
            _rig,
            _elapsedSeconds,
            _floatPhase,
            _breathePhase,
            _orbitAngle,
            _lookX,
            _lookY,
            _squash + hopSquash,
            hopY,
            1.0 - Math.Sin(_blinkPhase) * 0.92
        );

        context.Custom(new InteractiveLanaDrawOperation(new Rect(Bounds.Size), snapshot));
    }

    private EffectiveState ResolveEffectiveState()
    {
        if (EnableHoverFeedback && _isPointerHovered)
        {
            if (State == LanaVisualState.Sleep) return EffectiveState.WakePeek;
            if (State == LanaVisualState.Idle) return EffectiveState.WinkGreet;
        }

        return State switch
        {
            LanaVisualState.Sleep => EffectiveState.Sleep,
            LanaVisualState.Listening => EffectiveState.Listening,
            LanaVisualState.Thinking => EffectiveState.Thinking,
            LanaVisualState.Happy => EffectiveState.Happy,
            _ => EffectiveState.Idle
        };
    }

    private static RigState GetPreset(EffectiveState state) => state switch
    {
        EffectiveState.Sleep => RigState.Sleep,
        EffectiveState.WakePeek => RigState.WakePeek,
        EffectiveState.WinkGreet => RigState.WinkGreet,
        EffectiveState.Listening => RigState.Listening,
        EffectiveState.Thinking => RigState.Thinking,
        EffectiveState.Happy => RigState.Happy,
        _ => RigState.Idle
    };

    private static double Damp(double current, double target, double lambda, double dt)
        => current + (target - current) * (1.0 - Math.Exp(-lambda * dt));

    private enum EffectiveState
    {
        Sleep,
        WakePeek,
        WinkGreet,
        Idle,
        Listening,
        Thinking,
        Happy
    }

    private struct RigState
    {
        public double BodyScale;
        public double BodyAlpha;
        public double FloatAmp;
        public double FloatSpeed;
        public double WobbleAmp;
        public double TiltDeg;
        public double LeftEyeRound;
        public double RightEyeRound;
        public double LeftEyeBend;
        public double RightEyeBend;
        public double EyeRy;
        public double MouthW;
        public double MouthTopY;
        public double MouthBotY;
        public double MouthFill;
        public double MouthOffsetX;
        public double BlushAlpha;
        public double OrbitAlpha;
        public double AuraAlpha;

        public static RigState Sleep => new()
        {
            BodyScale = 0.91, BodyAlpha = 0.86, FloatAmp = 1.8, FloatSpeed = 1.1, WobbleAmp = 0.3, TiltDeg = -1.8,
            LeftEyeRound = 0.0, RightEyeRound = 0.0, LeftEyeBend = 6.2, RightEyeBend = 6.2, EyeRy = 2.0,
            MouthW = 4.8, MouthTopY = 6.8, MouthBotY = 9.6, MouthFill = 0.0, MouthOffsetX = 0.0,
            BlushAlpha = 0.42, OrbitAlpha = 0.0, AuraAlpha = 0.0
        };

        public static RigState WakePeek => new()
        {
            BodyScale = 0.99, BodyAlpha = 1.0, FloatAmp = 2.8, FloatSpeed = 2.2, WobbleAmp = 0.7, TiltDeg = 5.2,
            LeftEyeRound = 1.0, RightEyeRound = 1.0, LeftEyeBend = -1.0, RightEyeBend = -1.0, EyeRy = 6.4,
            MouthW = 6.0, MouthTopY = 6.8, MouthBotY = 10.6, MouthFill = 0.0, MouthOffsetX = 0.0,
            BlushAlpha = 0.8, OrbitAlpha = 0.0, AuraAlpha = 0.0
        };

        public static RigState WinkGreet => new()
        {
            BodyScale = 1.03, BodyAlpha = 1.0, FloatAmp = 3.2, FloatSpeed = 2.3, WobbleAmp = 0.8, TiltDeg = -4.5,
            LeftEyeRound = 1.0, RightEyeRound = 0.0, LeftEyeBend = 0.0, RightEyeBend = -7.2, EyeRy = 7.4,
            MouthW = 7.2, MouthTopY = 6.4, MouthBotY = 11.8, MouthFill = 0.0, MouthOffsetX = 0.6,
            BlushAlpha = 0.9, OrbitAlpha = 0.0, AuraAlpha = 0.0
        };

        public static RigState Idle => new()
        {
            BodyScale = 1.0, BodyAlpha = 1.0, FloatAmp = 3.2, FloatSpeed = 1.85, WobbleAmp = 0.65, TiltDeg = 0.0,
            LeftEyeRound = 1.0, RightEyeRound = 1.0, LeftEyeBend = 0.0, RightEyeBend = 0.0, EyeRy = 7.2,
            MouthW = 7.0, MouthTopY = 6.6, MouthBotY = 11.2, MouthFill = 0.0, MouthOffsetX = 0.0,
            BlushAlpha = 0.72, OrbitAlpha = 0.0, AuraAlpha = 0.0
        };

        public static RigState Listening => new()
        {
            BodyScale = 1.03, BodyAlpha = 1.0, FloatAmp = 2.6, FloatSpeed = 2.4, WobbleAmp = 0.75, TiltDeg = 2.5,
            LeftEyeRound = 1.0, RightEyeRound = 1.0, LeftEyeBend = 0.0, RightEyeBend = 0.0, EyeRy = 8.2,
            MouthW = 4.4, MouthTopY = 6.8, MouthBotY = 13.4, MouthFill = 1.0, MouthOffsetX = 0.0,
            BlushAlpha = 0.82, OrbitAlpha = 0.0, AuraAlpha = 1.0
        };

        public static RigState Thinking => new()
        {
            BodyScale = 0.84, BodyAlpha = 1.0, FloatAmp = 2.5, FloatSpeed = 2.0, WobbleAmp = 0.9, TiltDeg = 4.0,
            LeftEyeRound = 1.0, RightEyeRound = 1.0, LeftEyeBend = 0.0, RightEyeBend = 0.0, EyeRy = 7.0,
            MouthW = 5.2, MouthTopY = 7.2, MouthBotY = 10.6, MouthFill = 0.0, MouthOffsetX = 2.2,
            BlushAlpha = 0.74, OrbitAlpha = 1.0, AuraAlpha = 0.0
        };

        public static RigState Happy => new()
        {
            BodyScale = 1.0, BodyAlpha = 1.0, FloatAmp = 2.8, FloatSpeed = 2.2, WobbleAmp = 0.95, TiltDeg = 0.0,
            LeftEyeRound = 0.0, RightEyeRound = 0.0, LeftEyeBend = -8.0, RightEyeBend = -8.0, EyeRy = 2.0,
            MouthW = 6.5, MouthTopY = 5.8, MouthBotY = 12.4, MouthFill = 1.0, MouthOffsetX = 0.0,
            BlushAlpha = 0.92, OrbitAlpha = 0.0, AuraAlpha = 0.0
        };

        public bool DampToward(in RigState t, double lambda, double dt)
        {
            double k = 1.0 - Math.Exp(-lambda * dt);
            BodyScale += (t.BodyScale - BodyScale) * k;
            BodyAlpha += (t.BodyAlpha - BodyAlpha) * k;
            FloatAmp += (t.FloatAmp - FloatAmp) * k;
            FloatSpeed += (t.FloatSpeed - FloatSpeed) * k;
            WobbleAmp += (t.WobbleAmp - WobbleAmp) * k;
            TiltDeg += (t.TiltDeg - TiltDeg) * k;
            LeftEyeRound += (t.LeftEyeRound - LeftEyeRound) * k;
            RightEyeRound += (t.RightEyeRound - RightEyeRound) * k;
            LeftEyeBend += (t.LeftEyeBend - LeftEyeBend) * k;
            RightEyeBend += (t.RightEyeBend - RightEyeBend) * k;
            EyeRy += (t.EyeRy - EyeRy) * k;
            MouthW += (t.MouthW - MouthW) * k;
            MouthTopY += (t.MouthTopY - MouthTopY) * k;
            MouthBotY += (t.MouthBotY - MouthBotY) * k;
            MouthFill += (t.MouthFill - MouthFill) * k;
            MouthOffsetX += (t.MouthOffsetX - MouthOffsetX) * k;
            BlushAlpha += (t.BlushAlpha - BlushAlpha) * k;
            OrbitAlpha += (t.OrbitAlpha - OrbitAlpha) * k;
            AuraAlpha += (t.AuraAlpha - AuraAlpha) * k;

            return Math.Abs(t.BodyScale - BodyScale) < 0.002
                && Math.Abs(t.BodyAlpha - BodyAlpha) < 0.005
                && Math.Abs(t.LeftEyeRound - LeftEyeRound) < 0.005
                && Math.Abs(t.RightEyeRound - RightEyeRound) < 0.005
                && Math.Abs(t.OrbitAlpha - OrbitAlpha) < 0.005;
        }
    }

    private readonly record struct RenderSnapshot(
        RigState Rig,
        double Time,
        double FloatPhase,
        double BreathePhase,
        double OrbitAngle,
        double LookX,
        double LookY,
        double Squash,
        double HopY,
        double BlinkScale
    );

    /// <summary>
    /// 静态小头像绘制操作：使用缓存的离屏 SKImage，列表滚动 0 掉帧。
    /// </summary>
    private sealed class StaticAvatarDrawOperation(Rect bounds, LanaVisualState state, double dpiScale) : ICustomDrawOperation
    {
        public Rect Bounds { get; } = bounds;
        public LanaVisualState State { get; } = state;
        public double DpiScale { get; } = dpiScale;
        public void Dispose() { }
        public bool Equals(ICustomDrawOperation? other) =>
            other is StaticAvatarDrawOperation o &&
            o.Bounds == Bounds &&
            o.State == State &&
            Math.Abs(o.DpiScale - DpiScale) < 0.001;
        public bool HitTest(Point p) => Bounds.Contains(p);

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature is null) return;

            int px = Math.Clamp((int)Math.Ceiling(Math.Min(Bounds.Width, Bounds.Height) * DpiScale), 24, 256);
            var img = GetOrCreateStaticImage(State, px);

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            float side = (float)Math.Min(Bounds.Width, Bounds.Height);
            float dx = (float)(Bounds.Width - side) * 0.5f;
            float dy = (float)(Bounds.Height - side) * 0.5f;
            var dest = new SKRect(dx, dy, dx + side, dy + side);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawImage(img, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        }

        private static SKImage GetOrCreateStaticImage(LanaVisualState s, int px)
        {
            if (StaticCache.Count > MaxStaticCacheEntries)
                StaticCache.Clear();

            return StaticCache.GetOrAdd((s, px), key =>
            {
                var info = new SKImageInfo(key.PixelSize, key.PixelSize, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(info);
                var rig = key.State switch
                {
                    LanaVisualState.Sleep => RigState.Sleep,
                    LanaVisualState.Happy => RigState.Happy,
                    LanaVisualState.Listening => RigState.Listening,
                    LanaVisualState.Thinking => RigState.Thinking,
                    _ => RigState.Idle
                };
                // 静态小头像保持满饱和度、略微放大以在小尺寸下更清晰
                rig.BodyAlpha = 1.0;
                rig.BodyScale = 1.06;
                rig.OrbitAlpha = 0.0;
                rig.AuraAlpha = 0.0;

                var snap = new RenderSnapshot(rig, 0, 0, 0, 0, 0, 0, 0, 0, 1.0);
                DrawLanaToCanvas(surface.Canvas, key.PixelSize, key.PixelSize, snap);
                return surface.Snapshot();
            });
        }
    }

    /// <summary>
    /// 实时交互绘制操作（SkiaSharp SKCanvas 原生 60fps 绘制）。
    /// </summary>
    private sealed class InteractiveLanaDrawOperation(Rect bounds, RenderSnapshot snapshot) : ICustomDrawOperation
    {
        public Rect Bounds { get; } = bounds;
        public void Dispose() { }
        public bool Equals(ICustomDrawOperation? other) => false;
        public bool HitTest(Point p) => Bounds.Contains(p);

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature is null) return;

            using var lease = leaseFeature.Lease();
            DrawLanaToCanvas(lease.SkCanvas, (float)Bounds.Width, (float)Bounds.Height, snapshot);
        }
    }

    private static void DrawLanaToCanvas(SKCanvas canvas, float width, float height, in RenderSnapshot snap)
    {
        var rig = snap.Rig;
        float side = MathF.Min(width, height);
        float offsetX = (width - side) * 0.5f;
        float offsetY = (height - side) * 0.5f;
        float scale = side / 200f;

        canvas.Save();
        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scale, scale);

        if (rig.BodyAlpha < 0.999)
        {
            using var layerPaint = new SKPaint { Color = new SKColor(255, 255, 255, (byte)Math.Clamp(rig.BodyAlpha * 255.0, 0, 255)) };
            canvas.SaveLayer(layerPaint);
        }

        const float cx = 100f;
        const float cy = 102f;

        // 1. Listening 柔焦弥散呼吸光晕（无生硬实线圆圈，边缘自然羽化不裁切）
        if (rig.AuraAlpha > 0.01)
        {
            float breath = 0.5f + 0.5f * MathF.Sin((float)(snap.BreathePhase * 1.35));
            float haloR = 80f + breath * 13f;
            float haloA = (0.28f + breath * 0.16f) * (float)rig.AuraAlpha;
            byte a0 = (byte)Math.Clamp(haloA * 0.95f * 255f, 0, 255);
            byte a1 = (byte)Math.Clamp(haloA * 0.55f * 255f, 0, 255);

            using var haloShader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), haloR,
                [
                    new SKColor(232, 198, 255, a0),
                    new SKColor(182, 224, 255, a1),
                    new SKColor(182, 224, 255, 0)
                ],
                [0.45f, 0.72f, 1.0f],
                SKShaderTileMode.Clamp);
            using var haloPaint = new SKPaint
            {
                Shader = haloShader,
                IsAntialias = true
            };
            canvas.DrawCircle(cx, cy, haloR, haloPaint);
        }

        // 2. Thinking 边界内流光拖尾环绕
        if (rig.OrbitAlpha > 0.01)
        {
            const float orbitR = 78f;
            const int segs = 36;
            const double arcSpan = Math.PI * 1.35;
            var oval = new SKRect(cx - orbitR, cy - orbitR, cx + orbitR, cy + orbitR);

            using var segPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                IsAntialias = true
            };

            for (int i = 0; i < segs; i++)
            {
                float f0 = (float)i / segs;
                float f1 = (float)(i + 1) / segs;
                float startDeg = (float)((snap.OrbitAngle - arcSpan * (1.0 - f0)) * 180.0 / Math.PI);
                float sweepDeg = (float)((arcSpan / segs) * 180.0 / Math.PI);

                byte alpha = (byte)Math.Clamp(MathF.Pow(f1, 1.8f) * 0.88f * (float)rig.OrbitAlpha * 255f, 0, 255);
                byte r = (byte)(238 - f1 * 92);
                byte g = (byte)(154 + f1 * 68);
                byte b = (byte)(218 + f1 * 34);

                segPaint.Color = new SKColor(r, g, b, alpha);
                segPaint.StrokeWidth = 1.6f + f1 * 5.2f;
                canvas.DrawArc(oval, startDeg, sweepDeg + 0.5f, false, segPaint);
            }

            float hx = cx + MathF.Cos((float)snap.OrbitAngle) * orbitR;
            float hy = cy + MathF.Sin((float)snap.OrbitAngle) * orbitR;
            using var headShader = SKShader.CreateRadialGradient(
                new SKPoint(hx, hy), 11f,
                [
                    new SKColor(255, 255, 255, (byte)(242 * rig.OrbitAlpha)),
                    new SKColor(155, 222, 252, (byte)(190 * rig.OrbitAlpha)),
                    new SKColor(155, 222, 252, 0)
                ],
                [0f, 0.45f, 1f],
                SKShaderTileMode.Clamp);
            using var headPaint = new SKPaint { Shader = headShader, IsAntialias = true };
            canvas.DrawCircle(hx, hy, 11f, headPaint);

            float oppStartDeg = (float)((snap.OrbitAngle + Math.PI * 0.85) * 180.0 / Math.PI);
            segPaint.Color = new SKColor(230, 178, 240, (byte)(82 * rig.OrbitAlpha));
            segPaint.StrokeWidth = 3f;
            canvas.DrawArc(oval, oppStartDeg, 32f, false, segPaint);
        }

        // 3. 底部柔和投影
        float floatY = (float)(Math.Sin(snap.FloatPhase) * rig.FloatAmp);
        float totalY = floatY + (float)snap.HopY;
        float shRx = MathF.Max(22f, (float)(44.0 * rig.BodyScale) - MathF.Abs(totalY) * 0.5f);
        float shCx = cx + (float)snap.LookX * 3f;
        using (var shShader = SKShader.CreateRadialGradient(
            new SKPoint(shCx, 175f), shRx,
            [new SKColor(124, 135, 184, 41), new SKColor(124, 135, 184, 0)],
            SKShaderTileMode.Clamp))
        using (var shPaint = new SKPaint { Shader = shShader, IsAntialias = true })
        {
            canvas.DrawOval(shCx, 175f, shRx, 8f, shPaint);
        }

        // 4. Lana 有机果冻本体
        float breathe = (float)(Math.Sin(snap.BreathePhase) * 0.022);
        float sx = (float)(rig.BodyScale * (1.0 - snap.Squash * 0.13 + breathe));
        float sy = (float)(rig.BodyScale * (1.0 + snap.Squash * 0.13 - breathe * 0.6));
        float tiltDeg = (float)(rig.TiltDeg + snap.LookX * 3.2);

        canvas.Save();
        canvas.Translate(cx + (float)snap.LookX * 4.5f, cy + totalY + (float)snap.LookY * 3.0f);
        canvas.RotateDegrees(tiltDeg);
        canvas.Scale(sx, sy);

        using (var bodyPath = BuildOrganicBlobPath(snap.Time, rig.WobbleAmp))
        {
            using var bodyGrad = SKShader.CreateLinearGradient(
                new SKPoint(-56, -54), new SKPoint(58, 58),
                [new SKColor(255, 212, 231), new SKColor(237, 226, 252), new SKColor(184, 233, 255)],
                [0f, 0.46f, 1f],
                SKShaderTileMode.Clamp);
            using var bodyPaint = new SKPaint { Shader = bodyGrad, IsAntialias = true };
            canvas.DrawPath(bodyPath, bodyPaint);

            using var innerGrad = SKShader.CreateRadialGradient(
                new SKPoint(-22, -24), 72f,
                [new SKColor(255, 255, 255, 184), new SKColor(247, 236, 255, 31), new SKColor(156, 201, 247, 87)],
                [0f, 0.55f, 1f],
                SKShaderTileMode.Clamp);
            using var innerPaint = new SKPaint { Shader = innerGrad, IsAntialias = true };
            canvas.DrawPath(bodyPath, innerPaint);

            using var rimGrad = SKShader.CreateLinearGradient(
                new SKPoint(-60, -60), new SKPoint(60, 60),
                [new SKColor(247, 189, 219, 140), new SKColor(215, 201, 245, 115), new SKColor(154, 216, 250, 166)],
                [0f, 0.5f, 1f],
                SKShaderTileMode.Clamp);
            using var rimPaint = new SKPaint { Shader = rimGrad, Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f, IsAntialias = true };
            canvas.DrawPath(bodyPath, rimPaint);
        }

        // 左上角玻璃高光
        using (var specPath = new SKPath())
        using (var specPaint = new SKPaint { Color = new SKColor(255, 255, 255, 158), IsAntialias = true })
        {
            specPath.MoveTo(-45, -27);
            specPath.CubicTo(-40, -43, -26, -51, -15, -48);
            specPath.CubicTo(-12, -47, -14, -39, -21, -36);
            specPath.CubicTo(-30, -32, -36, -24, -40, -15);
            specPath.CubicTo(-42, -10, -48, -17, -45, -27);
            specPath.Close();
            canvas.DrawPath(specPath, specPaint);
        }

        // 5. 扁平五官（带 2.5D 视差偏移 + 眼球/高光二次眼神跟随）
        canvas.Save();
        canvas.Translate((float)snap.LookX * 7.5f, (float)snap.LookY * 5.0f);

        using (var blushPaint = new SKPaint
        {
            Color = new SKColor(247, 178, 198, (byte)Math.Clamp(rig.BlushAlpha * 255.0, 0, 255)),
            IsAntialias = true
        })
        {
            canvas.DrawOval(-34f, 10.5f, 10.5f, 5.4f, blushPaint);
            canvas.DrawOval(34f, 10.5f, 10.5f, 5.4f, blushPaint);
        }

        float eyeShiftX = (float)snap.LookX * 2.8f;
        float eyeShiftY = (float)snap.LookY * 2.0f;
        float hlShiftX = (float)snap.LookX * 1.1f;
        float hlShiftY = (float)snap.LookY * 0.85f;
        DrawSingleEye(canvas, -19f, rig.LeftEyeRound, rig.LeftEyeBend, snap.BlinkScale, rig.EyeRy, eyeShiftX, eyeShiftY, hlShiftX, hlShiftY);
        DrawSingleEye(canvas, 19f, rig.RightEyeRound, rig.RightEyeBend, snap.BlinkScale, rig.EyeRy, eyeShiftX, eyeShiftY, hlShiftX, hlShiftY);

        DrawMouth(canvas, rig);

        canvas.Restore(); // face
        canvas.Restore(); // body

        if (rig.BodyAlpha < 0.999)
            canvas.Restore(); // layer

        canvas.Restore(); // root scale
    }

    private static SKPath BuildOrganicBlobPath(double t, double wobbleAmp)
    {
        const int n = 32;
        const double baseR = 64.0;
        Span<SKPoint> pts = stackalloc SKPoint[n];

        for (int i = 0; i < n; i++)
        {
            double a = (i / (double)n) * Math.PI * 2.0;
            double delta = ((a - 5.55 + Math.PI * 3.0) % (Math.PI * 2.0)) - Math.PI;
            double indent = -5.0 * Math.Exp(-(delta * delta) * 3.2);
            double pear = 2.8 * Math.Sin(a);
            double wave = wobbleAmp * (Math.Sin(3.0 * a + t * 2.3) * 1.2 + Math.Cos(2.0 * a - t * 1.7) * 0.9);
            double r = baseR + indent + pear + wave;
            pts[i] = new SKPoint((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r));
        }

        var path = new SKPath();
        for (int i = 0; i < n; i++)
        {
            var p0 = pts[(i - 1 + n) % n];
            var p1 = pts[i];
            var p2 = pts[(i + 1) % n];
            var p3 = pts[(i + 2) % n];

            if (i == 0) path.MoveTo(p1);
            path.CubicTo(
                p1.X + (p2.X - p0.X) / 6f, p1.Y + (p2.Y - p0.Y) / 6f,
                p2.X - (p3.X - p1.X) / 6f, p2.Y - (p3.Y - p1.Y) / 6f,
                p2.X, p2.Y);
        }
        path.Close();
        return path;
    }

    private static void DrawSingleEye(
        SKCanvas canvas,
        float cx,
        double roundA,
        double bend,
        double blinkScale,
        double eyeRy,
        float eyeShiftX,
        float eyeShiftY,
        float hlShiftX,
        float hlShiftY)
    {
        if (roundA > 0.01)
        {
            float effRy = MathF.Max(0.8f, (float)(eyeRy * blinkScale * (0.25 + 0.75 * roundA)));
            float effRx = (float)(5.2 * (0.85 + 0.15 * roundA));
            float ex = cx + eyeShiftX;
            float ey = -4.5f + eyeShiftY;
            using var eyePaint = new SKPaint
            {
                Color = new SKColor(30, 43, 84, (byte)Math.Clamp(roundA * 255.0, 0, 255)),
                IsAntialias = true
            };
            canvas.DrawOval(ex, ey, effRx, effRy, eyePaint);

            if (blinkScale > 0.35 && roundA > 0.25)
            {
                using var hlPaint = new SKPaint
                {
                    Color = new SKColor(255, 255, 255, (byte)Math.Clamp(0.85 * roundA * 255.0, 0, 255)),
                    IsAntialias = true
                };
                canvas.DrawCircle(ex + 1.6f + hlShiftX, ey - 2.1f + hlShiftY, 1.8f, hlPaint);
            }
        }

        double arcA = 1.0 - roundA;
        if (arcA > 0.01)
        {
            float ax = cx + eyeShiftX * 0.6f;
            float ay = -3.5f + eyeShiftY * 0.6f;
            using var arcPath = new SKPath();
            arcPath.MoveTo(ax - 9f, ay);
            arcPath.QuadTo(ax, ay + (float)bend, ax + 9f, ay);
            using var arcPaint = new SKPaint
            {
                Color = new SKColor(30, 43, 84, (byte)Math.Clamp(arcA * 255.0, 0, 255)),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3.8f,
                StrokeCap = SKStrokeCap.Round,
                IsAntialias = true
            };
            canvas.DrawPath(arcPath, arcPaint);
        }
    }

    private static void DrawMouth(SKCanvas canvas, in RigState rig)
    {
        float mx = (float)rig.MouthOffsetX;
        float mw = (float)rig.MouthW;
        float mTop = (float)rig.MouthTopY;
        float mBot = (float)rig.MouthBotY;

        if (rig.MouthFill > 0.02)
        {
            using var fillPath = new SKPath();
            fillPath.MoveTo(mx - mw, mTop);
            fillPath.QuadTo(mx, mTop - 1.0f, mx + mw, mTop);
            fillPath.CubicTo(mx + mw * 0.92f, mBot + 1.2f, mx + mw * 0.45f, mBot + 2.2f, mx, mBot + 2.2f);
            fillPath.CubicTo(mx - mw * 0.45f, mBot + 2.2f, mx - mw * 0.92f, mBot + 1.2f, mx - mw, mTop);
            fillPath.Close();

            using var fillPaint = new SKPaint
            {
                Color = new SKColor(30, 43, 84, (byte)Math.Clamp(rig.MouthFill * 255.0, 0, 255)),
                IsAntialias = true
            };
            canvas.DrawPath(fillPath, fillPaint);
        }

        double lineA = 1.0 - rig.MouthFill;
        if (lineA > 0.02)
        {
            using var linePath = new SKPath();
            linePath.MoveTo(mx - mw, mTop);
            linePath.QuadTo(mx, mBot, mx + mw, mTop - (mx > 0.5f ? 0.8f : 0f));
            using var linePaint = new SKPaint
            {
                Color = new SKColor(30, 43, 84, (byte)Math.Clamp(lineA * 255.0, 0, 255)),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3.2f,
                StrokeCap = SKStrokeCap.Round,
                IsAntialias = true
            };
            canvas.DrawPath(linePath, linePaint);
        }
    }
}
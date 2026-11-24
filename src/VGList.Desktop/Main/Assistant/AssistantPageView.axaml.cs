using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VGList.Core.Models;
using VGList.Desktop.Controls;
using VGList.Desktop.Services;
using VGList.Platform;

namespace VGList.Desktop.Main.Assistant;

public partial class AssistantPageView : UserControl
{
    private const double SidebarExpandedWidth = 236;

    private readonly Border _historySidebar;
    private readonly Panel _chatStageHost;
    private readonly Panel _conversationStage;
    private readonly Grid _emptyStateLayer;
    private readonly StackPanel _emptyStateGreeting;
    private readonly Border _homeLanaAnchor;
    private readonly ItemsControl _messagesList;
    private readonly LanaAvatarControl _stageLanaAvatar;
    private readonly Border _proposalPriorityOverlay;
    private readonly ReminderEditorPopover _proposalReminderOverlay;
    private readonly ScrollViewer _messageScroll;
    private readonly Panel _scrollToBottomPill;
    private readonly TextBox _composerBox;
    private readonly DispatcherTimer _dragLeaveTimer;
    private AssistantPageViewModel? _subscribedVm;
    private TopLevel? _topLevel;
    private bool _autoFollowBottom = true;

    // 唯一活体 Lana 舞台定位与飞行转场状态
    private Rect _currentLanaRect;
    private Rect _lastHomeRect;
    private bool _hasValidLanaRect;
    private bool _hasLastHomeRect;
    private bool _isApplyingLanaRect;
    private ChatMessageViewModel? _anchoredAssistantMessage;

    private bool _hasPendingFlight;
    private LanaFlightKind _pendingFlightKind;
    private ChatMessageViewModel? _pendingFlightTarget;
    private Rect _pendingFlightOrigin;

    private bool _isFlightRunning;
    private bool _flightFrameRequested;
    private bool _flightThinkingRevealed;
    private int _flightToken;
    private int _greetingFadeToken;
    private LanaFlightKind _activeFlightKind;
    private ChatMessageViewModel? _activeFlightTarget;
    private Rect _activeFlightFrom;
    private TimeSpan _flightStartTimestamp;

    public AssistantPageView()
    {
        InitializeComponent();

        _historySidebar = this.FindControl<Border>("HistorySidebar")!;
        _chatStageHost = this.FindControl<Panel>("ChatStageHost")!;
        _conversationStage = this.FindControl<Panel>("ConversationStage")!;
        _emptyStateLayer = this.FindControl<Grid>("EmptyStateLayer")!;
        _emptyStateGreeting = this.FindControl<StackPanel>("EmptyStateGreeting")!;
        _homeLanaAnchor = this.FindControl<Border>("HomeLanaAnchor")!;
        _messagesList = this.FindControl<ItemsControl>("MessagesList")!;
        _stageLanaAvatar = this.FindControl<LanaAvatarControl>("StageLanaAvatar")!;
        _proposalPriorityOverlay = this.FindControl<Border>("ProposalPriorityOverlay")!;
        _proposalReminderOverlay = this.FindControl<ReminderEditorPopover>("ProposalReminderOverlay")!;
        _messageScroll = this.FindControl<ScrollViewer>("MessageScroll")!;
        _scrollToBottomPill = this.FindControl<Panel>("ScrollToBottomPill")!;
        _composerBox = this.FindControl<TextBox>("ComposerBox")!;

        _dragLeaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _dragLeaveTimer.Tick += (_, _) =>
        {
            _dragLeaveTimer.Stop();
            if (ViewModel is { IsDragOver: true } vm)
            {
                vm.IsDragOver = false;
            }
        };

        _composerBox.AddHandler(KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);
        _composerBox.PropertyChanged += OnComposerPropertyChanged;
        _composerBox.GotFocus += (_, _) =>
        {
            if (ViewModel is { } vm)
            {
                vm.IsComposerFocused = true;
            }
        };
        _composerBox.LostFocus += (_, _) =>
        {
            if (ViewModel is { } vm)
            {
                vm.IsComposerFocused = false;
            }
        };

        AddHandler(DragDrop.DragEnterEvent, OnDragEnter, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble, handledEventsToo: true);

        _conversationStage.LayoutUpdated += OnConversationStageLayoutUpdated;

        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnViewAttachedToVisualTree;
        DetachedFromVisualTree += OnViewDetachedFromVisualTree;
    }

    private AssistantPageViewModel? ViewModel => DataContext as AssistantPageViewModel;

    public void FocusComposer() =>
        Dispatcher.UIThread.Post(() =>
        {
            SyncMotionPreference();
            ViewModel?.RefreshGreeting(pickNewVariant: false);
            _composerBox.Focus();
            _composerBox.CaretIndex = _composerBox.Text?.Length ?? 0;
        }, DispatcherPriority.Input);

    public void SyncMotionPreference()
    {
        Classes.Set("no-motion", MotionHelper.ReduceMotion);
        if (MotionHelper.ReduceMotion && (_isFlightRunning || _hasPendingFlight))
        {
            CancelLanaFlight();
            SyncEmptyStateLayerImmediate();
            SyncDockedLanaPosition();
        }
    }

    private void OnViewAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _topLevel = TopLevel.GetTopLevel(this);
        MotionHelper.ReducedMotionChanged += OnReducedMotionChanged;
        SyncMotionPreference();
        ViewModel?.RefreshGreeting(pickNewVariant: false);
        SyncEmptyStateLayerImmediate();
        Dispatcher.UIThread.Post(() =>
        {
            SyncDockedLanaPosition();
            _stageLanaAvatar.ResumeIfVisible();
        }, DispatcherPriority.Loaded);
    }

    private void OnViewDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        MotionHelper.ReducedMotionChanged -= OnReducedMotionChanged;
        CancelLanaFlight();
        _topLevel = null;
    }

    private void OnReducedMotionChanged(object? sender, EventArgs e) => SyncMotionPreference();

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
            _subscribedVm.MessagesUpdated -= OnMessagesUpdated;
            _subscribedVm.SessionSwitched -= OnSessionSwitched;
            _subscribedVm.StageTransitionRequested -= OnStageTransitionRequested;
        }

        _subscribedVm = ViewModel;
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged += OnVmPropertyChanged;
            _subscribedVm.MessagesUpdated += OnMessagesUpdated;
            _subscribedVm.SessionSwitched += OnSessionSwitched;
            _subscribedVm.StageTransitionRequested += OnStageTransitionRequested;
            _anchoredAssistantMessage = _subscribedVm.LatestAssistantMessage;
            SyncSidebarVisual();
            SyncMotionPreference();
            SyncEmptyStateLayerImmediate();
            Dispatcher.UIThread.Post(SyncDockedLanaPosition, DispatcherPriority.Loaded);
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssistantPageViewModel.IsHistorySidebarOpen))
        {
            SyncSidebarVisual();
        }
        else if (e.PropertyName == nameof(AssistantPageViewModel.IsPageActive))
        {
            if (ViewModel is { IsPageActive: true })
            {
                Dispatcher.UIThread.Post(() =>
                {
                    SyncEmptyStateLayerImmediate();
                    SyncDockedLanaPosition();
                    _stageLanaAvatar.ResumeIfVisible();
                }, DispatcherPriority.Loaded);
            }
            else
            {
                CancelLanaFlight();
            }
        }
    }

    private void SyncSidebarVisual()
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var open = vm.IsHistorySidebarOpen;
        _historySidebar.Width = open ? SidebarExpandedWidth : 0;
        _historySidebar.Opacity = open ? 1 : 0;
        _historySidebar.IsHitTestVisible = open;
    }

    private void OnSessionSwitched(object? sender, EventArgs e)
    {
        _autoFollowBottom = true;
        _scrollToBottomPill.IsVisible = false;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollToBottomPill.IsVisible = false;
            if (ViewModel is { HasMessages: true })
            {
                _messageScroll.ScrollToEnd();
            }

            TryAdvanceLanaPosition();
        }, DispatcherPriority.Background);
    }

    private void OnMessagesUpdated(object? sender, EventArgs e)
    {
        if (!_autoFollowBottom)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _messageScroll.ScrollToEnd();
            if (!_isFlightRunning)
            {
                TryAdvanceLanaPosition();
            }
        }, DispatcherPriority.Background);
    }

    private void OnMessageScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (ViewModel is not { HasMessages: true })
        {
            _autoFollowBottom = true;
            _scrollToBottomPill.IsVisible = false;
        }
        else
        {
            var maxOffset = Math.Max(0, _messageScroll.Extent.Height - _messageScroll.Viewport.Height);
            var distanceFromBottom = maxOffset - _messageScroll.Offset.Y;
            _autoFollowBottom = distanceFromBottom <= 36;
            _scrollToBottomPill.IsVisible = !_autoFollowBottom && maxOffset > 80;
        }

        if (!_isFlightRunning && !_hasPendingFlight)
        {
            SyncDockedLanaPosition();
        }
    }

    private void OnScrollToBottomPressed(object? sender, PointerPressedEventArgs e)
    {
        _autoFollowBottom = true;
        _scrollToBottomPill.IsVisible = false;
        _messageScroll.ScrollToEnd();
        e.Handled = true;
    }

    // ===== 唯一活体 Lana 舞台定位、首轮飞翔与多轮滑落引擎 =====

    private void OnStageLanaHappyCompleted(object? sender, EventArgs e) =>
        ViewModel?.CompleteLanaHappy();

    private void OnStageLanaPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (ViewModel is not { HasMessages: true })
        {
            return;
        }

        var maxOffset = Math.Max(0, _messageScroll.Extent.Height - _messageScroll.Viewport.Height);
        if (maxOffset <= 0)
        {
            return;
        }

        var nextY = Math.Clamp(_messageScroll.Offset.Y - e.Delta.Y * 48.0, 0, maxOffset);
        _messageScroll.Offset = new Vector(_messageScroll.Offset.X, nextY);
        e.Handled = true;
    }

    private void OnStageTransitionRequested(object? sender, LanaStageTransitionEventArgs e)
    {
        CancelLanaFlight();
        _anchoredAssistantMessage = e.TargetAssistantMessage;

        switch (e.Kind)
        {
            case LanaFlightKind.FirstTurn:
                FadeOutEmptyStateGreeting();
                break;
            case LanaFlightKind.ReturnHome:
                RevealEmptyStateGreeting();
                break;
            default:
                SyncEmptyStateLayerImmediate();
                break;
        }

        var canFly = e.Kind != LanaFlightKind.None &&
                     !MotionHelper.ReduceMotion &&
                     _hasValidLanaRect &&
                     _topLevel is not null &&
                     IsEffectivelyVisible &&
                     _conversationStage.Bounds.Width > 0 &&
                     _conversationStage.Bounds.Height > 0;

        if (canFly)
        {
            if (e.Kind is LanaFlightKind.FirstTurn or LanaFlightKind.NextTurn &&
                e.TargetAssistantMessage is { IsThinking: true } targetMsg)
            {
                targetMsg.IsThinkingRevealed = false;
            }

            var origin = LanaStageMotion.ClampFlightOriginToViewport(_currentLanaRect, _conversationStage.Bounds.Height);
            _hasPendingFlight = true;
            _pendingFlightKind = e.Kind;
            _pendingFlightTarget = e.TargetAssistantMessage;
            _pendingFlightOrigin = origin;
            ApplyLanaRect(origin, duringFlight: true);
            Dispatcher.UIThread.Post(TryAdvanceLanaPosition, DispatcherPriority.Loaded);
        }
        else
        {
            ReleaseThinkingReveal(e.TargetAssistantMessage);
            SyncDockedLanaPosition();
            Dispatcher.UIThread.Post(SyncDockedLanaPosition, DispatcherPriority.Loaded);
        }
    }

    private void OnConversationStageLayoutUpdated(object? sender, EventArgs e)
    {
        if (_isApplyingLanaRect || ViewModel is null || !IsEffectivelyVisible)
        {
            return;
        }

        TryAdvanceLanaPosition();
    }

    private void TryAdvanceLanaPosition()
    {
        if (ViewModel is null || !IsEffectivelyVisible || _conversationStage.Bounds.Width <= 0 || _conversationStage.Bounds.Height <= 0)
        {
            return;
        }

        if (_hasPendingFlight)
        {
            var dest = ResolveTargetRect(_pendingFlightTarget);
            if (dest is not { } targetRect)
            {
                return;
            }

            _hasPendingFlight = false;
            var pendingTarget = _pendingFlightTarget;
            _pendingFlightTarget = null;
            var dist = double.Hypot(targetRect.X - _pendingFlightOrigin.X, targetRect.Y - _pendingFlightOrigin.Y);
            var sizeDiff = Math.Abs(targetRect.Width - _pendingFlightOrigin.Width);
            if (dist < 2.0 && sizeDiff < 1.0)
            {
                ApplyLanaRect(targetRect, duringFlight: false);
                ReleaseThinkingReveal(pendingTarget);
                if (_pendingFlightKind is LanaFlightKind.FirstTurn or LanaFlightKind.NextTurn)
                {
                    _stageLanaAvatar.TriggerBounce(-0.9);
                }
            }
            else
            {
                StartLanaFlight(_pendingFlightOrigin, pendingTarget, _pendingFlightKind);
            }

            return;
        }

        if (!_isFlightRunning)
        {
            SyncDockedLanaPosition();
        }
    }

    private void StartLanaFlight(Rect fromRect, ChatMessageViewModel? targetMsg, LanaFlightKind kind)
    {
        if (_topLevel is null || MotionHelper.ReduceMotion)
        {
            ReleaseThinkingReveal(targetMsg);
            SyncDockedLanaPosition();
            return;
        }

        var token = ++_flightToken;
        _isFlightRunning = true;
        _flightThinkingRevealed = false;
        _activeFlightKind = kind;
        _activeFlightTarget = targetMsg;
        _activeFlightFrom = fromRect;
        _flightStartTimestamp = TimeSpan.Zero;
        RequestNextFlightFrame(token);
    }

    private void RequestNextFlightFrame(int token)
    {
        if (_flightFrameRequested || !_isFlightRunning || token != _flightToken || _topLevel is null)
        {
            return;
        }

        _flightFrameRequested = true;
        _topLevel.RequestAnimationFrame(ts => OnFlightFrame(ts, token));
    }

    private void OnFlightFrame(TimeSpan timestamp, int token)
    {
        _flightFrameRequested = false;
        if (!_isFlightRunning || token != _flightToken || !IsEffectivelyVisible || _topLevel is null)
        {
            _isFlightRunning = false;
            ReleaseThinkingReveal(_activeFlightTarget);
            _activeFlightTarget = null;
            return;
        }

        if (_flightStartTimestamp == TimeSpan.Zero)
        {
            _flightStartTimestamp = timestamp;
        }

        var durationMs = LanaStageMotion.GetDurationMs(_activeFlightKind);
        var elapsedMs = (timestamp - _flightStartTimestamp).TotalMilliseconds;
        var t = durationMs <= 0 ? 1.0 : Math.Clamp(elapsedMs / durationMs, 0.0, 1.0);

        if (!_flightThinkingRevealed && t >= LanaStageMotion.GetThinkingRevealThreshold(_activeFlightKind))
        {
            _flightThinkingRevealed = true;
            ReleaseThinkingReveal(_activeFlightTarget);
        }

        var liveDest = ResolveTargetRect(_activeFlightTarget);
        if (liveDest is { } dest)
        {
            var rect = LanaStageMotion.InterpolateFlightRect(_activeFlightFrom, dest, t, _activeFlightKind);
            ApplyLanaRect(rect, duringFlight: t < 1.0);
        }

        if (t < 1.0)
        {
            RequestNextFlightFrame(token);
            return;
        }

        _isFlightRunning = false;
        ReleaseThinkingReveal(_activeFlightTarget);
        _activeFlightTarget = null;
        var impulse = LanaStageMotion.GetLandingBounceImpulse(_activeFlightKind);
        if (impulse != 0)
        {
            _stageLanaAvatar.TriggerBounce(impulse);
        }

        SyncDockedLanaPosition();
    }

    private void CancelLanaFlight()
    {
        ++_flightToken;
        _hasPendingFlight = false;
        _isFlightRunning = false;
        _flightFrameRequested = false;
        _flightThinkingRevealed = false;
        ReleaseThinkingReveal(_pendingFlightTarget);
        ReleaseThinkingReveal(_activeFlightTarget);
        _pendingFlightTarget = null;
        _activeFlightTarget = null;
    }

    private static void ReleaseThinkingReveal(ChatMessageViewModel? msg)
    {
        if (msg is not null && !msg.IsThinkingRevealed)
        {
            msg.IsThinkingRevealed = true;
        }
    }

    private void SyncDockedLanaPosition()
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        if (vm.IsEmptyState)
        {
            _anchoredAssistantMessage = null;
            var home = ResolveHomeRect();
            if (home is { } h)
            {
                ApplyLanaRect(h, duringFlight: false);
            }

            return;
        }

        _anchoredAssistantMessage = vm.LatestAssistantMessage;
        if (_anchoredAssistantMessage is null)
        {
            _stageLanaAvatar.IsVisible = false;
            return;
        }

        var slot = ResolveMessageSlotRect(_anchoredAssistantMessage);
        if (slot is { } s)
        {
            ApplyLanaRect(s, duringFlight: false);
        }
    }

    private Rect? ResolveTargetRect(ChatMessageViewModel? targetAssistantMessage) =>
        targetAssistantMessage is not null
            ? ResolveMessageSlotRect(targetAssistantMessage)
            : ResolveHomeRect();

    private Rect? ResolveHomeRect()
    {
        if (_emptyStateLayer.IsVisible && _homeLanaAnchor.Bounds.Width > 0 && _homeLanaAnchor.Bounds.Height > 0)
        {
            var pt = _homeLanaAnchor.TranslatePoint(new Point(0, 0), _conversationStage);
            if (pt is { } p)
            {
                _lastHomeRect = new Rect(p.X, p.Y, _homeLanaAnchor.Bounds.Width, _homeLanaAnchor.Bounds.Height);
                _hasLastHomeRect = true;
                return _lastHomeRect;
            }
        }

        if (_hasLastHomeRect)
        {
            return _lastHomeRect;
        }

        if (_conversationStage.Bounds.Width > 0 && _conversationStage.Bounds.Height > 0)
        {
            var x = (_conversationStage.Bounds.Width - LanaStageMotion.HeroSize) * 0.5;
            var y = Math.Max(16, (_conversationStage.Bounds.Height - LanaStageMotion.HeroSize) * 0.5 - 52);
            return new Rect(x, y, LanaStageMotion.HeroSize, LanaStageMotion.HeroSize);
        }

        return null;
    }

    private Rect? ResolveMessageSlotRect(ChatMessageViewModel? targetMsg)
    {
        if (targetMsg is null || ViewModel is not { } vm)
        {
            return null;
        }

        var idx = vm.Messages.IndexOf(targetMsg);
        if (idx < 0)
        {
            return null;
        }

        var container = _messagesList.ContainerFromIndex(idx);
        if (container is null)
        {
            return null;
        }

        var slot = container.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(c => c.Name == "LanaMessageSlot");
        if (slot is null || !slot.IsEffectivelyVisible || slot.Bounds.Width <= 0 || slot.Bounds.Height <= 0)
        {
            return null;
        }

        var pt = slot.TranslatePoint(new Point(0, 0), _conversationStage);
        return pt is { } p
            ? new Rect(p.X, p.Y, slot.Bounds.Width, slot.Bounds.Height)
            : null;
    }

    private void ApplyLanaRect(Rect rect, bool duringFlight)
    {
        var visible = duringFlight || LanaStageMotion.IsRectInViewport(rect, _conversationStage.Bounds.Height);
        var geomChanged = !_hasValidLanaRect ||
                          Math.Abs(rect.X - _currentLanaRect.X) > 0.1 ||
                          Math.Abs(rect.Y - _currentLanaRect.Y) > 0.1 ||
                          Math.Abs(rect.Width - _currentLanaRect.Width) > 0.1 ||
                          Math.Abs(rect.Height - _currentLanaRect.Height) > 0.1;

        _currentLanaRect = rect;
        _hasValidLanaRect = true;

        if (_stageLanaAvatar.IsVisible != visible)
        {
            _stageLanaAvatar.IsVisible = visible;
        }

        if (!geomChanged)
        {
            return;
        }

        _isApplyingLanaRect = true;
        try
        {
            Canvas.SetLeft(_stageLanaAvatar, rect.X);
            Canvas.SetTop(_stageLanaAvatar, rect.Y);
            if (Math.Abs(_stageLanaAvatar.Width - rect.Width) > 0.05)
            {
                _stageLanaAvatar.Width = rect.Width;
            }

            if (Math.Abs(_stageLanaAvatar.Height - rect.Height) > 0.05)
            {
                _stageLanaAvatar.Height = rect.Height;
            }
        }
        finally
        {
            _isApplyingLanaRect = false;
        }
    }

    private void SyncEmptyStateLayerImmediate()
    {
        ++_greetingFadeToken;
        var isEmpty = ViewModel?.IsEmptyState ?? true;
        _emptyStateLayer.IsVisible = isEmpty;
        _emptyStateLayer.IsHitTestVisible = isEmpty;
        _emptyStateGreeting.Transitions = null;
        _emptyStateGreeting.Opacity = 1;
        _emptyStateGreeting.RenderTransform = null;
    }

    private void RevealEmptyStateGreeting()
    {
        ++_greetingFadeToken;
        _emptyStateLayer.IsVisible = true;
        _emptyStateLayer.IsHitTestVisible = true;
        MotionHelper.PlayEnter(_emptyStateGreeting, offsetX: 0, offsetY: 10, durationMs: 220);
    }

    private void FadeOutEmptyStateGreeting()
    {
        if (MotionHelper.ReduceMotion || !_emptyStateLayer.IsVisible)
        {
            SyncEmptyStateLayerImmediate();
            return;
        }

        var token = ++_greetingFadeToken;
        _emptyStateLayer.IsHitTestVisible = false;
        _emptyStateGreeting.Transitions =
        [
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(200),
                Easing = new CubicEaseOut(),
            },
            new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(200),
                Easing = new CubicEaseOut(),
            },
        ];
        _emptyStateGreeting.Opacity = 0;
        _emptyStateGreeting.RenderTransform = TransformOperations.Parse("translate(0px, -12px)");

        DispatcherTimer.RunOnce(() =>
        {
            if (token != _greetingFadeToken || ViewModel is { IsEmptyState: true })
            {
                return;
            }

            _emptyStateLayer.IsVisible = false;
            _emptyStateGreeting.Transitions = null;
            _emptyStateGreeting.Opacity = 1;
            _emptyStateGreeting.RenderTransform = null;
        }, TimeSpan.FromMilliseconds(220));
    }

    private void OnComposerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        if (e.Property == TextBox.TextProperty || e.Property == TextBox.CaretIndexProperty)
        {
            vm.UpdateMentionState(_composerBox.Text ?? string.Empty, _composerBox.CaretIndex);
        }
    }

    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        if (vm.IsMentionOpen)
        {
            if (e.Key == Key.Down)
            {
                vm.MoveMentionSelection(1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                vm.MoveMentionSelection(-1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                vm.CloseMention();
                e.Handled = true;
                return;
            }

            if (e.Key is Key.Enter or Key.Tab)
            {
                ApplyMentionAndUpdateBox(null);
                e.Handled = true;
                return;
            }
        }

        // Ctrl+V 支持粘贴剪贴板文件与截图图片
        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (PlatformServices.Clipboard.TryGetFilePaths(out var files) && files.Length > 0)
            {
                vm.AddAttachmentFiles(files);
                e.Handled = true;
                return;
            }

            if (PlatformServices.Clipboard.TryGetImageBytes(out var imgBytes, out var isPng) && imgBytes.Length > 0)
            {
                if (vm.AddPastedImageBytes(imgBytes, isPng))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (vm.CanSend)
            {
                _autoFollowBottom = true;
                _ = vm.SendAsync();
            }

            e.Handled = true;
        }
    }

    private void ApplyMentionAndUpdateBox(MentionFileCandidate? explicitCandidate)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var currentText = _composerBox.Text ?? string.Empty;
        if (!vm.ApplyMentionCandidate(explicitCandidate, currentText, out var needsBrowse, out var updatedText, out var updatedCaret))
        {
            return;
        }

        vm.DraftInput = updatedText;
        Dispatcher.UIThread.Post(() =>
        {
            _composerBox.Focus();
            _composerBox.CaretIndex = Math.Clamp(updatedCaret, 0, _composerBox.Text?.Length ?? 0);
        }, DispatcherPriority.Input);

        if (needsBrowse)
        {
            _ = PickAttachmentFilesAsync(targetEditMessage: null);
        }
    }

    private void OnMentionCandidatePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: MentionFileCandidate candidate })
        {
            ApplyMentionAndUpdateBox(candidate);
        }

        e.Handled = true;
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm || !e.DataTransfer.Contains(DataFormat.File))
        {
            return;
        }

        _dragLeaveTimer.Stop();
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
        if (!vm.IsDragOver)
        {
            vm.IsDragOver = true;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm || !e.DataTransfer.Contains(DataFormat.File))
        {
            return;
        }

        _dragLeaveTimer.Stop();
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
        if (!vm.IsDragOver)
        {
            vm.IsDragOver = true;
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        // 子控件边界冒泡会紧跟 DragEnter/DragOver，延迟 100ms 再隐藏以彻底消除移动闪屏
        _dragLeaveTimer.Stop();
        _dragLeaveTimer.Start();
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        _dragLeaveTimer.Stop();
        if (ViewModel is not { } vm)
        {
            return;
        }

        vm.IsDragOver = false;
        if (!e.DataTransfer.Contains(DataFormat.File))
        {
            return;
        }

        e.Handled = true;
        var files = e.DataTransfer.TryGetFiles();
        if (files is not null)
        {
            var paths = files
                .Select(f => f.TryGetLocalPath())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Cast<string>();
            var editing = vm.Messages.FirstOrDefault(m => m.IsEditing);
            if (editing is not null)
            {
                vm.AddEditAttachmentFiles(editing, paths);
            }
            else
            {
                vm.AddAttachmentFiles(paths);
            }
        }
    }

    private async void OnPickAttachmentPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        await PickAttachmentFilesAsync(targetEditMessage: null);
    }

    private async Task PickAttachmentFilesAsync(ChatMessageViewModel? targetEditMessage)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
        {
            return;
        }

        var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择图片或文档附件",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("支持的全部图片与文档")
                {
                    Patterns = AttachmentFormats.AllPickerPatterns,
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = ["*.*"],
                },
            ],
        });

        var paths = picked
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Cast<string>();

        if (targetEditMessage is not null)
        {
            vm.AddEditAttachmentFiles(targetEditMessage, paths);
        }
        else
        {
            vm.AddAttachmentFiles(paths);
        }
    }

    // ===== 历史消息编辑（铅笔按钮 / 附件增删 / 快捷键） =====

    private void OnEditMessagePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChatMessageViewModel msg } && ViewModel is { } vm)
        {
            vm.BeginEditMessage(msg);
        }

        e.Handled = true;
    }

    private void OnCancelEditMessagePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChatMessageViewModel msg } && ViewModel is { } vm)
        {
            vm.CancelEditMessage(msg);
        }

        e.Handled = true;
    }

    private void OnCommitEditMessagePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChatMessageViewModel msg } && ViewModel is { } vm && msg.CanSaveEdit)
        {
            _autoFollowBottom = true;
            _ = vm.CommitEditMessageAsync(msg);
        }

        e.Handled = true;
    }

    private async void OnPickEditAttachmentPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control { DataContext: ChatMessageViewModel msg })
        {
            await PickAttachmentFilesAsync(targetEditMessage: msg);
        }
    }

    private void OnRemoveEditAttachmentPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChatAttachmentItem att } ctrl && ViewModel is { } vm)
        {
            var hostMsg = ctrl.GetVisualAncestors()
                .OfType<Control>()
                .Select(c => c.DataContext)
                .OfType<ChatMessageViewModel>()
                .FirstOrDefault();
            if (hostMsg is not null)
            {
                vm.RemoveEditAttachment(hostMsg, att);
            }
        }

        e.Handled = true;
    }

    private void OnEditMessageBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ChatMessageViewModel msg } || ViewModel is not { } vm)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            vm.CancelEditMessage(msg);
            e.Handled = true;
            return;
        }

        // 编辑框内也支持 Ctrl+V 粘贴截图与文件
        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (PlatformServices.Clipboard.TryGetFilePaths(out var files) && files.Length > 0)
            {
                vm.AddEditAttachmentFiles(msg, files);
                e.Handled = true;
                return;
            }

            if (PlatformServices.Clipboard.TryGetImageBytes(out var imgBytes, out var isPng) && imgBytes.Length > 0)
            {
                if (vm.AddEditPastedImageBytes(msg, imgBytes, isPng))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (msg.CanSaveEdit)
            {
                _autoFollowBottom = true;
                _ = vm.CommitEditMessageAsync(msg);
            }

            e.Handled = true;
        }
    }

    // ===== 顶栏与侧栏 =====

    private void OnToggleHistoryPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ToggleHistorySidebarCommand.Execute(null);
        e.Handled = true;
    }

    private void OnNewSessionPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.StartNewSessionCommand.Execute(null);
        FocusComposer();
        e.Handled = true;
    }

    private void OnSessionItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChatSessionItemViewModel session })
        {
            ViewModel?.SelectSession(session);
        }

        e.Handled = true;
    }

    private void OnDeleteSessionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChatSessionItemViewModel session })
        {
            ViewModel?.DeleteSession(session);
        }

        e.Handled = true;
    }

    private void OnModelPillPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ToggleModelDropdownCommand.Execute(null);
        e.Handled = true;
    }

    private void OnDismissDropdownPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.CloseModelDropdown();
        e.Handled = true;
    }

    private void OnSelectModelItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ModelSwitcherItemViewModel item })
        {
            ViewModel?.SelectModelProfile(item);
        }

        e.Handled = true;
    }

    private void OnOpenModelSettingsPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.OpenModelSettingsCommand.Execute(null);
        e.Handled = true;
    }

    private void OnRemoveAttachmentPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChatAttachmentItem item })
        {
            ViewModel?.RemoveAttachment(item);
        }

        e.Handled = true;
    }

    private void OnSendPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { CanSend: true } vm)
        {
            _autoFollowBottom = true;
            _ = vm.SendAsync();
        }

        e.Handled = true;
    }

    private void OnStopPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.StopGenerationCommand.Execute(null);
        e.Handled = true;
    }

    private async void OnCopyMessagePressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control { DataContext: ChatMessageViewModel msg } && ViewModel is { } vm)
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is not null)
            {
                await top.Clipboard.SetTextAsync(msg.Text);
            }

            await vm.MarkCopiedAsync(msg);
        }
    }

    private void OnRetryMessagePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChatMessageViewModel msg } && ViewModel is { } vm)
        {
            _autoFollowBottom = true;
            _ = vm.RetryMessageAsync(msg);
        }

        e.Handled = true;
    }

    // ===== 提案与细粒度撤销卡片交互 =====

    private void OnToggleProposalItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChatProposalItemViewModel item })
        {
            item.ToggleCheckbox();
        }

        e.Handled = true;
    }

    private void OnUndoSingleProposalItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChatProposalItemViewModel item } ctrl)
        {
            var card = ctrl.GetVisualAncestors()
                .OfType<Control>()
                .Select(c => c.DataContext)
                .OfType<ChatProposalCardViewModel>()
                .FirstOrDefault();
            card?.UndoSingle(item);
        }

        e.Handled = true;
    }

    private void OnUndoSelectedProposalItemsPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChatProposalCardViewModel card })
        {
            card.UndoSelected();
        }

        e.Handled = true;
    }

    private void OnApplyProposalPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChatProposalCardViewModel { CanApply: true } card })
        {
            card.Apply();
        }

        e.Handled = true;
    }

    private void OnDiscardProposalPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChatProposalCardViewModel card })
        {
            card.Discard();
        }

        e.Handled = true;
    }

    private void OnUndoProposalPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChatProposalCardViewModel card })
        {
            card.UndoAll();
        }

        e.Handled = true;
    }

    private void OnProposalPriorityPillPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChatProposalItemViewModel item } pill && ViewModel is { } vm)
        {
            var card = pill.GetVisualAncestors()
                .OfType<Control>()
                .Select(c => c.DataContext)
                .OfType<ChatProposalCardViewModel>()
                .FirstOrDefault();
            if (card is not null && item.CanEditTaskProps)
            {
                vm.OpenProposalPriorityMenu(card, item);
                if (vm.IsProposalPriorityMenuOpen)
                {
                    var pos = pill.TranslatePoint(new Point(0, 0), _chatStageHost);
                    var placement = pos is { } p && _chatStageHost.Bounds.Height > 0 && p.Y > _chatStageHost.Bounds.Height * 0.65
                        ? OverlayPlacement.AboveLeftAligned
                        : OverlayPlacement.BelowLeftAligned;
                    OverlayPositioner.PlaceAndShow(_chatStageHost, _proposalPriorityOverlay, pill, placement);
                }
            }
        }

        e.Handled = true;
    }

    private void OnProposalReminderPillPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChatProposalItemViewModel item } pill && ViewModel is { } vm)
        {
            var card = pill.GetVisualAncestors()
                .OfType<Control>()
                .Select(c => c.DataContext)
                .OfType<ChatProposalCardViewModel>()
                .FirstOrDefault();
            if (card is not null && item.CanEditTaskProps)
            {
                vm.OpenProposalReminderEditor(card, item);
                if (vm.IsProposalReminderEditorOpen)
                {
                    var pos = pill.TranslatePoint(new Point(0, 0), _chatStageHost);
                    var placement = pos is { } p && _chatStageHost.Bounds.Height > 0 && p.Y > _chatStageHost.Bounds.Height * 0.48
                        ? OverlayPlacement.AboveLeftAligned
                        : OverlayPlacement.BelowLeftAligned;
                    OverlayPositioner.PlaceAndShow(_chatStageHost, _proposalReminderOverlay, pill, placement);
                }
            }
        }

        e.Handled = true;
    }

    private void OnDismissProposalOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.CloseProposalOverlays();
        e.Handled = true;
    }

    private void OnOverlayCardPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private void OnPickProposalPriorityHighPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.SelectActiveProposalPriority(TaskPriority.High);
        e.Handled = true;
    }

    private void OnPickProposalPriorityMediumPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.SelectActiveProposalPriority(TaskPriority.Medium);
        e.Handled = true;
    }

    private void OnPickProposalPriorityLowPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.SelectActiveProposalPriority(TaskPriority.Low);
        e.Handled = true;
    }

    private void OnPickProposalPriorityNonePressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.SelectActiveProposalPriority(TaskPriority.None);
        e.Handled = true;
    }
}

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VGList.App.Assistant;
using VGList.App.Settings;
using VGList.Widgets.Host;
using VGList.Widgets.Support;

namespace VGList.Widgets.Today;

/// <summary>今日日程小组件窗口：轻量录入 + 任务详情面板入口。</summary>
public partial class TodayWidgetWindow : Window
{
    private const double BaseWidth = 331;
    private const double BaseHeight = 454;
    private const double InputMinHeight = 38;
    private const double InputMaxHeight = 118;
    private const double InputLineHeight = 20;
    private const double InputVerticalPadding = 18;

    private readonly TodayWidgetViewModel _vm;
    private readonly WidgetHost _host;
    private readonly Border _rootCard;
    private readonly Border _backdrop;
    private readonly Border _inputBorder;
    private readonly Panel _inputPanel;
    private readonly TextBox _inputBox;
    private readonly Border _dateChip;
    private readonly Popup _viewMenuPopup;
    private readonly Popup _moreMenuPopup;
    private readonly ScrollViewer _taskScroll;
    private readonly Border _scrollTrack;
    private readonly Border _scrollThumb;
    private readonly Transitions _viewTransitions;
    private TaskDetailWindow? _detailWindow;
    private WidgetSettingsWindow? _settingsWindow;
    private int _viewAnimationToken;
    private bool _resizingInput;
    private bool _chipInline = true;
    private bool _draggingThumb;
    private double _dragStartY;
    private double _dragStartOffset;
    private double _thumbTop;
    private long _lastInputPressTicks;

    /// <summary>刚刚在输入区内按下（窗口激活可能晚一步清掉焦点，这段时间内不因失焦退出编辑态）。</summary>
    private bool IsInputPressRecent =>
        _lastInputPressTicks != 0 &&
        System.Diagnostics.Stopwatch.GetElapsedTime(_lastInputPressTicks) < TimeSpan.FromMilliseconds(500);

    public TodayWidgetWindow(TodayWidgetViewModel vm, SettingsStore settingsStore)
    {
        InitializeComponent();

        _vm = vm;
        DataContext = vm;

        _rootCard = Find<Border>("RootCard");
        _backdrop = Find<Border>("Backdrop");
        _inputBorder = Find<Border>("InputBorder");
        _inputPanel = Find<Panel>("InputPanel");
        _inputBox = Find<TextBox>("InputBox");
        _dateChip = Find<Border>("DateChip");
        _viewMenuPopup = Find<Popup>("ViewMenuPopup");
        _moreMenuPopup = Find<Popup>("MoreMenuPopup");
        _taskScroll = Find<ScrollViewer>("TaskScroll");
        _scrollTrack = Find<Border>("ScrollTrack");
        _scrollThumb = Find<Border>("ScrollThumb");

        _viewTransitions =
        [
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(160),
                Easing = new CubicEaseOut(),
            },
            new TransformOperationsTransition
            {
                Property = RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(160),
                Easing = new CubicEaseOut(),
            },
        ];

        _host = new WidgetHost(this, settingsStore, BaseWidth, BaseHeight);
        _host.Attach();

        ApplyStoredSettings();

        // 编辑态只由「点击录入区」触发；点到卡片其他区域立即收起滑块与弹窗。
        _rootCard.AddHandler(PointerPressedEvent, OnCardPointerPressed, RoutingStrategies.Tunnel);

        // Enter 在 TextBox.OnKeyDown 内部被先行消费，冒泡处理器收不到，必须在隧道阶段拦截。
        _inputBox.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        _inputBox.SizeChanged += (_, _) => UpdateInputHeight();
        _dateChip.SizeChanged += (_, _) => UpdateInputHeight();
        _inputBox.LostFocus += (_, _) => OnInputLostFocus();

        // 覆盖式滚动条跟随内容、视口与偏移变化。
        _taskScroll.ScrollChanged += (_, _) => UpdateScrollBar();
        _scrollTrack.SizeChanged += (_, _) => UpdateScrollBar();

        // 点到窗口外（任务栏、桌面、详情面板）同样要收回滑块与日历。
        Deactivated += (_, _) =>
        {
            if (!_vm.IsCalendarOpen)
            {
                _vm.EndEditing();
            }
        };

        _vm.MorningBriefActivated += (_, info) => MorningBriefActivated?.Invoke(this, info);

        // 编辑态与日期文案都会改变日期 chip 的宽度，需要重新决定它放哪一行。
        _vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(TodayWidgetViewModel.IsEditing):
                case nameof(TodayWidgetViewModel.DateLabel):
                case nameof(TodayWidgetViewModel.TitleLabel):
                    Dispatcher.UIThread.Post(UpdateInputHeight, DispatcherPriority.Background);
                    break;

                case nameof(TodayWidgetViewModel.Groups):
                case nameof(TodayWidgetViewModel.CompletedTasks):
                case nameof(TodayWidgetViewModel.CompletedExpanded):
                case nameof(TodayWidgetViewModel.ShowCompletedSection):
                    Dispatcher.UIThread.Post(UpdateScrollBar, DispatcherPriority.Background);
                    break;
            }
        };

        Closed += (_, _) =>
        {
            _settingsWindow?.Close();
            _detailWindow?.Close();
            _vm.Dispose();
        };
    }

    private T Find<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new InvalidOperationException($"{name} 未在 XAML 中定义。");

    // ---------- 窗口行为 ----------

    private void OnDragAreaPressed(object? sender, PointerPressedEventArgs e) => _host.BeginDrag(e);

    private void OnRootPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Border { Name: "RootCard" or "Backdrop" })
        {
            _host.BeginDrag(e);
        }
    }

    private void OnResizeWestPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.West, e);

    private void OnResizeEastPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.East, e);

    private void OnResizeNorthPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.North, e);

    private void OnResizeSouthPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.South, e);

    private void OnResizeNorthWestPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.NorthWest, e);

    private void OnResizeNorthEastPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.NorthEast, e);

    private void OnResizeSouthWestPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.SouthWest, e);

    private void OnResizeSouthEastPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.SouthEast, e);

    private void BeginResize(WindowEdge edge, PointerPressedEventArgs e)
    {
        if (_host.IsLocked || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        BeginResizeDrag(edge, e);
        e.Handled = true;
    }

    /// <summary>点击录入区进入编辑态，点击任何其他区域退出编辑态并把焦点移出输入框。</summary>
    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var source = e.Source as Visual;

        if (IsWithin(source, _inputBorder))
        {
            _lastInputPressTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            _vm.IsEditing = true;
            FocusInput();
            return;
        }

        _vm.EndEditing();

        if (_inputBox.IsFocused)
        {
            _rootCard.Focus();
        }
    }

    private static bool IsWithin(Visual? source, Visual container)
    {
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, container))
            {
                return true;
            }
        }

        return false;
    }

    // ---------- 视图与设置 ----------

    private void OnViewButtonPressed(object? sender, PointerPressedEventArgs e)
    {
        _viewMenuPopup.IsOpen = !_viewMenuPopup.IsOpen;
        e.Handled = true;
    }

    private void OnViewMenuSelected(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string tag } &&
            Enum.TryParse<WidgetViewMode>(tag, out var view) &&
            view != _vm.ActiveView)
        {
            _vm.SetView(view);
            PlayViewSwitchAnimation();
        }

        _viewMenuPopup.IsOpen = false;
        e.Handled = true;
    }

    private void PlayViewSwitchAnimation()
    {
        var token = ++_viewAnimationToken;
        _taskScroll.Transitions = null;
        _taskScroll.Opacity = 0;
        _taskScroll.RenderTransform = TransformOperations.Parse("translateY(6px)");

        Dispatcher.UIThread.Post(
            () =>
            {
                if (token != _viewAnimationToken)
                {
                    return;
                }

                _taskScroll.Transitions = _viewTransitions;
                _taskScroll.Opacity = 1;
                _taskScroll.RenderTransform = TransformOperations.Parse("translateY(0px)");
            },
            DispatcherPriority.Render);
    }

    private void OnMoreButtonPressed(object? sender, PointerPressedEventArgs e)
    {
        _moreMenuPopup.IsOpen = !_moreMenuPopup.IsOpen;
        e.Handled = true;
    }

    private void OnOpenSettingsPressed(object? sender, PointerPressedEventArgs e)
    {
        _moreMenuPopup.IsOpen = false;
        OpenSettings();
        e.Handled = true;
    }

    /// <summary>设置是独立的可拖动窗口，打开期间保持置顶，避免被其他应用盖住。</summary>
    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        var window = new WidgetSettingsWindow(_host.Settings);
        _settingsWindow = window;

        window.OpacityChanged += (_, value) =>
        {
            _backdrop.Opacity = value;
            _host.Settings.Opacity = value;
        };
        window.OpacityCommitted += (_, _) => PersistSettings();

        window.TopmostChanged += (_, value) => SetWidgetTopmost(value);

        window.ShowInputChanged += (_, value) =>
        {
            _inputPanel.IsVisible = value;
            if (!value)
            {
                _vm.EndEditing();
            }

            _host.Settings.ShowInput = value;
            PersistSettings();
        };

        window.ShowCompletedChanged += (_, value) =>
        {
            _vm.ShowCompleted = value;
            _host.Settings.ShowCompleted = value;
            PersistSettings();
        };

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_settingsWindow, window))
            {
                _settingsWindow = null;
            }
        };

        window.Opened += (_, _) => WidgetWindowPositioner.PlaceBeside(this, window, preferRight: false);

        // 小组件可能挂在桌面层，设置窗口必须置顶才能操作。
        window.Topmost = true;
        window.Show();
    }

    public event EventHandler? SettingsUpdated;

    public WidgetSettings CurrentSettings => _host.Settings;

    public void SetBackdropOpacity(double opacity, bool persist = true)
    {
        var clamped = Math.Clamp(opacity, 0.4, 1.0);
        _backdrop.Opacity = clamped;
        _host.Settings.Opacity = clamped;
        if (persist)
        {
            PersistSettings();
        }
    }

    public void SetWidgetTopmost(bool topmost)
    {
        _host.SetTopmost(topmost);
        SettingsUpdated?.Invoke(this, EventArgs.Empty);
    }

    public void SetLocked(bool locked)
    {
        _host.SetLocked(locked);
        SettingsUpdated?.Invoke(this, EventArgs.Empty);
    }

    public void SetClickThrough(bool clickThrough)
    {
        _host.SetClickThrough(clickThrough);
        SettingsUpdated?.Invoke(this, EventArgs.Empty);
    }

    public void SetShowInput(bool showInput)
    {
        _inputPanel.IsVisible = showInput;
        if (!showInput)
        {
            _vm.EndEditing();
        }

        _host.Settings.ShowInput = showInput;
        PersistSettings();
    }

    public void SetShowCompleted(bool showCompleted)
    {
        _vm.ShowCompleted = showCompleted;
        _host.Settings.ShowCompleted = showCompleted;
        PersistSettings();
    }

    public void ResetPosition() => _host.ResetToDefault();

    private void ApplyStoredSettings()
    {
        var settings = _host.Settings;

        _backdrop.Opacity = Math.Clamp(settings.Opacity, 0.4, 1.0);
        _vm.ShowCompleted = settings.ShowCompleted;
        _inputPanel.IsVisible = settings.ShowInput;
    }

    private void PersistSettings()
    {
        _host.SaveSettings();
        SettingsUpdated?.Invoke(this, EventArgs.Empty);
    }

    // ---------- 输入区 ----------

    private void OnInputLostFocus()
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!_inputBox.IsFocused && !_vm.IsCalendarOpen && !IsInputPressRecent)
                {
                    _vm.IsEditing = false;
                }
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    /// 点击输入框内的空白或日期 chip 区域时命中的是外层边框，而不是文字框；
    /// 首次点击桌面小组件时窗口还在激活过程中，立即 Focus 会被晚一步的失焦流程清掉，
    /// 所以再补一次焦点，保证点完就能直接打字。
    /// </summary>
    private void FocusInput()
    {
        _inputBox.Focus();

        DispatcherTimer.RunOnce(
            () =>
            {
                if (_vm.IsEditing && !_inputBox.IsFocused)
                {
                    _inputBox.Focus();
                }
            },
            TimeSpan.FromMilliseconds(120));
    }

    private void OnInputTextChanged(object? sender, TextChangedEventArgs e) => UpdateInputHeight();

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when !e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                _vm.SubmitDraft();
                UpdateInputHeight();
                e.Handled = true;
                break;

            case Key.Escape:
                if (_vm.IsCalendarOpen)
                {
                    _vm.IsCalendarOpen = false;
                }
                else
                {
                    _vm.EndEditing();
                    _rootCard.Focus();
                }

                e.Handled = true;
                break;

            // Tab / Shift+Tab 循环切换优先级，数字键保持正常输入。
            case Key.Tab:
                _vm.CyclePriority(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                break;
        }
    }

    private void UpdateInputHeight()
    {
        if (_resizingInput)
        {
            return;
        }

        // 边框 2 + 左右内边距 18，得到录入区真正可用的文字宽度。
        var fullWidth = _inputBorder.Bounds.Width - 20;
        if (fullWidth <= 0)
        {
            return;
        }

        var text = _inputBox.Text ?? string.Empty;
        var chipWidth = GetDateChipWidth();

        // 日期 chip 优先停在第 1 行右侧；只有首行文字会顶到它时才换到下一行（仍靠右）。
        var inline = chipWidth <= 0 || MeasureLineCount(text, fullWidth - chipWidth - 6) <= 1;
        ApplyChipPlacement(inline);

        var available = inline && chipWidth > 0 ? fullWidth - chipWidth - 6 : fullWidth;
        var lines = MeasureLineCount(text, available);
        var target = Math.Clamp(
            lines * InputLineHeight + InputVerticalPadding,
            InputMinHeight,
            InputMaxHeight);

        if (Math.Abs(_inputBox.Height - target) < 0.5)
        {
            return;
        }

        _resizingInput = true;
        _inputBox.Height = target;
        _resizingInput = false;
    }

    private double GetDateChipWidth()
    {
        var width = _dateChip.Bounds.Width;
        if (width <= 0)
        {
            width = _dateChip.DesiredSize.Width;
        }

        return width > 0 ? width : 60;
    }

    /// <summary>inline=true 时 chip 贴在第 1 行右侧；否则独占第 2 行并右对齐。</summary>
    private void ApplyChipPlacement(bool inline)
    {
        if (_chipInline == inline)
        {
            return;
        }

        _chipInline = inline;
        Grid.SetColumnSpan(_inputBox, inline ? 1 : 2);

        if (inline)
        {
            Grid.SetRow(_dateChip, 0);
            Grid.SetColumn(_dateChip, 1);
            Grid.SetColumnSpan(_dateChip, 1);
            _dateChip.Margin = new Thickness(6, 7, 0, 0);
        }
        else
        {
            Grid.SetRow(_dateChip, 1);
            Grid.SetColumn(_dateChip, 0);
            Grid.SetColumnSpan(_dateChip, 2);
            _dateChip.Margin = new Thickness(0, 0, 0, 7);
        }

        _dateChip.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        _dateChip.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
    }

    private int MeasureLineCount(string text, double maxWidth)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 1;
        }

        var typeface = new Typeface(_inputBox.FontFamily, _inputBox.FontStyle, _inputBox.FontWeight);
        var layout = new TextLayout(
            text,
            typeface,
            _inputBox.FontSize,
            Brushes.White,
            TextAlignment.Left,
            TextWrapping.Wrap,
            TextTrimming.None,
            maxWidth: maxWidth,
            lineHeight: InputLineHeight);

        return Math.Max(1, layout.TextLines.Count);
    }

    // ---------- 覆盖式滚动条 ----------

    private void UpdateScrollBar()
    {
        var trackHeight = _scrollTrack.Bounds.Height;
        var viewport = _taskScroll.Viewport.Height;
        var extent = _taskScroll.Extent.Height;
        var maxOffset = Math.Max(0, extent - viewport);

        if (trackHeight <= 0 || viewport <= 0 || maxOffset < 1)
        {
            _scrollThumb.IsVisible = false;
            return;
        }

        _scrollThumb.IsVisible = true;

        var thumbHeight = Math.Clamp(viewport / extent * trackHeight, 30, trackHeight);
        if (Math.Abs(_scrollThumb.Height - thumbHeight) > 0.5)
        {
            _scrollThumb.Height = thumbHeight;
        }

        var ratio = Math.Clamp(_taskScroll.Offset.Y / maxOffset, 0, 1);
        var top = Math.Round(ratio * (trackHeight - thumbHeight));
        if (Math.Abs(_thumbTop - top) > 0.5)
        {
            _thumbTop = top;
            _scrollThumb.RenderTransform = new TranslateTransform(0, top);
        }
    }

    private void OnScrollThumbPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_scrollThumb).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _draggingThumb = true;
        _dragStartY = e.GetPosition(_scrollTrack).Y;
        _dragStartOffset = _taskScroll.Offset.Y;
        e.Pointer.Capture(_scrollThumb);
        e.Handled = true;
    }

    private void OnScrollThumbMoved(object? sender, PointerEventArgs e)
    {
        if (!_draggingThumb)
        {
            return;
        }

        var travel = _scrollTrack.Bounds.Height - _scrollThumb.Bounds.Height;
        if (travel <= 0)
        {
            return;
        }

        var maxOffset = Math.Max(0, _taskScroll.Extent.Height - _taskScroll.Viewport.Height);
        var delta = e.GetPosition(_scrollTrack).Y - _dragStartY;
        SetScrollOffset(_dragStartOffset + delta / travel * maxOffset);
        e.Handled = true;
    }

    private void OnScrollThumbReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndThumbDrag(e.Pointer);
        e.Handled = true;
    }

    private void OnScrollThumbCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndThumbDrag(null);

    private void EndThumbDrag(IPointer? pointer)
    {
        if (!_draggingThumb)
        {
            return;
        }

        _draggingThumb = false;
        pointer?.Capture(null);
    }

    private void OnScrollTrackPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsWithin(e.Source as Visual, _scrollThumb))
        {
            return;
        }

        var trackHeight = _scrollTrack.Bounds.Height;
        var thumbHeight = _scrollThumb.Bounds.Height;
        if (trackHeight <= 0 || thumbHeight <= 0)
        {
            return;
        }

        // 点击轨道空白处：滑块中心直接跳到点击位置。
        var y = e.GetPosition(_scrollTrack).Y - thumbHeight / 2;
        var ratio = Math.Clamp(y / Math.Max(1, trackHeight - thumbHeight), 0, 1);
        var maxOffset = Math.Max(0, _taskScroll.Extent.Height - _taskScroll.Viewport.Height);
        SetScrollOffset(ratio * maxOffset);
        e.Handled = true;
    }

    private void OnScrollTrackWheel(object? sender, PointerWheelEventArgs e)
    {
        SetScrollOffset(_taskScroll.Offset.Y - e.Delta.Y * 56);
        e.Handled = true;
    }

    private void SetScrollOffset(double y)
    {
        var maxOffset = Math.Max(0, _taskScroll.Extent.Height - _taskScroll.Viewport.Height);
        _taskScroll.Offset = new Vector(_taskScroll.Offset.X, Math.Clamp(y, 0, maxOffset));
    }

    // ---------- 日期 ----------

    private void OnDateChipPressed(object? sender, PointerPressedEventArgs e)
    {
        _vm.ToggleCalendar();
        e.Handled = true;
    }

    private void OnQuickToday(object? sender, PointerPressedEventArgs e) => PickDate(_vm.Today, e);

    private void OnQuickTomorrow(object? sender, PointerPressedEventArgs e) => PickDate(_vm.Today.AddDays(1), e);

    private void OnQuickDayAfter(object? sender, PointerPressedEventArgs e) => PickDate(_vm.Today.AddDays(2), e);

    private void PickDate(DateOnly date, PointerPressedEventArgs e)
    {
        _vm.PickDate(date);
        RestoreInputFocus();
        e.Handled = true;
    }

    private void OnClearDate(object? sender, PointerPressedEventArgs e)
    {
        _vm.ClearDate();
        RestoreInputFocus();
        e.Handled = true;
    }

    private void OnConfirmDate(object? sender, PointerPressedEventArgs e)
    {
        _vm.CloseCalendar();
        RestoreInputFocus();
        e.Handled = true;
    }

    private void RestoreInputFocus()
    {
        Activate();
        _inputBox.Focus();
        _vm.IsEditing = true;
    }

    private void OnPreviousMonth(object? sender, PointerPressedEventArgs e)
    {
        _vm.PreviousMonthCommand.Execute(null);
        e.Handled = true;
    }

    private void OnNextMonth(object? sender, PointerPressedEventArgs e)
    {
        _vm.NextMonthCommand.Execute(null);
        e.Handled = true;
    }

    private void OnTodayMonth(object? sender, PointerPressedEventArgs e)
    {
        _vm.ShowMonth(_vm.Today);
        e.Handled = true;
    }

    private void OnDayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: DayCellViewModel cell })
        {
            cell.PickCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ---------- 任务 ----------

    private void OnCheckPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: TaskRowViewModel row })
        {
            return;
        }

        e.Handled = true;

        // 已完成任务点勾直接恢复，不需要收起动画。
        if (row.IsCompleted)
        {
            row.ToggleCommand.Execute(null);
            return;
        }

        if (row.IsCompleting)
        {
            return;
        }

        // 勾出现 → 标题从左到右划掉 → 整行被吸走，动画走完再真正提交到仓储。
        row.IsCompleting = true;
        DispatcherTimer.RunOnce(
            () =>
            {
                row.ToggleCommand.Execute(null);
                row.IsCompleting = false;
            },
            TimeSpan.FromMilliseconds(380));
    }

    public event EventHandler<MorningBriefInfo>? MorningBriefActivated;

    public void RefreshMorningBrief(DateOnly? currentDate = null) =>
        _vm.RefreshMorningBrief(currentDate);

    private void OnPostponeOverduePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _vm.PostponeOverdue();
        }

        e.Handled = true;
    }

    private void OnCompletedHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        _vm.ToggleCompletedGroup();
        e.Handled = true;
    }

    private void OnTaskRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: TaskRowViewModel row })
        {
            OpenTaskDetail(row);
        }

        e.Handled = true;
    }

    private void OpenTaskDetail(TaskRowViewModel row)
    {
        if (_detailWindow is { IsVisible: true })
        {
            _detailWindow.Close();
        }

        var detail = new TaskDetailWindow(_vm.CreateDetail(row.Task));
        _detailWindow = detail;
        detail.Closed += (_, _) =>
        {
            if (ReferenceEquals(_detailWindow, detail))
            {
                _detailWindow = null;
            }
        };

        detail.Opened += (_, _) => WidgetWindowPositioner.PlaceBeside(this, detail, preferRight: true);

        // 小组件可能被挂进 WorkerW 成为子窗口，不能做 owner；用 Topmost 跟随保证层级。
        detail.Topmost = Topmost;
        detail.Show();
    }
}

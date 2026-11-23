using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VGList.App.Settings;
using VGList.Core.Models;
using VGList.Widgets.Host;
using VGList.Widgets.Support;
using VGList.Widgets.Today;

namespace VGList.Widgets.Month;

/// <summary>月历小组件窗口：自适应多行芯片、窗内当日日程浮层、年月选择器、详情联调与设置记忆。</summary>
public partial class MonthWidgetWindow : Window
{
    private const double BaseWidth = 662;
    private const double BaseHeight = 544;
    private const double TodayBaseWidth = 331;
    private const double MonthShift = 12;
    private const double OverlayMargin = 10;

    private static readonly TimeSpan MonthAnimationDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan OverlayAnimationDuration = TimeSpan.FromMilliseconds(160);

    private readonly MonthWidgetViewModel _vm;
    private readonly WidgetHost _host;
    private readonly Border _rootCard;
    private readonly Border _backdrop;
    private readonly Grid _monthGrid;
    private readonly Border _monthTitleButton;
    private readonly Border _viewButton;
    private readonly Border _moreButton;
    private readonly Border _monthPickerOverlay;
    private readonly Border _viewOverlay;
    private readonly Border _moreOverlay;
    private readonly Border _dayAgendaOverlay;
    private readonly TextBox _agendaTitleBox;
    private readonly TextBox _agendaDescBox;
    private readonly ItemsControl _agendaSubtasksControl;
    private readonly Transitions _monthTransitions;
    private readonly Transitions _overlayTransitions;
    private TaskDetailWindow? _detailWindow;
    private WidgetSettingsWindow? _settingsWindow;
    private int _animationToken;
    private int _overlayAnimationToken;
    private long _lastWheelTicks;

    public MonthWidgetWindow(MonthWidgetViewModel vm, SettingsStore settingsStore)
    {
        InitializeComponent();

        _vm = vm;
        DataContext = vm;

        _rootCard = Find<Border>("RootCard");
        _backdrop = Find<Border>("Backdrop");
        _monthGrid = Find<Grid>("MonthGrid");
        _monthTitleButton = Find<Border>("MonthTitleButton");
        _viewButton = Find<Border>("ViewButton");
        _moreButton = Find<Border>("MoreButton");
        _monthPickerOverlay = Find<Border>("MonthPickerOverlay");
        _viewOverlay = Find<Border>("ViewOverlay");
        _moreOverlay = Find<Border>("MoreOverlay");
        _dayAgendaOverlay = Find<Border>("DayAgendaOverlay");
        _agendaTitleBox = Find<TextBox>("AgendaTitleBox");
        _agendaDescBox = Find<TextBox>("AgendaDescBox");
        _agendaSubtasksControl = Find<ItemsControl>("AgendaSubtasksControl");

        _monthTransitions =
        [
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = MonthAnimationDuration,
                Easing = new CubicEaseOut(),
            },
            new TransformOperationsTransition
            {
                Property = RenderTransformProperty,
                Duration = MonthAnimationDuration,
                Easing = new CubicEaseOut(),
            },
        ];

        _overlayTransitions =
        [
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = OverlayAnimationDuration,
                Easing = new CubicEaseOut(),
            },
            new TransformOperationsTransition
            {
                Property = RenderTransformProperty,
                Duration = OverlayAnimationDuration,
                Easing = new CubicEaseOut(),
            },
        ];

        _host = new WidgetHost(
            this,
            settingsStore,
            BaseWidth,
            BaseHeight,
            WidgetSlot.Month,
            settingsStore.LoadWidget(),
            TodayBaseWidth);
        _host.Attach();

        ApplyStoredSettings();

        _monthGrid.SizeChanged += (_, _) =>
        {
            UpdateChipCapacity();
            if (_vm.HasAnyOverlay)
            {
                CloseAllOverlays();
            }
        };

        // Tab 在 TextBox 里默认切焦点，必须在隧道阶段拦截用于切换优先级。
        _agendaTitleBox.AddHandler(KeyDownEvent, OnAgendaTitleKeyDown, RoutingStrategies.Tunnel);
        _agendaDescBox.AddHandler(KeyDownEvent, OnAgendaDescKeyDown, RoutingStrategies.Tunnel);
        _agendaSubtasksControl.AddHandler(KeyDownEvent, OnSubtaskKeyDown, RoutingStrategies.Tunnel);

        _vm.DetailRequested += (_, task) => OpenTaskDetail(task);
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MonthWidgetViewModel.WeekRows))
            {
                Dispatcher.UIThread.Post(UpdateChipCapacity, DispatcherPriority.Background);
            }
        };

        KeyDown += OnWindowKeyDown;
        Closed += (_, _) =>
        {
            _settingsWindow?.Close();
            _detailWindow?.Close();
            _vm.Dispose();
        };
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

    public void SetShowCompleted(bool showCompleted)
    {
        _vm.ShowCompleted = showCompleted;
        _host.Settings.ShowCompleted = showCompleted;
        PersistSettings();
    }

    public void SetShowLunar(bool showLunar)
    {
        _vm.ShowLunar = showLunar;
        _host.Settings.ShowLunar = showLunar;
        PersistSettings();
    }

    public void ResetPosition() => _host.ResetToDefault();

    private T Find<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new InvalidOperationException($"{name} 未在 XAML 中定义。");

    private void ApplyStoredSettings()
    {
        var settings = _host.Settings;
        _backdrop.Opacity = Math.Clamp(settings.Opacity, 0.4, 1.0);
        _vm.ShowCompleted = settings.ShowCompleted;
        _vm.ShowLunar = settings.ShowLunar;
    }

    private void PersistSettings()
    {
        _host.SaveSettings();
        SettingsUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateChipCapacity()
    {
        var gridHeight = _monthGrid.Bounds.Height;
        if (gridHeight <= 0)
        {
            return;
        }

        var cellHeight = gridHeight / Math.Max(4, _vm.WeekRows);
        _vm.UpdateChipCapacity(MonthWidgetViewModel.ComputeChipCapacity(cellHeight));
    }

    // ---------- 窗口拖动与缩放 ----------

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

    // ---------- 月份切换与年月选择器 ----------

    private void OnPreviousMonthPressed(object? sender, PointerPressedEventArgs e)
    {
        SwitchMonth(-1);
        e.Handled = true;
    }

    private void OnNextMonthPressed(object? sender, PointerPressedEventArgs e)
    {
        SwitchMonth(1);
        e.Handled = true;
    }

    private void OnTodayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm.GoToToday())
        {
            PlayMonthAnimation(0);
        }

        e.Handled = true;
    }

    private void OnMonthTitlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm.IsMonthPickerOpen)
        {
            CloseAllOverlays();
        }
        else
        {
            ShowOverlay(_monthPickerOverlay, () => _vm.IsMonthPickerOpen = true, _monthTitleButton);
        }

        e.Handled = true;
    }

    private void OnPickerPrevYearPressed(object? sender, PointerPressedEventArgs e)
    {
        _vm.ShiftPickerYear(-1);
        e.Handled = true;
    }

    private void OnPickerNextYearPressed(object? sender, PointerPressedEventArgs e)
    {
        _vm.ShiftPickerYear(1);
        e.Handled = true;
    }

    private void OnPickerMonthPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: MonthPickerItemViewModel item })
        {
            if (_vm.JumpToMonth(item.Year, item.Month))
            {
                PlayMonthAnimation(0);
            }
        }

        e.Handled = true;
    }

    private void OnGridWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_vm.HasAnyOverlay)
        {
            return;
        }

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_lastWheelTicks != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(_lastWheelTicks) < TimeSpan.FromMilliseconds(240))
        {
            return;
        }

        if (Math.Abs(e.Delta.Y) < 0.1)
        {
            return;
        }

        _lastWheelTicks = now;
        SwitchMonth(e.Delta.Y > 0 ? -1 : 1);
        e.Handled = true;
    }

    private void SwitchMonth(int delta)
    {
        CloseAllOverlays();
        _vm.ShiftMonth(delta);
        PlayMonthAnimation(delta);
    }

    /// <summary>先把新月份瞬时移到起点，再在下一帧淡入并归位；连续翻月只保留最后一次。</summary>
    private void PlayMonthAnimation(int direction)
    {
        var token = ++_animationToken;
        var offset = direction == 0 ? 0 : direction * MonthShift;

        _monthGrid.Transitions = null;
        _monthGrid.Opacity = 0;
        _monthGrid.RenderTransform = TransformOperations.Parse($"translateX({offset}px)");

        Dispatcher.UIThread.Post(
            () =>
            {
                if (token != _animationToken)
                {
                    return;
                }

                _monthGrid.Transitions = _monthTransitions;
                _monthGrid.Opacity = 1;
                _monthGrid.RenderTransform = TransformOperations.Parse("translateX(0px)");
            },
            DispatcherPriority.Render);
    }

    // ---------- 单元格、任务条与当日浮层 ----------

    private void OnDayCellPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: MonthDayViewModel day } cell)
        {
            return;
        }

        if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed)
        {
            return;
        }

        OpenAgendaForCell(cell, day.Date);
        e.Handled = true;
    }

    private void OnOverflowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: MonthDayViewModel day } control)
        {
            return;
        }

        var cell = FindAncestorCell(control);
        OpenAgendaForCell(cell ?? control, day.Date);
        e.Handled = true;
    }

    private void OpenAgendaForCell(Control anchor, DateOnly date)
    {
        var dayItem = _vm.Days.FirstOrDefault(d => d.Date == date);
        var column = dayItem is not null ? _vm.Days.IndexOf(dayItem) % 7 : 0;
        var preferRight = column < 4;

        ShowOverlay(
            _dayAgendaOverlay,
            () => _vm.OpenAgenda(date),
            anchor,
            besideAnchor: true,
            preferRight: preferRight);
        Dispatcher.UIThread.Post(() => _agendaTitleBox.Focus(), DispatcherPriority.Input);
    }

    private static Border? FindAncestorCell(Control control)
    {
        for (Visual? current = control; current is not null; current = current.Parent as Visual)
        {
            if (current is Border border && border.Classes.Contains("month-cell"))
            {
                return border;
            }
        }

        return null;
    }

    private void OnTaskChipPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: MonthTaskChipViewModel chip })
        {
            OpenTaskDetail(chip.Task);
            e.Handled = true;
        }
    }

    private void OnAgendaTaskPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: MonthAgendaTaskViewModel item })
        {
            CloseAllOverlays();
            item.OpenDetailCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnAgendaCheckPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: MonthAgendaTaskViewModel item })
        {
            item.ToggleCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnAgendaDeletePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: MonthAgendaTaskViewModel item })
        {
            item.DeleteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnCloseAgendaPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseAllOverlays();
        e.Handled = true;
    }

    private void OnAgendaTitleKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when !e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                if (_vm.SubmitQuickCreate())
                {
                    ReclampOverlay(_dayAgendaOverlay);
                }

                e.Handled = true;
                break;

            case Key.Tab:
                _vm.QuickCreate.CyclePriority(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                break;

            case Key.Escape:
                CloseAllOverlays();
                e.Handled = true;
                break;
        }
    }

    private void OnAgendaDescKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                if (_vm.SubmitQuickCreate())
                {
                    ReclampOverlay(_dayAgendaOverlay);
                }

                e.Handled = true;
                break;

            case Key.Tab:
                _vm.QuickCreate.CyclePriority(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                break;

            case Key.Escape:
                CloseAllOverlays();
                e.Handled = true;
                break;
        }
    }

    private void OnQuickCreatePriorityPressed(object? sender, PointerPressedEventArgs e)
    {
        _vm.QuickCreate.CyclePriority();
        e.Handled = true;
    }

    private void OnQuickCreateListPressed(object? sender, PointerPressedEventArgs e)
    {
        _vm.QuickCreate.ToggleList();
        e.Handled = true;
    }

    private void OnQuickCreateSubmitPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm.SubmitQuickCreate())
        {
            ReclampOverlay(_dayAgendaOverlay);
        }

        e.Handled = true;
    }

    private void OnToggleChecklistPressed(object? sender, PointerPressedEventArgs e)
    {
        _vm.QuickCreate.ToggleChecklistMode();
        if (_vm.QuickCreate.IsChecklistMode && _vm.QuickCreate.Subtasks.Count > 0)
        {
            FocusAgendaSubtask(_vm.QuickCreate.Subtasks[0]);
        }
        else
        {
            Dispatcher.UIThread.Post(() => _agendaTitleBox.Focus(), DispatcherPriority.Input);
        }

        e.Handled = true;
    }

    private void OnSubtaskCheckPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: QuickSubtaskItem item })
        {
            item.IsCompleted = !item.IsCompleted;
        }

        e.Handled = true;
    }

    private void OnSubtaskKeyDown(object? sender, KeyEventArgs e)
    {
        var box = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<TextBox>().FirstOrDefault();
        if (box?.DataContext is not QuickSubtaskItem item)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                if (_vm.SubmitQuickCreate())
                {
                    ReclampOverlay(_dayAgendaOverlay);
                }

                e.Handled = true;
                break;

            case Key.Enter:
                var added = _vm.QuickCreate.AddSubtaskAfter(item);
                FocusAgendaSubtask(added);
                ReclampOverlay(_dayAgendaOverlay);
                e.Handled = true;
                break;

            case Key.Back when string.IsNullOrEmpty(box.Text):
            case Key.Delete when string.IsNullOrEmpty(box.Text):
                var prev = _vm.QuickCreate.RemoveSubtask(item);
                if (prev is not null)
                {
                    FocusAgendaSubtask(prev);
                }
                else
                {
                    Dispatcher.UIThread.Post(() => _agendaTitleBox.Focus(), DispatcherPriority.Input);
                }

                e.Handled = true;
                break;

            case Key.Tab:
                _vm.QuickCreate.CyclePriority(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                break;

            case Key.Escape:
                CloseAllOverlays();
                e.Handled = true;
                break;
        }
    }

    private void FocusAgendaSubtask(QuickSubtaskItem target)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                var container = _agendaSubtasksControl.ContainerFromItem(target);
                var box = container?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                if (box is not null)
                {
                    box.Focus();
                    box.CaretIndex = box.Text?.Length ?? 0;
                }
            },
            DispatcherPriority.Input);
    }

    private void OpenTaskDetail(TaskItem task)
    {
        if (_detailWindow is { IsVisible: true })
        {
            _detailWindow.Close();
        }

        var detail = new TaskDetailWindow(_vm.CreateDetail(task));
        _detailWindow = detail;
        detail.Closed += (_, _) =>
        {
            if (ReferenceEquals(_detailWindow, detail))
            {
                _detailWindow = null;
            }
        };

        detail.Opened += (_, _) => WidgetWindowPositioner.PlaceBeside(this, detail, preferRight: false);
        detail.Topmost = Topmost;
        detail.Show();
    }

    // ---------- 视图菜单与设置 ----------

    private void OnViewButtonPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm.IsViewMenuOpen)
        {
            CloseAllOverlays();
        }
        else
        {
            ShowOverlay(_viewOverlay, () => _vm.IsViewMenuOpen = true, _viewButton, alignRight: true);
        }

        e.Handled = true;
    }

    private void OnViewMenuSelected(object? sender, PointerPressedEventArgs e)
    {
        CloseAllOverlays();
        e.Handled = true;
    }

    private void OnMoreButtonPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm.IsMoreMenuOpen)
        {
            CloseAllOverlays();
        }
        else
        {
            ShowOverlay(_moreOverlay, () => _vm.IsMoreMenuOpen = true, _moreButton, alignRight: true);
        }

        e.Handled = true;
    }

    private void OnOpenSettingsPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseAllOverlays();
        OpenSettings();
        e.Handled = true;
    }

    private void OnOverlayDismissPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseAllOverlays();
        e.Handled = true;
    }

    private void OnOverlayCardPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private void CloseAllOverlays()
    {
        _overlayAnimationToken++;
        _vm.AgendaDraftText = string.Empty;
        _vm.CloseAllOverlays();
    }

    private void ShowOverlay(
        Border overlay,
        Action openAction,
        Control anchor,
        bool alignRight = false,
        bool besideAnchor = false,
        bool preferRight = true)
    {
        CloseAllOverlays();
        openAction();

        var hostWidth = _rootCard.Bounds.Width > 0 ? _rootCard.Bounds.Width : Width;
        var hostHeight = _rootCard.Bounds.Height > 0 ? _rootCard.Bounds.Height : Height;
        overlay.Measure(new Size(hostWidth, hostHeight));

        var cardWidth = !double.IsNaN(overlay.Width) ? overlay.Width : overlay.DesiredSize.Width;
        var cardHeight = Math.Max(40, overlay.DesiredSize.Height);
        var anchorPos = anchor.TranslatePoint(new Point(0, 0), _rootCard) ?? new Point(16, 56);

        double left;
        double top;
        string startTransform;
        if (besideAnchor)
        {
            var canFitRight = anchorPos.X + anchor.Bounds.Width + 6 + cardWidth + OverlayMargin <= hostWidth;
            var actualRight = preferRight && canFitRight;
            left = actualRight ? anchorPos.X + anchor.Bounds.Width + 6 : anchorPos.X - cardWidth - 6;
            top = anchorPos.Y;
            startTransform = actualRight ? "translateX(-8px)" : "translateX(8px)";
        }
        else
        {
            left = alignRight ? anchorPos.X + anchor.Bounds.Width - cardWidth : anchorPos.X;
            top = anchorPos.Y + anchor.Bounds.Height + 6;
            startTransform = "translateY(-6px)";
        }

        var maxLeft = Math.Max(OverlayMargin, hostWidth - cardWidth - OverlayMargin);
        var maxTop = Math.Max(OverlayMargin, hostHeight - cardHeight - OverlayMargin);
        Canvas.SetLeft(overlay, Math.Clamp(left, OverlayMargin, maxLeft));
        Canvas.SetTop(overlay, Math.Clamp(top, OverlayMargin, maxTop));

        var token = ++_overlayAnimationToken;
        overlay.Transitions = null;
        overlay.Opacity = 0;
        overlay.RenderTransform = TransformOperations.Parse(startTransform);

        Dispatcher.UIThread.Post(
            () =>
            {
                if (token != _overlayAnimationToken)
                {
                    return;
                }

                overlay.Transitions = _overlayTransitions;
                overlay.Opacity = 1;
                overlay.RenderTransform = TransformOperations.Parse("translateX(0px) translateY(0px)");
            },
            DispatcherPriority.Render);
    }

    private void ReclampOverlay(Border overlay)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                var hostWidth = _rootCard.Bounds.Width > 0 ? _rootCard.Bounds.Width : Width;
                var hostHeight = _rootCard.Bounds.Height > 0 ? _rootCard.Bounds.Height : Height;
                overlay.Measure(new Size(hostWidth, hostHeight));
                var cardHeight = Math.Max(40, overlay.DesiredSize.Height);
                var maxTop = Math.Max(OverlayMargin, hostHeight - cardHeight - OverlayMargin);
                var currentTop = Canvas.GetTop(overlay);
                if (!double.IsNaN(currentTop) && currentTop > maxTop)
                {
                    Canvas.SetTop(overlay, maxTop);
                }
            },
            DispatcherPriority.Loaded);
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        var window = new WidgetSettingsWindow(_host.Settings, isMonthWidget: true);
        _settingsWindow = window;

        window.OpacityChanged += (_, value) => SetBackdropOpacity(value, persist: false);
        window.OpacityCommitted += (_, _) => PersistSettings();
        window.TopmostChanged += (_, value) => SetWidgetTopmost(value);
        window.ShowCompletedChanged += (_, value) => SetShowCompleted(value);
        window.ShowLunarChanged += (_, value) => SetShowLunar(value);

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_settingsWindow, window))
            {
                _settingsWindow = null;
            }
        };

        window.Opened += (_, _) => WidgetWindowPositioner.PlaceBeside(this, window, preferRight: false);
        window.Topmost = true;
        window.Show();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseAllOverlays();
            return;
        }

        // 输入框聚焦时不拦截方向键和快捷键。
        if (_agendaTitleBox.IsFocused || _agendaDescBox.IsFocused || _vm.IsAgendaOpen)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                SwitchMonth(-1);
                e.Handled = true;
                break;
            case Key.Right:
                SwitchMonth(1);
                e.Handled = true;
                break;
            case Key.T:
                if (_vm.GoToToday())
                {
                    PlayMonthAnimation(0);
                }

                e.Handled = true;
                break;
        }
    }
}

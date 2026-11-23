using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VGList.Core.Models;
using VGList.Desktop.Controls;
using VGList.Desktop.Services;

namespace VGList.Desktop.Main.Calendar;

public partial class CalendarPageView : UserControl
{
    private readonly Grid _calendarRoot;
    private readonly Panel _calendarBodyHost;
    private readonly Border _dragGhost;
    private readonly TextBlock _dragGhostText;
    private readonly TaskQuickCreateCard _quickAddCard;
    private readonly Border _overflowOverlay;
    private readonly ItemsControl _monthGridControl;
    private readonly ItemsControl _weekColumnsControl;

    private CalendarPageViewModel? _subscribedVm;
    private TaskItem? _dragCandidate;
    private Control? _dragAnchorControl;
    private Control? _lastOverflowAnchor;
    private Point _dragStartPoint;
    private bool _isDragging;

    public CalendarPageView()
    {
        InitializeComponent();

        _calendarRoot = this.FindControl<Grid>("CalendarRoot")!;
        _calendarBodyHost = this.FindControl<Panel>("CalendarBodyHost")!;
        _dragGhost = this.FindControl<Border>("DragGhost")!;
        _dragGhostText = this.FindControl<TextBlock>("DragGhostText")!;
        _quickAddCard = this.FindControl<TaskQuickCreateCard>("QuickAddCard")!;
        _overflowOverlay = this.FindControl<Border>("OverflowOverlay")!;
        _monthGridControl = this.FindControl<ItemsControl>("MonthGridControl")!;
        _weekColumnsControl = this.FindControl<ItemsControl>("WeekColumnsControl")!;

        _calendarBodyHost.AddHandler(PointerMovedEvent, OnBodyPointerMoved, RoutingStrategies.Tunnel);
        _calendarBodyHost.AddHandler(PointerReleasedEvent, OnBodyPointerReleased, RoutingStrategies.Tunnel);
        _monthGridControl.SizeChanged += (_, _) => UpdateMonthChipCapacity();

        DataContextChanged += OnDataContextChanged;
    }

    private void UpdateMonthChipCapacity()
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var gridHeight = _monthGridControl.Bounds.Height;
        if (gridHeight <= 0)
        {
            return;
        }

        var cellHeight = gridHeight / Math.Max(4, vm.WeekRows);
        var available = cellHeight - 33.0; // 内边距 8 + 头部 22 + 间距 3
        var capacity = Math.Clamp((int)Math.Floor((available + 2.0) / 21.0), 1, 6);
        vm.UpdateMonthChipCapacity(capacity);
    }

    private CalendarPageViewModel? ViewModel => DataContext as CalendarPageViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm is not null)
        {
            _subscribedVm.ViewAnimatedRefresh -= OnViewAnimatedRefresh;
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
        }

        _subscribedVm = ViewModel;
        if (_subscribedVm is not null)
        {
            _subscribedVm.ViewAnimatedRefresh += OnViewAnimatedRefresh;
            _subscribedVm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalendarPageViewModel.WeekRows))
        {
            UpdateMonthChipCapacity();
        }
    }

    private void OnViewAnimatedRefresh(object? sender, EventArgs e) =>
        MotionHelper.PlayEnter(_calendarBodyHost, offsetY: 6, durationMs: 180);

    private void OnPrevPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.PreviousPeriodCommand.Execute(null);
        e.Handled = true;
    }

    private void OnNextPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.NextPeriodCommand.Execute(null);
        e.Handled = true;
    }

    private void OnTodayPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.GoTodayCommand.Execute(null);
        e.Handled = true;
    }

    private void OnSwitchMonthPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.SwitchToMonthCommand.Execute(null);
        e.Handled = true;
    }

    private void OnSwitchWeekPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.SwitchToWeekCommand.Execute(null);
        e.Handled = true;
    }

    private void OnMonthCellPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: CalendarMonthCellViewModel cell } cellControl &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            OpenQuickCreateAt(cellControl, cell.Date);
            e.Handled = true;
        }
    }

    private void OnCellQuickAddPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: CalendarMonthCellViewModel cell } control)
        {
            var anchor = control.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("m-cell"));
            OpenQuickCreateAt(anchor ?? control, cell.Date);
        }

        e.Handled = true;
    }

    private void OnWeekAddPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: CalendarWeekDayViewModel day } control)
        {
            var col = control.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("w-col"));
            OpenQuickCreateAt(col ?? control, day.Date);
        }

        e.Handled = true;
    }

    private void OpenQuickCreateAt(Control anchor, DateOnly date)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        vm.OpenQuickAdd(date);
        OverlayPositioner.PlaceAndShow(_calendarBodyHost, _quickAddCard, anchor, OverlayPlacement.BesidePreferRight, gap: 4, edgeMargin: 4);
        _quickAddCard.FocusTitle();
    }

    private void OnMonthOverflowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: CalendarMonthCellViewModel cell } control && ViewModel is { } vm)
        {
            vm.OpenOverflow(cell.Date);
            var anchor = control.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("m-cell"));
            _lastOverflowAnchor = anchor ?? control;
            OverlayPositioner.PlaceAndShow(_calendarBodyHost, _overflowOverlay, _lastOverflowAnchor, OverlayPlacement.BesidePreferRight, gap: 6);
        }

        e.Handled = true;
    }

    private void OnOverflowItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: CalendarTaskItemViewModel item })
        {
            OpenTaskEditAt(_lastOverflowAnchor ?? sender as Control, item.Task);
        }

        e.Handled = true;
    }

    private void OpenTaskEditAt(Control? anchor, TaskItem task)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        vm.OpenEditTask(task);
        OverlayPositioner.PlaceAndShow(
            _calendarBodyHost,
            _quickAddCard,
            anchor ?? _calendarBodyHost,
            OverlayPlacement.BesidePreferRight,
            gap: 6,
            edgeMargin: 6);
        _quickAddCard.FocusTitle();
    }

    private void OnCloseOverflowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.IsOverflowOpen = false;
        }

        e.Handled = true;
    }

    private void OnCalendarTaskCheckPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: CalendarTaskItemViewModel item })
        {
            ViewModel?.ToggleTask(item.Task);
        }

        e.Handled = true;
    }

    private void OnCalendarTaskPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: CalendarTaskItemViewModel item } control &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragCandidate = item.Task;
            _dragAnchorControl = control;
            _dragStartPoint = e.GetPosition(_calendarBodyHost);
            _isDragging = false;
            e.Handled = true;
        }
    }

    private void OnBodyPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCandidate is null || ViewModel is not { } vm)
        {
            return;
        }

        var pos = e.GetPosition(_calendarBodyHost);
        if (!_isDragging)
        {
            var dx = pos.X - _dragStartPoint.X;
            var dy = pos.Y - _dragStartPoint.Y;
            if (dx * dx + dy * dy < 36) // 6px 拖拽阈值
            {
                return;
            }

            _isDragging = true;
            _dragGhostText.Text = _dragCandidate.Title;
            _dragGhost.IsVisible = true;
            e.Pointer.Capture(_calendarBodyHost);
        }

        Canvas.SetLeft(_dragGhost, pos.X + 12);
        Canvas.SetTop(_dragGhost, pos.Y + 12);
        vm.SetDropTarget(FindTargetDateAtPoint(pos));
    }

    private void OnBodyPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragCandidate is null || ViewModel is not { } vm)
        {
            return;
        }

        var task = _dragCandidate;
        var anchor = _dragAnchorControl;
        var wasDragging = _isDragging;
        _dragCandidate = null;
        _dragAnchorControl = null;
        _isDragging = false;
        _dragGhost.IsVisible = false;
        e.Pointer.Capture(null);
        vm.SetDropTarget(null);

        if (wasDragging)
        {
            var target = FindTargetDateAtPoint(e.GetPosition(_calendarBodyHost));
            if (target is { } newDate)
            {
                vm.RescheduleTask(task, newDate);
            }
        }
        else
        {
            OpenTaskEditAt(anchor, task);
        }

        e.Handled = true;
    }

    private DateOnly? FindTargetDateAtPoint(Point pointInBody)
    {
        if (ViewModel is not { } vm)
        {
            return null;
        }

        var host = vm.IsMonthView ? _monthGridControl : _weekColumnsControl;
        foreach (var child in host.GetVisualDescendants().OfType<Border>())
        {
            if (child.DataContext is not ICalendarDateTarget target)
            {
                continue;
            }

            var topLeft = child.TranslatePoint(new Point(0, 0), _calendarBodyHost);
            if (topLeft is not { } origin)
            {
                continue;
            }

            var rect = new Rect(origin, child.Bounds.Size);
            if (rect.Contains(pointInBody))
            {
                return target.Date;
            }
        }

        return null;
    }

    private void OnOverlayDismissPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.CloseOverlays();
        e.Handled = true;
    }

    private void OnOverlayCardPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private void OnQuickCreateSubmitted(object? sender, EventArgs e) =>
        ViewModel?.SubmitQuickAdd();

    private void OnQuickCreateCancelled(object? sender, EventArgs e) =>
        ViewModel?.CloseOverlays();

    private void OnQuickCreateDeleteRequested(object? sender, EventArgs e) =>
        ViewModel?.DeleteEditingTask();
}

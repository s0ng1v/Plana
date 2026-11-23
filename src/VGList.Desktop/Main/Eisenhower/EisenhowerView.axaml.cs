using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VGList.Core.Models;
using VGList.Desktop.Controls;
using VGList.Desktop.Main.Tasks;
using VGList.Desktop.Services;

namespace VGList.Desktop.Main.Eisenhower;

public partial class EisenhowerView : UserControl
{
    private const double DragThresholdPx = 6.0;

    private readonly Panel _eisenhowerHost;
    private readonly TaskQuickCreateCard _quickAddCard;
    private readonly ContentControl _q1Host;
    private readonly ContentControl _q2Host;
    private readonly ContentControl _q3Host;
    private readonly ContentControl _q4Host;
    private readonly Border _dragGhost;
    private readonly TextBlock _dragGhostTitle;

    private TaskRowControl? _pressedRowControl;
    private MainTaskRowViewModel? _pressedRow;
    private Point _dragStartPoint;
    private bool _isDragging;
    private TaskPriority? _hoveredPriority;

    public EisenhowerView()
    {
        InitializeComponent();

        _eisenhowerHost = this.FindControl<Panel>("EisenhowerHost")!;
        _quickAddCard = this.FindControl<TaskQuickCreateCard>("QuickAddCard")!;
        _q1Host = this.FindControl<ContentControl>("Q1Host")!;
        _q2Host = this.FindControl<ContentControl>("Q2Host")!;
        _q3Host = this.FindControl<ContentControl>("Q3Host")!;
        _q4Host = this.FindControl<ContentControl>("Q4Host")!;
        _dragGhost = this.FindControl<Border>("DragGhost")!;
        _dragGhostTitle = this.FindControl<TextBlock>("DragGhostTitle")!;

        _eisenhowerHost.AddHandler(PointerPressedEvent, OnHostPointerPressed, RoutingStrategies.Bubble);
        _eisenhowerHost.AddHandler(PointerMovedEvent, OnHostPointerMoved, RoutingStrategies.Bubble);
        _eisenhowerHost.AddHandler(PointerReleasedEvent, OnHostPointerReleased, RoutingStrategies.Bubble);
        _eisenhowerHost.AddHandler(PointerCaptureLostEvent, OnHostPointerCaptureLost, RoutingStrategies.Bubble);
    }

    private EisenhowerViewModel? ViewModel => DataContext as EisenhowerViewModel;

    private void OnAllScopePressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.SetAllScopeCommand.Execute(null);
        e.Handled = true;
    }

    private void OnTodayScopePressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.SetTodayScopeCommand.Execute(null);
        e.Handled = true;
    }

    private void OnQuadrantAddPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm && sender is Control { DataContext: QuadrantViewModel q } anchor)
        {
            vm.OpenQuickCreate(q.Priority);
            var isRightColumn = q.Priority is TaskPriority.Medium or TaskPriority.None;
            OverlayPositioner.PlaceAndShow(
                _eisenhowerHost,
                _quickAddCard,
                anchor,
                isRightColumn ? OverlayPlacement.BesideLeft : OverlayPlacement.BesideRight,
                gap: 8);
            _quickAddCard.FocusTitle();
        }

        e.Handled = true;
    }

    private void OnOverlayDismissPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.CloseQuickCreate();
        e.Handled = true;
    }

    private void OnQuickCreateSubmitted(object? sender, EventArgs e) =>
        ViewModel?.SubmitQuickCreate();

    private void OnQuickCreateCancelled(object? sender, EventArgs e) =>
        ViewModel?.CloseQuickCreate();

    private void OnQuickCreateDeleteRequested(object? sender, EventArgs e) =>
        ViewModel?.DeleteEditingTask();

    private void OnQuadrantCompletedPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: QuadrantViewModel q })
        {
            q.ToggleCompletedCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void OnTaskRowSelected(object? sender, MainTaskRowViewModel row)
    {
        if (ViewModel is not { } vm || sender is not Control anchor)
        {
            return;
        }

        vm.OpenEditTask(row);

        // 左列象限向右伸出，右列象限向左伸出，避免卡片盖住被点击的任务行。
        var isRightColumn = row.Task.Priority is TaskPriority.Medium or TaskPriority.None;
        OverlayPositioner.PlaceAndShow(
            _eisenhowerHost,
            _quickAddCard,
            anchor,
            isRightColumn ? OverlayPlacement.BesideLeft : OverlayPlacement.BesideRight,
            gap: 8,
            edgeMargin: 8);
        _quickAddCard.FocusTitle();
    }

    private void OnTaskCheckToggled(object? sender, MainTaskRowViewModel row) =>
        ViewModel?.ToggleTask(row);

    private void OnQuickReschedule(object? sender, MainTaskRowViewModel row) =>
        ViewModel?.QuickReschedule(row);

    private void OnQuickPriority(object? sender, MainTaskRowViewModel row) =>
        ViewModel?.QuickCyclePriority(row);

    private void OnQuickDelete(object? sender, MainTaskRowViewModel row) =>
        ViewModel?.DeleteTaskWithUndo(row.Task);

    // ---------- 跨象限拖拽改优先级 ----------

    private void OnHostPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is null || !e.GetCurrentPoint(_eisenhowerHost).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var rowControl = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<TaskRowControl>().FirstOrDefault();
        if (rowControl?.DataContext is not MainTaskRowViewModel row)
        {
            return;
        }

        _pressedRowControl = rowControl;
        _pressedRow = row;
        _dragStartPoint = e.GetPosition(_eisenhowerHost);
        _isDragging = false;
        _hoveredPriority = null;
        e.Pointer.Capture(_eisenhowerHost);
        e.Handled = true;
    }

    private void OnHostPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedRow is null)
        {
            return;
        }

        var pos = e.GetPosition(_eisenhowerHost);
        if (!_isDragging)
        {
            var dx = pos.X - _dragStartPoint.X;
            var dy = pos.Y - _dragStartPoint.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < DragThresholdPx)
            {
                return;
            }

            _isDragging = true;
            ViewModel?.CloseQuickCreate();
            _dragGhostTitle.Text = _pressedRow.Title;
            _dragGhost.IsVisible = true;
        }

        Canvas.SetLeft(_dragGhost, pos.X + 12);
        Canvas.SetTop(_dragGhost, pos.Y - 14);

        _hoveredPriority = HitTestQuadrant(pos);
        ViewModel?.SetDropTarget(_hoveredPriority is { } p && p != _pressedRow.Task.Priority ? p : null);
        e.Handled = true;
    }

    private void OnHostPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressedRow is not { } row || _pressedRowControl is not { } rowControl)
        {
            return;
        }

        var wasDragging = _isDragging;
        var targetPriority = _hoveredPriority;
        ResetDrag(e.Pointer);

        if (wasDragging)
        {
            if (targetPriority is { } target && target != row.Task.Priority)
            {
                ViewModel?.MoveTaskToPriority(row.Task, target);
            }
        }
        else
        {
            OnTaskRowSelected(rowControl, row);
        }

        e.Handled = true;
    }

    private void OnHostPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        ResetDrag(null);

    private void ResetDrag(IPointer? pointer)
    {
        _pressedRowControl = null;
        _pressedRow = null;
        _isDragging = false;
        _hoveredPriority = null;
        _dragGhost.IsVisible = false;
        ViewModel?.SetDropTarget(null);
        pointer?.Capture(null);
    }

    private TaskPriority? HitTestQuadrant(Point hostPoint)
    {
        if (IsPointInControl(_q1Host, hostPoint)) return TaskPriority.High;
        if (IsPointInControl(_q2Host, hostPoint)) return TaskPriority.Medium;
        if (IsPointInControl(_q3Host, hostPoint)) return TaskPriority.Low;
        if (IsPointInControl(_q4Host, hostPoint)) return TaskPriority.None;
        return null;
    }

    private bool IsPointInControl(Control control, Point hostPoint)
    {
        var origin = control.TranslatePoint(new Point(0, 0), _eisenhowerHost);
        if (origin is null)
        {
            return false;
        }

        var rect = new Rect(origin.Value, control.Bounds.Size);
        return rect.Contains(hostPoint);
    }
}

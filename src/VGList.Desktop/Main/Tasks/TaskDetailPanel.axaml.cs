using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VGList.Desktop.Controls;
using VGList.Desktop.Services;
using VGList.Widgets.Support;

namespace VGList.Desktop.Main.Tasks;

public partial class TaskDetailPanel : UserControl
{
    private readonly Panel _detailHost;
    private readonly Grid _panelRoot;
    private readonly Border _dateChip;
    private readonly Border _reminderChip;
    private readonly Border _recurrenceChip;
    private readonly Border _priorityChip;
    private readonly Border _listChip;
    private readonly DatePickerPopover _dateOverlay;
    private readonly ReminderEditorPopover _reminderOverlay;
    private readonly Border _recurrenceOverlay;
    private readonly Border _priorityOverlay;
    private readonly Border _listOverlay;
    private readonly TextBox _titleBox;
    private readonly TextBox _contentBox;
    private readonly TextBox _tagInputBox;
    private readonly ItemsControl _subtasksControl;
    private string? _lastTaskId;

    public TaskDetailPanel()
    {
        InitializeComponent();

        _detailHost = this.FindControl<Panel>("DetailHost")!;
        _panelRoot = this.FindControl<Grid>("PanelRoot")!;
        _dateChip = this.FindControl<Border>("DateChip")!;
        _reminderChip = this.FindControl<Border>("ReminderChip")!;
        _recurrenceChip = this.FindControl<Border>("RecurrenceChip")!;
        _priorityChip = this.FindControl<Border>("PriorityChip")!;
        _listChip = this.FindControl<Border>("ListChip")!;
        _dateOverlay = this.FindControl<DatePickerPopover>("DetailDateOverlay")!;
        _reminderOverlay = this.FindControl<ReminderEditorPopover>("DetailReminderOverlay")!;
        _recurrenceOverlay = this.FindControl<Border>("DetailRecurrenceOverlay")!;
        _priorityOverlay = this.FindControl<Border>("DetailPriorityOverlay")!;
        _listOverlay = this.FindControl<Border>("DetailListOverlay")!;
        _titleBox = this.FindControl<TextBox>("TitleBox")!;
        _contentBox = this.FindControl<TextBox>("ContentBox")!;
        _tagInputBox = this.FindControl<TextBox>("TagInputBox")!;
        _subtasksControl = this.FindControl<ItemsControl>("DetailSubtasksControl")!;

        _titleBox.AddHandler(KeyDownEvent, OnTitleKeyDown, RoutingStrategies.Tunnel);
        _tagInputBox.AddHandler(KeyDownEvent, OnTagInputKeyDown, RoutingStrategies.Tunnel);
        _tagInputBox.LostFocus += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(ViewModel?.NewTagText))
            {
                ViewModel.AddTag();
            }
        };
        _subtasksControl.AddHandler(KeyDownEvent, OnDetailSubtaskKeyDown, RoutingStrategies.Tunnel);
        _titleBox.GotFocus += OnTitleGotFocus;
        _titleBox.LostFocus += OnTitleLostFocus;
        _contentBox.GotFocus += OnContentGotFocus;
        _contentBox.LostFocus += OnContentLostFocus;

        DataContextChanged += OnDataContextChanged;
    }

    private MainTaskDetailViewModel? ViewModel => DataContext as MainTaskDetailViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            _lastTaskId = null;
            return;
        }

        if (!string.Equals(_lastTaskId, vm.Task.Id, StringComparison.Ordinal))
        {
            _lastTaskId = vm.Task.Id;
            MotionHelper.PlayEnter(_panelRoot, offsetX: 10, offsetY: 0, durationMs: 180);
        }
    }

    private void OnCheckPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ToggleCompletedCommand.Execute(null);
        e.Handled = true;
    }

    private void OnDatePressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            var next = !vm.IsCalendarOpen;
            vm.CloseOpenPopups();
            if (next)
            {
                vm.ToggleCalendarCommand.Execute(null);
                if (vm.IsCalendarOpen)
                {
                    OverlayPositioner.PlaceAndShow(_detailHost, _dateOverlay, _dateChip, OverlayPlacement.BelowLeftAligned);
                }
            }
        }

        e.Handled = true;
    }

    private void OnPriorityPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            var next = !vm.IsPriorityMenuOpen;
            vm.CloseOpenPopups();
            if (next)
            {
                vm.IsPriorityMenuOpen = true;
                OverlayPositioner.PlaceAndShow(_detailHost, _priorityOverlay, _priorityChip, OverlayPlacement.BelowRightAligned);
            }
        }

        e.Handled = true;
    }

    private void OnPriorityHighPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.SetPriorityHighCommand.Execute(null);
        e.Handled = true;
    }

    private void OnPriorityMediumPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.SetPriorityMediumCommand.Execute(null);
        e.Handled = true;
    }

    private void OnPriorityLowPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.SetPriorityLowCommand.Execute(null);
        e.Handled = true;
    }

    private void OnPriorityNonePressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.SetPriorityNoneCommand.Execute(null);
        e.Handled = true;
    }

    private void OnClosePressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.CloseCommand.Execute(null);
        e.Handled = true;
    }

    private void OnListPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            var next = !vm.IsListMenuOpen;
            vm.CloseOpenPopups();
            if (next)
            {
                vm.IsListMenuOpen = true;
                OverlayPositioner.PlaceAndShow(_detailHost, _listOverlay, _listChip, OverlayPlacement.AboveLeftAligned);
            }
        }

        e.Handled = true;
    }

    private void OnOverlayDismissPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.CloseOpenPopups();
        e.Handled = true;
    }

    private void OnOverlayCardPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private void OnToggleChecklistPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.ToggleChecklistMode();
            if (vm.IsChecklistMode && vm.Subtasks.Count > 0)
            {
                FocusSubtask(vm.Subtasks[0]);
            }
        }

        e.Handled = true;
    }

    private void OnDetailSubtaskCheckPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: QuickSubtaskItem item })
        {
            item.IsCompleted = !item.IsCompleted;
        }

        e.Handled = true;
    }

    private void OnDetailSubtaskKeyDown(object? sender, KeyEventArgs e)
    {
        var box = (e.Source as Avalonia.Visual)?.GetSelfAndVisualAncestors().OfType<TextBox>().FirstOrDefault();
        if (ViewModel is not { } vm || box?.DataContext is not QuickSubtaskItem item)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                var added = vm.AddSubtaskAfter(item);
                FocusSubtask(added);
                e.Handled = true;
                break;

            case Key.Back when string.IsNullOrEmpty(box.Text):
            case Key.Delete when string.IsNullOrEmpty(box.Text):
                var prev = vm.RemoveSubtask(item);
                if (prev is not null)
                {
                    FocusSubtask(prev);
                }

                e.Handled = true;
                break;
        }
    }

    private void FocusSubtask(QuickSubtaskItem target)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                var container = _subtasksControl.ContainerFromItem(target);
                var box = container?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                if (box is not null)
                {
                    box.Focus();
                    box.CaretIndex = box.Text?.Length ?? 0;
                }
            },
            DispatcherPriority.Input);
    }

    private void OnPickListPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.Tag is string id)
        {
            ViewModel?.PickList(id);
        }

        e.Handled = true;
    }

    private void OnReminderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            var next = !vm.IsReminderMenuOpen;
            vm.CloseOpenPopups();
            if (next)
            {
                vm.PrepareReminderEditor();
                vm.IsReminderMenuOpen = true;
                OverlayPositioner.PlaceAndShow(_detailHost, _reminderOverlay, _reminderChip, OverlayPlacement.BelowLeftAligned);
            }
        }

        e.Handled = true;
    }

    private void OnRecurrencePressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            var next = !vm.IsRecurrenceMenuOpen;
            vm.CloseOpenPopups();
            if (next)
            {
                vm.IsRecurrenceMenuOpen = true;
                OverlayPositioner.PlaceAndShow(_detailHost, _recurrenceOverlay, _recurrenceChip, OverlayPlacement.BelowLeftAligned);
            }
        }

        e.Handled = true;
    }

    private void OnPickRecurrencePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: RecurrencePickItem item })
        {
            ViewModel?.PickRecurrence(item.Kind);
        }

        e.Handled = true;
    }

    private void OnRemoveTagPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.Tag is string tag)
        {
            ViewModel?.RemoveTag(tag);
        }

        e.Handled = true;
    }

    private void OnTagInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        if (e.Key is Key.Enter or Key.OemComma)
        {
            vm.AddTag();
            e.Handled = true;
        }
        else if (e.Key == Key.Back && string.IsNullOrEmpty(_tagInputBox.Text))
        {
            vm.RemoveLastTag();
            e.Handled = true;
        }
    }

    private void OnDeletePressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.DeleteCommand.Execute(null);
        e.Handled = true;
    }

    private void OnTitleGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.IsTitleFocused = true;
        }
    }

    private void OnTitleLostFocus(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.IsTitleFocused = false;
            vm.CommitTitle();
        }
    }

    private void OnTitleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ViewModel?.CommitTitle();
        _contentBox.Focus();
        e.Handled = true;
    }

    private void OnContentGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.IsContentFocused = true;
        }
    }

    private void OnContentLostFocus(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.IsContentFocused = false;
        }
    }
}

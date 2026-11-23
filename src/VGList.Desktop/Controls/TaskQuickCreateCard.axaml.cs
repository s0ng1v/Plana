using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VGList.Widgets.Support;

namespace VGList.Desktop.Controls;

/// <summary>
/// 统一任务快速新建悬浮卡片：支持日历贴边伸出、四象限加号伸出、Tab 切换优先级、待办事项互转与快捷键盘操作。
/// </summary>
public partial class TaskQuickCreateCard : UserControl
{
    private readonly TextBox _titleBox;
    private readonly ItemsControl _subtasksControl;

    public TaskQuickCreateCard()
    {
        InitializeComponent();

        _titleBox = Find<TextBox>("TitleBox");
        _subtasksControl = Find<ItemsControl>("SubtasksControl");

        _titleBox.AddHandler(KeyDownEvent, OnTitleKeyDown, RoutingStrategies.Tunnel);
        Find<TextBox>("DescBox").AddHandler(KeyDownEvent, OnDescKeyDown, RoutingStrategies.Tunnel);
        _subtasksControl.AddHandler(KeyDownEvent, OnSubtaskKeyDown, RoutingStrategies.Tunnel);
    }

    public event EventHandler? Submitted;
    public event EventHandler? Cancelled;
    public event EventHandler? DeleteRequested;

    public void FocusTitle() =>
        Dispatcher.UIThread.Post(() =>
        {
            _titleBox.Focus();
            _titleBox.CaretIndex = _titleBox.Text?.Length ?? 0;
        }, DispatcherPriority.Input);

    private T Find<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new InvalidOperationException($"{name} 未在 TaskQuickCreateCard XAML 中定义。");

    private void OnCardBackgroundPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private void OnTaskCheckPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is QuickCreateViewModel vm)
        {
            vm.ToggleCompleted();
        }

        e.Handled = true;
    }

    private void OnDeletePressed(object? sender, PointerPressedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnDateLabelPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is QuickCreateViewModel vm)
        {
            vm.PickManualDate(vm.TargetDate.AddDays(1));
        }

        e.Handled = true;
    }

    private void OnPriorityFlagPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is QuickCreateViewModel vm)
        {
            vm.CyclePriority();
        }

        e.Handled = true;
    }

    private void OnToggleChecklistPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not QuickCreateViewModel vm)
        {
            return;
        }

        vm.ToggleChecklistMode();
        if (vm.IsChecklistMode && vm.Subtasks.Count > 0)
        {
            FocusSubtask(vm.Subtasks[0], caretToEnd: true);
        }
        else
        {
            FocusTitle();
        }

        e.Handled = true;
    }

    private void OnToggleListPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is QuickCreateViewModel vm)
        {
            vm.ToggleList();
        }

        e.Handled = true;
    }

    private void OnCycleRecurrencePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is QuickCreateViewModel vm)
        {
            vm.CycleRecurrence();
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

    private void OnSubmitPressed(object? sender, PointerPressedEventArgs e)
    {
        Submitted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnTitleKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not QuickCreateViewModel vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when !e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                Submitted?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;

            case Key.Tab:
                vm.CyclePriority(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                break;

            case Key.Escape:
                Cancelled?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    private void OnDescKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not QuickCreateViewModel vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                Submitted?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;

            case Key.Tab:
                vm.CyclePriority(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                break;

            case Key.Escape:
                Cancelled?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    private void OnSubtaskKeyDown(object? sender, KeyEventArgs e)
    {
        var box = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<TextBox>().FirstOrDefault();
        if (DataContext is not QuickCreateViewModel vm ||
            box?.DataContext is not QuickSubtaskItem item)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                Submitted?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;

            case Key.Enter:
                var added = vm.AddSubtaskAfter(item);
                FocusSubtask(added, caretToEnd: true);
                e.Handled = true;
                break;

            case Key.Back when string.IsNullOrEmpty(box.Text):
            case Key.Delete when string.IsNullOrEmpty(box.Text):
                var prev = vm.RemoveSubtask(item);
                if (prev is not null)
                {
                    FocusSubtask(prev, caretToEnd: true);
                }
                else
                {
                    FocusTitle();
                }

                e.Handled = true;
                break;

            case Key.Tab:
                vm.CyclePriority(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                break;

            case Key.Escape:
                Cancelled?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    private void FocusSubtask(QuickSubtaskItem target, bool caretToEnd)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                var container = _subtasksControl.ContainerFromItem(target);
                var box = container?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                if (box is not null)
                {
                    box.Focus();
                    if (caretToEnd)
                    {
                        box.CaretIndex = box.Text?.Length ?? 0;
                    }
                }
            },
            DispatcherPriority.Input);
    }
}

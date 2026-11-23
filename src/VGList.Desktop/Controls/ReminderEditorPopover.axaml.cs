using Avalonia.Controls;
using Avalonia.Input;

namespace VGList.Desktop.Controls;

public partial class ReminderEditorPopover : UserControl
{
    public ReminderEditorPopover()
    {
        InitializeComponent();
    }

    private ReminderEditorViewModel? ViewModel => DataContext as ReminderEditorViewModel;

    private void OnQuickTimePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string hhmm })
        {
            ViewModel?.PickQuickTime(hhmm);
        }

        e.Handled = true;
    }

    private void OnClearTimePressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ClearDueTime();
        e.Handled = true;
    }

    private void OnDisablePressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.DisableReminder();
        e.Handled = true;
    }

    private void OnPresetOffsetPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string raw } && int.TryParse(raw, out var minutes))
        {
            ViewModel?.SelectPresetOffset(minutes);
        }

        e.Handled = true;
    }

    private void OnUnitPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string raw } && Enum.TryParse<ReminderOffsetUnit>(raw, out var unit))
        {
            ViewModel?.SelectUnit(unit);
        }

        e.Handled = true;
    }

    private void OnToggleRepeatPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ToggleRepeat();
        e.Handled = true;
    }

    private void OnRepeatIntervalPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string raw } && int.TryParse(raw, out var minutes))
        {
            ViewModel?.SelectRepeatInterval(minutes);
        }

        e.Handled = true;
    }

    private void OnRepeatMaxPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string raw } && int.TryParse(raw, out var max))
        {
            ViewModel?.SelectRepeatMaxCount(max);
        }

        e.Handled = true;
    }
}

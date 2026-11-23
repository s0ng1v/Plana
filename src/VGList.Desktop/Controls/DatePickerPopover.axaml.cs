using Avalonia.Controls;
using Avalonia.Input;

namespace VGList.Desktop.Controls;

public partial class DatePickerPopover : UserControl
{
    public DatePickerPopover()
    {
        InitializeComponent();
    }

    private DatePickerViewModel? ViewModel => DataContext as DatePickerViewModel;

    private void OnTodayPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.PickTodayCommand.Execute(null);
        e.Handled = true;
    }

    private void OnTomorrowPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.PickTomorrowCommand.Execute(null);
        e.Handled = true;
    }

    private void OnDayAfterPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.PickDayAfterCommand.Execute(null);
        e.Handled = true;
    }

    private void OnNextWeekPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.PickNextWeekCommand.Execute(null);
        e.Handled = true;
    }

    private void OnPrevMonthPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.PreviousMonthCommand.Execute(null);
        e.Handled = true;
    }

    private void OnNextMonthPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.NextMonthCommand.Execute(null);
        e.Handled = true;
    }

    private void OnDayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: DatePickerDayViewModel day })
        {
            day.PickCommand.Execute(null);
        }

        e.Handled = true;
    }
}

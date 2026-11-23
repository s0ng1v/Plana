using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VGList.Core.Dates;
using VGList.Widgets.Support;

namespace VGList.Desktop.Controls;

/// <summary>日期弹层中的单个日期格。</summary>
public sealed class DatePickerDayViewModel
{
    public DatePickerDayViewModel(
        DateOnly date,
        bool isCurrentMonth,
        bool isToday,
        bool isSelected,
        Action<DateOnly> pick)
    {
        Date = date;
        DayLabel = date.Day.ToString();
        IsOutside = !isCurrentMonth;
        IsToday = isToday;
        IsSelected = isSelected;

        var lunar = LunarCalendar.Describe(date);
        LunarLabel = lunar.Text;
        IsFestival = lunar.Kind == CalendarLabelKind.Festival;
        IsSolarTerm = lunar.Kind == CalendarLabelKind.SolarTerm;

        PickCommand = new RelayCommand(() => pick(date));
    }

    public DateOnly Date { get; }

    public string DayLabel { get; }

    public string LunarLabel { get; }

    public bool IsOutside { get; }

    public bool IsToday { get; }

    public bool IsSelected { get; }

    public bool IsFestival { get; }

    public bool IsSolarTerm { get; }

    public IRelayCommand PickCommand { get; }
}

/// <summary>可复用的浅色日期选择器 ViewModel（支持快捷日与 6×7 农历日历）。</summary>
public sealed partial class DatePickerViewModel : ObservableObject
{
    private readonly DateOnly _today;
    private readonly Action<DateOnly> _onPicked;
    private DateOnly _selectedDate;
    private DateOnly _visibleMonth;

    public DatePickerViewModel(DateOnly today, DateOnly selectedDate, Action<DateOnly> onPicked)
    {
        _today = today;
        _selectedDate = selectedDate;
        _visibleMonth = new DateOnly(selectedDate.Year, selectedDate.Month, 1);
        _onPicked = onPicked;

        PreviousMonthCommand = new RelayCommand(() => ShiftMonth(-1));
        NextMonthCommand = new RelayCommand(() => ShiftMonth(1));
        PickTodayCommand = new RelayCommand(() => Pick(_today));
        PickTomorrowCommand = new RelayCommand(() => Pick(_today.AddDays(1)));
        PickDayAfterCommand = new RelayCommand(() => Pick(_today.AddDays(2)));
        PickNextWeekCommand = new RelayCommand(() => Pick(_today.AddDays(7)));

        BuildMonth();
    }

    public ObservableCollection<DatePickerDayViewModel> Days { get; } = [];

    [ObservableProperty]
    private string _monthLabel = string.Empty;

    public IRelayCommand PreviousMonthCommand { get; }
    public IRelayCommand NextMonthCommand { get; }
    public IRelayCommand PickTodayCommand { get; }
    public IRelayCommand PickTomorrowCommand { get; }
    public IRelayCommand PickDayAfterCommand { get; }
    public IRelayCommand PickNextWeekCommand { get; }

    public void SyncSelectedDate(DateOnly date)
    {
        _selectedDate = date;
        _visibleMonth = new DateOnly(date.Year, date.Month, 1);
        BuildMonth();
    }

    private void Pick(DateOnly date)
    {
        _selectedDate = date;
        _visibleMonth = new DateOnly(date.Year, date.Month, 1);
        BuildMonth();
        _onPicked(date);
    }

    private void ShiftMonth(int delta)
    {
        _visibleMonth = _visibleMonth.AddMonths(delta);
        BuildMonth();
    }

    private void BuildMonth()
    {
        Days.Clear();
        MonthLabel = $"{_visibleMonth.Year}年{_visibleMonth.Month}月";

        var offset = (int)_visibleMonth.DayOfWeek;
        var start = _visibleMonth.AddDays(-offset);
        for (var i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            Days.Add(new DatePickerDayViewModel(
                date,
                date.Month == _visibleMonth.Month,
                date == _today,
                date == _selectedDate,
                Pick));
        }
    }
}

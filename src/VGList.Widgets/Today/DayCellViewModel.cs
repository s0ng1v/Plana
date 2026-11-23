using CommunityToolkit.Mvvm.Input;

namespace VGList.Widgets.Today;

/// <summary>轻量日历里的一格。</summary>
public sealed class DayCellViewModel
{
    public DayCellViewModel(DateOnly date, bool isOutside, bool isToday, bool isSelected, Action<DateOnly> pick)
    {
        Date = date;
        IsOutside = isOutside;
        IsToday = isToday;
        IsSelected = isSelected;
        PickCommand = new RelayCommand(() => pick(Date));
    }

    public DateOnly Date { get; }

    public string DayText => Date.Day.ToString();

    public bool IsOutside { get; }

    public bool IsToday { get; }

    public bool IsSelected { get; }

    public IRelayCommand PickCommand { get; }
}

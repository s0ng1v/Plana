using VGList.Core.Dates;

namespace VGList.Widgets.Support;

/// <summary>兼容包装：历法核心已下沉至 VGList.Core.Dates.CnCalendar，供小组件与助理共用。</summary>
public static class LunarCalendar
{
    public static CalendarLabel Describe(DateOnly date) => CnCalendar.Describe(date);
}

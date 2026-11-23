using VGList.App.Services;
using VGList.Core.Models;
using VGList.Widgets.Month;
using Xunit;

namespace VGList.Tests;

public sealed class MonthWidgetViewModelTests
{
    private static readonly DateOnly Today = new(2026, 11, 18);

    [Fact]
    public void BuildDays_Produces42CellsStartingSundayWithAdaptiveChips()
    {
        var repo = new InMemoryTaskRepository();
        repo.Create(new TaskDraft { Title = "任务1", Date = Today, Priority = TaskPriority.Low });
        repo.Create(new TaskDraft { Title = "任务2", Date = Today, Priority = TaskPriority.High });
        repo.Create(new TaskDraft { Title = "任务3", Date = Today, Priority = TaskPriority.Medium });
        repo.Create(new TaskDraft { Title = "任务4", Date = Today, Priority = TaskPriority.None });

        var vm = new MonthWidgetViewModel(repo, Today);

        // 2026 年 11 月 1 日为周日、共 30 天，精确占 5 行（35 格），不再出现全为 12 月的冗余第 6 行
        Assert.Equal(5, vm.WeekRows);
        Assert.Equal(35, vm.Days.Count);
        Assert.Equal(DayOfWeek.Sunday, vm.Days[0].Date.DayOfWeek);

        var todayCell = Assert.Single(vm.Days, d => d.IsToday);
        // 默认容量 2：第 1 条独占一行 + 第 2 条与 +2 共享第二行，高优先级排最前
        var topChip = Assert.Single(todayCell.FullWidthChips);
        Assert.Equal("任务2", topChip.Title);
        Assert.True(topChip.IsHigh);
        Assert.Equal("任务3", todayCell.BottomChip?.Title);
        Assert.True(todayCell.HasOverflow);
        Assert.Equal("+2", todayCell.OverflowLabel);

        // 窗口拉高容量到 4 后，4 条全部展开、无溢出
        vm.UpdateChipCapacity(4);
        Assert.Equal(4, todayCell.FullWidthChips.Count);
        Assert.False(todayCell.HasBottomChip);
        Assert.False(todayCell.HasOverflow);
    }

    [Theory]
    [InlineData(89.0, 3)]   // 默认 544 高、5 行月份
    [InlineData(74.3, 2)]   // 默认 544 高、6 行月份
    [InlineData(70.8, 2)]   // 缩到 454 高、5 行月份
    [InlineData(56.2, 1)]   // 最小 436 高、6 行月份
    [InlineData(116.2, 4)]  // 放大到 820 × 680、5 行月份
    [InlineData(200.0, 5)]  // 触顶后夹在 5 条
    public void ComputeChipCapacity_AlwaysFitsWholeChipRows(double cellHeight, int expected)
    {
        var capacity = MonthWidgetViewModel.ComputeChipCapacity(cellHeight);

        Assert.Equal(expected, capacity);

        // 固定开销 35 + 首条 16 + 其余每条步距 18，末条必须完整落在格子内。
        Assert.True(35 + 16 + (capacity - 1) * 18 <= cellHeight);

        // 未触顶时再多放一条就会越过底边格线，说明容量没有保守少算。
        if (capacity < 5)
        {
            Assert.True(35 + 16 + capacity * 18 > cellHeight);
        }
    }

    [Fact]
    public void OpenAgenda_ListsAllDayTasksAndCreatesOnTargetDate()
    {
        var repo = new InMemoryTaskRepository();
        var target = Today.AddDays(5);
        var vm = new MonthWidgetViewModel(repo, Today);

        vm.OpenAgenda(target);
        Assert.True(vm.IsAgendaOpen);
        Assert.Equal(target, vm.AgendaDate);
        Assert.True(vm.IsAgendaEmpty);

        vm.CycleAgendaPriority();
        Assert.Equal(TaskPriority.High, vm.AgendaDraftPriority);
        vm.AgendaDraftText = "准备月度汇报";
        Assert.True(vm.SubmitAgendaDraft());

        Assert.Single(vm.AgendaTasks);
        Assert.Equal("准备月度汇报", vm.AgendaTasks[0].Title);
        Assert.True(vm.AgendaTasks[0].IsHigh);
        Assert.Equal(TaskPriority.None, vm.AgendaDraftPriority);

        var targetCell = Assert.Single(vm.Days, d => d.Date == target);
        Assert.True(targetCell.HasTasks);
        Assert.Equal("准备月度汇报", targetCell.FullWidthChips[0].Title);
    }

    [Fact]
    public void SelectionHighlight_OnlyExistsWhileAgendaIsOpen()
    {
        var repo = new InMemoryTaskRepository();
        var target = Today.AddDays(3);
        var vm = new MonthWidgetViewModel(repo, Today);

        vm.OpenAgenda(target);
        Assert.True(vm.Days.First(d => d.Date == target).IsSelected);

        vm.CloseAllOverlays();
        Assert.False(vm.IsAgendaOpen);
        Assert.All(vm.Days, day => Assert.False(day.IsSelected));
    }

    [Fact]
    public void JumpToMonth_UpdatesVisibleMonthAndGoToTodayReturnsFalseIfAlreadyCurrent()
    {
        var repo = new InMemoryTaskRepository();
        var vm = new MonthWidgetViewModel(repo, Today);

        Assert.False(vm.GoToToday());

        Assert.True(vm.JumpToMonth(2027, 2));
        Assert.Equal("2027年2月", vm.MonthTitle);
        Assert.False(vm.IsCurrentMonth);

        Assert.True(vm.GoToToday());
        Assert.Equal("2026年11月", vm.MonthTitle);
        Assert.Equal(Today, vm.SelectedDate);
    }

    [Fact]
    public void OverlayState_AndAgendaDelete_UpdateSynchronously()
    {
        var repo = new InMemoryTaskRepository();
        repo.Create(new TaskDraft { Title = "日程删除测试", Date = Today });
        var vm = new MonthWidgetViewModel(repo, Today);

        Assert.False(vm.HasAnyOverlay);
        vm.OpenAgenda(Today);
        Assert.True(vm.HasAnyOverlay);
        Assert.Single(vm.AgendaTasks);

        vm.AgendaTasks[0].DeleteCommand.Execute(null);
        Assert.Empty(vm.AgendaTasks);
        Assert.True(vm.IsAgendaEmpty);

        vm.IsAddOpen = true;
        vm.CloseAllOverlays();
        Assert.False(vm.HasAnyOverlay);
        Assert.False(vm.IsAgendaOpen);
        Assert.False(vm.IsAddOpen);
    }
}

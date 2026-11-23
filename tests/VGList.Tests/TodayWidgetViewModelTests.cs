using VGList.App.Services;
using VGList.Core.Models;
using VGList.Widgets.Today;
using Xunit;

namespace VGList.Tests;

public sealed class TodayWidgetViewModelTests
{
    private static readonly DateOnly Today = new(2026, 11, 17);

    private static TodayWidgetViewModel Create(bool seeded = false)
    {
        var repo = new InMemoryTaskRepository();
        if (seeded)
        {
            repo.Create(new TaskDraft { Title = "旧任务", Date = Today, Priority = TaskPriority.Low });
        }

        return new TodayWidgetViewModel(repo, Today);
    }

    [Fact]
    public void SubmitDraft_AddsTaskToTopWithCurrentPriorityAndDate()
    {
        var vm = Create(seeded: true);

        vm.SetPriority(TaskPriority.High);
        vm.DraftText = "  写周报  ";
        vm.SubmitDraft();

        Assert.Equal("写周报", vm.Tasks[0].Title);
        Assert.True(vm.Tasks[0].IsHigh);
        Assert.Equal(Today, vm.Tasks[0].Task.Date);

        Assert.Equal(string.Empty, vm.DraftText);
        Assert.Equal(TaskPriority.None, vm.DraftPriority);
        Assert.True(vm.IsNone);
    }

    [Fact]
    public void SubmitDraft_IgnoresBlankText()
    {
        var vm = Create();

        vm.DraftText = "   ";
        vm.SubmitDraft();

        Assert.Empty(vm.Tasks);
        Assert.Equal("   ", vm.DraftText);
    }

    [Fact]
    public void PickDate_UpdatesLabelAndClosesCalendar()
    {
        var vm = Create();
        vm.IsCalendarOpen = true;

        vm.PickDate(Today.AddDays(1));

        Assert.Equal("明天", vm.DateLabel);
        Assert.False(vm.IsCalendarOpen);
        Assert.Equal(new DateTime(2026, 11, 1), vm.VisibleMonth);
    }

    [Fact]
    public void Calendar_StartsOnSundayAndHasFortyTwoCells()
    {
        var vm = Create();

        Assert.Equal(42, vm.Days.Count);
        Assert.Equal(DayOfWeek.Sunday, vm.Days[0].Date.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 11, 1), vm.Days[0].Date);
        Assert.Contains(vm.Days, d => d.IsToday && d.Date == Today);
    }

    [Fact]
    public void SelectDate_UpdatesLabelButKeepsCalendarOpen()
    {
        var vm = Create();
        vm.IsCalendarOpen = true;

        vm.SelectDate(Today.AddDays(3));

        Assert.Equal("11月20日", vm.DateLabel);
        Assert.True(vm.IsCalendarOpen);
    }

    [Fact]
    public void ToggleTask_MovesRowIntoCompletedGroup()
    {
        var vm = Create(seeded: true);

        vm.ToggleTask(vm.Tasks[0].Task);

        Assert.Empty(vm.Tasks);
        var done = Assert.Single(vm.CompletedTasks);
        Assert.True(done.IsCompleted);
        Assert.Equal("1", vm.CompletedCount);
    }

    [Fact]
    public void CyclePriority_FirstPressGoesHighThenMediumLowNone()
    {
        var vm = Create();

        vm.CyclePriority();
        Assert.Equal(TaskPriority.High, vm.DraftPriority);
        vm.CyclePriority();
        Assert.Equal(TaskPriority.Medium, vm.DraftPriority);
        vm.CyclePriority();
        Assert.Equal(TaskPriority.Low, vm.DraftPriority);
        vm.CyclePriority();
        Assert.Equal(TaskPriority.None, vm.DraftPriority);
        vm.CyclePriority(reverse: true);
        Assert.Equal(TaskPriority.Low, vm.DraftPriority);
    }

    [Fact]
    public void Placeholder_ShowsHintThenPriorityNameIncludingNone()
    {
        var vm = Create();
        vm.IsEditing = true;

        // 刚点进输入框：只提示按 Tab，不报默认优先级。
        Assert.Equal("按Tab切换优先级", vm.PlaceholderText);
        Assert.True(vm.IsPriorityHint);

        // 第一次 Tab 直接到「高」，再依次 中 → 低 → 无。
        vm.CyclePriority();
        Assert.Equal(TaskPriority.High, vm.DraftPriority);
        Assert.Equal("高优先级", vm.PlaceholderText);
        Assert.False(vm.IsPriorityHint);

        vm.CyclePriority();
        vm.CyclePriority();
        vm.CyclePriority();
        Assert.Equal(TaskPriority.None, vm.DraftPriority);
        Assert.Equal("无优先级", vm.PlaceholderText);
    }

    [Fact]
    public void AllView_GroupsByDateWithTodayFirst()
    {
        var repo = new InMemoryTaskRepository();
        repo.Create(new TaskDraft { Title = "三天后任务", Date = Today.AddDays(3) });
        repo.Create(new TaskDraft { Title = "后天任务", Date = Today.AddDays(2) });
        repo.Create(new TaskDraft { Title = "明天任务", Date = Today.AddDays(1) });
        repo.Create(new TaskDraft { Title = "今天任务", Date = Today });
        repo.Create(new TaskDraft { Title = "昨天任务", Date = Today.AddDays(-1) });

        var vm = new TodayWidgetViewModel(repo, Today)
        {
            ActiveView = WidgetViewMode.All,
        };

        Assert.Equal("所有", vm.TitleLabel);
        Assert.Equal(5, vm.Groups.Count);
        Assert.Equal("今天", vm.Groups[0].Header);
        Assert.Equal("明天", vm.Groups[1].Header);
        Assert.Equal("后天", vm.Groups[2].Header);
        Assert.Equal("11月20日 周五", vm.Groups[3].Header);
        Assert.Equal("昨天", vm.Groups[4].Header);
        Assert.All(vm.Groups, g => Assert.True(g.IsHeaderVisible));
        Assert.Equal("今天任务", vm.Tasks[0].Title);
    }

    [Fact]
    public void TodayView_UsesSingleGroupWithoutHeader()
    {
        var vm = Create(seeded: true);

        var group = Assert.Single(vm.Groups);
        Assert.False(group.IsHeaderVisible);
        Assert.Single(group.Tasks);
        Assert.Equal("今天", vm.TitleLabel);
    }

    [Fact]
    public void ShowCompleted_HidesCompletedSection()
    {
        var vm = Create(seeded: true);
        vm.ToggleTask(vm.Tasks[0].Task);
        Assert.True(vm.ShowCompletedSection);

        vm.ShowCompleted = false;

        Assert.False(vm.ShowCompletedSection);
        Assert.False(vm.IsCompletedListVisible);
    }

    [Fact]
    public void TodayView_ListsOverdueTasksInDedicatedGroup_AndPostponeOverdueMovesThemToToday()
    {
        var repo = new InMemoryTaskRepository();
        repo.Create(new TaskDraft { Title = "昨天未完成", Date = Today.AddDays(-1), Priority = TaskPriority.High });
        repo.Create(new TaskDraft { Title = "前天未完成", Date = Today.AddDays(-2), Priority = TaskPriority.Medium });
        var oldDone = repo.Create(new TaskDraft { Title = "昨天已完成", Date = Today.AddDays(-1) });
        repo.SetCompleted(oldDone.Id, true);
        repo.Create(new TaskDraft { Title = "今天的任务", Date = Today });

        using var vm = new TodayWidgetViewModel(repo, Today);

        // 逾期未完成排在「已过期」分组（带顺延按钮），昨天已完成不混入今日已完成
        Assert.Equal(2, vm.Groups.Count);
        Assert.Equal("已过期", vm.Groups[0].Header);
        Assert.True(vm.Groups[0].IsHeaderVisible);
        Assert.True(vm.Groups[0].IsOverdueGroup);
        Assert.Equal("2", vm.Groups[0].CountLabel);
        Assert.All(vm.Groups[0].Tasks, row => Assert.True(row.IsOverdue));

        Assert.Equal("今天", vm.Groups[1].Header);
        Assert.True(vm.Groups[1].IsHeaderVisible);
        Assert.False(vm.Groups[1].IsOverdueGroup);
        Assert.Single(vm.Groups[1].Tasks);
        Assert.Empty(vm.CompletedTasks);

        // 一键「顺延到今天」后，所有逾期未完成任务日期变为今天并合入单组无标题视图
        vm.PostponeOverdue();

        var singleGroup = Assert.Single(vm.Groups);
        Assert.False(singleGroup.IsHeaderVisible);
        Assert.False(singleGroup.IsOverdueGroup);
        Assert.Equal(3, singleGroup.Tasks.Count);
        Assert.All(singleGroup.Tasks, row =>
        {
            Assert.Equal(Today, row.Task.Date);
            Assert.False(row.IsOverdue);
        });
    }
}

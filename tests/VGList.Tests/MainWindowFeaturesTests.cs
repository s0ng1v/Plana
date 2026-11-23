using VGList.App.Services;
using VGList.App.Settings;
using VGList.Core.Models;
using VGList.Desktop.Main.Calendar;
using VGList.Desktop.Main.Eisenhower;
using VGList.Desktop.Main.Tasks;
using VGList.Desktop.Services;
using VGList.Widgets.Support;
using Xunit;

namespace VGList.Tests;

public sealed class MainWindowFeaturesTests
{
    private static readonly DateOnly Today = new(2026, 11, 18);

    [Fact]
    public void TasksPage_PostponeOverdueAndUndoRestoresOriginalDates()
    {
        var repo = new InMemoryTaskRepository();
        var overdue = repo.Create(new TaskDraft
        {
            Title = "逾期任务",
            Date = Today.AddDays(-2),
            Priority = TaskPriority.High,
        });
        var toast = new ToastService();
        var vm = new TasksPageViewModel(repo, toast, Today);

        Assert.Contains(vm.Groups, g => g.IsOverdueGroup);

        vm.PostponeOverdue();

        Assert.Equal(Today, overdue.Date);
        Assert.DoesNotContain(vm.Groups, g => g.IsOverdueGroup);
        Assert.True(toast.IsVisible);
        Assert.True(toast.HasAction);

        toast.ExecuteActionCommand.Execute(null);

        Assert.Equal(Today.AddDays(-2), overdue.Date);
        Assert.Contains(vm.Groups, g => g.IsOverdueGroup);
    }

    [Fact]
    public void TasksPage_DeleteWithUndoRestoresTaskAndContent()
    {
        var repo = new InMemoryTaskRepository();
        var task = repo.Create(new TaskDraft { Title = "可恢复任务", Date = Today, Priority = TaskPriority.Medium });
        task.Content = "详细笔记";
        repo.Update(task);

        var toast = new ToastService();
        var vm = new TasksPageViewModel(repo, toast, Today);

        vm.DeleteTaskWithUndo(task);
        Assert.Empty(repo.Query(new TaskQuery()));
        Assert.True(toast.IsVisible);

        toast.ExecuteActionCommand.Execute(null);
        var restored = Assert.Single(repo.Query(new TaskQuery()));
        Assert.Equal("可恢复任务", restored.Title);
        Assert.Equal("详细笔记", restored.Content);
        Assert.Equal(TaskPriority.Medium, restored.Priority);
    }

    [Fact]
    public void TasksPage_InlineDraftRespectsPriorityDateAndListAndResetsPriority()
    {
        var repo = new InMemoryTaskRepository();
        var work = repo.CreateList("工作");
        var toast = new ToastService();
        var vm = new TasksPageViewModel(repo, toast, Today);

        vm.CycleDraftPriorityCommand.Execute(null); // High
        vm.DraftDate = Today.AddDays(1);
        vm.PickDraftList(work.Id);
        vm.DraftText = "新任务测试";
        vm.SubmitDraft();

        var created = Assert.Single(repo.Query(new TaskQuery()));
        Assert.Equal("新任务测试", created.Title);
        Assert.Equal(TaskPriority.High, created.Priority);
        Assert.Equal(Today.AddDays(1), created.Date);
        Assert.Equal(work.Id, created.ListId);

        Assert.Equal(string.Empty, vm.DraftText);
        Assert.Equal(TaskPriority.None, vm.DraftPriority);
    }

    [Fact]
    public void Eisenhower_MapsPrioritiesToFourQuadrantsAndSupportsQuickAdd()
    {
        var repo = new InMemoryTaskRepository();
        repo.Create(new TaskDraft { Title = "高优", Date = Today, Priority = TaskPriority.High });
        repo.Create(new TaskDraft { Title = "中优", Date = Today, Priority = TaskPriority.Medium });
        repo.Create(new TaskDraft { Title = "低优", Date = Today, Priority = TaskPriority.Low });
        repo.Create(new TaskDraft { Title = "无优", Date = Today, Priority = TaskPriority.None });

        var vm = new EisenhowerViewModel(repo, new ToastService(), Today);

        Assert.Single(vm.QuadrantI.PendingTasks);
        Assert.Single(vm.QuadrantII.PendingTasks);
        Assert.Single(vm.QuadrantIII.PendingTasks);
        Assert.Single(vm.QuadrantIV.PendingTasks);

        vm.QuadrantII.DraftText = "重要不紧急新增";
        vm.QuadrantII.SubmitDraft();

        Assert.Equal(2, vm.QuadrantII.PendingTasks.Count);
        Assert.Contains(repo.Query(new TaskQuery()), t => t.Title == "重要不紧急新增" && t.Priority == TaskPriority.Medium);
    }

    [Fact]
    public void CalendarPage_MonthAndWeekViewsAndDragRescheduleWithUndo()
    {
        var repo = new InMemoryTaskRepository();
        var task = repo.Create(new TaskDraft { Title = "日历拖拽任务", Date = Today, Priority = TaskPriority.High });
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), $"vglist-test-{Guid.NewGuid():N}.json"));
        var coordinator = new WidgetCoordinator(repo, store);
        var toast = new ToastService();
        var vm = new CalendarPageViewModel(repo, toast, coordinator, Today);

        // 2026-11（周日起 30 天）动态为 5 行 35 格
        Assert.Equal(5, vm.WeekRows);
        Assert.Equal(35, vm.MonthCells.Count);

        vm.SwitchToWeekCommand.Execute(null);
        Assert.True(vm.IsWeekView);
        Assert.Equal(7, vm.WeekColumns.Count);

        var targetDate = Today.AddDays(2);
        vm.RescheduleTask(task, targetDate);
        Assert.Equal(targetDate, task.Date);
        Assert.True(toast.IsVisible);

        toast.ExecuteActionCommand.Execute(null);
        Assert.Equal(Today, task.Date);
    }

    [Fact]
    public void QuickCreate_ChecklistConversionAndPieProgressTrackSynchronously()
    {
        var repo = new InMemoryTaskRepository();
        var qc = new QuickCreateViewModel(Today);
        qc.Title = "111";
        qc.Description = "22\n打啥\n333";

        // 文本转待办事项
        qc.ToggleChecklistMode();
        Assert.True(qc.IsChecklistMode);
        Assert.Equal(3, qc.Subtasks.Count);
        Assert.Equal("打啥", qc.Subtasks[1].Title);

        // 勾选其中一条，更新饼状图进度计数
        qc.Subtasks[0].IsCompleted = true;
        Assert.Equal(1, qc.SubtasksCompleted);
        Assert.Equal(3, qc.SubtasksTotal);

        // 转回多行描述，再转回待办事项
        qc.ToggleChecklistMode();
        Assert.False(qc.IsChecklistMode);
        Assert.Equal("22\n打啥\n333", qc.Description.Replace("\r\n", "\n"));

        qc.ToggleChecklistMode();
        var added = qc.AddSubtaskAfter(qc.Subtasks[0]);
        Assert.Equal(4, qc.Subtasks.Count);
        var prev = qc.RemoveSubtask(added);
        Assert.Equal(3, qc.Subtasks.Count);
        Assert.Equal("22", prev?.Title);

        qc.Subtasks[0].IsCompleted = true;
        qc.Subtasks[1].IsCompleted = true;
        qc.CyclePriority(); // High
        var created = qc.Submit(repo);

        Assert.NotNull(created);
        Assert.Equal(TaskPriority.High, created!.Priority);
        Assert.Equal(3, created.Subtasks.Count);

        var rowVm = new MainTaskRowViewModel(created, Today);
        Assert.True(rowVm.HasSubtasks);
        Assert.Equal(2, rowVm.SubtasksCompleted);
        Assert.Equal(3, rowVm.SubtasksTotal);
    }

    [Fact]
    public void CalendarPage_HidesCompletedAndEditsOrDeletesViaQuickCard()
    {
        var repo = new InMemoryTaskRepository();
        var completed = repo.Create(new TaskDraft { Title = "已完成任务", Date = Today });
        repo.SetCompleted(completed.Id, true);
        var active = repo.Create(new TaskDraft { Title = "待编辑任务", Date = Today, Priority = TaskPriority.Low });

        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), $"vglist-test-{Guid.NewGuid():N}.json"));
        var coordinator = new WidgetCoordinator(repo, store);
        var toast = new ToastService();
        var vm = new CalendarPageViewModel(repo, toast, coordinator, Today);

        Assert.False(vm.ShowCompleted);
        var todayCell = Assert.Single(vm.MonthCells, c => c.Date == Today);
        Assert.Single(todayCell.VisibleTasks);
        Assert.Equal("待编辑任务", todayCell.VisibleTasks[0].Title);

        vm.OpenOverflow(Today);
        Assert.Single(vm.OverflowTasks);
        Assert.Equal("待编辑任务", vm.OverflowTasks[0].Title);

        vm.OpenEditTask(active);
        Assert.True(vm.IsQuickAddOpen);
        Assert.True(vm.QuickCreate.IsEditMode);
        Assert.Null(vm.Detail);

        vm.QuickCreate.Title = "已修改标题";
        vm.QuickCreate.CyclePriority(); // Low -> None
        vm.QuickCreate.ToggleChecklistMode();
        Assert.True(vm.QuickCreate.IsChecklistMode);
        var sub1 = vm.QuickCreate.Subtasks[0];
        sub1.Title = "步骤一";
        var sub2 = vm.QuickCreate.AddSubtaskAfter(sub1);
        Assert.Equal(2, vm.QuickCreate.Subtasks.Count);
        vm.QuickCreate.RemoveSubtask(sub2);
        Assert.Single(vm.QuickCreate.Subtasks);
        vm.QuickCreate.ToggleCompleted();

        Assert.Equal("已修改标题", active.Title);
        Assert.Equal(TaskPriority.None, active.Priority);
        Assert.Single(active.Subtasks);
        Assert.True(active.IsCompleted);

        vm.DeleteEditingTask();
        Assert.False(vm.IsQuickAddOpen);
        Assert.DoesNotContain(repo.Query(new TaskQuery { IncludeCompleted = true }), t => t.Id == active.Id);
        Assert.True(toast.IsVisible);

        toast.ExecuteActionCommand.Execute(null);
        Assert.Contains(repo.Query(new TaskQuery { IncludeCompleted = true }), t => t.Id == active.Id);
    }
}

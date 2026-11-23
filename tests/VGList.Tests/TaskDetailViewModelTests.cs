using VGList.App.Services;
using VGList.Core.Models;
using VGList.Widgets.Today;
using Xunit;

namespace VGList.Tests;

public sealed class TaskDetailViewModelTests
{
    private static readonly DateOnly Today = new(2026, 11, 17);

    [Fact]
    public void TitleAndContent_UpdateTaskImmediately()
    {
        var repo = new InMemoryTaskRepository();
        var task = repo.Create(new TaskDraft { Title = "旧标题", Date = Today });
        var vm = new TaskDetailViewModel(repo, task, Today);

        vm.Title = "新标题";
        vm.Content = "## 内容";

        Assert.Equal("新标题", task.Title);
        Assert.Equal("## 内容", task.Content);
    }

    [Fact]
    public void BlankTitle_DoesNotOverwriteTask()
    {
        var repo = new InMemoryTaskRepository();
        var task = repo.Create(new TaskDraft { Title = "原标题", Date = Today });
        var vm = new TaskDetailViewModel(repo, task, Today);

        vm.Title = "   ";

        Assert.Equal("原标题", task.Title);
    }

    [Fact]
    public void CyclePriority_WalksThroughAllLevels()
    {
        var repo = new InMemoryTaskRepository();
        var task = repo.Create(new TaskDraft { Title = "任务", Date = Today });
        var vm = new TaskDetailViewModel(repo, task, Today);

        vm.CyclePriorityCommand.Execute(null);
        Assert.Equal(TaskPriority.Low, task.Priority);

        vm.CyclePriorityCommand.Execute(null);
        Assert.Equal(TaskPriority.Medium, task.Priority);

        vm.CyclePriorityCommand.Execute(null);
        Assert.Equal(TaskPriority.High, task.Priority);

        vm.CyclePriorityCommand.Execute(null);
        Assert.Equal(TaskPriority.None, task.Priority);
    }
}

using VGList.App.Services;
using VGList.Core.Models;
using Xunit;

namespace VGList.Tests;

public sealed class InMemoryTaskRepositoryTests
{
    private static readonly DateOnly Today = new(2026, 11, 17);

    private static TaskItem Task(string title, DateOnly date, bool completed = false, TaskPriority priority = TaskPriority.None)
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            Date = date,
            Priority = priority,
            IsCompleted = completed,
            CreatedAt = DateTimeOffset.Now,
        };

    [Fact]
    public void Query_FiltersByDate()
    {
        var repository = new InMemoryTaskRepository(
        [
            Task("今天的事", Today),
            Task("明天的事", Today.AddDays(1)),
        ]);

        var items = repository.Query(TaskQuery.ForDate(Today));

        Assert.Single(items);
        Assert.Equal("今天的事", items[0].Title);
    }

    [Fact]
    public void Query_PutsUncompletedFirst()
    {
        var repository = new InMemoryTaskRepository(
        [
            Task("已完成", Today, completed: true),
            Task("未完成", Today),
        ]);

        var items = repository.Query(TaskQuery.ForDate(Today));

        Assert.False(items[0].IsCompleted);
        Assert.True(items[1].IsCompleted);
    }

    [Fact]
    public void Create_TrimsTitleAndRaisesChanged()
    {
        var repository = new InMemoryTaskRepository();
        TaskChangedEventArgs? raised = null;
        repository.Changed += (_, e) => raised = e;

        var created = repository.Create(new TaskDraft
        {
            Title = "  写周报  ",
            Date = Today,
            Priority = TaskPriority.High,
        });

        Assert.Equal("写周报", created.Title);
        Assert.Equal(TaskChangeKind.Created, raised?.Kind);
        Assert.Equal(created.Id, raised?.TaskId);
    }

    [Fact]
    public void SetCompleted_TogglesAndStampsCompletedAt()
    {
        var task = Task("勾选我", Today);
        var repository = new InMemoryTaskRepository([task]);

        repository.SetCompleted(task.Id, true);

        Assert.True(task.IsCompleted);
        Assert.NotNull(task.CompletedAt);

        repository.SetCompleted(task.Id, false);

        Assert.False(task.IsCompleted);
        Assert.Null(task.CompletedAt);
    }

    [Fact]
    public void Delete_RemovesTask()
    {
        var task = Task("删掉我", Today);
        var repository = new InMemoryTaskRepository([task]);

        repository.Delete(task.Id);

        Assert.Empty(repository.Query(new TaskQuery()));
    }

    [Fact]
    public void CreateList_AddsListAndRaisesListsChanged()
    {
        var repository = new InMemoryTaskRepository();
        var raised = 0;
        repository.ListsChanged += (_, _) => raised++;

        var created = repository.CreateList("  工作  ");

        Assert.Equal("工作", created.Name);
        Assert.Contains(repository.Lists, l => l.Id == created.Id && l.Name == "工作");
        Assert.Equal(1, raised);
    }

    [Fact]
    public void RenameList_UpdatesName_AndIgnoresInbox()
    {
        var repository = new InMemoryTaskRepository();
        var list = repository.CreateList("旧名");

        repository.RenameList(list.Id, "新名");
        repository.RenameList(TaskList.InboxId, "改名试试");

        Assert.Equal("新名", repository.Lists.Single(l => l.Id == list.Id).Name);
        Assert.Equal("收集箱", repository.Lists.Single(l => l.Id == TaskList.InboxId).Name);
    }

    [Fact]
    public void DeleteList_MovesItsTasksToInbox()
    {
        var repository = new InMemoryTaskRepository();
        var list = repository.CreateList("临时");
        var task = repository.Create(new TaskDraft { Title = "随清单删除", Date = Today, ListId = list.Id });

        repository.DeleteList(list.Id);

        Assert.DoesNotContain(repository.Lists, l => l.Id == list.Id);
        var moved = repository.Query(new TaskQuery()).Single(t => t.Id == task.Id);
        Assert.Equal(TaskList.InboxId, moved.ListId);
    }
}

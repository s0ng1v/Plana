using System.Text.Json;
using VGList.Core.Abstractions;
using VGList.Core.Models;

namespace VGList.App.Services;

/// <summary>
/// 内存仓储与撤销实现：用于单元测试与只读磁盘降级。
/// </summary>
public sealed class InMemoryTaskRepository : ITaskRepository, IScheduleService
{
    private static readonly string[] Palette = ["#8F7BE5", "#5FC7A2", "#66B9EE", "#E8A33D", "#D873CB", "#E5533D"];
    private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly List<TaskItem> _tasks = [];
    private readonly List<TaskList> _lists;
    private readonly List<ChangeSet> _changeSets = [];
    private readonly Lock _gate = new();

    public InMemoryTaskRepository(IEnumerable<TaskItem>? seed = null, IEnumerable<TaskList>? lists = null)
    {
        if (seed is not null)
        {
            _tasks.AddRange(seed);
        }

        _lists = lists?.Select(CloneList).ToList() ??
        [
            new TaskList { Id = TaskList.InboxId, Name = "收集箱", Color = "#E46B92" },
        ];

        TaskListCatalog.Replace(_lists);
    }

    public event EventHandler<TaskChangedEventArgs>? Changed;

    public event EventHandler? ListsChanged;

    public event EventHandler<ChangeSet>? ChangeSetApplied;

    public IReadOnlyList<TaskList> Lists
    {
        get
        {
            lock (_gate)
            {
                return _lists.Select(CloneList).ToList();
            }
        }
    }

    public TaskList CreateList(string name)
    {
        TaskList list;
        lock (_gate)
        {
            list = new TaskList
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = NormalizeName(name),
                Color = Palette[_lists.Count % Palette.Length],
            };
            _lists.Add(list);
        }

        SyncCatalog();
        ListsChanged?.Invoke(this, EventArgs.Empty);
        return CloneList(list);
    }

    public void RenameList(string id, string name)
    {
        if (id == TaskList.InboxId)
        {
            return;
        }

        var normalized = NormalizeName(name);
        lock (_gate)
        {
            var list = _lists.FirstOrDefault(l => l.Id == id);
            if (list is null || list.Name == normalized)
            {
                return;
            }

            list.Name = normalized;
        }

        SyncCatalog();
        ListsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteList(string id)
    {
        if (id == TaskList.InboxId)
        {
            return;
        }

        bool movedTasks;
        lock (_gate)
        {
            if (_lists.RemoveAll(l => l.Id == id) == 0)
            {
                return;
            }

            movedTasks = false;
            foreach (var task in _tasks.Where(t => t.ListId == id))
            {
                task.ListId = TaskList.InboxId;
                movedTasks = true;
            }
        }

        SyncCatalog();
        ListsChanged?.Invoke(this, EventArgs.Empty);

        if (movedTasks)
        {
            Raise(TaskChangeKind.Updated, string.Empty);
        }
    }

    private static TaskList CloneList(TaskList list) => new()
    {
        Id = list.Id,
        Name = list.Name,
        Color = list.Color,
    };

    private static string NormalizeName(string name)
    {
        var trimmed = name.Trim();
        return string.IsNullOrEmpty(trimmed) ? "新建清单" : trimmed;
    }

    private void SyncCatalog() => TaskListCatalog.Replace(Lists.Select(CloneList));

    public IReadOnlyList<TaskItem> Query(TaskQuery query)
    {
        lock (_gate)
        {
            IEnumerable<TaskItem> items = _tasks;

            if (query.Date is { } date)
            {
                items = items.Where(t => t.Date == date);
            }

            if (query.From is { } from)
            {
                items = items.Where(t => t.Date >= from);
            }

            if (query.To is { } to)
            {
                items = items.Where(t => t.Date <= to);
            }

            if (query.ListId is { } listId)
            {
                items = items.Where(t => t.ListId == listId);
            }

            if (!query.IncludeCompleted)
            {
                items = items.Where(t => !t.IsCompleted);
            }

            if (query.DeletedOnly)
            {
                items = items.Where(t => t.IsDeleted);
            }
            else if (!query.IncludeDeleted)
            {
                items = items.Where(t => !t.IsDeleted);
            }

            if (!string.IsNullOrWhiteSpace(query.Tag))
            {
                var targetTag = query.Tag.Trim();
                items = items.Where(t => t.Tags.Any(tag => string.Equals(tag, targetTag, StringComparison.OrdinalIgnoreCase)));
            }

            return query.DeletedOnly
                ? items.OrderByDescending(t => t.DeletedAt).ToList()
                : items.OrderBy(t => t.IsCompleted).ThenByDescending(t => t.CreatedAt).ToList();
        }
    }

    public TaskItem Create(TaskDraft draft)
    {
        var task = new TaskItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = draft.Title.Trim(),
            Content = draft.Content ?? string.Empty,
            Subtasks = draft.Subtasks?
                .Select(s => new TaskSubtask
                {
                    Id = string.IsNullOrEmpty(s.Id) ? Guid.NewGuid().ToString("N") : s.Id,
                    Title = s.Title.Trim(),
                    IsCompleted = s.IsCompleted,
                })
                .ToList() ?? [],
            Tags = TaskTagParser.Normalize(draft.Tags),
            Recurrence = draft.Recurrence,
            Date = draft.Date,
            DueTime = draft.DueTime,
            Reminder = draft.Reminder?.Clone(),
            Priority = draft.Priority,
            ListId = draft.ListId,
            IsCompleted = draft.IsCompleted,
            CompletedAt = draft.IsCompleted ? DateTimeOffset.Now : null,
            CreatedAt = DateTimeOffset.Now,
        };

        lock (_gate)
        {
            _tasks.Add(task);
        }

        Raise(TaskChangeKind.Created, task.Id);
        return task;
    }

    public void Update(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        lock (_gate)
        {
            var index = _tasks.FindIndex(t => t.Id == task.Id);
            if (index < 0)
            {
                return;
            }

            var existing = _tasks[index];
            if (!ReferenceEquals(existing, task))
            {
                if (task.Date > existing.Date)
                {
                    task.RescheduleCount = Math.Max(existing.RescheduleCount, task.RescheduleCount) + 1;
                }

                var scheduleChanged = task.Date != existing.Date ||
                                      task.DueTime != existing.DueTime ||
                                      !(task.Reminder?.ScheduleEquals(existing.Reminder) ?? existing.Reminder is null);
                if (scheduleChanged)
                {
                    task.Reminder?.ResetTriggerState();
                }
            }

            _tasks[index] = task;
        }

        Raise(TaskChangeKind.Updated, task.Id);
    }

    public void SetCompleted(string id, bool completed)
    {
        lock (_gate)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id && !t.IsDeleted);
            if (task is null || task.IsCompleted == completed)
            {
                return;
            }

            task.IsCompleted = completed;
            task.CompletedAt = completed ? DateTimeOffset.Now : null;

            if (completed && task.Recurrence != RecurrenceKind.None)
            {
                _tasks.Add(BuildNextRecurrence(task));
            }
        }

        Raise(TaskChangeKind.CompletedChanged, id);
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id && !t.IsDeleted);
            if (task is null)
            {
                return;
            }

            task.DeletedAt = DateTimeOffset.Now;
        }

        Raise(TaskChangeKind.Deleted, id);
    }

    public void RestoreFromTrash(string id)
    {
        lock (_gate)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id && t.IsDeleted);
            if (task is null)
            {
                return;
            }

            task.DeletedAt = null;
        }

        Raise(TaskChangeKind.Updated, id);
    }

    public void PermanentlyDelete(string id)
    {
        int removed;
        lock (_gate)
        {
            removed = _tasks.RemoveAll(t => t.Id == id);
        }

        if (removed > 0)
        {
            Raise(TaskChangeKind.Deleted, id);
        }
    }

    public int EmptyTrash()
    {
        int removed;
        lock (_gate)
        {
            removed = _tasks.RemoveAll(t => t.IsDeleted);
        }

        if (removed > 0)
        {
            Raise(TaskChangeKind.Deleted, string.Empty);
        }

        return removed;
    }

    public int PurgeExpiredTrash(int retentionDays = 30)
    {
        if (retentionDays <= 0)
        {
            return 0;
        }

        var cutoff = DateTimeOffset.Now.AddDays(-retentionDays);
        int removed;
        lock (_gate)
        {
            removed = _tasks.RemoveAll(t => t.DeletedAt is { } d && d < cutoff);
        }

        if (removed > 0)
        {
            Raise(TaskChangeKind.Deleted, string.Empty);
        }

        return removed;
    }

    public IReadOnlyList<string> GetAllTags()
    {
        lock (_gate)
        {
            return _tasks
                .Where(t => !t.IsDeleted)
                .SelectMany(t => t.Tags)
                .GroupBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }
    }

    public void RecordReminderFired(string id, int firedCount, DateTimeOffset firedAt)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        lock (_gate)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task?.Reminder is null)
            {
                return;
            }

            task.Reminder.FiredCount = Math.Max(task.Reminder.FiredCount, firedCount);
            task.Reminder.LastFiredAt = firedAt;
        }
    }

    private static TaskItem BuildNextRecurrence(TaskItem completed)
    {
        var nextDate = RecurrenceCalculator.NextDate(completed.Date, completed.Recurrence);
        return new TaskItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = completed.Title,
            Content = completed.Content,
            Date = nextDate,
            DueTime = completed.DueTime,
            Reminder = TaskReminderCalculator.CloneForNextOccurrence(completed.Reminder, completed.Date, nextDate),
            Priority = completed.Priority,
            ListId = completed.ListId,
            Tags = [.. completed.Tags],
            Recurrence = completed.Recurrence,
            Subtasks = completed.Subtasks.Select(s => new TaskSubtask
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = s.Title,
                IsCompleted = false,
            }).ToList(),
            CreatedAt = DateTimeOffset.Now,
        };
    }

    public void Restore(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        lock (_gate)
        {
            if (_tasks.All(t => t.Id != task.Id))
            {
                _tasks.Add(task.Clone());
            }
        }

        Raise(TaskChangeKind.Created, task.Id);
    }

    public void ReplaceAll(IEnumerable<TaskItem> tasks)
    {
        lock (_gate)
        {
            _tasks.Clear();
            _tasks.AddRange(tasks.Select(t => t.Clone()));
        }

        Raise(TaskChangeKind.Updated, string.Empty);
    }

    public ChangeSet Apply(IReadOnlyList<ScheduleOp> ops, ChangeSource source, string label)
    {
        var entries = new List<ChangeEntry>();
        var listsDirty = false;
        var tasksDirty = false;

        lock (_gate)
        {
            var backupTasks = _tasks.Select(t => t.Clone()).ToList();
            var backupLists = _lists.Select(CloneList).ToList();
            try
            {
                foreach (var op in ops)
                {
                    switch (op)
                    {
                        case CreateTaskOp create:
                        {
                            var task = new TaskItem
                            {
                                Id = Guid.NewGuid().ToString("N"),
                                Title = create.Draft.Title.Trim(),
                                Content = create.Draft.Content ?? string.Empty,
                                Subtasks = create.Draft.Subtasks?.Select(s => new TaskSubtask
                                {
                                    Id = string.IsNullOrEmpty(s.Id) ? Guid.NewGuid().ToString("N") : s.Id,
                                    Title = s.Title.Trim(),
                                    IsCompleted = s.IsCompleted,
                                }).ToList() ?? [],
                                Tags = TaskTagParser.Normalize(create.Draft.Tags),
                                Recurrence = create.Draft.Recurrence,
                                Date = create.Draft.Date,
                                DueTime = create.Draft.DueTime,
                                Reminder = create.Draft.Reminder?.Clone(),
                                Priority = create.Draft.Priority,
                                ListId = create.Draft.ListId,
                                IsCompleted = create.Draft.IsCompleted,
                                CompletedAt = create.Draft.IsCompleted ? DateTimeOffset.Now : null,
                                CreatedAt = DateTimeOffset.Now,
                            };
                            _tasks.Add(task);
                            entries.Add(new ChangeEntry(ChangeEntryKind.TaskCreated, task.Id, null, Serialize(task)));
                            tasksDirty = true;
                            break;
                        }

                        case UpdateTaskOp update:
                        {
                            var idx = _tasks.FindIndex(t => t.Id == update.TaskId);
                            if (idx < 0)
                            {
                                throw new InvalidOperationException($"任务不存在：{update.TaskId}");
                            }

                            var target = _tasks[idx];
                            var beforeJson = Serialize(target);
                            var origDate = target.Date;
                            var origDueTime = target.DueTime;
                            var origReminder = target.Reminder?.Clone();
                            if (update.Patch.Title is not null) target.Title = update.Patch.Title.Trim();
                            if (update.Patch.Content is not null) target.Content = update.Patch.Content;
                            if (update.Patch.AppendContentSection is { } sec)
                                target.Content = TaskPatch.ApplyContentSection(target.Content, sec.Heading, sec.Markdown);
                            if (update.Patch.Date is { } d)
                            {
                                if (d > target.Date) target.RescheduleCount++;
                                target.Date = d;
                            }
                            if (update.Patch.ClearDueTime) target.DueTime = null;
                            else if (update.Patch.DueTime is { } dt) target.DueTime = dt;
                            if (update.Patch.ClearReminder) target.Reminder = null;
                            else if (update.Patch.Reminder is { } rem) target.Reminder = rem.Clone();
                            if (update.Patch.Priority is { } p) target.Priority = p;
                            if (update.Patch.ListId is not null) target.ListId = update.Patch.ListId;
                            if (update.Patch.Tags is not null) target.Tags = TaskTagParser.Normalize(update.Patch.Tags);
                            if (update.Patch.Recurrence is { } rec) target.Recurrence = rec;
                            if (update.Patch.Subtasks is not null) target.Subtasks = update.Patch.Subtasks.ToList();
                            if (update.Patch.AppendSubtasks is not null) target.Subtasks.AddRange(update.Patch.AppendSubtasks);

                            var schedChanged = target.Date != origDate ||
                                               target.DueTime != origDueTime ||
                                               !(target.Reminder?.ScheduleEquals(origReminder) ?? origReminder is null);
                            if (schedChanged)
                            {
                                target.Reminder?.ResetTriggerState();
                            }

                            entries.Add(new ChangeEntry(ChangeEntryKind.TaskUpdated, target.Id, beforeJson, Serialize(target)));
                            tasksDirty = true;
                            break;
                        }

                        case SetCompletedOp comp:
                        {
                            var task = _tasks.FirstOrDefault(t => t.Id == comp.TaskId)
                                ?? throw new InvalidOperationException($"任务不存在：{comp.TaskId}");
                            var before = Serialize(task);
                            task.IsCompleted = comp.Completed;
                            task.CompletedAt = comp.Completed ? DateTimeOffset.Now : null;
                            entries.Add(new ChangeEntry(ChangeEntryKind.TaskUpdated, task.Id, before, Serialize(task)));
                            if (comp.Completed && task.Recurrence != RecurrenceKind.None)
                            {
                                var next = BuildNextRecurrence(task);
                                _tasks.Add(next);
                                entries.Add(new ChangeEntry(ChangeEntryKind.TaskCreated, next.Id, null, Serialize(next)));
                            }

                            tasksDirty = true;
                            break;
                        }

                        case DeleteTaskOp del:
                        {
                            var idx = _tasks.FindIndex(t => t.Id == del.TaskId && !t.IsDeleted);
                            if (idx < 0) throw new InvalidOperationException($"任务不存在：{del.TaskId}");
                            var target = _tasks[idx];
                            var before = Serialize(target);
                            target.DeletedAt = DateTimeOffset.Now;
                            entries.Add(new ChangeEntry(ChangeEntryKind.TaskUpdated, del.TaskId, before, Serialize(target)));
                            tasksDirty = true;
                            break;
                        }

                        case CreateListOp cl:
                        {
                            var list = new TaskList
                            {
                                Id = Guid.NewGuid().ToString("N"),
                                Name = NormalizeName(cl.Name),
                                Color = Palette[_lists.Count % Palette.Length],
                            };
                            _lists.Add(list);
                            entries.Add(new ChangeEntry(ChangeEntryKind.ListCreated, list.Id, null, Serialize(list)));
                            listsDirty = true;
                            break;
                        }
                    }
                }
            }
            catch
            {
                _tasks.Clear();
                _tasks.AddRange(backupTasks);
                _lists.Clear();
                _lists.AddRange(backupLists);
                throw;
            }

            var cs = new ChangeSet(Guid.NewGuid().ToString("N"), source, label, DateTimeOffset.Now, entries);
            _changeSets.Insert(0, cs);
            if (_changeSets.Count > 500) _changeSets.RemoveRange(500, _changeSets.Count - 500);

            if (listsDirty) SyncCatalog();
            if (listsDirty) ListsChanged?.Invoke(this, EventArgs.Empty);
            if (tasksDirty) Raise(TaskChangeKind.Updated, string.Empty);
            ChangeSetApplied?.Invoke(this, cs);
            return cs;
        }
    }

    public ChangeSet ReplaceAllTasks(IEnumerable<TaskItem> replacement, string label = "重置为示例数据")
    {
        var items = replacement.Select(t => t.Clone()).ToList();
        ChangeSet cs;
        lock (_gate)
        {
            var entries = new List<ChangeEntry>();
            foreach (var old in _tasks)
                entries.Add(new ChangeEntry(ChangeEntryKind.TaskDeleted, old.Id, Serialize(old), null));
            _tasks.Clear();
            foreach (var next in items)
            {
                _tasks.Add(next);
                entries.Add(new ChangeEntry(ChangeEntryKind.TaskCreated, next.Id, null, Serialize(next)));
            }

            cs = new ChangeSet(Guid.NewGuid().ToString("N"), ChangeSource.User, label, DateTimeOffset.Now, entries);
            _changeSets.Insert(0, cs);
        }

        Raise(TaskChangeKind.Updated, string.Empty);
        ChangeSetApplied?.Invoke(this, cs);
        return cs;
    }

    public UndoResult Undo(string changeSetId) => UndoInternal(changeSetId, null);

    public UndoResult UndoEntries(string changeSetId, IReadOnlyCollection<string> entityIds)
    {
        if (entityIds is null || entityIds.Count == 0)
        {
            return new UndoResult(false, 0, [], "未指定要撤销的条目");
        }

        return UndoInternal(changeSetId, new HashSet<string>(entityIds, StringComparer.Ordinal));
    }

    private UndoResult UndoInternal(string changeSetId, HashSet<string>? filterEntityIds)
    {
        var skipped = new List<UndoSkippedItem>();
        var reverted = 0;

        lock (_gate)
        {
            var idx = _changeSets.FindIndex(c => c.Id == changeSetId);
            if (idx < 0) return new UndoResult(false, 0, [], "未找到变更记录");
            var cs = _changeSets[idx];
            if (cs.UndoneAt is not null) return new UndoResult(false, 0, [], "已撤销过");

            var revertedIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = cs.Entries.Count - 1; i >= 0; i--)
            {
                var e = cs.Entries[i];
                if (filterEntityIds is not null && !filterEntityIds.Contains(e.EntityId))
                {
                    continue;
                }

                switch (e.Kind)
                {
                    case ChangeEntryKind.TaskCreated:
                        var ci = _tasks.FindIndex(t => t.Id == e.EntityId);
                        if (ci < 0 || Serialize(_tasks[ci]) != e.AfterJson) { skipped.Add(new(e.EntityId, "已变更")); continue; }
                        _tasks.RemoveAt(ci);
                        reverted++;
                        revertedIds.Add(e.EntityId);
                        break;
                    case ChangeEntryKind.TaskUpdated:
                        var ui = _tasks.FindIndex(t => t.Id == e.EntityId);
                        if (ui < 0 || Serialize(_tasks[ui]) != e.AfterJson) { skipped.Add(new(e.EntityId, "已变更")); continue; }
                        var restored = JsonSerializer.Deserialize<TaskItem>(e.BeforeJson!, SnapshotJson)!;
                        var cur = _tasks[ui];
                        cur.Title = restored.Title;
                        cur.Content = restored.Content;
                        cur.Subtasks = restored.Subtasks;
                        cur.Tags = [.. restored.Tags];
                        cur.Recurrence = restored.Recurrence;
                        cur.Date = restored.Date;
                        cur.DueTime = restored.DueTime;
                        cur.Reminder = restored.Reminder?.Clone();
                        cur.Priority = restored.Priority;
                        cur.ListId = restored.ListId;
                        cur.IsCompleted = restored.IsCompleted;
                        cur.CompletedAt = restored.CompletedAt;
                        cur.DeletedAt = restored.DeletedAt;
                        cur.RescheduleCount = restored.RescheduleCount;
                        reverted++;
                        revertedIds.Add(e.EntityId);
                        break;
                    case ChangeEntryKind.TaskDeleted:
                        if (_tasks.Any(t => t.Id == e.EntityId)) { skipped.Add(new(e.EntityId, "已存在")); continue; }
                        _tasks.Add(JsonSerializer.Deserialize<TaskItem>(e.BeforeJson!, SnapshotJson)!);
                        reverted++;
                        revertedIds.Add(e.EntityId);
                        break;
                }
            }

            if (filterEntityIds is null)
            {
                _changeSets[idx] = cs with { UndoneAt = DateTimeOffset.Now };
            }
            else if (revertedIds.Count > 0)
            {
                var remaining = cs.Entries.Where(e => !revertedIds.Contains(e.EntityId)).ToList();
                _changeSets[idx] = remaining.Count == 0
                    ? cs with { Entries = remaining, UndoneAt = DateTimeOffset.Now }
                    : cs with { Entries = remaining };
            }
        }

        Raise(TaskChangeKind.Updated, string.Empty);
        return new UndoResult(true, reverted, skipped);
    }

    public UndoResult? UndoLatest(IReadOnlyCollection<ChangeSource> sources, TimeSpan within)
    {
        var cutoff = DateTimeOffset.Now - within;
        var target = _changeSets.FirstOrDefault(c => c.UndoneAt is null && c.At >= cutoff && sources.Contains(c.Source));
        return target is null ? null : Undo(target.Id);
    }

    public IReadOnlyList<ChangeSet> Recent(int count, ChangeSource? source = null)
    {
        lock (_gate)
        {
            IEnumerable<ChangeSet> q = _changeSets;
            if (source is { } s) q = q.Where(c => c.Source == s);
            return q.Take(count).ToList();
        }
    }

    private static string Serialize<T>(T value)
    {
        if (value is TaskItem t && (t.Reminder is { FiredCount: > 0 } or { LastFiredAt: not null }))
        {
            var normalized = t.Clone();
            normalized.Reminder?.ResetTriggerState();
            return JsonSerializer.Serialize(normalized, SnapshotJson);
        }

        return JsonSerializer.Serialize(value, SnapshotJson);
    }

    private void Raise(TaskChangeKind kind, string taskId) =>
        Changed?.Invoke(this, new TaskChangedEventArgs(kind, taskId));
}

using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using VGList.Core.Abstractions;
using VGList.Core.Models;

namespace VGList.Data;

/// <summary>
/// 基于 SQLite(WAL) 的任务/清单唯一仓储与批量撤销服务。
/// 每次单步用户操作或批量 Apply 都在单事务内完成并写入 ChangeSet，支持整批冲突感知撤销。
/// </summary>
public sealed class SqliteTaskRepository : ITaskRepository, IScheduleService, IDisposable
{
    private const int MaxChangeSets = 500;
    private static readonly TimeSpan ChangeSetRetention = TimeSpan.FromDays(90);
    private static readonly string[] Palette = ["#8F7BE5", "#5FC7A2", "#66B9EE", "#E8A33D", "#D873CB", "#E5533D"];

    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly SqliteDatabase _db;
    private readonly AsyncLocal<ActiveScope?> _scope = new();
    private bool _disposed;

    public SqliteTaskRepository(SqliteDatabase db)
    {
        _db = db;
        EnsureInboxExists();
        if (!db.IsReadOnlyFutureVersion)
        {
            PurgeExpiredTrash(30);
        }

        SyncCatalog();
    }

    public bool RecoveredFromCorruption => _db.RecoveredFromCorruption;

    public bool IsReadOnlyFutureVersion => _db.IsReadOnlyFutureVersion;

    public SqliteDatabase Database => _db;

    public event EventHandler<TaskChangedEventArgs>? Changed;

    public event EventHandler? ListsChanged;

    public event EventHandler<ChangeSet>? ChangeSetApplied;

    public bool IsEmpty
    {
        get
        {
            lock (_db.Gate)
            {
                using var cmd = _db.Connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(1) FROM tasks;";
                var taskCount = Convert.ToInt64(cmd.ExecuteScalar());
                cmd.CommandText = "SELECT COUNT(1) FROM lists WHERE id != $inbox;";
                cmd.Parameters.AddWithValue("$inbox", TaskList.InboxId);
                var extraLists = Convert.ToInt64(cmd.ExecuteScalar());
                return taskCount == 0 && extraLists == 0;
            }
        }
    }

    /// <summary>首次空库写入初始欢迎数据（source=System，不进入用户撤销栈）。</summary>
    public void SeedInitial(IEnumerable<TaskList> lists, IEnumerable<TaskItem> tasks)
    {
        lock (_db.Gate)
        {
            EnsureWritable();
            using var tx = _db.Connection.BeginTransaction();

            foreach (var list in lists)
            {
                UpsertListRaw(tx, list);
            }

            foreach (var task in tasks)
            {
                InsertTaskRaw(tx, task);
            }

            tx.Commit();
        }

        SyncCatalog();
        ListsChanged?.Invoke(this, EventArgs.Empty);
        Raise(TaskChangeKind.Updated, string.Empty);
    }

    public IReadOnlyList<TaskList> Lists
    {
        get
        {
            lock (_db.Gate)
            {
                return LoadListsRaw(null);
            }
        }
    }

    public TaskList CreateList(string name)
    {
        TaskList created;
        ChangeSet? cs = null;

        lock (_db.Gate)
        {
            EnsureWritable();
            var active = _scope.Value;
            if (active is not null)
            {
                created = CreateListCore(active.Transaction, name, active.Entries);
                active.ListsDirty = true;
            }
            else
            {
                using var tx = _db.Connection.BeginTransaction();
                var entries = new List<ChangeEntry>();
                created = CreateListCore(tx, name, entries);
                cs = PersistChangeSet(tx, ChangeSource.User, $"新建清单「{created.Name}」", entries);
                tx.Commit();
            }
        }

        if (_scope.Value is null)
        {
            SyncCatalog();
            ListsChanged?.Invoke(this, EventArgs.Empty);
            if (cs is not null)
            {
                ChangeSetApplied?.Invoke(this, cs);
            }
        }

        return CloneList(created);
    }

    public void RenameList(string id, string name)
    {
        if (id == TaskList.InboxId)
        {
            return;
        }

        ChangeSet? cs = null;
        var changed = false;

        lock (_db.Gate)
        {
            EnsureWritable();
            var normalized = NormalizeName(name);
            var active = _scope.Value;
            var tx = active?.Transaction ?? _db.Connection.BeginTransaction();
            try
            {
                var before = LoadListByIdRaw(tx, id);
                if (before is null || before.Name == normalized)
                {
                    if (active is null)
                    {
                        tx.Rollback();
                    }

                    return;
                }

                var after = CloneList(before);
                after.Name = normalized;

                using (var cmd = _db.Connection.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "UPDATE lists SET name = $name WHERE id = $id;";
                    cmd.Parameters.AddWithValue("$name", normalized);
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.ExecuteNonQuery();
                }

                var entry = new ChangeEntry(
                    ChangeEntryKind.ListRenamed,
                    id,
                    SerializeList(before),
                    SerializeList(after));

                if (active is not null)
                {
                    active.Entries.Add(entry);
                    active.ListsDirty = true;
                }
                else
                {
                    cs = PersistChangeSet(tx, ChangeSource.User, $"重命名清单「{normalized}」", [entry]);
                    tx.Commit();
                }

                changed = true;
            }
            finally
            {
                if (active is null)
                {
                    tx.Dispose();
                }
            }
        }

        if (changed && _scope.Value is null)
        {
            SyncCatalog();
            ListsChanged?.Invoke(this, EventArgs.Empty);
            if (cs is not null)
            {
                ChangeSetApplied?.Invoke(this, cs);
            }
        }
    }

    public void DeleteList(string id)
    {
        if (id == TaskList.InboxId)
        {
            return;
        }

        ChangeSet? cs = null;
        var deleted = false;
        var movedTasks = false;

        lock (_db.Gate)
        {
            EnsureWritable();
            var active = _scope.Value;
            var tx = active?.Transaction ?? _db.Connection.BeginTransaction();
            try
            {
                var beforeList = LoadListByIdRaw(tx, id);
                if (beforeList is null)
                {
                    if (active is null)
                    {
                        tx.Rollback();
                    }

                    return;
                }

                var entries = new List<ChangeEntry>();
                // 先记录每个被迁回收集箱的任务前后快照，确保撤销删除清单时任务归属一并恢复
                var affectedTasks = QueryRaw(tx, new TaskQuery { ListId = id });
                foreach (var task in affectedTasks)
                {
                    var beforeJson = SerializeTask(task);
                    task.ListId = TaskList.InboxId;
                    UpdateTaskRaw(tx, task);
                    entries.Add(new ChangeEntry(ChangeEntryKind.TaskUpdated, task.Id, beforeJson, SerializeTask(task)));
                    movedTasks = true;
                }

                using (var cmd = _db.Connection.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM lists WHERE id = $id;";
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.ExecuteNonQuery();
                }

                entries.Add(new ChangeEntry(ChangeEntryKind.ListDeleted, id, SerializeList(beforeList), null));

                if (active is not null)
                {
                    active.Entries.AddRange(entries);
                    active.ListsDirty = true;
                    if (movedTasks)
                    {
                        active.TasksDirty = true;
                    }
                }
                else
                {
                    cs = PersistChangeSet(tx, ChangeSource.User, $"删除清单「{beforeList.Name}」", entries);
                    tx.Commit();
                }

                deleted = true;
            }
            finally
            {
                if (active is null)
                {
                    tx.Dispose();
                }
            }
        }

        if (deleted && _scope.Value is null)
        {
            SyncCatalog();
            ListsChanged?.Invoke(this, EventArgs.Empty);
            if (movedTasks)
            {
                Raise(TaskChangeKind.Updated, string.Empty);
            }

            if (cs is not null)
            {
                ChangeSetApplied?.Invoke(this, cs);
            }
        }
    }

    public IReadOnlyList<TaskItem> Query(TaskQuery query)
    {
        lock (_db.Gate)
        {
            return QueryRaw(null, query);
        }
    }

    public TaskItem Create(TaskDraft draft)
    {
        TaskItem created;
        ChangeSet? cs = null;

        lock (_db.Gate)
        {
            EnsureWritable();
            var active = _scope.Value;
            if (active is not null)
            {
                created = CreateTaskCore(active.Transaction, draft, active.Entries);
                active.TasksDirty = true;
            }
            else
            {
                using var tx = _db.Connection.BeginTransaction();
                var entries = new List<ChangeEntry>();
                created = CreateTaskCore(tx, draft, entries);
                cs = PersistChangeSet(tx, ChangeSource.User, $"新建任务「{created.Title}」", entries);
                tx.Commit();
            }
        }

        if (_scope.Value is null)
        {
            Raise(TaskChangeKind.Created, created.Id);
            if (cs is not null)
            {
                ChangeSetApplied?.Invoke(this, cs);
            }
        }

        return created.Clone();
    }

    public void Update(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        ChangeSet? cs = null;
        var updated = false;

        lock (_db.Gate)
        {
            EnsureWritable();
            var active = _scope.Value;
            var tx = active?.Transaction ?? _db.Connection.BeginTransaction();
            try
            {
                var existing = LoadTaskByIdRaw(tx, task.Id);
                if (existing is null)
                {
                    if (active is null)
                    {
                        tx.Rollback();
                    }

                    return;
                }

                var beforeJson = SerializeTask(existing);
                var next = task.Clone();
                // 当日期被推迟到更晚的日期时，RescheduleCount 自动递增（保留现有计数避免调用方未赋值时归零）
                next.RescheduleCount = Math.Max(existing.RescheduleCount, task.RescheduleCount);
                if (next.Date > existing.Date)
                {
                    next.RescheduleCount = existing.RescheduleCount + 1;
                }

                // 若日期、时刻或提醒计划配置发生变化，则重置触发状态以便重新提醒
                var reminderScheduleChanged = next.Date != existing.Date ||
                                              next.DueTime != existing.DueTime ||
                                              !(next.Reminder?.ScheduleEquals(existing.Reminder) ?? existing.Reminder is null);
                if (reminderScheduleChanged)
                {
                    next.Reminder?.ResetTriggerState();
                }

                UpdateTaskRaw(tx, next);
                var entry = new ChangeEntry(ChangeEntryKind.TaskUpdated, next.Id, beforeJson, SerializeTask(next));

                if (active is not null)
                {
                    active.Entries.Add(entry);
                    active.TasksDirty = true;
                }
                else
                {
                    cs = PersistChangeSet(tx, ChangeSource.User, $"更新任务「{next.Title}」", [entry]);
                    tx.Commit();
                }

                updated = true;
            }
            finally
            {
                if (active is null)
                {
                    tx.Dispose();
                }
            }
        }

        if (updated && _scope.Value is null)
        {
            Raise(TaskChangeKind.Updated, task.Id);
            if (cs is not null)
            {
                ChangeSetApplied?.Invoke(this, cs);
            }
        }
    }

    public void SetCompleted(string id, bool completed)
    {
        ChangeSet? cs = null;
        var changed = false;

        lock (_db.Gate)
        {
            EnsureWritable();
            var active = _scope.Value;
            var tx = active?.Transaction ?? _db.Connection.BeginTransaction();
            try
            {
                var existing = LoadTaskByIdRaw(tx, id);
                if (existing is null || existing.IsCompleted == completed)
                {
                    if (active is null)
                    {
                        tx.Rollback();
                    }

                    return;
                }

                var beforeJson = SerializeTask(existing);
                existing.IsCompleted = completed;
                existing.CompletedAt = completed ? DateTimeOffset.Now : null;
                UpdateTaskRaw(tx, existing);
                var entries = new List<ChangeEntry>
                {
                    new(ChangeEntryKind.TaskUpdated, existing.Id, beforeJson, SerializeTask(existing)),
                };

                // P1：完成重复任务时，在同一事务与同一 ChangeSet 内自动生成下一期
                if (completed && existing.Recurrence != RecurrenceKind.None)
                {
                    CreateNextRecurrenceCore(tx, existing, entries);
                }

                if (active is not null)
                {
                    active.Entries.AddRange(entries);
                    active.TasksDirty = true;
                }
                else
                {
                    cs = PersistChangeSet(
                        tx,
                        ChangeSource.User,
                        completed ? $"完成任务「{existing.Title}」" : $"恢复任务「{existing.Title}」",
                        entries);
                    tx.Commit();
                }

                changed = true;
            }
            finally
            {
                if (active is null)
                {
                    tx.Dispose();
                }
            }
        }

        if (changed && _scope.Value is null)
        {
            Raise(TaskChangeKind.CompletedChanged, id);
            if (cs is not null)
            {
                ChangeSetApplied?.Invoke(this, cs);
            }
        }
    }

    public void Delete(string id)
    {
        ChangeSet? cs = null;
        var deleted = false;

        lock (_db.Gate)
        {
            EnsureWritable();
            var active = _scope.Value;
            var tx = active?.Transaction ?? _db.Connection.BeginTransaction();
            try
            {
                var existing = LoadTaskByIdRaw(tx, id);
                if (existing is null || existing.IsDeleted)
                {
                    if (active is null)
                    {
                        tx.Rollback();
                    }

                    return;
                }

                var beforeJson = SerializeTask(existing);
                existing.DeletedAt = DateTimeOffset.Now;
                UpdateTaskRaw(tx, existing);
                var entry = new ChangeEntry(ChangeEntryKind.TaskUpdated, id, beforeJson, SerializeTask(existing));

                if (active is not null)
                {
                    active.Entries.Add(entry);
                    active.TasksDirty = true;
                }
                else
                {
                    cs = PersistChangeSet(tx, ChangeSource.User, $"移入回收站「{existing.Title}」", [entry]);
                    tx.Commit();
                }

                deleted = true;
            }
            finally
            {
                if (active is null)
                {
                    tx.Dispose();
                }
            }
        }

        if (deleted && _scope.Value is null)
        {
            Raise(TaskChangeKind.Deleted, id);
            if (cs is not null)
            {
                ChangeSetApplied?.Invoke(this, cs);
            }
        }
    }

    public void RestoreFromTrash(string id)
    {
        ChangeSet? cs = null;
        var restored = false;

        lock (_db.Gate)
        {
            EnsureWritable();
            using var tx = _db.Connection.BeginTransaction();
            var existing = LoadTaskByIdRaw(tx, id);
            if (existing is null || !existing.IsDeleted)
            {
                tx.Rollback();
                return;
            }

            var beforeJson = SerializeTask(existing);
            existing.DeletedAt = null;
            UpdateTaskRaw(tx, existing);
            var entry = new ChangeEntry(ChangeEntryKind.TaskUpdated, id, beforeJson, SerializeTask(existing));
            cs = PersistChangeSet(tx, ChangeSource.User, $"从回收站恢复「{existing.Title}」", [entry]);
            tx.Commit();
            restored = true;
        }

        if (restored)
        {
            Raise(TaskChangeKind.Updated, id);
            if (cs is not null)
            {
                ChangeSetApplied?.Invoke(this, cs);
            }
        }
    }

    public void PermanentlyDelete(string id)
    {
        var removed = false;
        lock (_db.Gate)
        {
            EnsureWritable();
            using var tx = _db.Connection.BeginTransaction();
            var existing = LoadTaskByIdRaw(tx, id);
            if (existing is null)
            {
                tx.Rollback();
                return;
            }

            DeleteTaskRaw(tx, id);
            tx.Commit();
            removed = true;
        }

        if (removed)
        {
            Raise(TaskChangeKind.Deleted, id);
        }
    }

    public int EmptyTrash()
    {
        int removed;
        lock (_db.Gate)
        {
            EnsureWritable();
            using var tx = _db.Connection.BeginTransaction();
            using var cmd = _db.Connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM tasks WHERE deleted_at IS NOT NULL;";
            removed = cmd.ExecuteNonQuery();
            tx.Commit();
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
        var expiredIds = new List<string>();
        lock (_db.Gate)
        {
            if (_db.IsReadOnlyFutureVersion)
            {
                return 0;
            }

            using var tx = _db.Connection.BeginTransaction();
            using (var queryCmd = _db.Connection.CreateCommand())
            {
                queryCmd.Transaction = tx;
                queryCmd.CommandText = "SELECT id, deleted_at FROM tasks WHERE deleted_at IS NOT NULL;";
                using var reader = queryCmd.ExecuteReader();
                while (reader.Read())
                {
                    if (!reader.IsDBNull(1) &&
                        DateTimeOffset.TryParse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var deletedAt) &&
                        deletedAt < cutoff)
                    {
                        expiredIds.Add(reader.GetString(0));
                    }
                }
            }

            foreach (var id in expiredIds)
            {
                DeleteTaskRaw(tx, id);
            }

            tx.Commit();
        }

        if (expiredIds.Count > 0)
        {
            Raise(TaskChangeKind.Deleted, string.Empty);
        }

        return expiredIds.Count;
    }

    public IReadOnlyList<string> GetAllTags()
    {
        lock (_db.Gate)
        {
            var active = QueryRaw(null, new TaskQuery { IncludeCompleted = true });
            return active
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

        lock (_db.Gate)
        {
            if (_db.IsReadOnlyFutureVersion)
            {
                return;
            }

            var existing = LoadTaskByIdRaw(null, id);
            if (existing?.Reminder is null)
            {
                return;
            }

            existing.Reminder.FiredCount = Math.Max(existing.Reminder.FiredCount, firedCount);
            existing.Reminder.LastFiredAt = firedAt;

            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "UPDATE tasks SET reminder_json = $reminderJson WHERE id = $id;";
            cmd.Parameters.AddWithValue("$reminderJson", SerializeReminder(existing.Reminder) ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>原子执行多条声明式日程操作，产出单个 ChangeSet；任一操作失败则整体回滚。</summary>
    public ChangeSet Apply(IReadOnlyList<ScheduleOp> ops, ChangeSource source, string label)
    {
        ArgumentNullException.ThrowIfNull(ops);
        if (ops.Count == 0)
        {
            return new ChangeSet(Guid.NewGuid().ToString("N"), source, label, DateTimeOffset.Now, []);
        }

        ChangeSet changeSet;
        bool tasksDirty;
        bool listsDirty;

        lock (_db.Gate)
        {
            EnsureWritable();
            using var tx = _db.Connection.BeginTransaction();
            var scope = new ActiveScope(tx);
            _scope.Value = scope;
            try
            {
                foreach (var op in ops)
                {
                    ExecuteOpInScope(tx, op, scope);
                }

                changeSet = PersistChangeSet(tx, source, label, scope.Entries);
                tx.Commit();
                tasksDirty = scope.TasksDirty;
                listsDirty = scope.ListsDirty;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
            finally
            {
                _scope.Value = null;
            }
        }

        if (listsDirty)
        {
            SyncCatalog();
            ListsChanged?.Invoke(this, EventArgs.Empty);
        }

        if (tasksDirty)
        {
            Raise(TaskChangeKind.Updated, string.Empty);
        }

        ChangeSetApplied?.Invoke(this, changeSet);
        return changeSet;
    }

    public ChangeSet ReplaceAllTasks(IEnumerable<TaskItem> replacement, string label = "重置为示例数据")
    {
        var items = replacement.Select(t => t.Clone()).ToList();
        ChangeSet changeSet;

        lock (_db.Gate)
        {
            EnsureWritable();
            using var tx = _db.Connection.BeginTransaction();
            var entries = new List<ChangeEntry>();

            var current = QueryRaw(tx, new TaskQuery());
            foreach (var old in current)
            {
                entries.Add(new ChangeEntry(ChangeEntryKind.TaskDeleted, old.Id, SerializeTask(old), null));
                DeleteTaskRaw(tx, old.Id);
            }

            foreach (var item in items)
            {
                InsertTaskRaw(tx, item);
                entries.Add(new ChangeEntry(ChangeEntryKind.TaskCreated, item.Id, null, SerializeTask(item)));
            }

            changeSet = PersistChangeSet(tx, ChangeSource.User, label, entries);
            tx.Commit();
        }

        Raise(TaskChangeKind.Updated, string.Empty);
        ChangeSetApplied?.Invoke(this, changeSet);
        return changeSet;
    }

    public UndoResult Undo(string changeSetId) => UndoInternal(changeSetId, null);

    public UndoResult UndoEntries(string changeSetId, IReadOnlyCollection<string> entityIds)
    {
        if (entityIds is null || entityIds.Count == 0)
        {
            return new UndoResult(false, 0, [], "未指定要撤销的条目");
        }

        var filter = new HashSet<string>(entityIds, StringComparer.Ordinal);
        return UndoInternal(changeSetId, filter);
    }

    private UndoResult UndoInternal(string changeSetId, HashSet<string>? filterEntityIds)
    {
        if (string.IsNullOrWhiteSpace(changeSetId))
        {
            return new UndoResult(false, 0, [], "无效的变更集标识");
        }

        var skipped = new List<UndoSkippedItem>();
        var reverted = 0;
        var tasksDirty = false;
        var listsDirty = false;

        lock (_db.Gate)
        {
            EnsureWritable();
            var cs = LoadChangeSetByIdRaw(null, changeSetId);
            if (cs is null)
            {
                return new UndoResult(false, 0, [], "未找到该变更记录");
            }

            if (cs.UndoneAt is not null)
            {
                return new UndoResult(false, 0, [], "该变更记录已经撤销过");
            }

            using var tx = _db.Connection.BeginTransaction();
            var processedEntityIds = new HashSet<string>(StringComparer.Ordinal);

            for (var i = cs.Entries.Count - 1; i >= 0; i--)
            {
                var entry = cs.Entries[i];
                if (filterEntityIds is not null && !filterEntityIds.Contains(entry.EntityId))
                {
                    continue;
                }

                var entryReverted = false;
                switch (entry.Kind)
                {
                    case ChangeEntryKind.TaskCreated:
                    {
                        var current = LoadTaskByIdRaw(tx, entry.EntityId);
                        if (current is null)
                        {
                            skipped.Add(new UndoSkippedItem(entry.EntityId, "该任务已被删除"));
                            continue;
                        }

                        if (!string.Equals(SerializeTask(current), entry.AfterJson, StringComparison.Ordinal))
                        {
                            skipped.Add(new UndoSkippedItem(entry.EntityId, $"「{current.Title}」之后又被修改，已保留"));
                            continue;
                        }

                        DeleteTaskRaw(tx, entry.EntityId);
                        reverted++;
                        tasksDirty = true;
                        entryReverted = true;
                        break;
                    }

                    case ChangeEntryKind.TaskUpdated:
                    {
                        var current = LoadTaskByIdRaw(tx, entry.EntityId);
                        if (current is null)
                        {
                            skipped.Add(new UndoSkippedItem(entry.EntityId, "该任务已被删除"));
                            continue;
                        }

                        if (!string.Equals(SerializeTask(current), entry.AfterJson, StringComparison.Ordinal))
                        {
                            skipped.Add(new UndoSkippedItem(entry.EntityId, $"「{current.Title}」之后又被修改，已保留"));
                            continue;
                        }

                        var before = DeserializeTask(entry.BeforeJson);
                        if (before is null)
                        {
                            skipped.Add(new UndoSkippedItem(entry.EntityId, "历史快照缺失"));
                            continue;
                        }

                        UpdateTaskRaw(tx, before);
                        reverted++;
                        tasksDirty = true;
                        entryReverted = true;
                        break;
                    }

                    case ChangeEntryKind.TaskDeleted:
                    {
                        var existing = LoadTaskByIdRaw(tx, entry.EntityId);
                        if (existing is not null)
                        {
                            skipped.Add(new UndoSkippedItem(entry.EntityId, $"「{existing.Title}」已存在，未覆盖"));
                            continue;
                        }

                        var before = DeserializeTask(entry.BeforeJson);
                        if (before is null)
                        {
                            skipped.Add(new UndoSkippedItem(entry.EntityId, "历史快照缺失"));
                            continue;
                        }

                        InsertTaskRaw(tx, before);
                        reverted++;
                        tasksDirty = true;
                        entryReverted = true;
                        break;
                    }

                    case ChangeEntryKind.ListCreated:
                    {
                        var current = LoadListByIdRaw(tx, entry.EntityId);
                        if (current is null)
                        {
                            skipped.Add(new UndoSkippedItem(entry.EntityId, "该清单已不存在"));
                            continue;
                        }

                        if (!string.Equals(SerializeList(current), entry.AfterJson, StringComparison.Ordinal))
                        {
                            skipped.Add(new UndoSkippedItem(entry.EntityId, $"清单「{current.Name}」之后又被修改，已保留"));
                            continue;
                        }

                        using (var del = _db.Connection.CreateCommand())
                        {
                            del.Transaction = tx;
                            del.CommandText = "DELETE FROM lists WHERE id = $id;";
                            del.Parameters.AddWithValue("$id", entry.EntityId);
                            del.ExecuteNonQuery();
                        }

                        reverted++;
                        listsDirty = true;
                        entryReverted = true;
                        break;
                    }

                    case ChangeEntryKind.ListRenamed:
                    {
                        var current = LoadListByIdRaw(tx, entry.EntityId);
                        var before = DeserializeList(entry.BeforeJson);
                        if (current is null || before is null)
                        {
                            skipped.Add(new UndoSkippedItem(entry.EntityId, "该清单已不存在"));
                            continue;
                        }

                        if (!string.Equals(SerializeList(current), entry.AfterJson, StringComparison.Ordinal))
                        {
                            skipped.Add(new UndoSkippedItem(entry.EntityId, $"清单「{current.Name}」之后又被修改，已保留"));
                            continue;
                        }

                        UpsertListRaw(tx, before);
                        reverted++;
                        listsDirty = true;
                        entryReverted = true;
                        break;
                    }

                    case ChangeEntryKind.ListDeleted:
                    {
                        var before = DeserializeList(entry.BeforeJson);
                        if (before is null)
                        {
                            skipped.Add(new UndoSkippedItem(entry.EntityId, "历史快照缺失"));
                            continue;
                        }

                        UpsertListRaw(tx, before);
                        reverted++;
                        listsDirty = true;
                        entryReverted = true;
                        break;
                    }
                }

                if (entryReverted)
                {
                    processedEntityIds.Add(entry.EntityId);
                }
            }

            if (filterEntityIds is null)
            {
                using var mark = _db.Connection.CreateCommand();
                mark.Transaction = tx;
                mark.CommandText = "UPDATE change_sets SET undone_at = $at WHERE id = $id;";
                mark.Parameters.AddWithValue("$at", DateTimeOffset.Now.ToString("O"));
                mark.Parameters.AddWithValue("$id", changeSetId);
                mark.ExecuteNonQuery();
            }
            else if (processedEntityIds.Count > 0)
            {
                foreach (var eid in processedEntityIds)
                {
                    using var delEntry = _db.Connection.CreateCommand();
                    delEntry.Transaction = tx;
                    delEntry.CommandText = "DELETE FROM change_entries WHERE change_set_id = $cs AND entity_id = $eid;";
                    delEntry.Parameters.AddWithValue("$cs", changeSetId);
                    delEntry.Parameters.AddWithValue("$eid", eid);
                    delEntry.ExecuteNonQuery();
                }

                // 若所有条目均已撤销，则自动将整个变更集标记为已撤销
                using var cntCmd = _db.Connection.CreateCommand();
                cntCmd.Transaction = tx;
                cntCmd.CommandText = "SELECT COUNT(*) FROM change_entries WHERE change_set_id = $cs;";
                cntCmd.Parameters.AddWithValue("$cs", changeSetId);
                var remaining = Convert.ToInt64(cntCmd.ExecuteScalar());
                if (remaining == 0)
                {
                    using var mark = _db.Connection.CreateCommand();
                    mark.Transaction = tx;
                    mark.CommandText = "UPDATE change_sets SET undone_at = $at WHERE id = $id;";
                    mark.Parameters.AddWithValue("$at", DateTimeOffset.Now.ToString("O"));
                    mark.Parameters.AddWithValue("$id", changeSetId);
                    mark.ExecuteNonQuery();
                }
            }

            tx.Commit();
        }

        if (listsDirty)
        {
            SyncCatalog();
            ListsChanged?.Invoke(this, EventArgs.Empty);
        }

        if (tasksDirty)
        {
            Raise(TaskChangeKind.Updated, string.Empty);
        }

        return new UndoResult(true, reverted, skipped);
    }

    public UndoResult? UndoLatest(IReadOnlyCollection<ChangeSource> sources, TimeSpan within)
    {
        var cutoff = DateTimeOffset.Now - within;
        string? targetId = null;

        lock (_db.Gate)
        {
            var recent = RecentRaw(50, null);
            foreach (var cs in recent)
            {
                if (cs.UndoneAt is null && cs.At >= cutoff && sources.Contains(cs.Source))
                {
                    targetId = cs.Id;
                    break;
                }
            }
        }

        return targetId is null ? null : Undo(targetId);
    }

    public IReadOnlyList<ChangeSet> Recent(int count, ChangeSource? source = null)
    {
        lock (_db.Gate)
        {
            return RecentRaw(Math.Max(1, count), source);
        }
    }

    private void ExecuteOpInScope(SqliteTransaction tx, ScheduleOp op, ActiveScope scope)
    {
        switch (op)
        {
            case CreateTaskOp create:
                if (string.IsNullOrWhiteSpace(create.Draft.Title))
                {
                    throw new InvalidOperationException("任务标题不能为空。");
                }

                CreateTaskCore(tx, create.Draft, scope.Entries);
                scope.TasksDirty = true;
                break;

            case UpdateTaskOp update:
            {
                var existing = LoadTaskByIdRaw(tx, update.TaskId)
                    ?? throw new InvalidOperationException($"任务不存在：{update.TaskId}");
                var beforeJson = SerializeTask(existing);
                var next = ApplyPatch(existing, update.Patch);
                if (next.Date > existing.Date)
                {
                    next.RescheduleCount = existing.RescheduleCount + 1;
                }

                UpdateTaskRaw(tx, next);
                scope.Entries.Add(new ChangeEntry(ChangeEntryKind.TaskUpdated, next.Id, beforeJson, SerializeTask(next)));
                scope.TasksDirty = true;
                break;
            }

            case SetCompletedOp setCompleted:
            {
                var existing = LoadTaskByIdRaw(tx, setCompleted.TaskId)
                    ?? throw new InvalidOperationException($"任务不存在：{setCompleted.TaskId}");
                if (existing.IsCompleted == setCompleted.Completed)
                {
                    break;
                }

                var beforeJson = SerializeTask(existing);
                existing.IsCompleted = setCompleted.Completed;
                existing.CompletedAt = setCompleted.Completed ? DateTimeOffset.Now : null;
                UpdateTaskRaw(tx, existing);
                scope.Entries.Add(new ChangeEntry(ChangeEntryKind.TaskUpdated, existing.Id, beforeJson, SerializeTask(existing)));
                if (setCompleted.Completed && existing.Recurrence != RecurrenceKind.None)
                {
                    CreateNextRecurrenceCore(tx, existing, scope.Entries);
                }

                scope.TasksDirty = true;
                break;
            }

            case DeleteTaskOp delete:
            {
                var existing = LoadTaskByIdRaw(tx, delete.TaskId)
                    ?? throw new InvalidOperationException($"任务不存在：{delete.TaskId}");
                if (existing.IsDeleted)
                {
                    break;
                }

                var beforeJson = SerializeTask(existing);
                existing.DeletedAt = DateTimeOffset.Now;
                UpdateTaskRaw(tx, existing);
                scope.Entries.Add(new ChangeEntry(ChangeEntryKind.TaskUpdated, delete.TaskId, beforeJson, SerializeTask(existing)));
                scope.TasksDirty = true;
                break;
            }

            case CreateListOp createList:
                CreateListCore(tx, createList.Name, scope.Entries);
                scope.ListsDirty = true;
                break;

            default:
                throw new NotSupportedException($"不支持的日程操作：{op.GetType().Name}");
        }
    }

    private static TaskItem ApplyPatch(TaskItem existing, TaskPatch patch)
    {
        var next = existing.Clone();

        if (patch.Title is not null)
        {
            var t = patch.Title.Trim();
            if (string.IsNullOrEmpty(t))
            {
                throw new InvalidOperationException("任务标题不能为空。");
            }

            next.Title = t;
        }

        if (patch.Content is not null)
        {
            next.Content = patch.Content;
        }

        if (patch.AppendContentSection is { } section)
        {
            next.Content = TaskPatch.ApplyContentSection(next.Content, section.Heading, section.Markdown);
        }

        if (patch.Date is { } date)
        {
            next.Date = date;
        }

        if (patch.ClearDueTime)
        {
            next.DueTime = null;
        }
        else if (patch.DueTime is { } dueTime)
        {
            next.DueTime = dueTime;
        }

        if (patch.ClearReminder)
        {
            next.Reminder = null;
        }
        else if (patch.Reminder is { } reminder)
        {
            next.Reminder = reminder.Clone();
        }

        if (patch.Priority is { } prio)
        {
            next.Priority = prio;
        }

        if (patch.ListId is not null)
        {
            next.ListId = patch.ListId;
        }

        if (patch.Tags is not null)
        {
            next.Tags = TaskTagParser.Normalize(patch.Tags);
        }

        if (patch.Recurrence is { } recurrence)
        {
            next.Recurrence = recurrence;
        }

        if (patch.Subtasks is not null)
        {
            next.Subtasks = patch.Subtasks
                .Where(s => !string.IsNullOrWhiteSpace(s.Title))
                .Select(s => new TaskSubtask
                {
                    Id = string.IsNullOrEmpty(s.Id) ? Guid.NewGuid().ToString("N") : s.Id,
                    Title = s.Title.Trim(),
                    IsCompleted = s.IsCompleted,
                })
                .ToList();
        }

        if (patch.AppendSubtasks is not null)
        {
            foreach (var s in patch.AppendSubtasks.Where(x => !string.IsNullOrWhiteSpace(x.Title)))
            {
                next.Subtasks.Add(new TaskSubtask
                {
                    Id = string.IsNullOrEmpty(s.Id) ? Guid.NewGuid().ToString("N") : s.Id,
                    Title = s.Title.Trim(),
                    IsCompleted = s.IsCompleted,
                });
            }
        }

        var scheduleChanged = next.Date != existing.Date ||
                              next.DueTime != existing.DueTime ||
                              !(next.Reminder?.ScheduleEquals(existing.Reminder) ?? existing.Reminder is null);
        if (scheduleChanged)
        {
            next.Reminder?.ResetTriggerState();
        }

        return next;
    }

    private TaskList CreateListCore(SqliteTransaction tx, string name, List<ChangeEntry> entries)
    {
        var count = LoadListsRaw(tx).Count;
        var list = new TaskList
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = NormalizeName(name),
            Color = Palette[count % Palette.Length],
        };

        UpsertListRaw(tx, list, count);
        entries.Add(new ChangeEntry(ChangeEntryKind.ListCreated, list.Id, null, SerializeList(list)));
        return list;
    }

    private TaskItem CreateTaskCore(SqliteTransaction tx, TaskDraft draft, List<ChangeEntry> entries)
    {
        var task = new TaskItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = draft.Title.Trim(),
            Content = draft.Content ?? string.Empty,
            Subtasks = draft.Subtasks?
                .Where(s => !string.IsNullOrWhiteSpace(s.Title))
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
            ListId = string.IsNullOrEmpty(draft.ListId) ? TaskList.InboxId : draft.ListId,
            IsCompleted = draft.IsCompleted,
            CompletedAt = draft.IsCompleted ? DateTimeOffset.Now : null,
            CreatedAt = DateTimeOffset.Now,
        };

        InsertTaskRaw(tx, task);
        entries.Add(new ChangeEntry(ChangeEntryKind.TaskCreated, task.Id, null, SerializeTask(task)));
        return task;
    }

    private TaskItem CreateNextRecurrenceCore(SqliteTransaction tx, TaskItem completed, List<ChangeEntry> entries)
    {
        var nextDate = RecurrenceCalculator.NextDate(completed.Date, completed.Recurrence);
        var nextDraft = new TaskDraft
        {
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
        };

        return CreateTaskCore(tx, nextDraft, entries);
    }

    private ChangeSet PersistChangeSet(SqliteTransaction tx, ChangeSource source, string label, List<ChangeEntry> entries)
    {
        var cs = new ChangeSet(
            Guid.NewGuid().ToString("N"),
            source,
            label,
            DateTimeOffset.Now,
            entries.ToList());

        using (var cmd = _db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO change_sets(id, source, label, at, undone_at) VALUES($id, $source, $label, $at, NULL);";
            cmd.Parameters.AddWithValue("$id", cs.Id);
            cmd.Parameters.AddWithValue("$source", (int)cs.Source);
            cmd.Parameters.AddWithValue("$label", cs.Label);
            cmd.Parameters.AddWithValue("$at", cs.At.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        for (var i = 0; i < cs.Entries.Count; i++)
        {
            var e = cs.Entries[i];
            using var entryCmd = _db.Connection.CreateCommand();
            entryCmd.Transaction = tx;
            entryCmd.CommandText = """
                INSERT INTO change_entries(change_set_id, seq, kind, entity_id, before_json, after_json)
                VALUES($csId, $seq, $kind, $entityId, $before, $after);
                """;
            entryCmd.Parameters.AddWithValue("$csId", cs.Id);
            entryCmd.Parameters.AddWithValue("$seq", i);
            entryCmd.Parameters.AddWithValue("$kind", (int)e.Kind);
            entryCmd.Parameters.AddWithValue("$entityId", e.EntityId);
            entryCmd.Parameters.AddWithValue("$before", (object?)e.BeforeJson ?? DBNull.Value);
            entryCmd.Parameters.AddWithValue("$after", (object?)e.AfterJson ?? DBNull.Value);
            entryCmd.ExecuteNonQuery();
        }

        PruneOldChangeSetsRaw(tx);
        return cs;
    }

    private void PruneOldChangeSetsRaw(SqliteTransaction tx)
    {
        var cutoff = (DateTimeOffset.Now - ChangeSetRetention).ToString("O");
        using (var cmd = _db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM change_sets WHERE at < $cutoff;";
            cmd.Parameters.AddWithValue("$cutoff", cutoff);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = _db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                DELETE FROM change_sets
                WHERE id NOT IN (
                    SELECT id FROM change_sets ORDER BY at DESC LIMIT $max
                );
                """;
            cmd.Parameters.AddWithValue("$max", MaxChangeSets);
            cmd.ExecuteNonQuery();
        }
    }

    private void EnsureInboxExists()
    {
        lock (_db.Gate)
        {
            if (_db.IsReadOnlyFutureVersion)
            {
                return;
            }

            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO lists(id, name, color, sort_order)
                VALUES($id, '收集箱', '#8F7BE5', 0);
                """;
            cmd.Parameters.AddWithValue("$id", TaskList.InboxId);
            cmd.ExecuteNonQuery();
        }
    }

    private void EnsureWritable()
    {
        if (_db.IsReadOnlyFutureVersion)
        {
            throw new InvalidOperationException("本地数据库来自更高版本的 Plana，当前以只读模式打开。请升级 Plana 后再编辑。");
        }
    }

    private List<TaskList> LoadListsRaw(SqliteTransaction? tx)
    {
        var result = new List<TaskList>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, name, color FROM lists ORDER BY CASE WHEN id = $inbox THEN 0 ELSE 1 END, sort_order, rowid;";
        cmd.Parameters.AddWithValue("$inbox", TaskList.InboxId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new TaskList
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Color = reader.GetString(2),
            });
        }

        return result;
    }

    private TaskList? LoadListByIdRaw(SqliteTransaction? tx, string id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, name, color FROM lists WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new TaskList
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Color = reader.GetString(2),
        };
    }

    private void UpsertListRaw(SqliteTransaction tx, TaskList list, int? sortOrder = null)
    {
        var order = sortOrder ?? (list.Id == TaskList.InboxId ? 0 : LoadListsRaw(tx).Count);
        using var cmd = _db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO lists(id, name, color, sort_order)
            VALUES($id, $name, $color, $order)
            ON CONFLICT(id) DO UPDATE SET name = excluded.name, color = excluded.color;
            """;
        cmd.Parameters.AddWithValue("$id", list.Id);
        cmd.Parameters.AddWithValue("$name", list.Name);
        cmd.Parameters.AddWithValue("$color", list.Color);
        cmd.Parameters.AddWithValue("$order", order);
        cmd.ExecuteNonQuery();
    }

    private List<TaskItem> QueryRaw(SqliteTransaction? tx, TaskQuery query)
    {
        var where = new List<string>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.Transaction = tx;

        if (query.Date is { } date)
        {
            where.Add("date = $date");
            cmd.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (query.From is { } from)
        {
            where.Add("date >= $from");
            cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (query.To is { } to)
        {
            where.Add("date <= $to");
            cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (query.ListId is { } listId)
        {
            where.Add("list_id = $listId");
            cmd.Parameters.AddWithValue("$listId", listId);
        }

        if (!query.IncludeCompleted)
        {
            where.Add("is_completed = 0");
        }

        if (query.DeletedOnly)
        {
            where.Add("deleted_at IS NOT NULL");
        }
        else if (!query.IncludeDeleted)
        {
            where.Add("deleted_at IS NULL");
        }

        var orderBy = query.DeletedOnly
            ? " ORDER BY deleted_at DESC;"
            : " ORDER BY is_completed ASC, created_at DESC;";

        var sql = """
            SELECT id, title, content, list_id, date, priority, is_completed, created_at, completed_at, reschedule_count, tags_json, recurrence, deleted_at, due_time, reminder_json
            FROM tasks
            """ + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : string.Empty)
            + orderBy;

        cmd.CommandText = sql;
        var tasks = new List<TaskItem>();
        var byId = new Dictionary<string, TaskItem>(StringComparer.Ordinal);
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var item = ReadTaskRow(reader);
                if (!string.IsNullOrWhiteSpace(query.Tag) &&
                    !item.Tags.Any(t => string.Equals(t, query.Tag.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                tasks.Add(item);
                byId[item.Id] = item;
            }
        }

        if (tasks.Count > 0)
        {
            LoadSubtasksForTasksRaw(tx, byId);
        }

        return tasks;
    }

    private TaskItem? LoadTaskByIdRaw(SqliteTransaction? tx, string id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, title, content, list_id, date, priority, is_completed, created_at, completed_at, reschedule_count, tags_json, recurrence, deleted_at, due_time, reminder_json
            FROM tasks WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        TaskItem? task = null;
        using (var reader = cmd.ExecuteReader())
        {
            if (reader.Read())
            {
                task = ReadTaskRow(reader);
            }
        }

        if (task is null)
        {
            return null;
        }

        LoadSubtasksForTasksRaw(tx, new Dictionary<string, TaskItem>(StringComparer.Ordinal) { [task.Id] = task });
        return task;
    }

    private void LoadSubtasksForTasksRaw(SqliteTransaction? tx, Dictionary<string, TaskItem> byId)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.Transaction = tx;
        if (byId.Count == 1)
        {
            var onlyId = byId.Keys.First();
            cmd.CommandText = "SELECT id, task_id, title, is_completed FROM subtasks WHERE task_id = $tid ORDER BY sort_order ASC, rowid ASC;";
            cmd.Parameters.AddWithValue("$tid", onlyId);
        }
        else
        {
            cmd.CommandText = "SELECT id, task_id, title, is_completed FROM subtasks ORDER BY sort_order ASC, rowid ASC;";
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var taskId = reader.GetString(1);
            if (!byId.TryGetValue(taskId, out var owner))
            {
                continue;
            }

            owner.Subtasks.Add(new TaskSubtask
            {
                Id = reader.GetString(0),
                Title = reader.GetString(2),
                IsCompleted = reader.GetInt32(3) != 0,
            });
        }
    }

    private static TaskItem ReadTaskRow(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Title = reader.GetString(1),
        Content = reader.GetString(2),
        ListId = reader.GetString(3),
        Date = DateOnly.ParseExact(reader.GetString(4), "yyyy-MM-dd", CultureInfo.InvariantCulture),
        Priority = (TaskPriority)reader.GetInt32(5),
        IsCompleted = reader.GetInt32(6) != 0,
        CreatedAt = DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        CompletedAt = reader.IsDBNull(8)
            ? null
            : DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        RescheduleCount = reader.GetInt32(9),
        Tags = DeserializeTags(reader.IsDBNull(10) ? null : reader.GetString(10)),
        Recurrence = (RecurrenceKind)reader.GetInt32(11),
        DeletedAt = reader.IsDBNull(12)
            ? null
            : DateTimeOffset.Parse(reader.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        DueTime = reader.IsDBNull(13)
            ? null
            : (TaskReminderCalculator.TryParseTimeOnly(reader.GetString(13), out var dueTime) ? dueTime : null),
        Reminder = DeserializeReminder(reader.IsDBNull(14) ? null : reader.GetString(14)),
    };

    private static TaskReminder? DeserializeReminder(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TaskReminder>(json, SnapshotJson);
        }
        catch
        {
            return null;
        }
    }

    private static string? SerializeReminder(TaskReminder? reminder) =>
        reminder is null ? null : JsonSerializer.Serialize(reminder, SnapshotJson);

    private static List<string> DeserializeTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return [];
        }

        try
        {
            return TaskTagParser.Normalize(JsonSerializer.Deserialize<List<string>>(json, SnapshotJson));
        }
        catch
        {
            return [];
        }
    }

    private void InsertTaskRaw(SqliteTransaction tx, TaskItem task)
    {
        using (var cmd = _db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO tasks(id, title, content, list_id, date, priority, is_completed, created_at, completed_at, reschedule_count, sort_order, tags_json, recurrence, deleted_at, due_time, reminder_json)
                VALUES($id, $title, $content, $listId, $date, $priority, $completed, $createdAt, $completedAt, $reschedule, 0, $tagsJson, $recurrence, $deletedAt, $dueTime, $reminderJson);
                """;
            BindTaskParams(cmd, task);
            cmd.ExecuteNonQuery();
        }

        ReplaceSubtasksRaw(tx, task.Id, task.Subtasks);
    }

    private void UpdateTaskRaw(SqliteTransaction tx, TaskItem task)
    {
        using (var cmd = _db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE tasks
                SET title = $title,
                    content = $content,
                    list_id = $listId,
                    date = $date,
                    priority = $priority,
                    is_completed = $completed,
                    completed_at = $completedAt,
                    reschedule_count = $reschedule,
                    tags_json = $tagsJson,
                    recurrence = $recurrence,
                    deleted_at = $deletedAt,
                    due_time = $dueTime,
                    reminder_json = $reminderJson
                WHERE id = $id;
                """;
            BindTaskParams(cmd, task);
            cmd.ExecuteNonQuery();
        }

        ReplaceSubtasksRaw(tx, task.Id, task.Subtasks);
    }

    private void DeleteTaskRaw(SqliteTransaction tx, string id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM tasks WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private void ReplaceSubtasksRaw(SqliteTransaction tx, string taskId, IReadOnlyList<TaskSubtask> subtasks)
    {
        using (var del = _db.Connection.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM subtasks WHERE task_id = $tid;";
            del.Parameters.AddWithValue("$tid", taskId);
            del.ExecuteNonQuery();
        }

        for (var i = 0; i < subtasks.Count; i++)
        {
            var s = subtasks[i];
            using var ins = _db.Connection.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO subtasks(id, task_id, title, is_completed, sort_order)
                VALUES($id, $tid, $title, $done, $order);
                """;
            ins.Parameters.AddWithValue("$id", string.IsNullOrEmpty(s.Id) ? Guid.NewGuid().ToString("N") : s.Id);
            ins.Parameters.AddWithValue("$tid", taskId);
            ins.Parameters.AddWithValue("$title", s.Title);
            ins.Parameters.AddWithValue("$done", s.IsCompleted ? 1 : 0);
            ins.Parameters.AddWithValue("$order", i);
            ins.ExecuteNonQuery();
        }
    }

    private static void BindTaskParams(SqliteCommand cmd, TaskItem task)
    {
        cmd.Parameters.AddWithValue("$id", task.Id);
        cmd.Parameters.AddWithValue("$title", task.Title);
        cmd.Parameters.AddWithValue("$content", task.Content ?? string.Empty);
        cmd.Parameters.AddWithValue("$listId", string.IsNullOrEmpty(task.ListId) ? TaskList.InboxId : task.ListId);
        cmd.Parameters.AddWithValue("$date", task.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$priority", (int)task.Priority);
        cmd.Parameters.AddWithValue("$completed", task.IsCompleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$createdAt", task.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$completedAt", task.CompletedAt is { } c ? c.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("$reschedule", task.RescheduleCount);
        cmd.Parameters.AddWithValue("$tagsJson", JsonSerializer.Serialize(TaskTagParser.Normalize(task.Tags), SnapshotJson));
        cmd.Parameters.AddWithValue("$recurrence", (int)task.Recurrence);
        cmd.Parameters.AddWithValue("$deletedAt", task.DeletedAt is { } d ? d.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("$dueTime", task.DueTime is { } dt ? dt.ToString("HH:mm", CultureInfo.InvariantCulture) : DBNull.Value);
        cmd.Parameters.AddWithValue("$reminderJson", (object?)SerializeReminder(task.Reminder) ?? DBNull.Value);
    }

    private ChangeSet? LoadChangeSetByIdRaw(SqliteTransaction? tx, string id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, source, label, at, undone_at FROM change_sets WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var source = (ChangeSource)reader.GetInt32(1);
        var label = reader.GetString(2);
        var at = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        DateTimeOffset? undone = reader.IsDBNull(4)
            ? null
            : DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        reader.Close();

        var entries = LoadChangeEntriesRaw(tx, id);
        return new ChangeSet(id, source, label, at, entries, undone);
    }

    private List<ChangeSet> RecentRaw(int count, ChangeSource? source)
    {
        var headers = new List<(string Id, ChangeSource Source, string Label, DateTimeOffset At, DateTimeOffset? Undone)>();
        using (var cmd = _db.Connection.CreateCommand())
        {
            if (source is { } s)
            {
                cmd.CommandText = "SELECT id, source, label, at, undone_at FROM change_sets WHERE source = $s ORDER BY at DESC LIMIT $n;";
                cmd.Parameters.AddWithValue("$s", (int)s);
            }
            else
            {
                cmd.CommandText = "SELECT id, source, label, at, undone_at FROM change_sets ORDER BY at DESC LIMIT $n;";
            }

            cmd.Parameters.AddWithValue("$n", count);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                headers.Add((
                    reader.GetString(0),
                    (ChangeSource)reader.GetInt32(1),
                    reader.GetString(2),
                    DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    reader.IsDBNull(4)
                        ? null
                        : DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
            }
        }

        return headers
            .Select(h => new ChangeSet(h.Id, h.Source, h.Label, h.At, LoadChangeEntriesRaw(null, h.Id), h.Undone))
            .ToList();
    }

    private List<ChangeEntry> LoadChangeEntriesRaw(SqliteTransaction? tx, string changeSetId)
    {
        var entries = new List<ChangeEntry>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT kind, entity_id, before_json, after_json FROM change_entries WHERE change_set_id = $id ORDER BY seq ASC;";
        cmd.Parameters.AddWithValue("$id", changeSetId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new ChangeEntry(
                (ChangeEntryKind)reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return entries;
    }

    private void SyncCatalog() => TaskListCatalog.Replace(Lists.Select(CloneList));

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

    private static string SerializeTask(TaskItem task)
    {
        if (task.Reminder is { FiredCount: > 0 } or { LastFiredAt: not null })
        {
            var normalized = task.Clone();
            normalized.Reminder?.ResetTriggerState();
            return JsonSerializer.Serialize(normalized, SnapshotJson);
        }

        return JsonSerializer.Serialize(task, SnapshotJson);
    }

    private static TaskItem? DeserializeTask(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<TaskItem>(json, SnapshotJson);

    private static string SerializeList(TaskList list) => JsonSerializer.Serialize(list, SnapshotJson);

    private static TaskList? DeserializeList(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<TaskList>(json, SnapshotJson);

    private void Raise(TaskChangeKind kind, string taskId) =>
        Changed?.Invoke(this, new TaskChangedEventArgs(kind, taskId));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _db.Dispose();
    }

    private sealed class ActiveScope(SqliteTransaction transaction)
    {
        public SqliteTransaction Transaction { get; } = transaction;
        public List<ChangeEntry> Entries { get; } = [];
        public bool TasksDirty { get; set; }
        public bool ListsDirty { get; set; }
    }
}

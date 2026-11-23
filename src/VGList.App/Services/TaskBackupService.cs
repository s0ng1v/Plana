using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using VGList.Core.Abstractions;
using VGList.Core.Dates;
using VGList.Core.Models;

namespace VGList.App.Services;

/// <summary>导入备份的结果统计与可撤销变更集 ID。</summary>
public sealed record BackupImportResult(
    int ImportedCount,
    int SkippedCount,
    int CreatedListsCount,
    string? ChangeSetId,
    string FormatLabel);

/// <summary>
/// 本地数据导出 / 导入备份服务（P1，ADR-0053）：
/// 支持 JSON（完整结构）、Markdown（可读任务清单）、RFC 5545 ICS（通用日历事件）。
/// 所有导入操作均通过 IScheduleService 原子写入并支持一键撤销。
/// </summary>
public static partial class TaskBackupService
{
    private const int BackupSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [GeneratedRegex(@"^(\s*)[-*+]\s+\[([ xX])\]\s+(.+)$")]
    private static partial Regex MarkdownTaskLineRegex();

    [GeneratedRegex(@"`(\d{4}-\d{2}-\d{2})`")]
    private static partial Regex MarkdownDateTokenRegex();

    [GeneratedRegex(@"`\[(高|中|低)\]`")]
    private static partial Regex MarkdownPriorityTokenRegex();

    [GeneratedRegex(@"`\[重复:\s*([^\]]+)\]`")]
    private static partial Regex MarkdownRecurrenceTokenRegex();

    // ==================== 导出 ====================

    public static string ExportJson(ITaskRepository repository)
    {
        var lists = repository.Lists;
        var listNameById = lists.ToDictionary(l => l.Id, l => l.Name, StringComparer.Ordinal);
        var tasks = repository.Query(new TaskQuery { IncludeCompleted = true });

        var payload = new BackupDocumentDto
        {
            Version = BackupSchemaVersion,
            App = "Plana",
            ExportedAt = DateTimeOffset.Now.ToString("O"),
            Lists = lists.Select(l => new BackupListDto
            {
                Id = l.Id,
                Name = l.Name,
            }).ToList(),
            Tasks = tasks.Select(t => new BackupTaskDto
            {
                Title = t.Title,
                Content = t.Content,
                Date = t.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DueTime = t.DueTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
                Reminder = t.Reminder is { } rem ? CloneReminderWithoutRuntime(rem) : null,
                Priority = t.Priority.ToString(),
                ListId = t.ListId,
                ListName = listNameById.TryGetValue(t.ListId, out var name) ? name : "收集箱",
                IsCompleted = t.IsCompleted,
                Recurrence = t.Recurrence.ToString(),
                Tags = [.. t.Tags],
                Subtasks = t.Subtasks.Select(s => new BackupSubtaskDto
                {
                    Title = s.Title,
                    IsCompleted = s.IsCompleted,
                }).ToList(),
            }).ToList(),
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string ExportMarkdown(ITaskRepository repository)
    {
        var lists = repository.Lists;
        var tasks = repository.Query(new TaskQuery { IncludeCompleted = true });
        var grouped = tasks
            .GroupBy(t => t.ListId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.IsCompleted).ThenBy(t => t.Date).ToList(), StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.AppendLine("# Plana 任务备份");
        sb.AppendLine();
        sb.AppendLine($"> 导出时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm} · 共 {tasks.Count} 项任务");
        sb.AppendLine();

        foreach (var list in lists)
        {
            if (!grouped.TryGetValue(list.Id, out var listTasks) || listTasks.Count == 0)
            {
                continue;
            }

            sb.Append("## ").AppendLine(list.Name);
            sb.AppendLine();

            foreach (var task in listTasks)
            {
                var check = task.IsCompleted ? "x" : " ";
                sb.Append("- [").Append(check).Append("] ").Append(task.Title.Trim());
                sb.Append(" `").Append(task.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('`');

                var priorityLabel = FormatPriorityToken(task.Priority);
                if (!string.IsNullOrEmpty(priorityLabel))
                {
                    sb.Append(" `[").Append(priorityLabel).Append("]`");
                }

                if (task.Recurrence != RecurrenceKind.None)
                {
                    sb.Append(" `[重复: ").Append(RecurrenceCalculator.FormatLabel(task.Recurrence)).Append("]`");
                }

                foreach (var tag in task.Tags)
                {
                    sb.Append(" #").Append(tag);
                }

                sb.AppendLine();

                if (!string.IsNullOrWhiteSpace(task.Content))
                {
                    var contentLines = task.Content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
                    foreach (var line in contentLines)
                    {
                        sb.Append("  > ").AppendLine(line);
                    }
                }

                foreach (var sub in task.Subtasks)
                {
                    var subCheck = sub.IsCompleted ? "x" : " ";
                    sb.Append("  - [").Append(subCheck).Append("] ").AppendLine(sub.Title.Trim());
                }
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string ExportIcs(ITaskRepository repository)
    {
        var listNameById = repository.Lists.ToDictionary(l => l.Id, l => l.Name, StringComparer.Ordinal);
        var tasks = repository.Query(new TaskQuery { IncludeCompleted = true });
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        AppendIcsLine(sb, "BEGIN:VCALENDAR");
        AppendIcsLine(sb, "VERSION:2.0");
        AppendIcsLine(sb, "PRODID:-//Plana//VGList//ZH");
        AppendIcsLine(sb, "CALSCALE:GREGORIAN");
        AppendIcsLine(sb, "X-WR-CALNAME:Plana 日程");

        foreach (var task in tasks)
        {
            AppendIcsLine(sb, "BEGIN:VEVENT");
            AppendIcsLine(sb, $"UID:plana-{task.Id}@plana.local");
            AppendIcsLine(sb, $"DTSTAMP:{stamp}");
            if (task.DueTime is { } dt)
            {
                var startLocal = task.Date.ToDateTime(dt);
                var endLocal = startLocal.AddMinutes(30);
                AppendIcsLine(sb, $"DTSTART:{startLocal:yyyyMMdd'T'HHmmss}");
                AppendIcsLine(sb, $"DTEND:{endLocal:yyyyMMdd'T'HHmmss}");
            }
            else
            {
                AppendIcsLine(sb, $"DTSTART;VALUE=DATE:{task.Date:yyyyMMdd}");
                AppendIcsLine(sb, $"DTEND;VALUE=DATE:{task.Date.AddDays(1):yyyyMMdd}");
            }

            AppendIcsLine(sb, $"SUMMARY:{EscapeIcsText(task.Title)}");

            var description = BuildIcsDescription(task);
            if (!string.IsNullOrEmpty(description))
            {
                AppendIcsLine(sb, $"DESCRIPTION:{EscapeIcsText(description)}");
            }

            if (task.Tags.Count > 0)
            {
                var escapedTags = string.Join(",", task.Tags.Select(EscapeIcsText));
                AppendIcsLine(sb, $"CATEGORIES:{escapedTags}");
            }

            if (listNameById.TryGetValue(task.ListId, out var listName) &&
                !string.Equals(task.ListId, TaskList.InboxId, StringComparison.Ordinal))
            {
                AppendIcsLine(sb, $"X-PLANA-LIST:{EscapeIcsText(listName)}");
            }

            var icsPriority = task.Priority switch
            {
                TaskPriority.High => 1,
                TaskPriority.Medium => 5,
                TaskPriority.Low => 9,
                _ => 0,
            };
            if (icsPriority > 0)
            {
                AppendIcsLine(sb, $"PRIORITY:{icsPriority}");
            }

            var rrule = RecurrenceCalculator.ToIcsRRule(task.Recurrence);
            if (!string.IsNullOrEmpty(rrule))
            {
                AppendIcsLine(sb, $"RRULE:{rrule}");
            }

            AppendIcsLine(sb, task.IsCompleted ? "STATUS:COMPLETED" : "STATUS:CONFIRMED");
            if (task.IsCompleted)
            {
                AppendIcsLine(sb, "X-PLANA-COMPLETED:1");
            }

            if (task.Reminder is { Enabled: true } rem)
            {
                AppendIcsLine(sb, "BEGIN:VALARM");
                AppendIcsLine(sb, "ACTION:DISPLAY");
                AppendIcsLine(sb, $"DESCRIPTION:{EscapeIcsText(task.Title)}");
                AppendIcsLine(sb, $"TRIGGER:-PT{Math.Max(0, rem.OffsetMinutes)}M");
                AppendIcsLine(sb, "END:VALARM");
            }

            AppendIcsLine(sb, "END:VEVENT");
        }

        AppendIcsLine(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    // ==================== 导入 ====================

    public static BackupImportResult ImportAuto(
        string filePath,
        string content,
        DateOnly today,
        ITaskRepository repository)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".json" || content.TrimStart().StartsWith('{'))
        {
            return ImportJson(content, today, repository);
        }

        if (ext == ".ics" || content.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
        {
            return ImportIcs(content, today, repository);
        }

        return ImportMarkdown(content, today, repository);
    }

    public static BackupImportResult ImportJson(string json, DateOnly fallbackDate, ITaskRepository repository)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new BackupImportResult(0, 0, 0, null, "JSON");
        }

        BackupDocumentDto? doc;
        try
        {
            doc = JsonSerializer.Deserialize<BackupDocumentDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"JSON 备份文件格式无法解析：{ex.Message}", ex);
        }

        if (doc is null || doc.Tasks is null)
        {
            return new BackupImportResult(0, 0, 0, null, "JSON");
        }

        var listIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var createdLists = 0;

        if (doc.Lists is not null)
        {
            foreach (var listDto in doc.Lists)
            {
                var resolved = ResolveOrCreateList(repository, listDto.Id, listDto.Name, ref createdLists);
                if (!string.IsNullOrWhiteSpace(listDto.Id))
                {
                    listIdMap[listDto.Id] = resolved;
                }
            }
        }

        var items = new List<ParsedImportTask>();
        foreach (var dto in doc.Tasks)
        {
            var title = dto.Title?.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var date = DateOnly.TryParseExact(dto.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : fallbackDate;

            var priority = Enum.TryParse<TaskPriority>(dto.Priority, ignoreCase: true, out var p)
                ? p
                : TaskPriority.None;

            var recurrence = Enum.TryParse<RecurrenceKind>(dto.Recurrence, ignoreCase: true, out var r)
                ? r
                : RecurrenceKind.None;

            string listId;
            if (!string.IsNullOrWhiteSpace(dto.ListId) && listIdMap.TryGetValue(dto.ListId, out var mapped))
            {
                listId = mapped;
            }
            else
            {
                listId = ResolveOrCreateList(repository, dto.ListId, dto.ListName, ref createdLists);
            }

            var subtasks = dto.Subtasks?
                .Where(s => !string.IsNullOrWhiteSpace(s.Title))
                .Select(s => new TaskSubtask
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = s.Title!.Trim(),
                    IsCompleted = s.IsCompleted,
                })
                .ToList() ?? [];

            TimeOnly? dueTime = TaskReminderCalculator.TryParseTimeOnly(dto.DueTime, out var parsedTime)
                ? parsedTime
                : null;
            TaskReminder? reminder = dto.Reminder is { } rawRem ? CloneReminderWithoutRuntime(rawRem) : null;

            items.Add(new ParsedImportTask(
                Title: title,
                Content: dto.Content ?? string.Empty,
                Date: date,
                Priority: priority,
                ListId: listId,
                IsCompleted: dto.IsCompleted,
                Recurrence: recurrence,
                Tags: TaskTagParser.Normalize(dto.Tags),
                Subtasks: subtasks,
                DueTime: dueTime,
                Reminder: reminder));
        }

        return CommitImportedTasks(items, createdLists, "JSON", repository);
    }

    public static BackupImportResult ImportMarkdown(string markdown, DateOnly defaultDate, ITaskRepository repository)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new BackupImportResult(0, 0, 0, null, "Markdown");
        }

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var createdLists = 0;
        var currentListId = TaskList.InboxId;
        var parsedTasks = new List<MutableMarkdownTask>();
        MutableMarkdownTask? currentTask = null;

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = trimmed[3..].Trim();
                if (!string.IsNullOrEmpty(heading))
                {
                    currentListId = ResolveOrCreateList(repository, null, heading, ref createdLists);
                }

                currentTask = null;
                continue;
            }

            var match = MarkdownTaskLineRegex().Match(rawLine);
            if (match.Success)
            {
                var indent = match.Groups[1].Value.Length;
                var isDone = !string.Equals(match.Groups[2].Value, " ", StringComparison.Ordinal);
                var body = match.Groups[3].Value.Trim();

                if (indent >= 2 && currentTask is not null)
                {
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        currentTask.Subtasks.Add(new TaskSubtask
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Title = body,
                            IsCompleted = isDone,
                        });
                    }

                    continue;
                }

                var date = defaultDate;
                var dateMatch = MarkdownDateTokenRegex().Match(body);
                if (dateMatch.Success &&
                    DateOnly.TryParseExact(dateMatch.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var explicitDate))
                {
                    date = explicitDate;
                    body = MarkdownDateTokenRegex().Replace(body, " ", 1);
                }

                var priority = TaskPriority.None;
                var priorityMatch = MarkdownPriorityTokenRegex().Match(body);
                if (priorityMatch.Success)
                {
                    priority = priorityMatch.Groups[1].Value switch
                    {
                        "高" => TaskPriority.High,
                        "中" => TaskPriority.Medium,
                        "低" => TaskPriority.Low,
                        _ => TaskPriority.None,
                    };
                    body = MarkdownPriorityTokenRegex().Replace(body, " ", 1);
                }

                var recurrence = RecurrenceKind.None;
                var recMatch = MarkdownRecurrenceTokenRegex().Match(body);
                if (recMatch.Success)
                {
                    recurrence = ParseChineseRecurrenceLabel(recMatch.Groups[1].Value);
                    body = MarkdownRecurrenceTokenRegex().Replace(body, " ", 1);
                }

                var (cleanTitle, tags) = TaskTagParser.ExtractInlineTags(body);

                // 若未带显式 yyyy-MM-dd 日期，再尝试离线中文自然语言解析
                if (!dateMatch.Success)
                {
                    var nl = ChineseDateParser.Shared.Parse(cleanTitle, defaultDate);
                    if (nl is not null && !string.IsNullOrWhiteSpace(nl.CleanTitle))
                    {
                        date = nl.Date;
                        cleanTitle = nl.CleanTitle;
                    }
                }

                cleanTitle = Regex.Replace(cleanTitle, @"\s{2,}", " ").Trim();
                if (string.IsNullOrWhiteSpace(cleanTitle))
                {
                    continue;
                }

                currentTask = new MutableMarkdownTask
                {
                    Title = cleanTitle,
                    Date = date,
                    Priority = priority,
                    ListId = currentListId,
                    IsCompleted = isDone,
                    Recurrence = recurrence,
                    Tags = tags,
                };
                parsedTasks.Add(currentTask);
                continue;
            }

            if (currentTask is not null && rawLine.StartsWith("  > ", StringComparison.Ordinal))
            {
                currentTask.ContentLines.Add(rawLine[4..]);
            }
        }

        var items = parsedTasks.Select(t => new ParsedImportTask(
            Title: t.Title,
            Content: string.Join("\n", t.ContentLines),
            Date: t.Date,
            Priority: t.Priority,
            ListId: t.ListId,
            IsCompleted: t.IsCompleted,
            Recurrence: t.Recurrence,
            Tags: t.Tags,
            Subtasks: t.Subtasks)).ToList();

        return CommitImportedTasks(items, createdLists, "Markdown", repository);
    }

    public static BackupImportResult ImportIcs(string ics, DateOnly fallbackDate, ITaskRepository repository)
    {
        if (string.IsNullOrWhiteSpace(ics))
        {
            return new BackupImportResult(0, 0, 0, null, "ICS 日历");
        }

        var unfolded = UnfoldIcsLines(ics);
        var createdLists = 0;
        var items = new List<ParsedImportTask>();

        var inEntry = false;
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in unfolded)
        {
            var upper = line.Trim();
            if (upper.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase) ||
                upper.Equals("BEGIN:VTODO", StringComparison.OrdinalIgnoreCase))
            {
                inEntry = true;
                props.Clear();
                continue;
            }

            if (upper.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase) ||
                upper.Equals("END:VTODO", StringComparison.OrdinalIgnoreCase))
            {
                if (inEntry)
                {
                    var parsed = ParseIcsEntry(props, fallbackDate, repository, ref createdLists);
                    if (parsed is not null)
                    {
                        items.Add(parsed);
                    }
                }

                inEntry = false;
                props.Clear();
                continue;
            }

            if (!inEntry)
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var rawKey = line[..colon];
            var value = line[(colon + 1)..];
            var semi = rawKey.IndexOf(';');
            var key = (semi >= 0 ? rawKey[..semi] : rawKey).Trim().ToUpperInvariant();
            props[key] = value;
        }

        return CommitImportedTasks(items, createdLists, "ICS 日历", repository);
    }

    // ==================== 内部工具 ====================

    private static BackupImportResult CommitImportedTasks(
        IReadOnlyList<ParsedImportTask> items,
        int createdLists,
        string formatLabel,
        ITaskRepository repository)
    {
        if (items.Count == 0)
        {
            return new BackupImportResult(0, 0, createdLists, null, formatLabel);
        }

        var existingKeys = repository
            .Query(new TaskQuery { IncludeCompleted = true })
            .Select(t => MakeDedupKey(t.ListId, t.Date, t.Title))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ops = new List<ScheduleOp>();
        var skipped = 0;

        foreach (var item in items)
        {
            var key = MakeDedupKey(item.ListId, item.Date, item.Title);
            if (!existingKeys.Add(key))
            {
                skipped++;
                continue;
            }

            ops.Add(new CreateTaskOp(new TaskDraft
            {
                Title = item.Title,
                Content = item.Content,
                Date = item.Date,
                DueTime = item.DueTime,
                Reminder = item.Reminder?.Clone(),
                Priority = item.Priority,
                ListId = item.ListId,
                IsCompleted = item.IsCompleted,
                Recurrence = item.Recurrence,
                Tags = item.Tags,
                Subtasks = item.Subtasks,
            }));
        }

        if (ops.Count == 0)
        {
            return new BackupImportResult(0, skipped, createdLists, null, formatLabel);
        }

        string? changeSetId = null;
        if (repository is IScheduleService schedule)
        {
            var cs = schedule.Apply(ops, ChangeSource.User, $"导入 {formatLabel}（{ops.Count} 项）");
            changeSetId = cs.Id;
        }
        else
        {
            foreach (var op in ops.OfType<CreateTaskOp>())
            {
                repository.Create(op.Draft);
            }
        }

        return new BackupImportResult(ops.Count, skipped, createdLists, changeSetId, formatLabel);
    }

    private static string ResolveOrCreateList(
        ITaskRepository repository,
        string? listId,
        string? listName,
        ref int createdLists)
    {
        if (!string.IsNullOrWhiteSpace(listId))
        {
            var byId = repository.Lists.FirstOrDefault(l => string.Equals(l.Id, listId, StringComparison.Ordinal));
            if (byId is not null)
            {
                return byId.Id;
            }
        }

        var trimmedName = listName?.Trim();
        if (string.IsNullOrEmpty(trimmedName) ||
            string.Equals(trimmedName, "收集箱", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmedName, "Inbox", StringComparison.OrdinalIgnoreCase))
        {
            return TaskList.InboxId;
        }

        var byName = repository.Lists.FirstOrDefault(l =>
            string.Equals(l.Name, trimmedName, StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            return byName.Id;
        }

        var created = repository.CreateList(trimmedName);
        createdLists++;
        return created.Id;
    }

    private static string MakeDedupKey(string listId, DateOnly date, string title) =>
        $"{listId}|{date:yyyy-MM-dd}|{title.Trim().ToLowerInvariant()}";

    private static string FormatPriorityToken(TaskPriority priority) => priority switch
    {
        TaskPriority.High => "高",
        TaskPriority.Medium => "中",
        TaskPriority.Low => "低",
        _ => string.Empty,
    };

    private static RecurrenceKind ParseChineseRecurrenceLabel(string raw) => raw.Trim() switch
    {
        "每天" => RecurrenceKind.Daily,
        "工作日" => RecurrenceKind.Weekdays,
        "每周" => RecurrenceKind.Weekly,
        "每月" => RecurrenceKind.Monthly,
        "每年" => RecurrenceKind.Yearly,
        "农历每年" or "农历年" => RecurrenceKind.LunarYearly,
        var s when Enum.TryParse<RecurrenceKind>(s, ignoreCase: true, out var parsed) => parsed,
        _ => RecurrenceKind.None,
    };

    private static string BuildIcsDescription(TaskItem task)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(task.Content))
        {
            sb.Append(task.Content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd());
        }

        if (task.Subtasks.Count > 0)
        {
            if (sb.Length > 0)
            {
                sb.Append("\n\n");
            }

            for (var i = 0; i < task.Subtasks.Count; i++)
            {
                var sub = task.Subtasks[i];
                sb.Append(sub.IsCompleted ? "- [x] " : "- [ ] ").Append(sub.Title);
                if (i < task.Subtasks.Count - 1)
                {
                    sb.Append('\n');
                }
            }
        }

        return sb.ToString();
    }

    private static ParsedImportTask? ParseIcsEntry(
        Dictionary<string, string> props,
        DateOnly fallbackDate,
        ITaskRepository repository,
        ref int createdLists)
    {
        if (!props.TryGetValue("SUMMARY", out var rawSummary))
        {
            return null;
        }

        var title = UnescapeIcsText(rawSummary).Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var date = fallbackDate;
        if (props.TryGetValue("DTSTART", out var rawDate) || props.TryGetValue("DUE", out rawDate))
        {
            date = ParseIcsDate(rawDate, fallbackDate);
        }

        var content = string.Empty;
        var subtasks = new List<TaskSubtask>();
        if (props.TryGetValue("DESCRIPTION", out var rawDesc))
        {
            var unescaped = UnescapeIcsText(rawDesc);
            var descLines = unescaped.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var noteLines = new List<string>();
            foreach (var dl in descLines)
            {
                var subMatch = MarkdownTaskLineRegex().Match(dl);
                if (subMatch.Success)
                {
                    subtasks.Add(new TaskSubtask
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Title = subMatch.Groups[3].Value.Trim(),
                        IsCompleted = !string.Equals(subMatch.Groups[2].Value, " ", StringComparison.Ordinal),
                    });
                }
                else
                {
                    noteLines.Add(dl);
                }
            }

            content = string.Join("\n", noteLines).Trim();
        }

        var tags = new List<string>();
        if (props.TryGetValue("CATEGORIES", out var rawCategories))
        {
            var parts = SplitUnescapedCommas(rawCategories).Select(UnescapeIcsText);
            tags = TaskTagParser.Normalize(parts);
        }

        var listId = TaskList.InboxId;
        if (props.TryGetValue("X-PLANA-LIST", out var rawList))
        {
            listId = ResolveOrCreateList(repository, null, UnescapeIcsText(rawList), ref createdLists);
        }

        var priority = TaskPriority.None;
        if (props.TryGetValue("PRIORITY", out var rawPrio) && int.TryParse(rawPrio.Trim(), out var pNum))
        {
            priority = pNum switch
            {
                >= 1 and <= 4 => TaskPriority.High,
                5 => TaskPriority.Medium,
                >= 6 and <= 9 => TaskPriority.Low,
                _ => TaskPriority.None,
            };
        }

        var recurrence = props.TryGetValue("RRULE", out var rawRRule)
            ? RecurrenceCalculator.ParseIcsRRule(rawRRule)
            : RecurrenceKind.None;

        var isCompleted =
            (props.TryGetValue("X-PLANA-COMPLETED", out var compFlag) && compFlag.Trim() == "1") ||
            (props.TryGetValue("STATUS", out var status) &&
             status.Trim().Equals("COMPLETED", StringComparison.OrdinalIgnoreCase));

        return new ParsedImportTask(
            Title: title,
            Content: content,
            Date: date,
            Priority: priority,
            ListId: listId,
            IsCompleted: isCompleted,
            Recurrence: recurrence,
            Tags: tags,
            Subtasks: subtasks);
    }

    private static DateOnly ParseIcsDate(string raw, DateOnly fallback)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length >= 8 &&
            DateOnly.TryParseExact(trimmed[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        return fallback;
    }

    private static List<string> UnfoldIcsLines(string ics)
    {
        var normalized = ics.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var rawLines = normalized.Split('\n');
        var result = new List<string>(rawLines.Length);

        foreach (var line in rawLines)
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && result.Count > 0)
            {
                result[^1] += line[1..];
            }
            else if (line.Length > 0)
            {
                result.Add(line);
            }
        }

        return result;
    }

    private static void AppendIcsLine(StringBuilder sb, string line)
    {
        // RFC 5545 行折行：每行不超过 75 个 UTF-8 字节，续行以单个空格开头。
        if (Encoding.UTF8.GetByteCount(line) <= 75)
        {
            sb.Append(line).Append("\r\n");
            return;
        }

        var currentBytes = 0;
        var limit = 75;
        foreach (var rune in line.EnumerateRunes())
        {
            var runeBytes = rune.Utf8SequenceLength;
            if (currentBytes + runeBytes > limit && currentBytes > 0)
            {
                sb.Append("\r\n ");
                currentBytes = 1; // 续行前导空格占 1 字节
            }

            sb.Append(rune);
            currentBytes += runeBytes;
        }

        sb.Append("\r\n");
    }

    private static string EscapeIcsText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string UnescapeIcsText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                var next = text[i + 1];
                switch (next)
                {
                    case 'n':
                    case 'N':
                        sb.Append('\n');
                        i++;
                        continue;
                    case ',':
                    case ';':
                    case '\\':
                        sb.Append(next);
                        i++;
                        continue;
                }
            }

            sb.Append(text[i]);
        }

        return sb.ToString();
    }

    private static IEnumerable<string> SplitUnescapedCommas(string input)
    {
        var sb = new StringBuilder();
        var escaped = false;
        foreach (var ch in input)
        {
            if (escaped)
            {
                sb.Append('\\').Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == ',')
            {
                yield return sb.ToString();
                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        if (escaped)
        {
            sb.Append('\\');
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

    private static TaskReminder CloneReminderWithoutRuntime(TaskReminder source)
    {
        var copy = source.Clone();
        copy.ResetTriggerState();
        return copy;
    }

    private sealed record ParsedImportTask(
        string Title,
        string Content,
        DateOnly Date,
        TaskPriority Priority,
        string ListId,
        bool IsCompleted,
        RecurrenceKind Recurrence,
        List<string> Tags,
        List<TaskSubtask> Subtasks,
        TimeOnly? DueTime = null,
        TaskReminder? Reminder = null);

    private sealed class MutableMarkdownTask
    {
        public required string Title { get; init; }
        public DateOnly Date { get; init; }
        public TaskPriority Priority { get; init; }
        public string ListId { get; init; } = TaskList.InboxId;
        public bool IsCompleted { get; init; }
        public RecurrenceKind Recurrence { get; init; }
        public List<string> Tags { get; init; } = [];
        public List<string> ContentLines { get; } = [];
        public List<TaskSubtask> Subtasks { get; } = [];
    }

    private sealed class BackupDocumentDto
    {
        public int Version { get; set; } = BackupSchemaVersion;
        public string App { get; set; } = "Plana";
        public string ExportedAt { get; set; } = string.Empty;
        public List<BackupListDto> Lists { get; set; } = [];
        public List<BackupTaskDto> Tasks { get; set; } = [];
    }

    private sealed class BackupListDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private sealed class BackupTaskDto
    {
        public string? Title { get; set; }
        public string? Content { get; set; }
        public string? Date { get; set; }
        public string? DueTime { get; set; }
        public TaskReminder? Reminder { get; set; }
        public string? Priority { get; set; }
        public string? ListId { get; set; }
        public string? ListName { get; set; }
        public bool IsCompleted { get; set; }
        public string? Recurrence { get; set; }
        public List<string>? Tags { get; set; }
        public List<BackupSubtaskDto>? Subtasks { get; set; }
    }

    private sealed class BackupSubtaskDto
    {
        public string? Title { get; set; }
        public bool IsCompleted { get; set; }
    }
}

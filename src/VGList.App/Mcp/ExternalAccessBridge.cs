using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using VGList.App.Assistant;
using VGList.Core.Abstractions;
using VGList.Core.Dates;
using VGList.Core.Models;

namespace VGList.App.Mcp;

/// <summary>
/// A7 主进程本地 MCP 命名管道桥（VGList.App 层，不引用任何 MCP SDK 或 Agent 程序集）：
/// - 仅在用户开启「外部 AI 接入」开关时调用 Start() 监听命名管道，关闭即停止并断开现有客户端；
/// - 严格 10 个任务/日历工具白名单，绝不暴露记忆、画像、规则、对话或模型 API Key；
/// - 只读模式拦截全部写工具；写操作限流每客户端 60 次/分钟；
/// - 写入统一经 IScheduleService(ChangeSource.Mcp) 原子提交并支持撤销；删除与 >5 项批量改期强制走待确认队列（90s 超时）。
/// </summary>
public sealed class ExternalAccessBridge : IDisposable
{
    public const int MaxListLimit = 200;
    public const int MaxCreateBatch = 20;
    public const int MaxBulkBatch = 50;
    public const int BulkRescheduleConfirmThreshold = 5;
    public const int MaxContentChars = 8_000;
    public const int MaxWritesPerMinute = 60;

    private static readonly TimeSpan DefaultConfirmationTimeout = TimeSpan.FromSeconds(90);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HashSet<string> WriteTools = new(StringComparer.Ordinal)
    {
        "create_tasks",
        "update_task",
        "complete_task",
        "reschedule_tasks",
        "delete_tasks",
    };

    private readonly ITaskRepository _repository;
    private readonly IScheduleService _schedule;
    private readonly Func<bool> _isReadOnly;
    private readonly Func<DateOnly> _todayProvider;
    private readonly Func<string, NamedPipeServerStream> _pipeFactory;
    private readonly TimeSpan _confirmationTimeout;
    private readonly object _lifecycleGate = new();
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _writeRateWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, NamedPipeServerStream> _activeStreams = new();

    private CancellationTokenSource? _listenCts;
    private Task? _acceptLoopTask;
    private int _streamSeq;
    private bool _disposed;

    public ExternalAccessBridge(
        ITaskRepository repository,
        IScheduleService schedule,
        Func<bool> isReadOnly,
        string? pipeName = null,
        Func<string, NamedPipeServerStream>? serverPipeFactory = null,
        Func<DateOnly>? todayProvider = null,
        McpAuditLog? auditLog = null,
        McpPendingConfirmationQueue? confirmationQueue = null,
        TimeSpan? confirmationTimeout = null)
    {
        _repository = repository;
        _schedule = schedule;
        _isReadOnly = isReadOnly;
        PipeName = string.IsNullOrWhiteSpace(pipeName) ? $"plana-mcp-{Environment.UserName}" : pipeName;
        _pipeFactory = serverPipeFactory ?? (name => new NamedPipeServerStream(
            name,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous));
        _todayProvider = todayProvider ?? (() => DateOnly.FromDateTime(DateTime.Today));
        AuditLog = auditLog ?? new McpAuditLog();
        Confirmations = confirmationQueue ?? new McpPendingConfirmationQueue();
        _confirmationTimeout = confirmationTimeout ?? DefaultConfirmationTimeout;
    }

    public string PipeName { get; }
    public McpAuditLog AuditLog { get; }
    public McpPendingConfirmationQueue Confirmations { get; }

    public bool IsListening
    {
        get
        {
            lock (_lifecycleGate)
            {
                return _listenCts is { IsCancellationRequested: false };
            }
        }
    }

    public event EventHandler<McpAppliedEventArgs>? Applied;
    public event EventHandler? AuditUpdated;
    public event EventHandler? ListeningChanged;

    public void Start()
    {
        lock (_lifecycleGate)
        {
            if (_disposed || _listenCts is { IsCancellationRequested: false })
            {
                return;
            }

            _listenCts = new CancellationTokenSource();
            var ct = _listenCts.Token;
            _acceptLoopTask = Task.Run(() => AcceptLoopAsync(ct), CancellationToken.None);
        }

        ListeningChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_lifecycleGate)
        {
            cts = _listenCts;
            _listenCts = null;
            _acceptLoopTask = null;
        }

        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
            }

            cts.Dispose();
        }

        foreach (var kv in _activeStreams)
        {
            if (_activeStreams.TryRemove(kv.Key, out var stream))
            {
                try
                {
                    stream.Dispose();
                }
                catch
                {
                }
            }
        }

        Confirmations.RejectAll();
        ListeningChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            var id = Interlocked.Increment(ref _streamSeq);
            try
            {
                server = _pipeFactory(PipeName);
                _activeStreams[id] = server;
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                var connected = server;
                server = null;
                _ = Task.Run(() => HandleClientAsync(id, connected, ct), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                CleanupStream(id, server);
                break;
            }
            catch (ObjectDisposedException)
            {
                CleanupStream(id, server);
                if (ct.IsCancellationRequested)
                {
                    break;
                }
            }
            catch
            {
                CleanupStream(id, server);
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await Task.Delay(150, ct).ConfigureAwait(false);
                }
                catch
                {
                    break;
                }
            }
        }
    }

    private async Task HandleClientAsync(int streamId, NamedPipeServerStream stream, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 8192, leaveOpen: true)
            {
                AutoFlush = true,
            };

            while (!ct.IsCancellationRequested && stream.IsConnected)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                McpPipeResponse response;
                try
                {
                    var req = JsonSerializer.Deserialize<McpPipeRequest>(line, JsonOpts);
                    if (req is null)
                    {
                        response = ErrorResponse("?", "invalid_request", "请求 JSON 格式无效。");
                    }
                    else
                    {
                        response = await HandleRequestAsync(req, ct).ConfigureAwait(false);
                    }
                }
                catch (JsonException)
                {
                    response = ErrorResponse("?", "invalid_json", "无法解析请求 JSON。");
                }

                var payload = JsonSerializer.Serialize(response, JsonOpts);
                await writer.WriteLineAsync(payload.AsMemory(), ct).ConfigureAwait(false);
            }
        }
        catch
        {
            // 客户端断开或主程序停止监听
        }
        finally
        {
            CleanupStream(streamId, stream);
        }
    }

    private void CleanupStream(int id, NamedPipeServerStream? stream)
    {
        _activeStreams.TryRemove(id, out var tracked);
        var toDispose = stream ?? tracked;
        if (toDispose is not null)
        {
            try
            {
                toDispose.Dispose();
            }
            catch
            {
            }
        }
    }

    /// <summary>处理单条协议请求（管道客户端与单元测试共用入口）。</summary>
    public async Task<McpPipeResponse> HandleRequestAsync(McpPipeRequest req, CancellationToken ct = default)
    {
        var reqId = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("N")[..8] : req.Id;
        var client = SanitizeClientName(req.ClientName);
        var tool = (req.Tool ?? string.Empty).Trim();

        if (req.V != McpProtocol.CurrentVersion)
        {
            RecordAudit(client, tool, 0, "version_mismatch", null);
            return ErrorResponse(
                reqId,
                "version_mismatch",
                $"协议版本不匹配（收到 v={req.V}，需要 v={McpProtocol.CurrentVersion}），请确保 Plana 与 VGList.Mcp.exe 为同一版本。");
        }

        if (WriteTools.Contains(tool))
        {
            if (_isReadOnly())
            {
                RecordAudit(client, tool, 0, "read_only_denied", null);
                return ErrorResponse(reqId, "read_only", "Plana 外部 AI 接入当前为「只读模式」，已拒绝写操作。可在 设置 → 助理与记忆 → 外部 AI 接入 切换为「读写」。");
            }

            if (!TryConsumeWriteQuota(client))
            {
                RecordAudit(client, tool, 0, "rate_limited", null);
                return ErrorResponse(reqId, "rate_limited", $"客户端「{client}」写入过于频繁（上限 {MaxWritesPerMinute} 次/分钟），请稍后再试。");
            }
        }

        try
        {
            return tool switch
            {
                "get_today" => HandleGetToday(reqId, client),
                "list_tasks" => HandleListTasks(reqId, client, req.Args),
                "list_lists" => HandleListLists(reqId, client),
                "resolve_date" => HandleResolveDate(reqId, client, req.Args),
                "cn_calendar_info" => HandleCnCalendarInfo(reqId, client, req.Args),
                "create_tasks" => HandleCreateTasks(reqId, client, req.Args),
                "update_task" => HandleUpdateTask(reqId, client, req.Args),
                "complete_task" => HandleCompleteTask(reqId, client, req.Args),
                "reschedule_tasks" => await HandleRescheduleTasksAsync(reqId, client, req.Args, ct).ConfigureAwait(false),
                "delete_tasks" => await HandleDeleteTasksAsync(reqId, client, req.Args, ct).ConfigureAwait(false),
                _ => HandleUnknownTool(reqId, client, tool),
            };
        }
        catch (OperationCanceledException)
        {
            RecordAudit(client, tool, 0, "cancelled", null);
            return ErrorResponse(reqId, "cancelled", "操作已取消或 Plana 已关闭外部 AI 接入。");
        }
        catch (Exception ex)
        {
            RecordAudit(client, tool, 0, "error", null);
            return ErrorResponse(reqId, "internal_error", $"执行失败：{ex.Message}");
        }
    }

    private McpPipeResponse HandleUnknownTool(string reqId, string client, string tool)
    {
        RecordAudit(client, string.IsNullOrEmpty(tool) ? "(empty)" : tool, 0, "unknown_tool", null);
        return ErrorResponse(reqId, "unknown_tool", $"未知或未授权的工具「{tool}」。Plana 仅开放任务与日历相关工具，不暴露记忆、画像、规则与密钥。");
    }

    private McpPipeResponse HandleGetToday(string reqId, string client)
    {
        var today = _todayProvider();
        var lunar = CnCalendar.Describe(today);
        var todayTasks = _repository.Query(TaskQuery.ForDate(today));
        var overdue = _repository.Query(new TaskQuery { To = today.AddDays(-1), IncludeCompleted = false });

        var result = new
        {
            date = today.ToString("yyyy-MM-dd"),
            weekday = FormatWeekday(today.DayOfWeek),
            lunar = lunar.Text,
            lunarKind = lunar.Kind.ToString(),
            overdueCount = overdue.Count,
            todayPendingCount = todayTasks.Count(t => !t.IsCompleted),
            todayCompletedCount = todayTasks.Count(t => t.IsCompleted),
            tasks = todayTasks.Select(SerializeTask).ToList(),
            lists = _repository.Lists.Select(l => new { id = l.Id, name = l.Name, isInbox = l.Id == TaskList.InboxId }).ToList(),
        };

        RecordAudit(client, "get_today", todayTasks.Count, "ok", null);
        return OkResponse(reqId, result);
    }

    private McpPipeResponse HandleListTasks(string reqId, string client, JsonElement? args)
    {
        DateOnly? from = null;
        DateOnly? to = null;
        string? listId = null;
        var includeCompleted = false;
        var limit = 100;

        if (args is { ValueKind: JsonValueKind.Object } obj)
        {
            if (TryGetString(obj, "from", out var fromRaw) && !string.IsNullOrWhiteSpace(fromRaw))
            {
                if (!ScheduleOpValidator.TryParseStrictIsoDate(fromRaw, out var d))
                {
                    return ErrorResponse(reqId, "invalid_args", $"参数 from「{fromRaw}」格式无效，须为 yyyy-MM-dd。");
                }

                from = d;
            }

            if (TryGetString(obj, "to", out var toRaw) && !string.IsNullOrWhiteSpace(toRaw))
            {
                if (!ScheduleOpValidator.TryParseStrictIsoDate(toRaw, out var d))
                {
                    return ErrorResponse(reqId, "invalid_args", $"参数 to「{toRaw}」格式无效，须为 yyyy-MM-dd。");
                }

                to = d;
            }

            if (TryGetString(obj, "listId", out var lid) && !string.IsNullOrWhiteSpace(lid))
            {
                listId = lid.Trim();
            }

            if (TryGetBool(obj, "includeCompleted", out var inc))
            {
                includeCompleted = inc;
            }

            if (TryGetInt(obj, "limit", out var lim))
            {
                limit = Math.Clamp(lim, 1, MaxListLimit);
            }
        }

        var queried = _repository.Query(new TaskQuery
        {
            From = from,
            To = to,
            ListId = listId,
            IncludeCompleted = includeCompleted,
        });

        var truncated = queried.Count > limit;
        var slice = truncated ? queried.Take(limit).ToList() : queried;

        RecordAudit(client, "list_tasks", slice.Count, "ok", null);
        return OkResponse(reqId, new
        {
            totalMatched = queried.Count,
            returned = slice.Count,
            truncated,
            tasks = slice.Select(SerializeTask).ToList(),
        });
    }

    private McpPipeResponse HandleListLists(string reqId, string client)
    {
        var counts = _repository.Query(new TaskQuery { IncludeCompleted = false })
            .GroupBy(t => t.ListId)
            .ToDictionary(g => g.Key, g => g.Count());

        var lists = _repository.Lists.Select(l => new
        {
            id = l.Id,
            name = l.Name,
            isInbox = l.Id == TaskList.InboxId,
            pendingCount = counts.TryGetValue(l.Id, out var c) ? c : 0,
        }).ToList();

        RecordAudit(client, "list_lists", lists.Count, "ok", null);
        return OkResponse(reqId, new { lists });
    }

    private McpPipeResponse HandleResolveDate(string reqId, string client, JsonElement? args)
    {
        if (args is not { ValueKind: JsonValueKind.Object } obj ||
            !TryGetString(obj, "text", out var text) ||
            string.IsNullOrWhiteSpace(text))
        {
            return ErrorResponse(reqId, "invalid_args", "参数 text 不能为空（例如「下周三交周报」或「月底前」）。");
        }

        var baseDate = _todayProvider();
        if (TryGetString(obj, "baseDate", out var baseRaw) && !string.IsNullOrWhiteSpace(baseRaw))
        {
            if (!ScheduleOpValidator.TryParseStrictIsoDate(baseRaw, out var d))
            {
                return ErrorResponse(reqId, "invalid_args", $"参数 baseDate「{baseRaw}」格式无效，须为 yyyy-MM-dd。");
            }

            baseDate = d;
        }

        var matches = ChineseDateParser.Shared.ParseAll(text.Trim(), baseDate);
        var best = matches.FirstOrDefault();
        RecordAudit(client, "resolve_date", matches.Count, "ok", null);

        if (best is null)
        {
            return OkResponse(reqId, new { matched = false, baseDate = baseDate.ToString("yyyy-MM-dd") });
        }

        return OkResponse(reqId, new
        {
            matched = true,
            baseDate = baseDate.ToString("yyyy-MM-dd"),
            date = best.Date.ToString("yyyy-MM-dd"),
            weekday = FormatWeekday(best.Date.DayOfWeek),
            matchedText = best.Matched,
            cleanTitle = best.CleanTitle,
            lunar = CnCalendar.Describe(best.Date).Text,
        });
    }

    private McpPipeResponse HandleCnCalendarInfo(string reqId, string client, JsonElement? args)
    {
        var target = _todayProvider();
        if (args is { ValueKind: JsonValueKind.Object } obj &&
            TryGetString(obj, "date", out var dateRaw) &&
            !string.IsNullOrWhiteSpace(dateRaw))
        {
            if (!ScheduleOpValidator.TryParseStrictIsoDate(dateRaw, out var d))
            {
                return ErrorResponse(reqId, "invalid_args", $"参数 date「{dateRaw}」格式无效，须为 yyyy-MM-dd。");
            }

            target = d;
        }

        if (target.Year is < 1901 or > 2100)
        {
            return ErrorResponse(reqId, "invalid_args", "日期须在 1901–2100 年范围内。");
        }

        var desc = CnCalendar.Describe(target);
        var isWeekend = target.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        RecordAudit(client, "cn_calendar_info", 1, "ok", null);

        return OkResponse(reqId, new
        {
            date = target.ToString("yyyy-MM-dd"),
            weekday = FormatWeekday(target.DayOfWeek),
            isWeekend,
            label = desc.Text,
            kind = desc.Kind.ToString(),
        });
    }

    private McpPipeResponse HandleCreateTasks(string reqId, string client, JsonElement? args)
    {
        if (args is not { ValueKind: JsonValueKind.Object } obj ||
            !obj.TryGetProperty("tasks", out var tasksElem) ||
            tasksElem.ValueKind != JsonValueKind.Array)
        {
            return ErrorResponse(reqId, "invalid_args", "参数 tasks 必须为非空数组（单次最多 20 条）。");
        }

        var count = tasksElem.GetArrayLength();
        if (count == 0)
        {
            return ErrorResponse(reqId, "invalid_args", "tasks 数组不能为空。");
        }

        if (count > MaxCreateBatch)
        {
            return ErrorResponse(reqId, "invalid_args", $"单次最多创建 {MaxCreateBatch} 条任务（当前 {count} 条），请分批操作。");
        }

        var ops = new List<ScheduleOp>(count);
        var index = 0;
        foreach (var item in tasksElem.EnumerateArray())
        {
            index++;
            if (item.ValueKind != JsonValueKind.Object)
            {
                return ErrorResponse(reqId, "invalid_args", $"第 {index} 项必须为 JSON 对象。");
            }

            if (!TryGetString(item, "title", out var title) || string.IsNullOrWhiteSpace(title))
            {
                return ErrorResponse(reqId, "invalid_args", $"第 {index} 项缺少非空 title。");
            }

            var date = _todayProvider();
            if (TryGetString(item, "date", out var dateRaw) && !string.IsNullOrWhiteSpace(dateRaw))
            {
                if (!ScheduleOpValidator.TryParseStrictIsoDate(dateRaw, out var parsedDate))
                {
                    return ErrorResponse(reqId, "invalid_args", $"第 {index} 项日期「{dateRaw}」无效，须为 yyyy-MM-dd。");
                }

                date = parsedDate;
            }

            var priority = TaskPriority.None;
            if (TryGetString(item, "priority", out var prioRaw) && !string.IsNullOrWhiteSpace(prioRaw))
            {
                if (!TryParsePriority(prioRaw, out priority))
                {
                    return ErrorResponse(reqId, "invalid_args", $"第 {index} 项优先级「{prioRaw}」无效，允许 None / Low / Medium / High。");
                }
            }

            var listId = TaskList.InboxId;
            if (TryGetString(item, "listId", out var lid) && !string.IsNullOrWhiteSpace(lid))
            {
                listId = lid.Trim();
            }

            var content = string.Empty;
            if (TryGetString(item, "content", out var desc) && desc is not null)
            {
                if (desc.Length > MaxContentChars)
                {
                    return ErrorResponse(reqId, "invalid_args", $"第 {index} 项描述超过上限 {MaxContentChars} 字。");
                }

                content = desc.Trim();
            }

            List<TaskSubtask>? subtasks = null;
            if (item.TryGetProperty("subtasks", out var subsElem) && subsElem.ValueKind == JsonValueKind.Array)
            {
                subtasks = [];
                foreach (var s in subsElem.EnumerateArray())
                {
                    if (s.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(s.GetString()))
                    {
                        subtasks.Add(new TaskSubtask { Title = s.GetString()!.Trim() });
                    }
                }
            }

            var op = new CreateTaskOp(new TaskDraft
            {
                Title = title.Trim(),
                Date = date,
                Priority = priority,
                ListId = listId,
                Content = content,
                Subtasks = subtasks is { Count: > 0 } ? subtasks : null,
            });

            var (valid, err) = ScheduleOpValidator.Validate(op, _repository);
            if (!valid)
            {
                return ErrorResponse(reqId, "invalid_args", $"第 {index} 项校验失败：{err}");
            }

            ops.Add(op);
        }

        var label = $"由 {client} 添加 {ops.Count} 项";
        var changeSet = _schedule.Apply(ops, ChangeSource.Mcp, label);
        RecordAudit(client, "create_tasks", ops.Count, "ok", changeSet.Id);
        Applied?.Invoke(this, new McpAppliedEventArgs(client, "create_tasks", label, ops.Count, changeSet.Id));

        return OkResponse(reqId, new
        {
            createdCount = ops.Count,
            changeSetId = changeSet.Id,
            taskIds = changeSet.Entries.Select(e => e.EntityId).ToList(),
        });
    }

    private McpPipeResponse HandleUpdateTask(string reqId, string client, JsonElement? args)
    {
        if (args is not { ValueKind: JsonValueKind.Object } obj ||
            !TryGetString(obj, "taskId", out var taskId) ||
            string.IsNullOrWhiteSpace(taskId))
        {
            return ErrorResponse(reqId, "invalid_args", "缺少有效的 taskId。");
        }

        string? newTitle = null;
        if (TryGetString(obj, "title", out var tVal) && tVal is not null)
        {
            if (string.IsNullOrWhiteSpace(tVal))
            {
                return ErrorResponse(reqId, "invalid_args", "修改后的任务标题不能为空。");
            }

            if (tVal.Trim().Length > 200)
            {
                return ErrorResponse(reqId, "invalid_args", "任务标题过长（上限 200 字）。");
            }

            newTitle = tVal.Trim();
        }

        DateOnly? newDate = null;
        if (TryGetString(obj, "date", out var dVal) && !string.IsNullOrWhiteSpace(dVal))
        {
            if (!ScheduleOpValidator.TryParseStrictIsoDate(dVal, out var parsedDate))
            {
                return ErrorResponse(reqId, "invalid_args", $"日期「{dVal}」格式无效，须为 yyyy-MM-dd。");
            }

            newDate = parsedDate;
        }

        TaskPriority? newPriority = null;
        if (TryGetString(obj, "priority", out var pVal) && !string.IsNullOrWhiteSpace(pVal))
        {
            if (!TryParsePriority(pVal, out var parsedPrio))
            {
                return ErrorResponse(reqId, "invalid_args", $"优先级「{pVal}」无效，允许 None / Low / Medium / High。");
            }

            newPriority = parsedPrio;
        }

        string? newListId = null;
        if (TryGetString(obj, "listId", out var lVal) && !string.IsNullOrWhiteSpace(lVal))
        {
            newListId = lVal.Trim();
        }

        string? newContent = null;
        if (TryGetString(obj, "content", out var cVal) && cVal is not null)
        {
            if (cVal.Length > MaxContentChars)
            {
                return ErrorResponse(reqId, "invalid_args", $"任务描述超过上限 {MaxContentChars} 字。");
            }

            newContent = cVal.Trim();
        }

        List<TaskSubtask>? appendSubs = null;
        if (obj.TryGetProperty("appendSubtasks", out var subsElem) && subsElem.ValueKind == JsonValueKind.Array)
        {
            appendSubs = [];
            foreach (var s in subsElem.EnumerateArray())
            {
                if (s.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(s.GetString()))
                {
                    appendSubs.Add(new TaskSubtask { Title = s.GetString()!.Trim() });
                }
            }
        }

        var patch = new TaskPatch
        {
            Title = newTitle,
            Date = newDate,
            Priority = newPriority,
            ListId = newListId,
            Content = newContent,
            AppendSubtasks = appendSubs is { Count: > 0 } ? appendSubs : null,
        };

        if (patch.Title is null && patch.Date is null && patch.Priority is null &&
            patch.ListId is null && patch.Content is null && patch.AppendSubtasks is null)
        {
            return ErrorResponse(reqId, "invalid_args", "至少需要提供一项要修改的字段。");
        }

        var op = new UpdateTaskOp(taskId.Trim(), patch);
        var (valid, err) = ScheduleOpValidator.Validate(op, _repository);
        if (!valid)
        {
            return ErrorResponse(reqId, "invalid_args", err!);
        }

        var label = $"由 {client} 修改 1 项";
        var changeSet = _schedule.Apply([op], ChangeSource.Mcp, label);
        RecordAudit(client, "update_task", 1, "ok", changeSet.Id);
        Applied?.Invoke(this, new McpAppliedEventArgs(client, "update_task", label, 1, changeSet.Id));

        return OkResponse(reqId, new
        {
            updatedTaskId = op.TaskId,
            changeSetId = changeSet.Id,
        });
    }

    private McpPipeResponse HandleCompleteTask(string reqId, string client, JsonElement? args)
    {
        if (args is not { ValueKind: JsonValueKind.Object } obj ||
            !TryGetString(obj, "taskId", out var taskId) ||
            string.IsNullOrWhiteSpace(taskId))
        {
            return ErrorResponse(reqId, "invalid_args", "缺少有效的 taskId。");
        }

        var completed = true;
        if (TryGetBool(obj, "completed", out var cVal))
        {
            completed = cVal;
        }

        var op = new SetCompletedOp(taskId.Trim(), completed);
        var (valid, err) = ScheduleOpValidator.Validate(op, _repository);
        if (!valid)
        {
            return ErrorResponse(reqId, "invalid_args", err!);
        }

        var label = completed ? $"由 {client} 完成 1 项" : $"由 {client} 重开 1 项";
        var changeSet = _schedule.Apply([op], ChangeSource.Mcp, label);
        RecordAudit(client, "complete_task", 1, "ok", changeSet.Id);
        Applied?.Invoke(this, new McpAppliedEventArgs(client, "complete_task", label, 1, changeSet.Id));

        return OkResponse(reqId, new
        {
            taskId = op.TaskId,
            completed,
            changeSetId = changeSet.Id,
        });
    }

    private async Task<McpPipeResponse> HandleRescheduleTasksAsync(
        string reqId,
        string client,
        JsonElement? args,
        CancellationToken ct)
    {
        if (args is not { ValueKind: JsonValueKind.Object } obj ||
            !obj.TryGetProperty("items", out var itemsElem) ||
            itemsElem.ValueKind != JsonValueKind.Array)
        {
            return ErrorResponse(reqId, "invalid_args", "参数 items 必须为非空数组（包含 taskId 与 newDate）。");
        }

        var count = itemsElem.GetArrayLength();
        if (count == 0 || count > MaxBulkBatch)
        {
            return ErrorResponse(reqId, "invalid_args", $"items 数量须在 1–{MaxBulkBatch} 之间（当前 {count}）。");
        }

        var ops = new List<ScheduleOp>(count);
        var previewItems = new List<McpPendingRequestItem>(count);
        var idx = 0;

        foreach (var item in itemsElem.EnumerateArray())
        {
            idx++;
            if (item.ValueKind != JsonValueKind.Object ||
                !TryGetString(item, "taskId", out var taskId) ||
                string.IsNullOrWhiteSpace(taskId) ||
                !TryGetString(item, "newDate", out var dateRaw) ||
                !ScheduleOpValidator.TryParseStrictIsoDate(dateRaw, out var newDate))
            {
                return ErrorResponse(reqId, "invalid_args", $"第 {idx} 项必须提供有效的 taskId 与 newDate（yyyy-MM-dd）。");
            }

            var existing = _repository.GetById(taskId.Trim());
            if (existing is null)
            {
                return ErrorResponse(reqId, "invalid_args", $"第 {idx} 项任务 Id「{taskId}」不存在。");
            }

            var op = new UpdateTaskOp(existing.Id, new TaskPatch { Date = newDate });
            var (valid, err) = ScheduleOpValidator.Validate(op, _repository);
            if (!valid)
            {
                return ErrorResponse(reqId, "invalid_args", $"第 {idx} 项校验失败：{err}");
            }

            ops.Add(op);
            previewItems.Add(new McpPendingRequestItem("改期", existing.Title, $"{existing.Date:M月d日} → {newDate:M月d日}"));
        }

        if (ops.Count > BulkRescheduleConfirmThreshold)
        {
            var now = DateTimeOffset.Now;
            var confirmation = new McpPendingConfirmation(
                Id: Guid.NewGuid().ToString("N")[..10],
                ClientName: client,
                Tool: "reschedule_tasks",
                Summary: $"请求批量改期 {ops.Count} 项任务",
                Items: previewItems,
                CreatedAt: now,
                ExpiresAt: now.Add(_confirmationTimeout));

            var outcome = await Confirmations.EnqueueAndWaitAsync(confirmation, _confirmationTimeout, ct).ConfigureAwait(false);
            if (outcome != McpConfirmationOutcome.Approved)
            {
                var code = outcome == McpConfirmationOutcome.TimedOut ? "confirmation_timeout" : "user_denied";
                var msg = outcome == McpConfirmationOutcome.TimedOut
                    ? "用户在 90 秒内未确认，已自动拒绝该批量改期请求。"
                    : "用户拒绝了该批量改期请求。";
                RecordAudit(client, "reschedule_tasks", ops.Count, code, null);
                return ErrorResponse(reqId, code, msg);
            }
        }

        var label = $"由 {client} 改期 {ops.Count} 项";
        var changeSet = _schedule.Apply(ops, ChangeSource.Mcp, label);
        RecordAudit(client, "reschedule_tasks", ops.Count, "ok", changeSet.Id);
        Applied?.Invoke(this, new McpAppliedEventArgs(client, "reschedule_tasks", label, ops.Count, changeSet.Id));

        return OkResponse(reqId, new
        {
            rescheduledCount = ops.Count,
            changeSetId = changeSet.Id,
        });
    }

    private async Task<McpPipeResponse> HandleDeleteTasksAsync(
        string reqId,
        string client,
        JsonElement? args,
        CancellationToken ct)
    {
        if (args is not { ValueKind: JsonValueKind.Object } obj ||
            !obj.TryGetProperty("taskIds", out var idsElem) ||
            idsElem.ValueKind != JsonValueKind.Array)
        {
            return ErrorResponse(reqId, "invalid_args", "参数 taskIds 必须为非空字符串数组。");
        }

        var count = idsElem.GetArrayLength();
        if (count == 0 || count > MaxBulkBatch)
        {
            return ErrorResponse(reqId, "invalid_args", $"taskIds 数量须在 1–{MaxBulkBatch} 之间（当前 {count}）。");
        }

        var ops = new List<ScheduleOp>(count);
        var previewItems = new List<McpPendingRequestItem>(count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var idElem in idsElem.EnumerateArray())
        {
            if (idElem.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(idElem.GetString()))
            {
                return ErrorResponse(reqId, "invalid_args", "taskIds 中包含无效任务 Id。");
            }

            var id = idElem.GetString()!.Trim();
            if (!seen.Add(id))
            {
                continue;
            }

            var existing = _repository.GetById(id);
            if (existing is null)
            {
                return ErrorResponse(reqId, "invalid_args", $"任务 Id「{id}」不存在。");
            }

            ops.Add(new DeleteTaskOp(id));
            previewItems.Add(new McpPendingRequestItem("删除", existing.Title, $"原日期 {existing.Date:M月d日}"));
        }

        // 删除操作始终需要主窗口内用户确认
        var now = DateTimeOffset.Now;
        var confirmation = new McpPendingConfirmation(
            Id: Guid.NewGuid().ToString("N")[..10],
            ClientName: client,
            Tool: "delete_tasks",
            Summary: $"请求删除 {ops.Count} 项任务",
            Items: previewItems,
            CreatedAt: now,
            ExpiresAt: now.Add(_confirmationTimeout));

        var outcome = await Confirmations.EnqueueAndWaitAsync(confirmation, _confirmationTimeout, ct).ConfigureAwait(false);
        if (outcome != McpConfirmationOutcome.Approved)
        {
            var code = outcome == McpConfirmationOutcome.TimedOut ? "confirmation_timeout" : "user_denied";
            var msg = outcome == McpConfirmationOutcome.TimedOut
                ? "用户在 90 秒内未确认，已自动拒绝删除请求。"
                : "用户拒绝了该删除请求。";
            RecordAudit(client, "delete_tasks", ops.Count, code, null);
            return ErrorResponse(reqId, code, msg);
        }

        // 确认后过滤已被其他途径删除的任务，保证原子性
        var liveOps = ops.Where(o => o is DeleteTaskOp d && _repository.GetById(d.TaskId) is not null).ToList();
        if (liveOps.Count == 0)
        {
            RecordAudit(client, "delete_tasks", 0, "ok", null);
            return OkResponse(reqId, new { deletedCount = 0 });
        }

        var label = $"由 {client} 删除 {liveOps.Count} 项";
        var changeSet = _schedule.Apply(liveOps, ChangeSource.Mcp, label);
        RecordAudit(client, "delete_tasks", liveOps.Count, "ok", changeSet.Id);
        Applied?.Invoke(this, new McpAppliedEventArgs(client, "delete_tasks", label, liveOps.Count, changeSet.Id));

        return OkResponse(reqId, new
        {
            deletedCount = liveOps.Count,
            changeSetId = changeSet.Id,
        });
    }

    private bool TryConsumeWriteQuota(string client)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddMinutes(-1);
        var queue = _writeRateWindows.GetOrAdd(client, _ => new Queue<DateTimeOffset>());

        lock (queue)
        {
            while (queue.Count > 0 && queue.Peek() < cutoff)
            {
                queue.Dequeue();
            }

            if (queue.Count >= MaxWritesPerMinute)
            {
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }

    private void RecordAudit(string client, string tool, int count, string result, string? changeSetId)
    {
        AuditLog.Append(new McpAuditEntry(DateTimeOffset.Now, client, tool, count, result, changeSetId));
        AuditUpdated?.Invoke(this, EventArgs.Empty);
    }

    private static object SerializeTask(TaskItem t) => new
    {
        id = t.Id,
        title = t.Title,
        date = t.Date.ToString("yyyy-MM-dd"),
        priority = t.Priority.ToString(),
        listId = t.ListId,
        isCompleted = t.IsCompleted,
        content = t.Content,
        subtasks = t.Subtasks.Select(s => new { id = s.Id, title = s.Title, isCompleted = s.IsCompleted }).ToList(),
    };

    private static McpPipeResponse OkResponse(string id, object result) =>
        new(McpProtocol.CurrentVersion, id, Ok: true, Result: result);

    private static McpPipeResponse ErrorResponse(string id, string code, string message) =>
        new(McpProtocol.CurrentVersion, id, Ok: false, Error: new McpPipeError(code, message));

    private static string SanitizeClientName(string? name)
    {
        var clean = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(clean))
        {
            return "外部 AI";
        }

        return clean.Length > 40 ? clean[..40] : clean;
    }

    private static bool TryGetString(JsonElement obj, string prop, out string? value)
    {
        value = null;
        if (!obj.TryGetProperty(prop, out var el))
        {
            return false;
        }

        if (el.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (el.ValueKind == JsonValueKind.String)
        {
            value = el.GetString();
            return true;
        }

        return false;
    }

    private static bool TryGetBool(JsonElement obj, string prop, out bool value)
    {
        value = false;
        if (!obj.TryGetProperty(prop, out var el))
        {
            return false;
        }

        if (el.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = el.GetBoolean();
            return true;
        }

        return false;
    }

    private static bool TryGetInt(JsonElement obj, string prop, out int value)
    {
        value = 0;
        return obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value);
    }

    private static bool TryParsePriority(string raw, out TaskPriority priority)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "none" or "无":
                priority = TaskPriority.None;
                return true;
            case "low" or "低":
                priority = TaskPriority.Low;
                return true;
            case "medium" or "中":
                priority = TaskPriority.Medium;
                return true;
            case "high" or "高":
                priority = TaskPriority.High;
                return true;
            default:
                priority = TaskPriority.None;
                return false;
        }
    }

    private static string FormatWeekday(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        _ => "星期日",
    };
}

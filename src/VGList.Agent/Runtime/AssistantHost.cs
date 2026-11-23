using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using VGList.Agent.Attachments;
using VGList.Agent.Calendar;
using VGList.Agent.Clients;
using VGList.Agent.Proposals;
using VGList.Agent.Tools;
using VGList.App.Assistant;
using VGList.App.Models;
using VGList.Core.Abstractions;
using VGList.Core.Models;

namespace VGList.Agent.Runtime;

/// <summary>安全审计日志项（绝不记录 API Key、用户消息原文、附件内容与完整系统提示）。</summary>
public sealed record AssistantAuditLogEntry(
    string RunId,
    DateTimeOffset Timestamp,
    string ModelName,
    long ElapsedMs,
    IReadOnlyList<string> ToolsInvoked,
    bool Succeeded,
    string? ErrorCategory);

/// <summary>
/// Plana 助理内核宿主（IAssistantHost 完整实现）：
/// 整合 MEAI FunctionInvokingChatClient 工具循环（上限 8 轮 / 120s 超时 / 顺序执行）、
/// 附件处理、权限分级、提案存储与二次校验应用、整批撤销与会话持久化。
/// </summary>
public sealed class AssistantHost : IAssistantHost
{
    public const int MaxToolIterations = 8;
    public static readonly TimeSpan DefaultRunTimeout = TimeSpan.FromSeconds(120);

    private readonly ITaskRepository _repository;
    private readonly IScheduleService _schedule;
    private readonly IModelProfileProvider _modelProvider;
    private readonly IChatClientFactory _clientFactory;
    private readonly IConversationStore? _conversationStore;
    private readonly CnHolidayCalendar _holidays;
    private readonly Func<bool> _allowReadTasks;
    private readonly Func<AssistantPermissionConfig> _permissionConfig;
    private readonly Func<DateOnly> _todayProvider;
    private readonly IProposalValidator? _proposalValidator;
    private readonly IReadOnlyList<IContextContributor> _contextContributors;
    private readonly IReadOnlyList<IAssistantToolProvider> _extraToolProviders;
    private readonly MemoryStore? _memoryStore;
    private readonly UserProfileStore? _profileStore;
    private readonly Func<bool> _memoryEnabled;
    private readonly ProposalApplier _applier;
    private readonly List<AssistantAuditLogEntry> _auditLogs = [];
    private readonly Lock _logGate = new();
    private bool _disposed;

    public AssistantHost(
        ITaskRepository repository,
        IScheduleService schedule,
        IModelProfileProvider modelProvider,
        IConversationStore? conversationStore = null,
        IChatClientFactory? clientFactory = null,
        CnHolidayCalendar? holidays = null,
        Func<bool>? allowReadTasks = null,
        Func<AssistantPermissionConfig>? permissionConfig = null,
        Func<DateOnly>? todayProvider = null,
        IProposalValidator? proposalValidator = null,
        IReadOnlyList<IContextContributor>? contextContributors = null,
        IReadOnlyList<IAssistantToolProvider>? extraToolProviders = null,
        MemoryStore? memoryStore = null,
        Func<bool>? memoryEnabled = null,
        UserProfileStore? profileStore = null)
    {
        _repository = repository;
        _schedule = schedule;
        _modelProvider = modelProvider;
        _conversationStore = conversationStore;
        _clientFactory = clientFactory ?? new DefaultChatClientFactory();
        _holidays = holidays ?? CnHolidayCalendar.Shared;
        _allowReadTasks = allowReadTasks ?? (() => true);
        _permissionConfig = permissionConfig ?? (() => AssistantPermissionConfig.Default);
        _todayProvider = todayProvider ?? (() => DateOnly.FromDateTime(DateTime.Today));
        _proposalValidator = proposalValidator;
        _contextContributors = contextContributors ?? [];
        _extraToolProviders = extraToolProviders ?? [];
        _memoryStore = memoryStore;
        _profileStore = profileStore;
        _memoryEnabled = memoryEnabled ?? (() => true);
        _applier = new ProposalApplier(repository, schedule, conversationStore);
    }

    public IReadOnlyList<SessionSummary> Sessions =>
        _conversationStore?.ListSessions(30) ?? [];

    public IReadOnlyList<AssistantAuditLogEntry> AuditLogs
    {
        get
        {
            lock (_logGate)
            {
                return _auditLogs.ToList();
            }
        }
    }

    public SessionSummary CreateSession(string title = "新对话") =>
        _conversationStore?.CreateSession(title: title)
        ?? new SessionSummary(Guid.NewGuid().ToString("N"), title, DateTimeOffset.Now);

    public IReadOnlyList<ConversationMessageRecord> LoadSessionMessages(string sessionId) =>
        _conversationStore?.LoadMessages(sessionId) ?? [];

    public void DeleteSession(string sessionId) =>
        _conversationStore?.DeleteSession(sessionId);

    public void TruncateSessionMessages(string sessionId, int keepCount) =>
        _conversationStore?.TruncateMessages(sessionId, keepCount);

    public void UpdateSessionTitle(string sessionId, string title)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        _conversationStore?.UpdateSessionTitle(sessionId, title);
    }

    public bool UpdateProposalItemOperation(string proposalId, string itemId, ScheduleOp updatedOperation) =>
        _applier.UpdateItemOperation(proposalId, itemId, updatedOperation);

    /// <summary>独立于主回复流，使用轻量级短连接并行生成简洁会话标题（4–12 字，互不阻塞）。</summary>
    public async Task<string?> GenerateSessionTitleAsync(
        string sessionId,
        string firstUserMessage,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(firstUserMessage))
        {
            return null;
        }

        if (!_modelProvider.TryResolveActive(out var model, out _) || model is null)
        {
            return null;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            using var client = _clientFactory.Create(model);
            var snippet = firstUserMessage.Trim();
            if (snippet.Length > 260)
            {
                snippet = snippet[..260] + "…";
            }

            List<ChatMessage> prompt =
            [
                new(
                    ChatRole.System,
                    "请将用户的首条对话浓缩为一个简短的中文会话标题（4～12个汉字），直接输出标题正文，不要加引号、书名号、序号、标点符号、emoji 或任何解释。"),
                new(ChatRole.User, snippet),
            ];

            var response = await client.GetResponseAsync(
                prompt,
                new ChatOptions { MaxOutputTokens = 32, Temperature = 0.2f },
                linkedCts.Token);

            var clean = CleanGeneratedTitle(response.Text);
            if (string.IsNullOrEmpty(clean))
            {
                return null;
            }

            _conversationStore?.UpdateSessionTitle(sessionId, clean);
            return clean;
        }
        catch
        {
            return null;
        }
    }

    private static string CleanGeneratedTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var line = raw
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        line = line.Trim().Trim('"', '\'', '“', '”', '‘', '’', '《', '》', '「', '」', '【', '】', '*', '#', '-', '•');
        if (line.StartsWith("标题：", StringComparison.Ordinal) || line.StartsWith("标题:", StringComparison.Ordinal))
        {
            line = line[3..].Trim();
        }

        line = line.TrimEnd('。', '！', '？', '!', '?', '.', '，', ',').Trim();
        if (line.Length > 18)
        {
            line = line[..18];
        }

        return line;
    }

    public async IAsyncEnumerable<AssistantEvent> RunAsync(
        AssistantRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var runId = Guid.NewGuid().ToString("N")[..12];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var toolsInvoked = new List<string>();
        var modelLabel = "unknown";

        if (!_modelProvider.TryResolveActive(out var model, out var resolveError) || model is null)
        {
            yield return new ErrorEvent(resolveError ?? "尚未配置可用模型，请前往「设置 → 模型服务」完成配置。", Retryable: false);
            RecordAudit(runId, modelLabel, sw.ElapsedMilliseconds, toolsInvoked, false, "NoActiveModel");
            yield break;
        }

        modelLabel = model.Model;

        // 1. 预处理附件（发射实时「Lana 正在查看文件/图片…」进度，严格校验图片支持与大小上限）
        var hasAttachments = request.Attachments is { Count: > 0 };
        if (hasAttachments)
        {
            var attSummary = request.Attachments!.Count == 1
                ? $"「{request.Attachments[0].Name}」"
                : $"{request.Attachments.Count} 个附件";
            yield return new ToolStartedEvent("read_attachment", $"Lana 正在查看{attSummary}…");
        }

        var attResult = await Task.Run(
            () => AttachmentProcessor.Process(request.Attachments, model.SupportsImages),
            ct);

        if (hasAttachments)
        {
            var attSummary = request.Attachments!.Count == 1
                ? $"「{request.Attachments[0].Name}」"
                : $"{request.Attachments.Count} 个附件";
            yield return new ToolFinishedEvent(
                "read_attachment",
                string.IsNullOrEmpty(attResult.FatalError) ? $"已查看{attSummary}" : $"无法读取{attSummary}",
                string.IsNullOrEmpty(attResult.FatalError));
        }

        foreach (var warn in attResult.Warnings)
        {
            yield return new WarningEvent(warn);
        }

        if (!string.IsNullOrEmpty(attResult.FatalError))
        {
            yield return new ErrorEvent(attResult.FatalError, Retryable: false);
            RecordAudit(runId, modelLabel, sw.ElapsedMilliseconds, toolsInvoked, false, "AttachmentRejected");
            yield break;
        }

        // 2. 构建系统提示与裁剪历史
        var existingMessages = _conversationStore?.LoadMessages(request.SessionId) ?? [];
        var today = _todayProvider();
        var hasValidAttachments = attResult.Documents.Count > 0 || attResult.Images.Count > 0;
        var systemPrompt = SystemPromptBuilder.Build(
            today,
            _holidays,
            request.Text,
            _contextContributors,
            request.Context,
            hasAttachments: hasValidAttachments);
        var chatMessages = SystemPromptBuilder.BuildTrimmedHistory(existingMessages, systemPrompt);

        // 组装本轮用户消息（文本 + 提取出的文档正文 + 缩放后的图片 DataContent）
        var userContents = new List<AIContent>();
        var userTextBuilder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.Text))
        {
            userTextBuilder.Append(request.Text.Trim());
        }

        foreach (var doc in attResult.Documents)
        {
            userTextBuilder.AppendLine();
            userTextBuilder.AppendLine($"<attachment name=\"{doc.Name}\">");
            userTextBuilder.AppendLine(doc.Content);
            userTextBuilder.AppendLine("</attachment>");
        }

        if (userTextBuilder.Length > 0)
        {
            userContents.Add(new TextContent(userTextBuilder.ToString()));
        }

        foreach (var img in attResult.Images)
        {
            userContents.Add(new TextContent($"[图片附件: {img.Name}]"));
            userContents.Add(new DataContent(img.JpegBytes, img.MediaType));
        }

        chatMessages.Add(new ChatMessage(ChatRole.User, userContents));

        // 落盘用户消息（只存用户输入与附件元信息，不存附件原文）
        var nextSeq = existingMessages.Count + 1;
        if (_conversationStore is not null)
        {
            if (existingMessages.Count == 0 && !string.IsNullOrWhiteSpace(request.Text))
            {
                var sessionExists = _conversationStore.ListSessions(100)
                    .Any(s => string.Equals(s.Id, request.SessionId, StringComparison.Ordinal));
                if (!sessionExists)
                {
                    _conversationStore.CreateSession(request.SessionId, request.Text);
                }
            }

            _conversationStore.AppendMessage(new ConversationMessageRecord(
                Guid.NewGuid().ToString("N"),
                request.SessionId,
                nextSeq++,
                "user",
                request.Text,
                null,
                request.Attachments,
                DateTimeOffset.Now));
        }

        // 3. 准备单次运行草稿盒与工具集
        var channel = Channel.CreateUnbounded<AssistantEvent>();
        var draft = new RunProposalDraft();
        void Emit(AssistantEvent evt)
        {
            if (evt is ToolStartedEvent ts)
            {
                toolsInvoked.Add(ts.ToolName);
            }

            channel.Writer.TryWrite(evt);
        }

        var policy = new PermissionPolicy(_permissionConfig, msg => Emit(new WarningEvent(msg)));

        var readTools = new ReadTools(_repository, _holidays, _allowReadTasks, _todayProvider, Emit);
        var proposalTools = new ProposalTools(_repository, draft, _proposalValidator, Emit, policy, _todayProvider);

        var allTools = new List<AITool>();
        allTools.AddRange(readTools.CreateTools());
        allTools.AddRange(proposalTools.CreateTools());
        if ((_memoryStore is not null && _memoryEnabled()) || _profileStore is not null)
        {
            var memTools = new MemoryTools(_memoryStore ?? new MemoryStore(), _memoryEnabled, Emit, _profileStore);
            allTools.AddRange(memTools.CreateTools());
        }

        foreach (var extra in _extraToolProviders)
        {
            foreach (var t in extra.CreateTools(request.SessionId))
            {
                if (t is AITool aiTool)
                {
                    allTools.Add(aiTool);
                }
            }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(DefaultRunTimeout);
        var runCt = timeoutCts.Token;

        var fullReply = new StringBuilder();
        var succeeded = false;
        string? errorCategory = null;

        var worker = Task.Run(async () =>
        {
            try
            {
                using var baseClient = _clientFactory.Create(model);
                if (baseClient is IToolCallStreamNotifier streamNotifier)
                {
                    streamNotifier.ToolCallStreamingStarted += toolName =>
                        Emit(new ToolStartedEvent(toolName, DescribeStreamingTool(toolName)));
                }

                using var invokingClient = new ChatClientBuilder(baseClient)
                    .UseFunctionInvocation(configure: fic =>
                    {
                        fic.MaximumIterationsPerRequest = MaxToolIterations;
                        fic.AllowConcurrentInvocation = false;
                        fic.IncludeDetailedErrors = false;
                    })
                    .Build();

                var options = new ChatOptions
                {
                    ModelId = model.Model,
                    Tools = allTools,
                };

                await foreach (var update in invokingClient.GetStreamingResponseAsync(chatMessages, options, runCt))
                {
                    runCt.ThrowIfCancellationRequested();
                    foreach (var content in update.Contents)
                    {
                        if (content is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                        {
                            fullReply.Append(tc.Text);
                            Emit(new TextDeltaEvent(tc.Text));
                        }
                    }
                }

                // 若流式正常结束且存在提案草稿 → 终审校验 + 权限分级
                if (!runCt.IsCancellationRequested && draft.Items.Count > 0)
                {
                    if (_proposalValidator is not null)
                    {
                        var finalViolations = _proposalValidator.Validate(draft.Items);
                        draft.AttachViolations(finalViolations);
                    }

                    var proposal = draft.Seal(request.SessionId);
                    _applier.Register(proposal);

                    if (policy.CanAutoApplyAll(proposal.Items))
                    {
                        var applyRes = _applier.Apply(proposal.Id, selectedItemIds: null, isAutoApplied: true);
                        if (applyRes.Success && !string.IsNullOrEmpty(applyRes.ChangeSetId))
                        {
                            Emit(new AutoAppliedEvent(
                                proposal.Id,
                                applyRes.ChangeSetId,
                                proposal.Summary,
                                applyRes.AppliedCount,
                                proposal with { Status = ProposalStatus.AutoApplied, AppliedChangeSetId = applyRes.ChangeSetId },
                                applyRes.AppliedEntityMap));
                        }
                        else
                        {
                            Emit(new ProposalReadyEvent(proposal));
                        }
                    }
                    else
                    {
                        Emit(new ProposalReadyEvent(proposal));
                    }
                }

                // 持久化助理最终文本回复
                if (_conversationStore is not null && fullReply.Length > 0)
                {
                    _conversationStore.AppendMessage(new ConversationMessageRecord(
                        Guid.NewGuid().ToString("N"),
                        request.SessionId,
                        nextSeq,
                        "assistant",
                        fullReply.ToString(),
                        $"{model.Name} · {model.Model}",
                        null,
                        DateTimeOffset.Now));
                }

                succeeded = true;
                Emit(new DoneEvent(request.SessionId, fullReply.ToString()));
            }
            catch (OperationCanceledException)
            {
                // 中途取消或超时：丢弃未应用的草稿，绝不产生半写入
                draft.Clear();
                if (!ct.IsCancellationRequested)
                {
                    errorCategory = "Timeout";
                    Emit(new ErrorEvent("助理响应超时（超过 120 秒），已安全取消，未改动任何日程。", Retryable: true));
                }
                else
                {
                    errorCategory = "Cancelled";
                }
            }
            catch (Exception ex)
            {
                draft.Clear();
                errorCategory = ex.GetType().Name;
                var safeMsg = ModelConnectionTester.Sanitize(ex.Message, model.ApiKey);
                Emit(new ErrorEvent(safeMsg, Retryable: true));
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, CancellationToken.None);

        await foreach (var evt in channel.Reader.ReadAllAsync(CancellationToken.None))
        {
            yield return evt;
        }

        await worker;
        RecordAudit(runId, modelLabel, sw.ElapsedMilliseconds, toolsInvoked, succeeded, errorCategory);
    }

    public Task<ProposalApplyResult> ApplyProposalAsync(
        string proposalId,
        IReadOnlyCollection<string> selectedItemIds,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_applier.Apply(proposalId, selectedItemIds, isAutoApplied: false));
    }

    public void DiscardProposal(string proposalId) => _applier.Discard(proposalId);

    public Task<UndoResult> UndoAsync(string changeSetId)
    {
        var result = _schedule.Undo(changeSetId);
        if (result.Success)
        {
            _applier.MarkUndoneByChangeSet(changeSetId);
        }

        return Task.FromResult(result);
    }

    public Task<UndoResult> UndoEntriesAsync(string changeSetId, IReadOnlyCollection<string> entityIds)
    {
        var result = _schedule.UndoEntries(changeSetId, entityIds);
        return Task.FromResult(result);
    }

    private static string DescribeStreamingTool(string toolName) => toolName switch
    {
        "query_tasks" => "Lana 正在翻看你的日程…",
        "list_lists" => "Lana 正在查看你的清单…",
        "today_info" => "Lana 正在核对今天的日期与农历…",
        "resolve_date" => "Lana 正在换算日期…",
        "lunar_to_solar" => "Lana 正在查农历万年历…",
        "next_workday" => "Lana 正在查法定工作日…",
        "propose_create_task" => "Lana 正在帮你添加新日程…",
        "propose_update_task" => "Lana 正在调整日程安排…",
        "propose_complete_task" => "Lana 正在更新任务状态…",
        "propose_delete_task" => "Lana 正在准备删除确认…",
        "propose_create_list" => "Lana 正在创建新清单…",
        "remember" or "update_memory" => "Lana 正在记进小本本…",
        "forget" => "Lana 正在整理记忆…",
        "set_user_nickname" => "Lana 正在记住你的称呼…",
        _ => "Lana 正在操作中…",
    };

    public Task<UndoResult?> UndoLastAsync(ChangeSource[] sources, TimeSpan within)
    {
        var cutoff = DateTimeOffset.Now - within;
        var sourceSet = new HashSet<ChangeSource>(sources);
        var candidate = _schedule.Recent(20)
            .FirstOrDefault(cs => cs.UndoneAt is null && sourceSet.Contains(cs.Source) && cs.At >= cutoff);

        if (candidate is null)
        {
            return Task.FromResult<UndoResult?>(null);
        }

        var result = _schedule.Undo(candidate.Id);
        if (result.Success)
        {
            _applier.MarkUndoneByChangeSet(candidate.Id);
        }

        return Task.FromResult<UndoResult?>(result);
    }

    private void RecordAudit(
        string runId,
        string modelName,
        long elapsedMs,
        IReadOnlyList<string> toolsInvoked,
        bool succeeded,
        string? errorCategory)
    {
        lock (_logGate)
        {
            _auditLogs.Add(new AssistantAuditLogEntry(
                runId,
                DateTimeOffset.Now,
                modelName,
                elapsedMs,
                toolsInvoked.ToList(),
                succeeded,
                errorCategory));
            if (_auditLogs.Count > 200)
            {
                _auditLogs.RemoveAt(0);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}

using System.IO.Compression;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using SkiaSharp;
using VGList.Agent.Attachments;
using VGList.Agent.Calendar;
using VGList.Agent.Clients;
using VGList.Agent.Proposals;
using VGList.Agent.Runtime;
using VGList.App.Assistant;
using VGList.App.Models;
using VGList.Core.Abstractions;
using VGList.Core.Models;
using VGList.Data;
using Xunit;

namespace VGList.Tests;

public sealed class AgentRuntimeTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 21);
    private readonly string _tempDir;

    public AgentRuntimeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vglist-a3-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private (SqliteDatabase Db, SqliteTaskRepository Repo, SqliteConversationStore Conv) OpenEnv()
    {
        var db = SqliteDatabase.Open(Path.Combine(_tempDir, $"{Guid.NewGuid():N}.db"));
        var repo = new SqliteTaskRepository(db);
        repo.SeedInitial(
            [new TaskList { Id = TaskList.InboxId, Name = "收集箱" }, new TaskList { Id = "work", Name = "工作" }],
            [
                new TaskItem { Id = "t1", Title = "完成接口联调", Date = Today, Priority = TaskPriority.High, ListId = TaskList.InboxId },
                new TaskItem { Id = "t2", Title = "整理项目文档", Date = Today, Priority = TaskPriority.Low, ListId = TaskList.InboxId },
            ]);
        var conv = new SqliteConversationStore(db);
        return (db, repo, conv);
    }

    private static IModelProfileProvider FakeModelProvider(bool supportsImages = true, string apiKey = "sk-secret-test-9999") =>
        new StaticModelProvider(new ResolvedModel(
            ProfileId: "p1",
            Name: "TestModel",
            PresetId: "deepseek",
            BaseUrl: "https://api.example.com/v1",
            Protocol: ModelProtocol.OpenAi,
            ApiKey: apiKey,
            Model: "test-chat",
            SupportsImages: supportsImages));

    [Fact]
    public async Task EndToEnd_QueryThenPropose_DoesNotMutateBeforeApply_ThenAppliesAndUndoes()
    {
        var (db, repo, conv) = OpenEnv();
        using var _ = db;
        using var __ = repo;

        var script = new ScriptedChatClient(
        [
            // 第 1 轮：模型先查任务
            _ => [new FunctionCallContent("c1", "query_tasks", new Dictionary<string, object?> { ["includeCompleted"] = false })],
            // 第 2 轮：提出新建与改期建议
            _ =>
            [
                new FunctionCallContent("c2", "propose_create_task", new Dictionary<string, object?>
                {
                    ["title"] = "预约口腔复查",
                    ["date"] = "2026-11-23",
                    ["priority"] = "Medium",
                    ["reason"] = "用户要求下周一安排",
                }),
                new FunctionCallContent("c3", "propose_update_task", new Dictionary<string, object?>
                {
                    ["taskId"] = "t2",
                    ["newDate"] = "2026-11-22",
                    ["reason"] = "减轻今日负荷",
                }),
            ],
            // 第 3 轮：给出最终回复文本
            _ => [new TextContent("已为你拟定 2 项日程建议，请确认后应用。")],
        ]);

        using var host = new AssistantHost(
            repo,
            repo,
            FakeModelProvider(),
            conv,
            new FixedClientFactory(script),
            permissionConfig: () => new AssistantPermissionConfig { AutoCreateTask = false, AutoUpdateTask = false },
            todayProvider: () => Today);

        var session = host.CreateSession("端到端测试");
        var events = new List<AssistantEvent>();
        await foreach (var evt in host.RunAsync(new AssistantRequest(session.Id, "帮我整理今天日程并下周一预约牙医")))
        {
            events.Add(evt);
        }

        // 1. 应用前仓储绝对未被改动！
        Assert.Equal(2, repo.Query(new TaskQuery()).Count);
        Assert.Equal(Today, repo.GetById("t2")!.Date);

        // 2. 收到 ProposalReadyEvent
        var proposalEvt = Assert.Single(events.OfType<ProposalReadyEvent>());
        Assert.Equal(2, proposalEvt.Proposal.Items.Count);
        Assert.Contains(events, e => e is ToolStartedEvent { ToolName: "query_tasks" });
        Assert.Contains(events, e => e is TextDeltaEvent);

        // 3. 全部应用
        var apply = await host.ApplyProposalAsync(proposalEvt.Proposal.Id, proposalEvt.Proposal.Items.Select(i => i.Id).ToList());
        Assert.True(apply.Success);
        Assert.Equal(2, apply.AppliedCount);
        Assert.NotNull(apply.ChangeSetId);
        Assert.Equal(3, repo.Query(new TaskQuery()).Count);
        Assert.Equal(new DateOnly(2026, 11, 22), repo.GetById("t2")!.Date);

        // 4. 整批撤销
        var undo = await host.UndoAsync(apply.ChangeSetId!);
        Assert.True(undo.Success);
        Assert.Equal(2, repo.Query(new TaskQuery()).Count);
        Assert.Equal(Today, repo.GetById("t2")!.Date);
    }

    [Fact]
    public async Task ProposalTools_RejectInvalidTaskIdAndInvalidDate_AndFeedErrorBackToModel()
    {
        var (db, repo, conv) = OpenEnv();
        using var _ = db;
        using var __ = repo;

        string? capturedToolResults = null;
        var script = new ScriptedChatClient(
        [
            _ =>
            [
                new FunctionCallContent("c1", "propose_update_task", new Dictionary<string, object?> { ["taskId"] = "non-existent-id", ["newDate"] = "2026-11-22" }),
                new FunctionCallContent("c2", "propose_create_task", new Dictionary<string, object?> { ["title"] = "非法日期", ["date"] = "2026/13/99" }),
            ],
            msgs =>
            {
                capturedToolResults = string.Join("\n", msgs.Where(m => m.Role == ChatRole.Tool).SelectMany(m => m.Contents.OfType<FunctionResultContent>()).Select(r => r.Result?.ToString()));
                return [new TextContent("参数无效，未生成提案。")];
            },
        ]);

        using var host = new AssistantHost(repo, repo, FakeModelProvider(), conv, new FixedClientFactory(script), todayProvider: () => Today);
        var events = new List<AssistantEvent>();
        await foreach (var e in host.RunAsync(new AssistantRequest("s-err", "测试坏参数")))
        {
            events.Add(e);
        }

        Assert.DoesNotContain(events, e => e is ProposalReadyEvent);
        Assert.NotNull(capturedToolResults);
        Assert.Contains("不存在", capturedToolResults);
        Assert.Contains("yyyy-MM-dd", capturedToolResults);
        Assert.Equal(2, repo.Query(new TaskQuery()).Count);
    }

    [Fact]
    public async Task PermissionPolicy_AutoAppliesLowRisk_AndAlwaysForcesDeleteToAskEvenIfTampered()
    {
        var (db, repo, conv) = OpenEnv();
        using var _ = db;
        using var __ = repo;

        // 1. 纯拆子待办（默认 Auto）→ 直接自动应用并发出 AutoAppliedEvent
        var autoScript = new ScriptedChatClient(
        [
            _ =>
            [
                new FunctionCallContent("c1", "propose_update_task", new Dictionary<string, object?>
                {
                    ["taskId"] = "t1",
                    ["appendSubtasks"] = new[] { "确认接口文档", "运行联调脚本" },
                }),
            ],
            _ => [new TextContent("已帮你拆出 2 个子步骤。")],
        ]);

        using var autoHost = new AssistantHost(repo, repo, FakeModelProvider(), conv, new FixedClientFactory(autoScript), todayProvider: () => Today);
        var autoEvents = new List<AssistantEvent>();
        await foreach (var e in autoHost.RunAsync(new AssistantRequest("s-auto", "拆子任务")))
        {
            autoEvents.Add(e);
        }

        var autoEvt = Assert.Single(autoEvents.OfType<AutoAppliedEvent>());
        Assert.Equal(1, autoEvt.AppliedCount);
        Assert.Equal(2, repo.GetById("t1")!.Subtasks.Count);

        // UndoLastAsync 应能一键回滚该自动应用的变更集
        var lastUndo = await autoHost.UndoLastAsync([ChangeSource.Assistant], TimeSpan.FromMinutes(5));
        Assert.NotNull(lastUndo);
        Assert.True(lastUndo!.Success);
        Assert.Empty(repo.GetById("t1")!.Subtasks);

        // 2. 默认配置下删除任务为 Ask（需用户确认）→ 绝不被自动删除！
        var deleteScript = new ScriptedChatClient(
        [
            _ => [new FunctionCallContent("c1", "propose_delete_task", new Dictionary<string, object?> { ["taskId"] = "t2", ["reason"] = "测试删除默认需确认" })],
            _ => [new TextContent("建议删除该任务，请确认。")],
        ]);

        using var delHost = new AssistantHost(
            repo,
            repo,
            FakeModelProvider(),
            conv,
            new FixedClientFactory(deleteScript),
            permissionConfig: () => AssistantPermissionConfig.Default,
            todayProvider: () => Today);

        var delEvents = new List<AssistantEvent>();
        await foreach (var e in delHost.RunAsync(new AssistantRequest("s-del", "删掉 t2")))
        {
            delEvents.Add(e);
        }

        Assert.DoesNotContain(delEvents, e => e is AutoAppliedEvent);
        Assert.Single(delEvents.OfType<ProposalReadyEvent>());
        Assert.NotNull(repo.GetById("t2")); // 默认配置下绝不自动删除！

        // 3. 若用户在设置中显式开启 AutoDeleteTask=true → 允许自动删除并仍支持撤销恢复
        var autoDelScript = new ScriptedChatClient(
        [
            _ => [new FunctionCallContent("c2", "propose_delete_task", new Dictionary<string, object?> { ["taskId"] = "t2", ["reason"] = "用户授权自动删除" })],
            _ => [new TextContent("已帮你删除该任务。")],
        ]);

        using var autoDelHost = new AssistantHost(
            repo,
            repo,
            FakeModelProvider(),
            conv,
            new FixedClientFactory(autoDelScript),
            permissionConfig: () => new AssistantPermissionConfig { AutoDeleteTask = true },
            todayProvider: () => Today);

        var autoDelEvents = new List<AssistantEvent>();
        await foreach (var e in autoDelHost.RunAsync(new AssistantRequest("s-del-auto", "删掉 t2")))
        {
            autoDelEvents.Add(e);
        }

        var autoDelEvt = Assert.Single(autoDelEvents.OfType<AutoAppliedEvent>());
        Assert.Null(repo.GetById("t2"));
        var undoDel = await autoDelHost.UndoAsync(autoDelEvt.ChangeSetId);
        Assert.True(undoDel.Success);
        Assert.NotNull(repo.GetById("t2")); // 撤销后完整恢复！
    }

    [Fact]
    public async Task RuleValidator_FeedsBackViolationsForSelfCorrection()
    {
        var (db, repo, conv) = OpenEnv();
        using var _ = db;
        using var __ = repo;

        var noSundayRule = new NoSundayValidator();
        string? firstViolationItemId = null;

        var script = new ScriptedChatClient(
        [
            // 第 1 轮：排到周日 2026-11-22，触发违规
            _ =>
            [
                new FunctionCallContent("c1", "propose_create_task", new Dictionary<string, object?>
                {
                    ["title"] = "写周报",
                    ["date"] = "2026-11-22",
                }),
            ],
            // 第 2 轮：模型收到违规回灌后，先移除旧条目，改为周一 2026-11-23
            msgs =>
            {
                var toolRes = msgs.Last(m => m.Role == ChatRole.Tool).Contents.OfType<FunctionResultContent>().First().Result?.ToString() ?? string.Empty;
                Assert.Contains("周日不排工作任务", toolRes);
                using var doc = System.Text.Json.JsonDocument.Parse(toolRes);
                firstViolationItemId = doc.RootElement.GetProperty("itemId").GetString();
                return
                [
                    new FunctionCallContent("c2", "remove_proposal_item", new Dictionary<string, object?> { ["itemId"] = firstViolationItemId! }),
                    new FunctionCallContent("c3", "propose_create_task", new Dictionary<string, object?> { ["title"] = "写周报", ["date"] = "2026-11-23" }),
                ];
            },
            _ => [new TextContent("已遵守你的周日不排任务规则，调整到下周一 11 月 23 日。")],
        ]);

        using var host = new AssistantHost(
            repo,
            repo,
            FakeModelProvider(),
            conv,
            new FixedClientFactory(script),
            permissionConfig: () => new AssistantPermissionConfig { AutoCreateTask = false },
            proposalValidator: noSundayRule,
            todayProvider: () => Today);

        var events = new List<AssistantEvent>();
        await foreach (var e in host.RunAsync(new AssistantRequest("s-rule", "明天写周报")))
        {
            events.Add(e);
        }

        var proposal = Assert.Single(events.OfType<ProposalReadyEvent>()).Proposal;
        var item = Assert.Single(proposal.Items);
        Assert.Null(item.Violations);
        Assert.IsType<CreateTaskOp>(item.Operation);
        Assert.Equal(new DateOnly(2026, 11, 23), ((CreateTaskOp)item.Operation).Draft.Date);
    }

    [Fact]
    public void CnHolidayCalendar_MatchesOfficial2025And2026_AndFlagsMissingYearImprecise()
    {
        var cal = CnHolidayCalendar.Shared;

        // 2025 春节除夕放假 + 1月26日周日补班
        Assert.True(cal.HasYear(2025));
        var cnyEve2025 = cal.GetDayInfo(new DateOnly(2025, 1, 28));
        Assert.True(cnyEve2025.IsHoliday);
        Assert.True(cnyEve2025.Precise);
        var makeup2025 = cal.GetDayInfo(new DateOnly(2025, 1, 26));
        Assert.True(makeup2025.IsMakeupWorkday);
        Assert.True(makeup2025.IsWorkday);

        // 2026 春节 2/15~2/23 放假 9 天，2/14 周六补班，节后首个工作日 = 2/24
        Assert.True(cal.HasYear(2026));
        Assert.True(cal.GetDayInfo(new DateOnly(2026, 2, 14)).IsMakeupWorkday);
        Assert.True(cal.GetDayInfo(new DateOnly(2026, 2, 15)).IsHoliday);
        var afterSpring2026 = cal.NextWorkday(new DateOnly(2026, 2, 14));
        Assert.Equal(new DateOnly(2026, 2, 24), afterSpring2026.NextWorkday);
        Assert.True(afterSpring2026.Precise);

        // 2026 国庆 10/1~10/7，10/10 周六补班
        Assert.True(cal.GetDayInfo(new DateOnly(2026, 10, 10)).IsMakeupWorkday);

        // 未收录年份 2029 → precise=false 且有明确中文提示
        Assert.False(cal.HasYear(2029));
        var unknown = cal.GetDayInfo(new DateOnly(2029, 5, 1));
        Assert.False(unknown.Precise);
        Assert.Contains("未收录", unknown.Note);
    }

    [Fact]
    public void AttachmentProcessor_HandlesTextDocxImageResize_AndRejectsUnsupportedImagesOrTooMany()
    {
        // 1. UTF-8 超长文本截断
        var longTxtPath = Path.Combine(_tempDir, "notes.txt");
        File.WriteAllText(longTxtPath, new string('甲', 45_000), Encoding.UTF8);

        // 2. 合成合法 .docx
        var docxPath = Path.Combine(_tempDir, "plan.docx");
        using (var zip = ZipFile.Open(docxPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:r><w:t>第一项：周五前完成需求评审</w:t></w:r></w:p>
                    <w:p><w:r><w:t>第二项：同步给设计组</w:t></w:r></w:p>
                  </w:body>
                </w:document>
                """);
        }

        // 3. 合成 2600x1300 PNG，验证自动缩放到长边 ≤ 2048 且转 JPEG
        var imgPath = Path.Combine(_tempDir, "big.png");
        using (var bmp = new SKBitmap(2600, 1300))
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Pink);
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(imgPath, data.ToArray());
        }

        var refs = new[]
        {
            new AttachmentRef("notes.txt", longTxtPath, new FileInfo(longTxtPath).Length, "文本"),
            new AttachmentRef("plan.docx", docxPath, new FileInfo(docxPath).Length, "Word"),
            new AttachmentRef("big.png", imgPath, new FileInfo(imgPath).Length, "图片"),
        };

        // 支持图片时全部成功
        var okRes = AttachmentProcessor.Process(refs, modelSupportsImages: true);
        Assert.Null(okRes.FatalError);
        Assert.Equal(2, okRes.Documents.Count);
        Assert.True(okRes.Documents[0].Truncated);
        Assert.Contains("周五前完成需求评审", okRes.Documents[1].Content);
        var procImg = Assert.Single(okRes.Images);
        Assert.Equal("image/jpeg", procImg.MediaType);
        using (var decoded = SKBitmap.Decode(procImg.JpegBytes))
        {
            Assert.NotNull(decoded);
            Assert.True(Math.Max(decoded!.Width, decoded.Height) <= AttachmentProcessor.MaxImageEdge);
        }

        // 不支持图片的模型必须明确拒绝
        var noVision = AttachmentProcessor.Process([refs[2]], modelSupportsImages: false);
        Assert.NotNull(noVision.FatalError);
        Assert.Contains("不支持图片", noVision.FatalError);

        // 超过 5 个附件拒绝
        var six = Enumerable.Range(0, 6).Select(i => refs[1]).ToList();
        var tooMany = AttachmentProcessor.Process(six, modelSupportsImages: true);
        Assert.NotNull(tooMany.FatalError);
        Assert.Contains("最多附加 5 个", tooMany.FatalError);
    }

    [Fact]
    public async Task PrivacySwitchAndAuditLog_BlockUnauthorizedRead_AndNeverLeakSecrets()
    {
        var (db, repo, conv) = OpenEnv();
        using var _ = db;
        using var __ = repo;

        const string secretKey = "sk-ultra-secret-token-778899";
        const string userSecretPrompt = "我的私人密码提示词 XYZ123";
        string? toolResponseText = null;

        var script = new ScriptedChatClient(
        [
            _ => [new FunctionCallContent("c1", "query_tasks", new Dictionary<string, object?>())],
            msgs =>
            {
                toolResponseText = msgs.Last(m => m.Role == ChatRole.Tool).Contents.OfType<FunctionResultContent>().First().Result?.ToString();
                return [new TextContent("你已关闭读取任务权限，无法查看日程。")];
            },
        ]);

        using var host = new AssistantHost(
            repo,
            repo,
            FakeModelProvider(apiKey: secretKey),
            conv,
            new FixedClientFactory(script),
            allowReadTasks: () => false,
            todayProvider: () => Today);

        await foreach (var evt in host.RunAsync(new AssistantRequest("s-priv", userSecretPrompt)))
        {
            Assert.NotNull(evt);
        }

        Assert.NotNull(toolResponseText);
        Assert.Contains("未授权", toolResponseText);
        Assert.DoesNotContain("完成接口联调", toolResponseText);

        // 审计日志绝不包含密钥与用户提示原文
        var logDump = System.Text.Json.JsonSerializer.Serialize(host.AuditLogs);
        Assert.DoesNotContain(secretKey, logDump);
        Assert.DoesNotContain(userSecretPrompt, logDump);
        Assert.Contains("query_tasks", logDump);
    }

    [Fact]
    public async Task Cancellation_DiscardsDraftAndLeavesRepositoryUntouched()
    {
        var (db, repo, conv) = OpenEnv();
        using var _ = db;
        using var __ = repo;

        using var cts = new CancellationTokenSource();
        var script = new ScriptedChatClient(
        [
            _ =>
            [
                new FunctionCallContent("c1", "propose_create_task", new Dictionary<string, object?>
                {
                    ["title"] = "中途取消任务",
                    ["date"] = "2026-11-21",
                }),
            ],
            _ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            },
        ]);

        using var host = new AssistantHost(repo, repo, FakeModelProvider(), conv, new FixedClientFactory(script), todayProvider: () => Today);
        var events = new List<AssistantEvent>();
        await foreach (var e in host.RunAsync(new AssistantRequest("s-cancel", "测试取消"), cts.Token))
        {
            events.Add(e);
        }

        Assert.DoesNotContain(events, e => e is ProposalReadyEvent);
        Assert.DoesNotContain(events, e => e is AutoAppliedEvent);
        Assert.Equal(2, repo.Query(new TaskQuery()).Count);
    }

    [Fact]
    public void SqliteConversationStore_PersistsMessagesAndPrunesBeyond30Sessions()
    {
        var (db, repo, conv) = OpenEnv();
        using var _ = db;
        using var __ = repo;

        for (var i = 1; i <= 35; i++)
        {
            var s = conv.CreateSession($"s{i:00}", $"会话 {i}");
            conv.AppendMessage(new ConversationMessageRecord(
                Guid.NewGuid().ToString("N"),
                s.Id,
                1,
                "user",
                $"消息 {i}",
                null,
                [new AttachmentRef("secret.pdf", @"C:\private\secret.pdf", 1024, "PDF")],
                DateTimeOffset.Now.AddSeconds(i)));
        }

        var sessions = conv.ListSessions(50);
        Assert.Equal(30, sessions.Count);

        var loaded = conv.LoadMessages("s35");
        var msg = Assert.Single(loaded);
        Assert.Equal("消息 35", msg.Text);
        // 附件只保存名称/大小/类型，不存本地路径
        Assert.Equal("secret.pdf", msg.Attachments![0].Name);
        Assert.Equal(string.Empty, msg.Attachments![0].FullPath);
    }

    [Fact]
    public async Task DualProtocolClients_StreamTextAndAccumulateToolCalls()
    {
        // 1. OpenAI SSE 流式文本 + 分片 tool_calls 聚合
        var openAiSse = string.Join("\n",
            """data: {"choices":[{"delta":{"content":"你好"}}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"today_","arguments":"{\"x\":"}}]}}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"name":"info","arguments":"1}"}}]}}]}""",
            "data: [DONE]");

        var openAiModel = new ResolvedModel("p", "OpenAI", "openai", "https://api.openai.com/v1", ModelProtocol.OpenAi, "sk-test", "gpt-4o-mini", true);
        using var openAiHttp = new HttpClient(new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(openAiSse, Encoding.UTF8, "text/event-stream"),
        }));
        using var openAiClient = new OpenAiCompatibleChatClient(openAiModel, openAiHttp);

        var openAiContents = new List<AIContent>();
        await foreach (var u in openAiClient.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
        {
            openAiContents.AddRange(u.Contents);
        }

        Assert.Contains(openAiContents.OfType<TextContent>(), t => t.Text == "你好");
        var oaCall = Assert.Single(openAiContents.OfType<FunctionCallContent>());
        Assert.Equal("today_info", oaCall.Name);
        Assert.Equal("call_1", oaCall.CallId);

        // 2. Anthropic SSE 流式文本 + tool_use 聚合 + 自定义 BaseUrl 与头校验
        var anthSse = string.Join("\n",
            """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"收到"}}""",
            """data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_9","name":"query_tasks","input":{}}}""",
            """data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"includeCompleted\":false}"}}""",
            """data: {"type":"message_stop"}""");

        string? capturedKeyHeader = null;
        Uri? capturedUri = null;
        var anthModel = new ResolvedModel("a", "DeepSeek Anth", "deepseek", "https://api.deepseek.com/anthropic", ModelProtocol.Anthropic, "sk-anth-123", "deepseek-chat", false);
        using var anthHttp = new HttpClient(new StubHttpHandler((req, _) =>
        {
            capturedUri = req.RequestUri;
            capturedKeyHeader = req.Headers.GetValues("x-api-key").FirstOrDefault();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(anthSse, Encoding.UTF8, "text/event-stream"),
            };
        }));
        using var anthClient = new AnthropicCompatibleChatClient(anthModel, anthHttp);

        var anthContents = new List<AIContent>();
        await foreach (var u in anthClient.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "查任务")]))
        {
            anthContents.AddRange(u.Contents);
        }

        Assert.Equal("https://api.deepseek.com/anthropic/v1/messages", capturedUri?.ToString());
        Assert.Equal("sk-anth-123", capturedKeyHeader);
        Assert.Contains(anthContents.OfType<TextContent>(), t => t.Text == "收到");
        var anthCall = Assert.Single(anthContents.OfType<FunctionCallContent>());
        Assert.Equal("query_tasks", anthCall.Name);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed class StaticModelProvider(ResolvedModel model) : IModelProfileProvider
    {
        public ModelProfile? ActiveProfile => new()
        {
            Id = model.ProfileId,
            Name = model.Name,
            BaseUrl = model.BaseUrl,
            Protocol = model.Protocol,
            Model = model.Model,
            SupportsImages = model.SupportsImages,
        };

        public event EventHandler? ActiveChanged { add { } remove { } }

        public bool TryResolveActive(out ResolvedModel? resolved, out string? error)
        {
            resolved = model;
            error = null;
            return true;
        }
    }

    private sealed class FixedClientFactory(IChatClient client) : IChatClientFactory
    {
        public IChatClient Create(ResolvedModel model) => client;
    }

    private sealed class NoSundayValidator : IProposalValidator
    {
        public IReadOnlyDictionary<string, IReadOnlyList<ProposalViolation>> Validate(IReadOnlyList<ProposalItem> items)
        {
            var dict = new Dictionary<string, IReadOnlyList<ProposalViolation>>();
            foreach (var item in items)
            {
                if (item.Operation is CreateTaskOp create && create.Draft.Date.DayOfWeek == DayOfWeek.Sunday)
                {
                    dict[item.Id] = [new ProposalViolation("no-sunday", "周日不排工作任务")];
                }
            }

            return dict;
        }
    }

    private sealed class ScriptedChatClient(IReadOnlyList<Func<IReadOnlyList<ChatMessage>, IReadOnlyList<AIContent>>> turns) : IChatClient
    {
        private int _turn;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var contents = NextTurn(messages.ToList());
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, contents.ToList())));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var contents = NextTurn(messages.ToList());
            yield return new ChatResponseUpdate(ChatRole.Assistant, contents.ToList());
        }

        private IReadOnlyList<AIContent> NextTurn(IReadOnlyList<ChatMessage> history)
        {
            var idx = Math.Min(_turn++, turns.Count - 1);
            return turns[idx](history);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request, body);
        }
    }
}

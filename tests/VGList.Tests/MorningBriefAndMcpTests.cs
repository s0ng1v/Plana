using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VGList.App.Assistant;
using VGList.App.Mcp;
using VGList.App.Services;
using VGList.App.Settings;
using VGList.Core.Abstractions;
using VGList.Core.Models;
using VGList.Desktop.Main;
using VGList.Desktop.Main.Settings;
using VGList.Desktop.Main.Tasks;
using VGList.Desktop.Services;
using VGList.Platform.Win.Win32;
using VGList.Widgets.Today;
using Xunit;

namespace VGList.Tests;

public sealed class MorningBriefAndMcpTests
{
    private static readonly DateOnly Today = new(2026, 11, 21);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // ===== A6③ 早安提示 =====

    [Fact]
    public void MorningBriefService_OnlyTriggersWhenOverdueAndRespectsDismissAndToggle()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "vglist-brief-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var repo = new InMemoryTaskRepository();
            var service = new MorningBriefService(repo, tmpDir);

            // 无逾期任务 → 不出现
            repo.Create(new TaskDraft { Title = "今天任务", Date = Today });
            Assert.Null(service.Evaluate(Today, enabled: true));

            // 昨天未完成 1 项 → 出现「昨天还有 1 项未完成」
            var yestTask = repo.Create(new TaskDraft { Title = "昨天未交报告", Date = Today.AddDays(-1) });
            var brief1 = service.Evaluate(Today, enabled: true);
            Assert.NotNull(brief1);
            Assert.Equal(1, brief1!.YesterdayUnfinishedCount);
            Assert.Equal(1, brief1.TotalOverdueCount);
            Assert.Contains("昨天还有 1 项未完成", brief1.BannerText);

            // 跨多天逾期 → 改为「有 2 项逾期未完成」
            repo.Create(new TaskDraft { Title = "三天前遗留", Date = Today.AddDays(-3) });
            var brief2 = service.Evaluate(Today, enabled: true);
            Assert.NotNull(brief2);
            Assert.Equal(2, brief2!.TotalOverdueCount);
            Assert.Contains("有 2 项逾期未完成", brief2.BannerText);

            // 开关关闭 → 不出现
            Assert.Null(service.Evaluate(Today, enabled: false));

            // 今日关闭 → 当天不再提示，次日恢复
            service.DismissToday(Today);
            Assert.Null(service.Evaluate(Today, enabled: true));
            Assert.NotNull(service.Evaluate(Today.AddDays(1), enabled: true));

            // 今日小组件 ViewModel 联动：完成后自动消失、点击触发事件
            var service2 = new MorningBriefService(repo, Path.Combine(tmpDir, "w2"));
            using var widgetVm = new TodayWidgetViewModel(repo, Today, service2, () => true);
            Assert.True(widgetVm.HasMorningBrief);

            MorningBriefInfo? opened = null;
            widgetVm.MorningBriefActivated += (_, info) => opened = info;
            widgetVm.OpenMorningBrief();
            Assert.NotNull(opened);
            Assert.False(widgetVm.HasMorningBrief);
            Assert.True(service2.IsDismissedFor(Today));
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void TasksPage_ShowsMorningBriefFallbackOnlyWhenTodayWidgetHidden()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "vglist-brief-fb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var repo = new InMemoryTaskRepository();
            repo.Create(new TaskDraft { Title = "昨天待办", Date = Today.AddDays(-1) });
            var briefService = new MorningBriefService(repo, tmpDir);

            var showWidget = true;
            using var vm = new TasksPageViewModel(
                repo,
                new ToastService(),
                Today,
                briefService: briefService,
                shouldShowFallbackBrief: () => !showWidget);

            // 小组件可见时任务页不重复展示
            Assert.False(vm.ShowMorningBrief);

            // 小组件隐藏后在「今天」视图回退展示
            showWidget = false;
            vm.RefreshMorningBrief(Today);
            Assert.True(vm.ShowMorningBrief);

            // 切到「收集箱」隐藏，切回「今天」恢复
            vm.SelectSection(MainSection.Inbox);
            Assert.False(vm.ShowMorningBrief);
            vm.SelectSection(MainSection.Today);
            Assert.True(vm.ShowMorningBrief);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    // ===== A7 外部 AI（本地 MCP）核心协议、权限、确认队列、限流与隐私 =====

    [Fact]
    public async Task ExternalAccessBridge_DefaultOff_ReadOnly_Whitelist_RateLimit_AndPrivacyAudit()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "vglist-mcp-core-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var repo = new InMemoryTaskRepository();
            var settingsStore = new SettingsStore(Path.Combine(tmpDir, "settings.json"));
            var coordinator = new WidgetCoordinator(repo, settingsStore, Path.Combine(tmpDir, "ast"), Path.Combine(tmpDir, "mcp"));

            // 1. 默认关闭，不监听命名管道，且主进程未加载 VGList.Mcp 程序集
            Assert.False(coordinator.MainSettings.McpEnabled);
            Assert.False(coordinator.IsMcpListening);
            Assert.DoesNotContain(
                AppDomain.CurrentDomain.GetAssemblies(),
                a => string.Equals(a.GetName().Name, "VGList.Mcp", StringComparison.OrdinalIgnoreCase));

            var audit = new McpAuditLog(Path.Combine(tmpDir, "audit"));
            var readOnly = false;
            using var bridge = new ExternalAccessBridge(
                repo,
                repo,
                () => readOnly,
                todayProvider: () => Today,
                auditLog: audit);

            // 2. 协议版本校验
            var verMismatch = await bridge.HandleRequestAsync(new McpPipeRequest(99, "1", "Claude", "get_today", null));
            Assert.False(verMismatch.Ok);
            Assert.Equal("version_mismatch", verMismatch.Error!.Code);

            // 3. 白名单校验：绝不暴露记忆/画像/规则/API Key
            foreach (var forbidden in new[] { "read_memory", "get_profile", "get_rules", "get_api_key", "list_conversations" })
            {
                var denied = await bridge.HandleRequestAsync(new McpPipeRequest(1, "u", "Claude", forbidden, null));
                Assert.False(denied.Ok);
                Assert.Equal("unknown_tool", denied.Error!.Code);
            }

            // 4. 正常创建任务并校验隐私审计日志不含任务标题或正文
            const string secretTitle = "绝密并购合同审阅-SECRET-9988";
            const string secretContent = "不要把这段绝密描述写进审计日志";
            var createArgs = JsonDocument.Parse($$"""
                {
                  "tasks": [
                    { "title": "{{secretTitle}}", "date": "2026-11-22", "priority": "High", "content": "{{secretContent}}" }
                  ]
                }
                """).RootElement;

            McpAppliedEventArgs? appliedEvt = null;
            bridge.Applied += (_, e) => appliedEvt = e;

            var createResp = await bridge.HandleRequestAsync(new McpPipeRequest(1, "c1", "Claude", "create_tasks", createArgs));
            Assert.True(createResp.Ok);
            Assert.NotNull(appliedEvt);
            Assert.Contains("Claude", appliedEvt!.Label);
            Assert.Single(repo.Query(new TaskQuery()));

            // 审计日志存在，且绝不含任何任务标题或描述文字
            var rawAudit = File.ReadAllText(audit.FilePath);
            Assert.DoesNotContain(secretTitle, rawAudit, StringComparison.Ordinal);
            Assert.DoesNotContain(secretContent, rawAudit, StringComparison.Ordinal);
            Assert.Contains("create_tasks", rawAudit, StringComparison.Ordinal);

            // 撤销外部 AI 变更集
            var undoRes = repo.Undo(appliedEvt.ChangeSetId);
            Assert.True(undoRes.Success);
            Assert.Empty(repo.Query(new TaskQuery()));

            // 5. 只读模式拦截全部写操作，放行只读操作
            readOnly = true;
            var roWrite = await bridge.HandleRequestAsync(new McpPipeRequest(1, "ro1", "Cursor", "create_tasks", createArgs));
            Assert.False(roWrite.Ok);
            Assert.Equal("read_only", roWrite.Error!.Code);

            var roRead = await bridge.HandleRequestAsync(new McpPipeRequest(1, "ro2", "Cursor", "cn_calendar_info",
                JsonDocument.Parse("""{"date":"2026-11-21"}""").RootElement));
            Assert.True(roRead.Ok);

            // 6. 写入限流：同客户端 60 次/分钟放行，第 61 次被拒
            readOnly = false;
            var simpleCreate = JsonDocument.Parse("""{"tasks":[{"title":"限流压测","date":"2026-11-21"}]}""").RootElement;
            for (var i = 0; i < ExternalAccessBridge.MaxWritesPerMinute; i++)
            {
                var r = await bridge.HandleRequestAsync(new McpPipeRequest(1, $"rl-{i}", "SpamBot", "create_tasks", simpleCreate));
                Assert.True(r.Ok, $"第 {i + 1} 次应成功，实际失败：{r.Error?.Message}");
            }

            var rateLimited = await bridge.HandleRequestAsync(new McpPipeRequest(1, "rl-61", "SpamBot", "create_tasks", simpleCreate));
            Assert.False(rateLimited.Ok);
            Assert.Equal("rate_limited", rateLimited.Error!.Code);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExternalAccessBridge_DeleteAndBulkReschedule_RequireConfirmationWithApproveDenyAndTimeout()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "vglist-mcp-confirm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var repo = new InMemoryTaskRepository();
            var tasks = Enumerable.Range(1, 6)
                .Select(i => repo.Create(new TaskDraft { Title = $"任务{i}", Date = Today }))
                .ToList();

            var queue = new McpPendingConfirmationQueue();
            using var bridge = new ExternalAccessBridge(
                repo,
                repo,
                () => false,
                todayProvider: () => Today,
                auditLog: new McpAuditLog(tmpDir),
                confirmationQueue: queue,
                confirmationTimeout: TimeSpan.FromMilliseconds(300));

            // 1. 删除 → 用户拒绝 → 任务保留
            var delArgs = JsonDocument.Parse($$"""{"taskIds":["{{tasks[0].Id}}"]}""").RootElement;
            var denyTask = bridge.HandleRequestAsync(new McpPipeRequest(1, "d1", "Claude", "delete_tasks", delArgs));
            var pending1 = await WaitForSingleConfirmationAsync(queue);
            Assert.Equal("Claude", pending1.ClientName);
            queue.Resolve(pending1.Id, approved: false);
            var denyResp = await denyTask;
            Assert.False(denyResp.Ok);
            Assert.Equal("user_denied", denyResp.Error!.Code);
            Assert.NotNull(repo.GetById(tasks[0].Id));

            // 2. 删除 → 超时 → 自动拒绝，任务保留
            var timeoutResp = await bridge.HandleRequestAsync(new McpPipeRequest(1, "d2", "Claude", "delete_tasks", delArgs));
            Assert.False(timeoutResp.Ok);
            Assert.Equal("confirmation_timeout", timeoutResp.Error!.Code);
            Assert.NotNull(repo.GetById(tasks[0].Id));
            Assert.Equal(0, queue.Count);

            // 3. 删除 → 用户允许 → 删除成功且可撤销
            var approveTask = bridge.HandleRequestAsync(new McpPipeRequest(1, "d3", "Claude", "delete_tasks", delArgs));
            var pending3 = await WaitForSingleConfirmationAsync(queue);
            queue.Resolve(pending3.Id, approved: true);
            var approveResp = await approveTask;
            Assert.True(approveResp.Ok);
            Assert.Null(repo.GetById(tasks[0].Id));

            // 恢复 task[0] 以测试 6 项批量改期
            var csId = JsonSerializer.SerializeToNode(approveResp.Result, JsonOpts)!["changeSetId"]!.GetValue<string>();
            Assert.True(repo.Undo(csId).Success);
            Assert.NotNull(repo.GetById(tasks[0].Id));

            // 4. 改期 <= 5 项：直接执行无需确认
            var smallResched = JsonDocument.Parse($$"""
                {"items":[{"taskId":"{{tasks[0].Id}}","newDate":"2026-11-25"}]}
                """).RootElement;
            var smallResp = await bridge.HandleRequestAsync(new McpPipeRequest(1, "r1", "Cursor", "reschedule_tasks", smallResched));
            Assert.True(smallResp.Ok);
            Assert.Equal(new DateOnly(2026, 11, 25), repo.GetById(tasks[0].Id)!.Date);

            // 5. 改期 6 项（>5）：必须进入确认队列
            var bigItems = string.Join(",", tasks.Select(t => $$"""{"taskId":"{{t.Id}}","newDate":"2026-11-28"}"""));
            var bigArgs = JsonDocument.Parse($$"""{"items":[{{bigItems}}]}""").RootElement;
            var bigTask = bridge.HandleRequestAsync(new McpPipeRequest(1, "r2", "Cursor", "reschedule_tasks", bigArgs));
            var pendingBig = await WaitForSingleConfirmationAsync(queue);
            Assert.Equal("reschedule_tasks", pendingBig.Tool);
            Assert.Equal(6, pendingBig.Items.Count);
            queue.Resolve(pendingBig.Id, approved: true);
            var bigResp = await bigTask;
            Assert.True(bigResp.Ok);
            Assert.All(tasks, t => Assert.Equal(new DateOnly(2026, 11, 28), repo.GetById(t.Id)!.Date));
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public async Task McpServerExe_EndToEndOverStdioAndCurrentUserNamedPipe()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "vglist-mcp-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var pipeName = "plana-mcp-test-" + Guid.NewGuid().ToString("N")[..10];
        try
        {
            // 找到与测试同构建配置的 VGList.Mcp.exe
            var testBin = AppContext.BaseDirectory;
            var config = testBin.Contains("\\Release\\", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
            var repoRoot = Path.GetFullPath(Path.Combine(testBin, "..", "..", "..", "..", ".."));
            var mcpExe = Path.Combine(repoRoot, "src", "VGList.Desktop", "bin", config, "net10.0-windows", "VGList.Mcp.exe");
            Assert.True(File.Exists(mcpExe), $"未找到 VGList.Mcp.exe：{mcpExe}");

            var repo = new InMemoryTaskRepository();
            using var bridge = new ExternalAccessBridge(
                repo,
                repo,
                () => false,
                pipeName: pipeName,
                serverPipeFactory: name => WindowsMcpPlatform.CreateCurrentUserOnlyServerPipe(name),
                todayProvider: () => Today,
                auditLog: new McpAuditLog(tmpDir));

            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = mcpExe,
                    Arguments = $"--pipe {pipeName}",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardInputEncoding = new UTF8Encoding(false),
                    StandardOutputEncoding = new UTF8Encoding(false),
                },
            };

            Assert.True(proc.Start());
            try
            {
                // 1. MCP 握手 initialize
                await proc.StandardInput.WriteLineAsync(
                    """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","clientInfo":{"name":"Claude Desktop","version":"1.0"}}}""");
                var initResp = JsonNode.Parse((await proc.StandardOutput.ReadLineAsync())!)!;
                Assert.Equal("plana", initResp["result"]!["serverInfo"]!["name"]!.GetValue<string>());

                // 2. tools/list → 精确 10 个任务/日历工具，绝不含隐私工具
                await proc.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
                var listResp = JsonNode.Parse((await proc.StandardOutput.ReadLineAsync())!)!;
                var tools = listResp["result"]!["tools"]!.AsArray().Select(n => n!["name"]!.GetValue<string>()).ToList();
                Assert.Equal(10, tools.Count);
                Assert.Contains("create_tasks", tools);
                Assert.Contains("cn_calendar_info", tools);
                Assert.DoesNotContain(tools, t => t.Contains("memory", StringComparison.OrdinalIgnoreCase) || t.Contains("profile", StringComparison.OrdinalIgnoreCase));

                // 3. Bridge 未开启 → 返回清晰中文提示且不崩溃
                await proc.StandardInput.WriteLineAsync(
                    """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_today","arguments":{}}}""");
                var closedResp = JsonNode.Parse((await proc.StandardOutput.ReadLineAsync())!)!;
                Assert.True(closedResp["result"]!["isError"]!.GetValue<bool>());
                Assert.Contains("未运行", closedResp["result"]!["content"]![0]!["text"]!.GetValue<string>());

                // 4. 开启 Bridge（仅当前用户 SID ACL 命名管道）→ create_tasks 成功写入
                bridge.Start();
                await Task.Delay(80);

                await proc.StandardInput.WriteLineAsync(
                    """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"create_tasks","arguments":{"tasks":[{"title":"通过 MCP 新建的任务","date":"2026-11-21","priority":"Medium"}]}}}""");
                var createResp = JsonNode.Parse((await proc.StandardOutput.ReadLineAsync())!)!;
                Assert.False(createResp["result"]!["isError"]!.GetValue<bool>());
                Assert.Single(repo.Query(new TaskQuery()));
                Assert.Equal("通过 MCP 新建的任务", repo.Query(new TaskQuery())[0].Title);

                // 5. 关闭 Bridge → 立刻拒绝后续调用
                bridge.Stop();
                await Task.Delay(80);

                await proc.StandardInput.WriteLineAsync(
                    """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"get_today","arguments":{}}}""");
                var afterStopResp = JsonNode.Parse((await proc.StandardOutput.ReadLineAsync())!)!;
                Assert.True(afterStopResp["result"]!["isError"]!.GetValue<bool>());
            }
            finally
            {
                proc.StandardInput.Close();
                if (!proc.WaitForExit(3000))
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    private static async Task<McpPendingConfirmation> WaitForSingleConfirmationAsync(McpPendingConfirmationQueue queue)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var snap = queue.GetSnapshot();
            if (snap.Count > 0)
            {
                return snap[0];
            }

            await Task.Delay(15);
        }

        throw new TimeoutException("等待待确认请求入队超时。");
    }
}

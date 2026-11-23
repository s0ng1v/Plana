using System.Text;
using Microsoft.Extensions.AI;
using VGList.Agent.Calendar;
using VGList.Agent.Clients;
using VGList.Agent.Runtime;
using VGList.App.Assistant;
using VGList.App.Models;
using VGList.App.Services;
using VGList.Core.Models;
using VGList.Data;
using VGList.Desktop.Main;
using VGList.Desktop.Main.Assistant;
using VGList.Desktop.Main.Calendar;
using VGList.Desktop.Main.Eisenhower;
using VGList.Desktop.Main.Tasks;
using VGList.Platform;
using VGList.Desktop.Services;
using VGList.Widgets.Month;
using VGList.Widgets.Today;
using Xunit;

namespace VGList.Tests;

public sealed class Adr0057ReliabilityAndPlanningTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 22);
    private readonly string _tempDir;

    public Adr0057ReliabilityAndPlanningTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vglist-adr0057-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
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

    [Fact]
    public void BuildDefaultAttachmentPrompt_AsksForPlanAndConfirmation_InsteadOfRushingExecution()
    {
        var one = AssistantPageViewModel.BuildDefaultAttachmentPrompt(1);
        var three = AssistantPageViewModel.BuildDefaultAttachmentPrompt(3);

        Assert.DoesNotContain("并整理待办吧", one);
        Assert.DoesNotContain("并整理待办吧", three);
        Assert.Contains("计划", three);
        Assert.Contains("确认", three);
        Assert.Contains("3 个附件", three);
    }

    [Fact]
    public void SystemPromptBuilder_RequiresPlanAndUserConsentBeforeWriting_WhenAttachmentsProvided()
    {
        var defaultAttachPrompt = AssistantPageViewModel.BuildDefaultAttachmentPrompt(3);
        var sysWithAttachments = SystemPromptBuilder.Build(
            Today,
            CnHolidayCalendar.Shared,
            defaultAttachPrompt,
            contributors: null,
            context: null,
            hasAttachments: true);

        Assert.Contains("Lana 打算这样帮你安排", sysWithAttachments);
        Assert.Contains("本轮不要直接调用 `propose_create_task`", sysWithAttachments);

        // 用户主动要求「直接添加/无需确认」时不再阻止立即写入
        var sysDirectCommand = SystemPromptBuilder.Build(
            Today,
            CnHolidayCalendar.Shared,
            "直接添加这三个 HTML 里的待办，无需确认",
            contributors: null,
            context: null,
            hasAttachments: true);

        Assert.DoesNotContain("本轮不要直接调用 `propose_create_task`", sysDirectCommand);
    }

    [Fact]
    public async Task BackgroundRepositoryMutations_DoNotThrowAcrossSubscribedViewModels()
    {
        var dbPath = Path.Combine(_tempDir, "bg-sync.db");
        using var db = SqliteDatabase.Open(dbPath);
        using var repo = new SqliteTaskRepository(db);

        var toast = new ToastService();
        var modelStore = new ModelProfileStore(new PlainSecretProtector(), Path.Combine(_tempDir, "models.json"));
        using var todayVm = new TodayWidgetViewModel(repo, Today);
        using var monthVm = new MonthWidgetViewModel(repo, Today);
        using var tasksVm = new TasksPageViewModel(repo, toast, Today);
        using var eisenVm = new EisenhowerViewModel(repo, toast, Today);

        // 模拟 AssistantHost Worker 在线程池上连续创建清单与批量写入任务（即触发崩溃的场景）
        Exception? bgException = null;
        await Task.Run(() =>
        {
            try
            {
                var list = repo.CreateList("Agent 课程学习");
                repo.Apply(
                    [
                        new CreateTaskOp(new TaskDraft
                        {
                            Title = "Week1 Day1: ReAct 循环",
                            Date = Today,
                            Priority = TaskPriority.High,
                            ListId = list.Id,
                        }),
                        new CreateTaskOp(new TaskDraft
                        {
                            Title = "Week1 Day2: 路径穿越修复",
                            Date = Today.AddDays(1),
                            Priority = TaskPriority.Medium,
                            ListId = list.Id,
                        }),
                    ],
                    ChangeSource.Assistant,
                    "批量写入课程任务");
            }
            catch (Exception ex)
            {
                bgException = ex;
            }
        });

        Assert.Null(bgException);
        Assert.True(tasksVm.TodayCount >= 1);
        Assert.NotNull(tasksVm.DraftListDotBrush);
        Assert.Same(tasksVm.DraftListDotBrush, tasksVm.DraftListDotBrush);
    }

    [Fact]
    public async Task AssistantHost_ParsesHtmlAttachmentsAsynchronously_AndStreamsEventsWithoutBlocking()
    {
        var dbPath = Path.Combine(_tempDir, "host-html.db");
        using var db = SqliteDatabase.Open(dbPath);
        using var repo = new SqliteTaskRepository(db);

        var html1 = Path.Combine(_tempDir, "0-总大纲.html");
        var html2 = Path.Combine(_tempDir, "1-Day0.html");
        var html3 = Path.Combine(_tempDir, "2-Day1.html");
        File.WriteAllText(html1, "<html><body><h1>备战路线图</h1><p>Week1 完成核心骨架</p></body></html>", Encoding.UTF8);
        File.WriteAllText(html2, "<html><body><h1>Day0 核心原理</h1><p>阅读 Agent 论文</p></body></html>", Encoding.UTF8);
        File.WriteAllText(html3, "<html><body><h1>Day1 实战</h1><p>手写 ReAct Loop</p></body></html>", Encoding.UTF8);

        var modelStore = new ModelProfileStore(new PlainSecretProtector(), Path.Combine(_tempDir, "m.json"));
        modelStore.SaveProfile(
            new ModelProfile
            {
                PresetId = "deepseek",
                VariantId = "openai",
                Name = "DeepSeek",
                BaseUrl = "https://api.deepseek.com",
                Protocol = ModelProtocol.OpenAi,
                Model = "deepseek-flash",
                SupportsImages = true,
            },
            "sk-test",
            setAsActive: true);

        string? capturedSystemPrompt = null;
        var host = new AssistantHost(
            repository: repo,
            schedule: repo,
            modelProvider: modelStore,
            clientFactory: new CapturingPlanClientFactory(sys => capturedSystemPrompt = sys),
            todayProvider: () => Today);

        var attachments = new[] { html1, html2, html3 }
            .Select(p =>
            {
                var fi = new FileInfo(p);
                return new AttachmentRef(fi.Name, fi.FullName, fi.Length, "网页");
            })
            .ToList();

        var req = new AssistantRequest(
            "sess-1",
            AssistantPageViewModel.BuildDefaultAttachmentPrompt(3),
            attachments);

        var events = new List<AssistantEvent>();
        await foreach (var evt in host.RunAsync(req))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e is ToolStartedEvent { ToolName: "read_attachment" });
        Assert.Contains(events, e => e is ToolFinishedEvent { ToolName: "read_attachment", Succeeded: true });
        Assert.Contains(events, e => e is DoneEvent);
        Assert.DoesNotContain(events, e => e is ErrorEvent);
        Assert.NotNull(capturedSystemPrompt);
        Assert.Contains("本轮不要直接调用 `propose_create_task`", capturedSystemPrompt);
    }

    [Fact]
    public void ChatMarkdownView_ComputeTableColumnWidths_UsesStarWrappingForLongColumnsWithoutHorizontalOverflow()
    {
        var table = new MarkdownTableBlock(
            ["日期", "任务", "优先级", "说明"],
            [MarkdownColumnAlign.Left, MarkdownColumnAlign.Left, MarkdownColumnAlign.Center, MarkdownColumnAlign.Left],
            [
                ["11/27 周五", "Week1 Day6: 端到端实测 + 面试沉淀", "Medium", "跑 3 大场景（正常生成 / 故意写错自愈 / 熔断后补跑），产出 Week1 STAR 简历描述"],
                ["11/28 周六", "Week1 收尾缓冲日", "Low", "只做补漏 + 课件打卡清尾，不完结的内容顺延到下周"],
            ]);

        var widths = ChatMarkdownView.ComputeTableColumnWidths(table);
        Assert.Equal(4, widths.Count);
        Assert.True(widths[0].IsAuto); // 日期短列不折行
        Assert.True(widths[1].IsStar); // 任务长列自适应换行
        Assert.True(widths[2].IsAuto); // 优先级短列紧凑显示
        Assert.True(widths[3].IsStar); // 说明长列按更大权重自适应换行
        Assert.True(widths[3].Value >= widths[1].Value);
    }

    [Fact]
    public void ChatToolStepViewModel_TransitionsCleanlyFromRunningSpinnerToCompletedCheck()
    {
        var msg = new ChatMessageViewModel(isUser: false, string.Empty) { IsStreaming = true };
        var step = new ChatToolStepViewModel("Lana 正在翻看你的日程…", isRunning: true);
        msg.AddToolStep(step);

        Assert.True(step.IsRunning);
        Assert.False(step.IsCompletedSuccess);
        Assert.False(step.IsCompletedFailed);

        msg.CompleteToolStep(step, "已查看日程（11月23日~11月30日）", succeeded: true);

        Assert.False(step.IsRunning);
        Assert.True(step.IsCompletedSuccess);
        Assert.False(step.IsCompletedFailed);
        Assert.True(msg.IsThinking);
    }

    [Fact]
    public void FreshInstallDefaults_StartsWithZeroSeedTasks_AndClassicBlueTheme()
    {
        var freshDb = Path.Combine(_tempDir, "fresh-install.db");
        var (repo, _, notice) = SampleData.OpenPersistentRepository(freshDb);
        try
        {
            Assert.Null(notice);
            Assert.Empty(repo.Query(new TaskQuery { IncludeCompleted = true }));
            Assert.Contains(repo.Lists, l => l.Id == TaskList.InboxId);
        }
        finally
        {
            (repo as IDisposable)?.Dispose();
        }

        var settings = new VGList.App.Settings.MainSettings();
        Assert.Equal(AppTheme.ClassicBlue, settings.Theme);
        Assert.Equal(AppTheme.ClassicBlue, AppTheme.Normalize(null));
        Assert.Equal(AppTheme.Plana, AppTheme.Normalize(AppTheme.Plana));
    }

    private sealed class PlainSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public bool TryUnprotect(string ciphertext, out string plaintext)
        {
            plaintext = ciphertext;
            return true;
        }
    }

    private sealed class CapturingPlanClientFactory(Action<string> onSystemPrompt) : IChatClientFactory
    {
        public IChatClient Create(ResolvedModel model) => new CapturingClient(onSystemPrompt);

        private sealed class CapturingClient(Action<string> onSystemPrompt) : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                var sys = messages.FirstOrDefault(m => m.Role == ChatRole.System)?.Text ?? string.Empty;
                onSystemPrompt(sys);
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Lana 打算这样帮你安排：……你觉得可以吗？")));
            }

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                var sys = messages.FirstOrDefault(m => m.Role == ChatRole.System)?.Text ?? string.Empty;
                onSystemPrompt(sys);
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Lana 打算这样帮你安排：先征询你的意见，确认后再动手～");
            }

            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose() { }
        }
    }
}

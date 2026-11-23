using Avalonia;
using Avalonia.Media;
using Microsoft.Data.Sqlite;
using VGList.App.Assistant;
using VGList.App.Models;
using VGList.App.Services;
using VGList.App.Settings;
using VGList.Core.Abstractions;
using VGList.Core.Models;
using VGList.Data;
using VGList.Desktop;
using VGList.Desktop.Controls;
using VGList.Desktop.Main;
using VGList.Desktop.Main.Assistant;
using VGList.Desktop.Main.Tasks;
using VGList.Desktop.Services;
using VGList.Platform;
using VGList.Widgets.Themes;
using Xunit;

namespace VGList.Tests;

public sealed class LanaPolishTests
{
    private static readonly DateOnly Today = new(2026, 11, 21);

    [Fact]
    public void ChatMarkdownView_ParseBlocks_ParsesTableAndProseAndCode()
    {
        const string md = """
            先看看今天的任务清单：

            | # | 任务 | 优先级 | 日期 |
            |---|:---|---:|---|
            | 1 | 周五 15 分钟本周日程复盘 | Low | 11-23 |
            | 2 | 回复客户邮件 | **High** | 11-22 |

            ```json
            {"ok": true}
            ```

            需要调整随时告诉我哦 ✨
            """;

        var blocks = ChatMarkdownView.ParseBlocks(md);

        Assert.Equal(4, blocks.Count);
        Assert.IsType<MarkdownProseBlock>(blocks[0]);

        var table = Assert.IsType<MarkdownTableBlock>(blocks[1]);
        Assert.Equal(["#", "任务", "优先级", "日期"], table.Headers);
        Assert.Equal(
            [MarkdownColumnAlign.Left, MarkdownColumnAlign.Left, MarkdownColumnAlign.Right, MarkdownColumnAlign.Left],
            table.Alignments);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("周五 15 分钟本周日程复盘", table.Rows[0][1]);
        Assert.Equal("**High**", table.Rows[1][2]);

        var code = Assert.IsType<MarkdownCodeBlock>(blocks[2]);
        Assert.Equal("json", code.Language);
        Assert.Contains("\"ok\": true", code.Code);

        var tail = Assert.IsType<MarkdownProseBlock>(blocks[3]);
        Assert.Contains("随时告诉我", tail.Markdown);
    }

    [Fact]
    public void ChatMessageViewModel_InterleavesToolStepsAndTextChronologically_AndKeepsThinkingBetweenTools()
    {
        var msg = new ChatMessageViewModel(isUser: false, string.Empty)
        {
            IsThinking = true,
            IsStreaming = true,
        };

        // 1. 先输出前导文字
        msg.AppendStreamText("Lana 先帮你查一下今天的待办～", flushNow: true);
        Assert.False(msg.IsThinking);
        Assert.Single(msg.Blocks);
        Assert.IsType<ChatTextBlockViewModel>(msg.Blocks[0]);

        // 2. 中途发起工具调用 → 工具胶囊自身展示运行中流光（不提前打钩）
        var step = new ChatToolStepViewModel("Lana 正在翻看你的日程…", isRunning: true);
        msg.AddToolStep(step);
        Assert.True(step.IsRunning);
        Assert.False(step.IsCompletedSuccess);
        Assert.False(msg.IsThinking); // 工具本身在展示流光，避免叠双份
        Assert.Equal(2, msg.Blocks.Count);
        Assert.IsType<ChatToolStepViewModel>(msg.Blocks[1]);

        // 3. 工具完成 → 胶囊打勾，同时恢复流光思考态等待模型继续回复
        msg.CompleteToolStep(step, "已查看日程（11/21）", succeeded: true);
        Assert.False(step.IsRunning);
        Assert.True(step.IsCompletedSuccess);
        Assert.True(msg.IsThinking);

        // 4. 模型继续输出正文 → 开启新的正文块，工具夹在两段文字之间
        msg.AppendStreamText("查到啦，一共有 3 条待办 ✨", flushNow: true);
        Assert.False(msg.IsThinking);
        Assert.Equal(3, msg.Blocks.Count);
        Assert.IsType<ChatTextBlockViewModel>(msg.Blocks[2]);
        Assert.Contains("查一下", msg.Text);
        Assert.Contains("查到啦", msg.Text);

        msg.FinishStreaming();
        Assert.False(msg.IsStreaming);
        Assert.False(msg.IsThinking);
    }

    [Fact]
    public void ClassifyProposalAction_AndExtractBadges_NeverMislabelsTitleEditAsReschedule()
    {
        var repo = new InMemoryTaskRepository();
        var work = repo.CreateList("工作");
        var existing = repo.Create(new TaskDraft { Title = "提交软著", Date = Today, Priority = TaskPriority.High });

        // 仅修改标题与追加 1 个子待办，未改日期 → 分类应为 "edit"（「修改」），绝不能标为「改期」！
        var updateOp = new UpdateTaskOp(existing.Id, new TaskPatch
        {
            Title = "提交软著（今晚 20:00）",
            AppendSubtasks = [new TaskSubtask { Title = "打包源码压缩包" }],
        });
        var updateItem = new ProposalItem("p-upd", ProposalActionKind.UpdateTitleOrDate, "修改", "", "", "", updateOp);

        var (actionKind, actionTag) = AssistantPageViewModel.ClassifyProposalAction(updateItem);
        Assert.Equal("edit", actionKind);
        Assert.Equal("修改", actionTag);

        var (dateBadge, prioBadge, prioTier, extraBadge) = AssistantPageViewModel.ExtractProposalBadges(updateItem, repo, Today);
        Assert.Equal("今天 (11月21日)", dateBadge);
        Assert.Equal("高优先级", prioBadge);
        Assert.Equal(TaskPriority.High, prioTier);
        Assert.Contains("子待办：打包源码压缩包", extraBadge);
        Assert.DoesNotContain("+1 子步骤", extraBadge);

        // 新建任务带 2 个子待办 + 清单
        var createOp = new CreateTaskOp(new TaskDraft
        {
            Title = "提交松松记账软著",
            Date = Today,
            Priority = TaskPriority.High,
            ListId = work.Id,
            Subtasks = [new TaskSubtask { Title = "整理源码" }, new TaskSubtask { Title = "导出文档" }],
        });
        var createItem = new ProposalItem("p-new", ProposalActionKind.CreateTask, "新增", "提交松松记账软著", "", "", createOp);
        var (d2, p2, pt2, e2) = AssistantPageViewModel.ExtractProposalBadges(createItem, repo, Today);
        Assert.Equal("今天 (11月21日)", d2);
        Assert.Equal("高优先级", p2);
        Assert.Equal(TaskPriority.High, pt2);
        Assert.Equal("清单：工作 · 新增 2 项子待办", e2);
    }

    [Fact]
    public void SqliteRepository_UndoEntries_SupportsSingleMultiAndRemainingBatchUndo()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "vglist-undo-entries-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            using var db = SqliteDatabase.Open(Path.Combine(tmpDir, "test.db"));
            using var repo = new SqliteTaskRepository(db);

            var cs = repo.Apply(
            [
                new CreateTaskOp(new TaskDraft { Title = "任务 A", Date = Today }),
                new CreateTaskOp(new TaskDraft { Title = "任务 B", Date = Today }),
                new CreateTaskOp(new TaskDraft { Title = "任务 C", Date = Today }),
            ], ChangeSource.Assistant, "批量新建 3 项");

            Assert.Equal(3, cs.Entries.Count);
            Assert.Equal(3, repo.Query(new TaskQuery()).Count);

            var idA = cs.Entries[0].EntityId;
            var idB = cs.Entries[1].EntityId;
            var idC = cs.Entries[2].EntityId;

            // 1. 单条撤销 A
            var singleRes = repo.UndoEntries(cs.Id, [idA]);
            Assert.True(singleRes.Success);
            Assert.Equal(1, singleRes.RevertedCount);
            Assert.Null(repo.GetById(idA));
            Assert.NotNull(repo.GetById(idB));
            Assert.NotNull(repo.GetById(idC));

            // 2. 多选撤销 B + C → 剩余全部撤完后 ChangeSet 自动标为已撤销
            var multiRes = repo.UndoEntries(cs.Id, [idB, idC]);
            Assert.True(multiRes.Success);
            Assert.Equal(2, multiRes.RevertedCount);
            Assert.Empty(repo.Query(new TaskQuery()));
            Assert.NotNull(repo.Recent(5).Single(x => x.Id == cs.Id).UndoneAt);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void SqliteConversationStore_TruncateMessages_DropsRegeneratedOrEditedSuffix()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "vglist-conv-trunc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            using var db = SqliteDatabase.Open(Path.Combine(tmpDir, "conv.db"));
            var store = new SqliteConversationStore(db);
            store.CreateSession("s1", "测试对话");
            store.AppendMessage(new ConversationMessageRecord("m1", "s1", 1, "user", "第一句", null, null, DateTimeOffset.Now));
            store.AppendMessage(new ConversationMessageRecord("m2", "s1", 2, "assistant", "第一回", null, null, DateTimeOffset.Now));
            store.AppendMessage(new ConversationMessageRecord("m3", "s1", 3, "user", "第二句（旧）", null, null, DateTimeOffset.Now));
            store.AppendMessage(new ConversationMessageRecord("m4", "s1", 4, "assistant", "第二回（旧）", null, null, DateTimeOffset.Now));

            // 截断保留前 2 条（模拟重新生成或编辑第 2 轮用户消息）
            store.TruncateMessages("s1", keepCount: 2);
            var remaining = store.LoadMessages("s1");
            Assert.Equal(2, remaining.Count);
            Assert.Equal("m1", remaining[0].Id);
            Assert.Equal("m2", remaining[1].Id);

            // 截断为 0 条
            store.TruncateMessages("s1", keepCount: 0);
            Assert.Empty(store.LoadMessages("s1"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void UserProfileStore_StructuredFieldsAndOneTimeNicknamePrompt()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "vglist-profile-nick-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var store = new UserProfileStore(tmpDir);
            var contributor = new ProfileContextContributor(store, () => true);

            // 第 1 次对话且未填称呼 → 注入一次称呼引导
            var firstCtx = contributor.BuildContext("你好", Today);
            Assert.NotNull(firstCtx);
            Assert.Contains("怎么称呼你", firstCtx);
            Assert.True(store.HasAskedNickname());

            // 第 2 次对话仍未填称呼 → 不再重复追问！
            var secondCtx = contributor.BuildContext("帮我看看今天任务", Today);
            Assert.Null(secondCtx);

            // 结构化保存称呼与作息 → 读取不含 Markdown 符号且自动注入称呼
            store.SaveUserEditedFields(new UserProfileFields(
                Nickname: "阿宇",
                SleepAndRoutine: "晚上 11 点前睡觉",
                FrequentActivities: "写代码、健身",
                PeakHours: "上午 9:30–12:00",
                ProcrastinationTypes: "整理报销",
                OtherNotes: ""));

            var fields = store.LoadFields();
            Assert.Equal("阿宇", fields.Nickname);
            Assert.Equal("晚上 11 点前睡觉", fields.SleepAndRoutine);
            Assert.DoesNotContain("##", fields.SleepAndRoutine);

            var thirdCtx = contributor.BuildContext("安排一下明天", Today);
            Assert.NotNull(thirdCtx);
            Assert.Contains("阿宇", thirdCtx);
            Assert.DoesNotContain("怎么称呼你", thirdCtx);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("@", 1, true, 0, "")]
    [InlineData("@笔记", 3, true, 0, "笔记")]
    [InlineData("帮我整理 @AI助手.md", 13, true, 5, "AI助手.md")]
    [InlineData("请看，@会议", 6, true, 3, "会议")]
    [InlineData("test@example.com", 16, false, -1, "")]
    [InlineData("@笔记 之后", 6, false, -1, "")]
    public void TryExtractMentionQuery_ParsesAtMentionsAccurately(
        string text, int caret, bool expectedMatch, int expectedAt, string expectedQuery)
    {
        var matched = AssistantPageViewModel.TryExtractMentionQuery(text, caret, out var atIdx, out var query);
        Assert.Equal(expectedMatch, matched);
        Assert.Equal(expectedAt, atIdx);
        Assert.Equal(expectedQuery, query);
    }

    [Fact]
    public void ModelSwitcherDropdown_SwitchesActiveProfileInline_AndStartNewSessionNotifiesSwitch()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "vglist-lana-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var store = new ModelProfileStore(new NoopProtector(), Path.Combine(tmpDir, "models.json"));
            var p1 = store.SaveProfile(new ModelProfile
            {
                Id = "p-deepseek",
                PresetId = "deepseek",
                Name = "DeepSeek",
                BaseUrl = "https://api.deepseek.com/v1",
                Model = "deepseek-chat",
                Protocol = ModelProtocol.OpenAi,
            }, "sk-1111", setAsActive: true);

            var p2 = store.SaveProfile(new ModelProfile
            {
                Id = "p-kimi",
                PresetId = "kimi",
                Name = "Kimi",
                BaseUrl = "https://api.moonshot.cn/v1",
                Model = "kimi-k2",
                Protocol = ModelProtocol.OpenAi,
            }, "sk-2222", setAsActive: false);

            var openedSettings = false;
            using var vm = new AssistantPageViewModel(
                new InMemoryTaskRepository(),
                store,
                new ToastService(),
                () => openedSettings = true,
                Today,
                profileStore: new UserProfileStore(tmpDir));

            Assert.Equal(2, vm.AvailableModels.Count);
            Assert.Contains("DeepSeek", vm.ActiveModelLabel);

            vm.ToggleModelDropdown();
            Assert.True(vm.IsModelDropdownOpen);
            Assert.False(openedSettings);

            var kimiItem = vm.AvailableModels.Single(m => m.ProfileId == p2.Id);
            vm.SelectModelProfile(kimiItem);
            Assert.False(vm.IsModelDropdownOpen);
            Assert.False(openedSettings);
            Assert.Equal(p2.Id, store.ActiveProfileId);
            Assert.Contains("Kimi", vm.ActiveModelLabel);

            var switchedFired = false;
            vm.SessionSwitched += (_, _) => switchedFired = true;
            vm.StartNewSession();
            Assert.True(switchedFired);

            // 验证编辑历史消息可增删附件
            var attPath = Path.Combine(tmpDir, "note.md");
            File.WriteAllText(attPath, "# hello");
            var msg = new ChatMessageViewModel(isUser: true, "原消息");
            vm.Messages.Add(msg);
            vm.BeginEditMessage(msg);
            Assert.True(msg.IsEditing);
            vm.AddEditAttachmentFiles(msg, [attPath]);
            Assert.Single(msg.EditAttachments);
            vm.RemoveEditAttachment(msg, msg.EditAttachments[0]);
            Assert.Empty(msg.EditAttachments);
            vm.CancelEditMessage(msg);
            Assert.False(msg.IsEditing);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void TasksPageViewModel_HidesInboxLabelByDefaultAndShowsWhenCustomListSelected()
    {
        var repo = new InMemoryTaskRepository();
        var work = repo.CreateList("工作");
        using var vm = new TasksPageViewModel(repo, new ToastService(), Today);

        Assert.False(vm.IsNonInboxDraftList);

        vm.PickDraftList(work.Id);
        Assert.True(vm.IsNonInboxDraftList);
        Assert.Equal("工作", vm.DraftListName);

        vm.PickDraftList(TaskList.InboxId);
        Assert.False(vm.IsNonInboxDraftList);
    }

    [Fact]
    public void MainWindowCloseAction_PromptRememberAndSettingsStayInSync()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "vglist-close-action-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var settingsPath = Path.Combine(tmpDir, "settings.json");
            var settingsStore = new SettingsStore(settingsPath);
            var repo = new InMemoryTaskRepository();
            var coordinator = new WidgetCoordinator(repo, settingsStore);
            var modelStore = new ModelProfileStore(new NoopProtector(), Path.Combine(tmpDir, "models.json"));
            using var vm = new MainWindowViewModel(repo, coordinator, modelStore);

            var minimizeCount = 0;
            var exitCount = 0;
            vm.MinimizeRequested += (_, _) => minimizeCount++;
            vm.ExitConfirmed += (_, _) => exitCount++;

            // 默认每次询问
            Assert.True(vm.SettingsPage.IsCloseActionAsk);
            Assert.False(vm.RequestWindowClose());
            Assert.True(vm.IsClosePromptOpen);
            Assert.Equal(0, minimizeCount);

            // Esc 关闭确认框
            Assert.True(vm.HandleEscape());
            Assert.False(vm.IsClosePromptOpen);

            // 选择最小化但不记住 → 保持 Ask
            Assert.False(vm.RequestWindowClose());
            vm.RememberCloseChoice = false;
            vm.ConfirmMinimizeFromPromptCommand.Execute(null);
            Assert.False(vm.IsClosePromptOpen);
            Assert.Equal(1, minimizeCount);
            Assert.Equal(WindowCloseAction.Ask, coordinator.MainSettings.CloseAction);

            // 选择最小化并记住 → 持久化并同步设置页，下次直接最小化不弹窗
            Assert.False(vm.RequestWindowClose());
            vm.ToggleRememberCloseChoiceCommand.Execute(null);
            Assert.True(vm.RememberCloseChoice);
            vm.ConfirmMinimizeFromPromptCommand.Execute(null);
            Assert.Equal(2, minimizeCount);
            Assert.True(vm.SettingsPage.IsCloseActionMinimize);
            Assert.Equal(WindowCloseAction.Minimize, settingsStore.LoadMain().CloseAction);

            Assert.False(vm.RequestWindowClose());
            Assert.False(vm.IsClosePromptOpen);
            Assert.Equal(3, minimizeCount);

            // 在设置页改为直接退出 → RequestWindowClose 直接返回 true
            vm.SettingsPage.SetCloseAction(WindowCloseAction.Exit);
            Assert.True(vm.SettingsPage.IsCloseActionExit);
            Assert.True(vm.RequestWindowClose());

            // 改回 Ask 后选择退出并记住 → 触发 ExitConfirmed 并持久化 Exit
            vm.SettingsPage.SetCloseAction(WindowCloseAction.Ask);
            Assert.False(vm.RequestWindowClose());
            Assert.True(vm.IsClosePromptOpen);
            vm.RememberCloseChoice = true;
            vm.ConfirmExitFromPromptCommand.Execute(null);
            Assert.Equal(1, exitCount);
            Assert.Equal(WindowCloseAction.Exit, settingsStore.LoadMain().CloseAction);
            Assert.True(vm.SettingsPage.IsCloseActionExit);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void RulesStore_MigratesLegacyPermissionsAndPersistsAllToggles()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "vglist-rules-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var store = new RulesStore(tmpDir);
            // 模拟旧版 rules.json（无 permissionsSchemaVersion，新建/修改为 false）
            File.WriteAllText(store.FilePath, """
                {
                  "restDays": [],
                  "noWeekendOrHolidayListIds": [],
                  "preferenceText": "上午专注",
                  "permissions": {
                    "autoAppendSubtasks": true,
                    "autoCategorizeList": true,
                    "autoCreateTask": false,
                    "autoUpdateTask": false,
                    "autoCompleteTask": false,
                    "autoCreateList": false,
                    "autoDeleteTask": false
                  }
                }
                """);

            // 首次加载：一次性迁移到默认除删除外全直写，同时保留个人偏好
            var migrated = store.Load();
            Assert.Equal(UserRulesDocument.CurrentPermissionsSchemaVersion, migrated.PermissionsSchemaVersion);
            Assert.Equal("上午专注", migrated.PreferenceText);
            Assert.True(migrated.Permissions.AutoCreateTask);
            Assert.True(migrated.Permissions.AutoUpdateTask);
            Assert.True(migrated.Permissions.AutoCompleteTask);
            Assert.True(migrated.Permissions.AutoCreateList);
            Assert.False(migrated.Permissions.AutoDeleteTask);

            // 迁移后用户主动关闭新建直写、开启删除直写 → 持久化且不再被覆盖
            migrated.Permissions.AutoCreateTask = false;
            migrated.Permissions.AutoDeleteTask = true;
            store.Save(migrated);

            var reloaded = store.Load();
            Assert.False(reloaded.Permissions.AutoCreateTask);
            Assert.True(reloaded.Permissions.AutoDeleteTask);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void AnthropicFontCollection_RegistersEmbeddedFontsAndMappings()
    {
        var mappings = AnthropicFontCollection.CreateMappings();
        Assert.Equal(AnthropicFontCollection.CollectionKey, mappings["Anthropic Serif"].Key?.Source);
        Assert.Equal("Anthropic Serif Web", mappings["Anthropic Serif"].Name);
        Assert.Equal("Anthropic Sans Web", mappings["Anthropic Sans"].Name);
        Assert.Equal("Anthropic Mono Variable", mappings["Anthropic Mono"].Name);

        // 中文思源字体同样映射到内嵌集合（ADR-0063），未安装系统思源的机器不再回退微软雅黑
        Assert.Equal(AnthropicFontCollection.CollectionKey, mappings["Noto Sans SC"].Key?.Source);
        Assert.Equal("Noto Sans SC", mappings["Noto Sans SC"].Name);
        Assert.Equal(AnthropicFontCollection.CollectionKey, mappings["Noto Serif SC"].Key?.Source);
        Assert.Equal("Noto Serif SC", mappings["Noto Serif SC"].Name);

        // 字体文件的旧家族名（nameID 1，带 Text 后缀）也指向同一嵌入家族，避免平台按不同 name 记录解析时回退
        Assert.Equal(AnthropicFontCollection.CollectionKey, mappings["Anthropic Sans Web Text"].Key?.Source);
        Assert.Equal("Anthropic Sans Web", mappings["Anthropic Sans Web Text"].Name);
        Assert.Equal(AnthropicFontCollection.CollectionKey, mappings["Anthropic Serif Web Text"].Key?.Source);
        Assert.Equal("Anthropic Serif Web", mappings["Anthropic Serif Web Text"].Name);

        // 确认 3 个真实 Anthropic TTF 与 2 个中文思源 VF TTF 均已嵌入 VGList.Widgets 程序集资源中
        var widgetsAsm = typeof(AnthropicFontCollection).Assembly;
        using var resStream = widgetsAsm.GetManifestResourceStream("!AvaloniaResources");
        Assert.NotNull(resStream);
        using var ms = new MemoryStream();
        resStream!.CopyTo(ms);
        var raw = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("AnthropicSerifWebText.ttf", raw, StringComparison.Ordinal);
        Assert.Contains("AnthropicSansWebText.ttf", raw, StringComparison.Ordinal);
        Assert.Contains("AnthropicMonoVariable.ttf", raw, StringComparison.Ordinal);
        Assert.Contains("NotoSansSC-VF.ttf", raw, StringComparison.Ordinal);
        Assert.Contains("NotoSerifSC-VF.ttf", raw, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "Anthropic Sans", "Anthropic Serif", "Anthropic Mono")]
    [InlineData(FontPresetMode.AnthropicSerif, "Anthropic Sans", "Anthropic Serif", "Anthropic Mono")]
    [InlineData(FontPresetMode.AnthropicClassic, "Anthropic Sans", "Anthropic Serif", "Anthropic Mono")]
    [InlineData(FontPresetMode.CleanSans, "Noto Sans SC", "Noto Sans SC", "Anthropic Mono")]
    public void FontPresets_StartWithEmbeddedFamilies(string? preset, string ui, string display, string mono)
    {
        var (uiFont, displayFont, monoFont) = AppTheme.ResolveFonts(preset);
        var mappings = AnthropicFontCollection.CreateMappings();

        Assert.Equal(ui, uiFont.Name);
        Assert.Equal(display, displayFont.Name);
        Assert.Equal(mono, monoFont.Name);

        // 除「系统」预设外，所有预设的首选字体都必须是内嵌字体，否则不同机器渲染会不一致
        Assert.True(mappings.ContainsKey(uiFont.Name), $"界面字体 {uiFont.Name} 未内嵌");
        Assert.True(mappings.ContainsKey(displayFont.Name), $"显示字体 {displayFont.Name} 未内嵌");
        Assert.True(mappings.ContainsKey(monoFont.Name), $"等宽字体 {monoFont.Name} 未内嵌");
    }

    [Fact]
    public void FontPreset_DefaultsToAnthropicSerifAndPersistsChangesInSettings()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "vglist-font-preset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var settingsStore = new SettingsStore(Path.Combine(tmpDir, "settings.json"));
            var repo = new InMemoryTaskRepository();
            var coordinator = new WidgetCoordinator(repo, settingsStore);
            var modelStore = new ModelProfileStore(new NoopProtector(), Path.Combine(tmpDir, "models.json"));
            using var vm = new MainWindowViewModel(repo, coordinator, modelStore);

            // 默认 Anthropic + 思源宋体；旧版 Default 也解析为新默认
            Assert.True(vm.SettingsPage.IsFontAnthropicSerif);
            var (defUi, defDisplay, _) = AppTheme.ResolveFonts(FontPresetMode.Default);
            Assert.Contains("Anthropic Sans", defUi.FamilyNames);
            Assert.Contains("Anthropic Serif", defDisplay.FamilyNames);
            Assert.Contains("Noto Serif SC", defDisplay.FamilyNames);

            // 切换到 Anthropic + 思源宋体并验证持久化
            vm.SettingsPage.SetFontPreset(FontPresetMode.AnthropicSerif);
            Assert.True(vm.SettingsPage.IsFontAnthropicSerif);
            Assert.Equal(FontPresetMode.AnthropicSerif, settingsStore.LoadMain().FontPreset);
            var (_, anthDisplay, _) = AppTheme.ResolveFonts(FontPresetMode.AnthropicSerif);
            Assert.Contains("Anthropic Serif", anthDisplay.FamilyNames);
            Assert.Contains("Noto Serif SC", anthDisplay.FamilyNames);

            // 切换主题时保留已选字体预设
            vm.SettingsPage.SetTheme(AppTheme.ClassicBlue);
            Assert.Equal(FontPresetMode.AnthropicSerif, settingsStore.LoadMain().FontPreset);
            Assert.True(vm.SettingsPage.IsFontAnthropicSerif);

            // 切换到纯净黑体
            vm.SettingsPage.SetFontPreset(FontPresetMode.CleanSans);
            Assert.True(vm.SettingsPage.IsFontCleanSans);
            Assert.Equal(FontPresetMode.CleanSans, settingsStore.LoadMain().FontPreset);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void LanaAvatarControl_ComputeGazeVector_IsSmoothRadialAndBoundedWithinUnitCircle()
    {
        var (zx, zy) = LanaAvatarControl.ComputeGazeVector(0, 0);
        Assert.Equal(0.0, zx, 6);
        Assert.Equal(0.0, zy, 6);

        var (rx, ry) = LanaAvatarControl.ComputeGazeVector(120, 0, sensitivityRadius: 240);
        Assert.True(rx > 0.4 && rx < 0.6);
        Assert.Equal(0.0, ry, 6);

        // 对角远距离点也必须平滑收敛在单位圆内（避免眼睛挤进腮红或越界）
        var (dx, dy) = LanaAvatarControl.ComputeGazeVector(3000, 3000, sensitivityRadius: 200);
        var radius = Math.Sqrt(dx * dx + dy * dy);
        Assert.True(radius > 0.99 && radius <= 1.000001);
        Assert.Equal(dx, dy, 5);

        // 距离越远视线幅度单调递增
        var (nearX, _) = LanaAvatarControl.ComputeGazeVector(40, 0, sensitivityRadius: 200);
        var (farX, _) = LanaAvatarControl.ComputeGazeVector(400, 0, sensitivityRadius: 200);
        Assert.True(farX > nearX);
    }

    [Fact]
    public void LanaStageMotion_InterpolatesFirstTurnNextTurnReturnHomeAndClampsViewport()
    {
        var hero = new Rect(320, 210, LanaStageMotion.HeroSize, LanaStageMotion.HeroSize);
        var turn1 = new Rect(40, 96, LanaStageMotion.MessageAvatarSize, LanaStageMotion.MessageAvatarSize);
        var turn2 = new Rect(40, 360, LanaStageMotion.MessageAvatarSize, LanaStageMotion.MessageAvatarSize);

        // FirstTurn: 起点精确 108×108，终点精确 42×42，中段具备平滑微浮弧与尺寸过渡
        Assert.Equal(hero, LanaStageMotion.InterpolateFlightRect(hero, turn1, 0.0, LanaFlightKind.FirstTurn));
        Assert.Equal(turn1, LanaStageMotion.InterpolateFlightRect(hero, turn1, 1.0, LanaFlightKind.FirstTurn));
        var firstMid = LanaStageMotion.InterpolateFlightRect(hero, turn1, 0.5, LanaFlightKind.FirstTurn);
        Assert.InRange(firstMid.Width, LanaStageMotion.MessageAvatarSize, LanaStageMotion.HeroSize);
        Assert.InRange(firstMid.X, turn1.X, hero.X);

        // NextTurn: 沿左侧纵向下落，绝不向右侵入右侧消息正文区
        var nextMid = LanaStageMotion.InterpolateFlightRect(turn1, turn2, 0.5, LanaFlightKind.NextTurn);
        Assert.True(nextMid.X <= turn1.X);
        Assert.InRange(nextMid.Y, turn1.Y, turn2.Y);

        // ReturnHome: 尺寸从 42×42 平滑放大回 108×108
        var retMid = LanaStageMotion.InterpolateFlightRect(turn2, hero, 0.5, LanaFlightKind.ReturnHome);
        Assert.True(retMid.Width > turn2.Width && retMid.Width < hero.Width);

        // None: 立即贴合终点
        Assert.Equal(turn2, LanaStageMotion.InterpolateFlightRect(hero, turn2, 0.25, LanaFlightKind.None));

        // 长对话滚出视口后，飞行起点自动夹紧到视口边缘，避免超高速穿屏
        var farAbove = new Rect(40, -1400, 42, 42);
        var clampedTop = LanaStageMotion.ClampFlightOriginToViewport(farAbove, stageHeight: 600);
        Assert.Equal(-56.0, clampedTop.Y, 3);

        var farBelow = new Rect(40, 2200, 42, 42);
        var clampedBottom = LanaStageMotion.ClampFlightOriginToViewport(farBelow, stageHeight: 600);
        Assert.Equal(616.0, clampedBottom.Y, 3);

        Assert.False(LanaStageMotion.IsRectInViewport(farAbove, stageHeight: 600));
        Assert.True(LanaStageMotion.IsRectInViewport(turn1, stageHeight: 600));
    }

    [Fact]
    public async Task AssistantStageAndNavLana_SyncStatesAndOrchestrateMultiTurnEchoes()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "vglist-lana-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var settingsStore = new SettingsStore(Path.Combine(tmpDir, "settings.json"));
            var repo = new InMemoryTaskRepository();
            var coordinator = new WidgetCoordinator(repo, settingsStore);
            var modelStore = new ModelProfileStore(new NoopProtector(), Path.Combine(tmpDir, "models.json"));
            using var vm = new MainWindowViewModel(repo, coordinator, modelStore);

            var transitions = new List<LanaStageTransitionEventArgs>();
            vm.AssistantPage.StageTransitionRequested += (_, args) => transitions.Add(args);

            // 1. 初始在任务页：左栏与对话舞台均处于 Sleep 零开销态
            Assert.Equal(LanaVisualState.Sleep, vm.LanaNavState);
            Assert.Equal(LanaVisualState.Sleep, vm.AssistantPage.StageLanaState);

            // 2. 进入对话页：双端同步唤醒至 Idle，聚焦输入框进入 Listening
            vm.ShowAssistantCommand.Execute(null);
            Assert.Equal(LanaVisualState.Idle, vm.LanaNavState);
            Assert.Equal(LanaVisualState.Idle, vm.AssistantPage.StageLanaState);

            vm.AssistantPage.IsComposerFocused = true;
            Assert.Equal(LanaVisualState.Listening, vm.LanaNavState);
            Assert.Equal(LanaVisualState.Listening, vm.AssistantPage.StageLanaState);

            // 3. 模拟正常流式生成完成：Thinking -> Happy -> CompleteLanaHappy 回落至 Listening
            vm.AssistantPage.IsGenerating = true;
            Assert.Equal(LanaVisualState.Thinking, vm.LanaNavState);
            Assert.Equal(LanaVisualState.Thinking, vm.AssistantPage.StageLanaState);

            vm.AssistantPage.IsGenerating = false;
            Assert.Equal(LanaVisualState.Happy, vm.LanaNavState);
            Assert.Equal(LanaVisualState.Happy, vm.AssistantPage.StageLanaState);

            vm.AssistantPage.CompleteLanaHappy();
            Assert.Equal(LanaVisualState.Listening, vm.LanaNavState);
            Assert.Equal(LanaVisualState.Listening, vm.AssistantPage.StageLanaState);

            // 4. 首轮发送（未配置模型会报错）：触发 FirstTurn 飞向左侧，且报错不误触 Happy 庆祝
            vm.AssistantPage.DraftInput = "你好 Lana";
            await vm.AssistantPage.SendAsync();
            Assert.Single(transitions);
            Assert.Equal(LanaFlightKind.FirstTurn, transitions[0].Kind);
            var turn1Ai = Assert.IsType<ChatMessageViewModel>(transitions[0].TargetAssistantMessage);
            Assert.True(turn1Ai.IsLatestAssistantMessage);
            Assert.False(turn1Ai.ShowPastStaticAvatar);
            Assert.Equal(LanaVisualState.Listening, vm.AssistantPage.StageLanaState);

            // 5. 第二轮发送：触发 NextTurn 滑落；第一轮切换为历史静影，第二轮成为唯一活体锚点
            vm.AssistantPage.DraftInput = "帮我看看明天安排";
            await vm.AssistantPage.SendAsync();
            Assert.Equal(2, transitions.Count);
            Assert.Equal(LanaFlightKind.NextTurn, transitions[1].Kind);
            var turn2Ai = Assert.IsType<ChatMessageViewModel>(transitions[1].TargetAssistantMessage);
            Assert.False(turn1Ai.IsLatestAssistantMessage);
            Assert.True(turn1Ai.ShowPastStaticAvatar);
            Assert.True(turn2Ai.IsLatestAssistantMessage);
            Assert.False(turn2Ai.ShowPastStaticAvatar);

            var firstSession = vm.AssistantPage.Sessions[0];

            // 6. 新建对话：触发 ReturnHome 浮回居中主视觉
            vm.AssistantPage.StartNewSession();
            Assert.Equal(3, transitions.Count);
            Assert.Equal(LanaFlightKind.ReturnHome, transitions[2].Kind);
            Assert.Null(transitions[2].TargetAssistantMessage);
            Assert.Null(vm.AssistantPage.LatestAssistantMessage);

            // 7. 切回已有历史会话：即时贴合（None）到最后一条回复，历史轮次仍保留静影标记
            vm.AssistantPage.SelectSession(firstSession);
            Assert.Equal(4, transitions.Count);
            Assert.Equal(LanaFlightKind.None, transitions[3].Kind);
            Assert.Same(turn2Ai, transitions[3].TargetAssistantMessage);
            Assert.True(turn1Ai.ShowPastStaticAvatar);
            Assert.True(turn2Ai.IsLatestAssistantMessage);

            // 8. 切回任务页：双端立即同步回归 Sleep
            vm.ShowTasksCommand.Execute(null);
            Assert.Equal(LanaVisualState.Sleep, vm.LanaNavState);
            Assert.Equal(LanaVisualState.Sleep, vm.AssistantPage.StageLanaState);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    private sealed class NoopProtector : ISecretProtector
    {
        public string Protect(string plaintext) => "enc:" + plaintext;
        public bool TryUnprotect(string ciphertext, out string plaintext)
        {
            plaintext = ciphertext.StartsWith("enc:", StringComparison.Ordinal) ? ciphertext[4..] : ciphertext;
            return true;
        }
    }
}

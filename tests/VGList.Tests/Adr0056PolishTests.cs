using System.IO.Compression;
using System.Text;
using System.Text.Json;
using VGList.Agent.Attachments;
using VGList.App.Models;
using VGList.App.Services;
using VGList.Core.Models;
using VGList.Desktop.Main.Assistant;
using VGList.Desktop.Main.Settings;
using VGList.Desktop.Main.Tasks;
using VGList.Desktop.Services;
using VGList.Platform;
using Xunit;

namespace VGList.Tests;

public sealed class Adr0056PolishTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 22);
    private readonly string _tempDir;

    public Adr0056PolishTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vglist-adr0056-{Guid.NewGuid():N}");
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

    // ===== Item 2：删除「纯文本 / Markdown 笔记」与「待办事项 · 回车添项」冗余文案 =====

    [Fact]
    public void MainTaskDetailViewModel_HidesContentMetaWhenEmpty_AndShowsCountsWhenFilled()
    {
        var task = new TaskItem
        {
            Id = "adr0056-t1",
            Title = "提交松松记账软著",
            Date = Today,
            Priority = TaskPriority.High,
            Content = string.Empty,
        };
        var repo = new InMemoryTaskRepository([task]);

        var detail = new MainTaskDetailViewModel(repo, task, Today, () => { }, _ => { });

        // 空正文不显示「纯文本 / Markdown 笔记」
        Assert.Equal(string.Empty, detail.ContentMetaLabel);
        Assert.False(detail.HasContentMeta);

        // 填入正文后显示真实字数
        detail.Content = "你好世界！";
        Assert.Equal("5 字", detail.ContentMetaLabel);
        Assert.True(detail.HasContentMeta);

        // 切到空待办事项模式也不显示「待办事项 · 回车添项」
        detail.Content = string.Empty;
        detail.ToggleChecklistMode();
        Assert.Equal(string.Empty, detail.ContentMetaLabel);
        Assert.False(detail.HasContentMeta);

        // 填写子待办后显示完成进度
        detail.Subtasks[0].Title = "准备源码文档";
        detail.Subtasks[0].IsCompleted = true;
        var second = detail.AddSubtaskAfter(detail.Subtasks[0]);
        second.Title = "准备说明书";
        Assert.Contains("1/2", detail.ContentMetaLabel);
        Assert.True(detail.HasContentMeta);
    }

    // ===== Item 3：2026 官方默认模型目录 + 自动识别图片输入 + 新增自定义模型 =====

    [Fact]
    public void ProviderCatalog_2026Presets_HaveOfficialDefaultModelsAndVisionCapabilities()
    {
        var catalog = ProviderCatalog.Shared;

        // DeepSeek：默认 deepseek-flash（支持图片输入），同时提供 deepseek-v4-pro（纯文本）
        var deepseek = catalog.FindById("deepseek");
        Assert.NotNull(deepseek);
        var dsOpenAi = deepseek!.Variants.First(v => v.Id == "openai");
        Assert.Equal("deepseek-flash", dsOpenAi.DefaultModel);
        Assert.True(dsOpenAi.SupportsImages);
        Assert.Contains(dsOpenAi.EffectiveModels, m => m.Id == "deepseek-flash" && m.SupportsImages);
        Assert.Contains(dsOpenAi.EffectiveModels, m => m.Id == "deepseek-v4-pro" && !m.SupportsImages);

        // 自动视觉能力判定
        Assert.True(catalog.TryGetKnownSupportsImages("deepseek", "deepseek-flash", out var dsFlashVision));
        Assert.True(dsFlashVision);
        Assert.True(catalog.TryGetKnownSupportsImages("deepseek", "deepseek-v4-pro", out var dsProVision));
        Assert.False(dsProVision);
        Assert.True(catalog.TryGetKnownSupportsImages("deepseek", "deepseek-pro", out var dsShortProVision));
        Assert.False(dsShortProVision);

        // 其余主流厂商 2026 默认模型与视觉能力核验
        AssertDefaultModel(catalog, "openai", "gpt-5.4", expectVision: true);
        AssertDefaultModel(catalog, "anthropic", "claude-sonnet-5-5", expectVision: true);
        AssertDefaultModel(catalog, "gemini", "gemini-3-flash", expectVision: true);
        AssertDefaultModel(catalog, "kimi", "kimi-k3", expectVision: true);
        AssertDefaultModel(catalog, "zhipu-glm", "glm-5.3-flash", expectVision: true);
        AssertDefaultModel(catalog, "qianwenai", "qwen3.5-plus", expectVision: true);
        AssertDefaultModel(catalog, "qwencloud", "qwen3.5-plus", expectVision: true);
        AssertDefaultModel(catalog, "volcengine", "doubao-seed-2-0-lite-260428", expectVision: true);
        AssertDefaultModel(catalog, "minimax", "MiniMax-M3.1-Flash", expectVision: true);
        AssertDefaultModel(catalog, "stepfun", "step-3.7-flash", expectVision: true);
        AssertDefaultModel(catalog, "xiaomi-mimo", "mimo-v2.6-flash", expectVision: true);
        AssertDefaultModel(catalog, "longcat", "LongCat-2.5", expectVision: true);
    }

    private static void AssertDefaultModel(ProviderCatalog catalog, string providerId, string expectedModel, bool expectVision)
    {
        var preset = catalog.FindById(providerId);
        Assert.NotNull(preset);
        var variant = preset!.Variants[0];
        Assert.Equal(expectedModel, variant.DefaultModel);
        Assert.Equal(expectVision, variant.SupportsImages);
        Assert.True(catalog.TryGetKnownSupportsImages(providerId, expectedModel, out var known));
        Assert.Equal(expectVision, known);
    }

    [Fact]
    public void ModelProfileStore_AutoUpgradesLegacyDeepSeekFlashVisionCapabilityOnLoadAndResolve()
    {
        var file = Path.Combine(_tempDir, "legacy-models.json");
        var legacyJson = JsonSerializer.Serialize(new
        {
            schema = 1,
            activeProfileId = "legacy-ds",
            profiles = new[]
            {
                new
                {
                    id = "legacy-ds",
                    presetId = "deepseek",
                    variantId = "openai",
                    name = "DeepSeek",
                    baseUrl = "https://api.deepseek.com",
                    protocol = "OpenAi",
                    model = "deepseek-flash",
                    supportsImages = false,
                    encryptedApiKey = "sk-legacy-key",
                    maskedKey = "••••-key",
                    lastTestPassed = true,
                },
            },
        });
        File.WriteAllText(file, legacyJson, Encoding.UTF8);

        var store = new ModelProfileStore(new PlainSecretProtector(), file);
        Assert.NotNull(store.ActiveProfile);
        Assert.True(store.ActiveProfile!.SupportsImages);

        Assert.True(store.TryResolveActive(out var resolved, out var error));
        Assert.Null(error);
        Assert.True(resolved!.SupportsImages);
    }

    [Fact]
    public void ModelServicesViewModel_SupportsPresetPickingAutoVisionAndCustomModelPersistence()
    {
        var file = Path.Combine(_tempDir, "models-custom.json");
        var store = new ModelProfileStore(new PlainSecretProtector(), file);
        var toast = new ToastService();
        using var vm = new ModelServicesViewModel(store, new DummyConnectionTester(), toast);

        var deepseekCard = vm.ProviderCards.First(c => c.Id == "deepseek");
        vm.OpenCard(deepseekCard);

        Assert.Equal("deepseek-flash", vm.ModelName);
        Assert.True(vm.SupportsImages);
        Assert.True(vm.IsSupportsImagesAutoDetected);
        Assert.Contains(vm.PresetModels, m => m.Id == "deepseek-flash" && m.IsSelected && m.SupportsImages);
        Assert.Contains(vm.PresetModels, m => m.Id == "deepseek-v4-pro" && !m.SupportsImages);

        // 切换到纯文本模型 deepseek-v4-pro → 自动关闭图片输入
        var proChip = vm.PresetModels.First(m => m.Id == "deepseek-v4-pro");
        vm.PickPresetModel(proChip);
        Assert.Equal("deepseek-v4-pro", vm.ModelName);
        Assert.False(vm.SupportsImages);
        Assert.True(vm.IsSupportsImagesAutoDetected);

        // 切回 deepseek-flash → 自动重新开启图片输入
        var flashChip = vm.PresetModels.First(m => m.Id == "deepseek-flash");
        vm.PickPresetModel(flashChip);
        Assert.True(vm.SupportsImages);

        // 新增自定义模型并持久化到磁盘
        vm.NewModelInput = "deepseek-my-vision-preview";
        vm.AddCustomModel();
        Assert.Equal(string.Empty, vm.NewModelInput);
        Assert.Equal("deepseek-my-vision-preview", vm.ModelName);
        Assert.True(vm.SupportsImages); // 含 vision 关键字自动识别为支持图片
        Assert.Contains(vm.PresetModels, m => m.Id == "deepseek-my-vision-preview" && m.IsCustomAdded);

        // 重新加载 Store 后自定义模型依然保留
        var reloadedStore = new ModelProfileStore(new PlainSecretProtector(), file);
        Assert.Contains("deepseek-my-vision-preview", reloadedStore.GetCustomModels("deepseek"));

        // 删除自定义模型
        var customChip = vm.PresetModels.First(m => m.Id == "deepseek-my-vision-preview");
        vm.RemoveCustomModel(customChip);
        Assert.DoesNotContain(vm.PresetModels, m => m.Id == "deepseek-my-vision-preview");
        var reloadedAfterRemove = new ModelProfileStore(new PlainSecretProtector(), file);
        Assert.DoesNotContain("deepseek-my-vision-preview", reloadedAfterRemove.GetCustomModels("deepseek"));
    }

    // ===== Item 4：@ 仅引用输入框内已添加的图片/文件 =====

    [Fact]
    public void AssistantPageViewModel_MentionReferencesOnlyDraftAttachments_AndSupportsChinesePrefix()
    {
        var repo = new InMemoryTaskRepository();
        var store = new ModelProfileStore(new PlainSecretProtector(), Path.Combine(_tempDir, "mention-models.json"));
        var toast = new ToastService();
        using var vm = new AssistantPageViewModel(repo, store, toast, () => { }, Today);

        // 输入框尚无附件时，输入 @ 不弹出（不再扫描电脑桌面/文档）
        vm.UpdateMentionState("@", 1);
        Assert.False(vm.IsMentionOpen);
        Assert.Empty(vm.MentionCandidates);

        // 添加 2 个草稿附件
        var reportPath = Path.Combine(_tempDir, "周报计划.md");
        var notesPath = Path.Combine(_tempDir, "会议纪要.txt");
        File.WriteAllText(reportPath, "# 周报\n- 完成发布");
        File.WriteAllText(notesPath, "明天下午三点开评审会");
        vm.AddAttachmentFiles([reportPath, notesPath]);
        Assert.Equal(2, vm.DraftAttachments.Count);

        // 紧跟中文后输入 @ 应弹出且仅列出这 2 个草稿附件（无浏览本地电脑项）
        vm.UpdateMentionState("帮我看看@", 5);
        Assert.True(vm.IsMentionOpen);
        Assert.Equal(2, vm.MentionCandidates.Count);
        Assert.All(vm.MentionCandidates, c => Assert.False(c.IsBrowseAction));

        // 关键词过滤
        vm.UpdateMentionState("帮我看看@周报", 7);
        Assert.True(vm.IsMentionOpen);
        Assert.Single(vm.MentionCandidates);
        Assert.Equal("周报计划.md", vm.MentionCandidates[0].Name);

        // 选中候选 → 插入 @文件名 + 空格，不重复添加附件，也不弹窗浏览电脑
        var applied = vm.ApplyMentionCandidate(null, "帮我看看@周报", out var needsBrowse, out var updatedText, out var updatedCaret);
        Assert.True(applied);
        Assert.False(needsBrowse);
        Assert.Equal("帮我看看@周报计划.md ", updatedText);
        Assert.Equal(updatedText.Length, updatedCaret);
        Assert.Equal(2, vm.DraftAttachments.Count);
        Assert.False(vm.IsMentionOpen);

        // 邮箱地址不误触发 @
        vm.UpdateMentionState("联系 user@example.com", "联系 user@example.com".Length);
        Assert.False(vm.IsMentionOpen);
    }

    // ===== Item 6：附件提示改为 Toast 通知 + 已支持视觉模型不误报 =====

    [Fact]
    public void AssistantPageViewModel_RoutesAttachmentWarningsToToast_AndRespectsKnownVisionModel()
    {
        var repo = new InMemoryTaskRepository();
        var storePath = Path.Combine(_tempDir, "toast-models.json");
        var store = new ModelProfileStore(new PlainSecretProtector(), storePath);
        store.SaveProfile(
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
            "sk-test-1234",
            setAsActive: true);

        var toast = new ToastService();
        using var vm = new AssistantPageViewModel(repo, store, toast, () => { }, Today);

        // 不支持的扩展名通过 Toast 通知，不在输入框上方驻留 AttachmentNotice
        var exePath = Path.Combine(_tempDir, "installer.exe");
        File.WriteAllBytes(exePath, [0x4D, 0x5A, 0x90, 0x00]);
        vm.AddAttachmentFiles([exePath]);
        Assert.Null(vm.AttachmentNotice);
        Assert.True(toast.IsVisible);
        Assert.Contains(".exe", toast.Message);

        toast.Dismiss();

        // 给 deepseek-flash 添加合法 PNG → 不弹「不支持图片输入」提示
        var pngPath = Path.Combine(_tempDir, "shot.png");
        File.WriteAllBytes(pngPath, CreateMinimalPng());
        vm.AddAttachmentFiles([pngPath]);
        Assert.Single(vm.DraftAttachments);
        Assert.False(toast.IsVisible);

        // 切换到纯文本 deepseek-v4-pro → 添加图片触发 Toast 提醒（且自动消失式 Toast，不驻留 AttachmentNotice）
        store.SaveProfile(
            new ModelProfile
            {
                PresetId = "deepseek",
                VariantId = "openai",
                Name = "DeepSeek Pro",
                BaseUrl = "https://api.deepseek.com",
                Protocol = ModelProtocol.OpenAi,
                Model = "deepseek-v4-pro",
                SupportsImages = false,
            },
            "sk-test-5678",
            setAsActive: true);

        var png2Path = Path.Combine(_tempDir, "shot2.png");
        File.WriteAllBytes(png2Path, CreateMinimalPng());
        vm.AddAttachmentFiles([png2Path]);
        Assert.Null(vm.AttachmentNotice);
        Assert.True(toast.IsVisible);
        Assert.Contains("不支持图片输入", toast.Message);
    }

    // ===== Item 7：更多文件格式解析（HTML / XLSX / PPTX / EPUB / RTF / ICS / YAML / 代码） =====

    [Fact]
    public void AttachmentProcessor_ExtractsTextFromExpandedFileFormats()
    {
        // 1. HTML（去除 script/style 并解码实体）
        var htmlPath = Path.Combine(_tempDir, "schedule.html");
        File.WriteAllText(
            htmlPath,
            "<html><head><style>body{color:red;}</style><script>alert(1)</script></head>" +
            "<body><h1>季度评审 &amp; 发布</h1><p>周五 15:00 提交版本</p></body></html>",
            Encoding.UTF8);

        // 2. XLSX（原生 Zip+XML → Markdown 表格）
        var xlsxPath = Path.Combine(_tempDir, "plan.xlsx");
        CreateSampleXlsx(xlsxPath);

        // 3. PPTX（原生 Zip+XML → 按页抽取幻灯片）
        var pptxPath = Path.Combine(_tempDir, "slides.pptx");
        CreateSamplePptx(pptxPath);

        // 4. EPUB（电子书章节抽取）
        var epubPath = Path.Combine(_tempDir, "book.epub");
        CreateSampleEpub(epubPath);

        // 5. RTF（剥离控制字并保留文本）
        var rtfPath = Path.Combine(_tempDir, "memo.rtf");
        File.WriteAllText(rtfPath, @"{\rtf1\ansi\deff0 {\fonttbl {\f0 Arial;}} \b Weekly Review \b0 \par Finish API migration}", Encoding.ASCII);

        // 6. ICS / YAML / C#
        var icsPath = Path.Combine(_tempDir, "event.ics");
        File.WriteAllText(icsPath, "BEGIN:VCALENDAR\nBEGIN:VEVENT\nSUMMARY:项目复盘会\nEND:VEVENT\nEND:VCALENDAR", Encoding.UTF8);
        var yamlPath = Path.Combine(_tempDir, "config.yaml");
        File.WriteAllText(yamlPath, "release:\n  target: 2026-12-01\n", Encoding.UTF8);
        var csPath = Path.Combine(_tempDir, "Sample.cs");
        File.WriteAllText(csPath, "public record TodoItem(string Title);", Encoding.UTF8);

        // 全格式都被 AttachmentFormats 识别
        Assert.True(AttachmentFormats.IsSupported(".html"));
        Assert.True(AttachmentFormats.IsSupported(".xlsx"));
        Assert.True(AttachmentFormats.IsSupported(".pptx"));
        Assert.True(AttachmentFormats.IsSupported(".epub"));
        Assert.True(AttachmentFormats.IsSupported(".rtf"));
        Assert.True(AttachmentFormats.IsSupported(".ics"));
        Assert.True(AttachmentFormats.IsSupported(".yaml"));
        Assert.True(AttachmentFormats.IsSupported(".cs"));
        Assert.False(AttachmentFormats.IsSupported(".exe"));

        var batch1 = AttachmentProcessor.Process([ToRef(htmlPath), ToRef(xlsxPath), ToRef(pptxPath), ToRef(epubPath)], modelSupportsImages: true);
        var batch2 = AttachmentProcessor.Process([ToRef(rtfPath), ToRef(icsPath), ToRef(yamlPath), ToRef(csPath)], modelSupportsImages: true);

        Assert.Empty(batch1.Warnings);
        Assert.Empty(batch2.Warnings);
        Assert.Equal(4, batch1.Documents.Count);
        Assert.Equal(4, batch2.Documents.Count);

        var htmlDoc = batch1.Documents.Single(d => d.Name == "schedule.html");
        Assert.Contains("季度评审 & 发布", htmlDoc.Content);
        Assert.Contains("周五 15:00 提交版本", htmlDoc.Content);
        Assert.DoesNotContain("alert(1)", htmlDoc.Content);
        Assert.DoesNotContain("color:red", htmlDoc.Content);

        var xlsxDoc = batch1.Documents.Single(d => d.Name == "plan.xlsx");
        Assert.Contains("任务名称", xlsxDoc.Content);
        Assert.Contains("交付日期", xlsxDoc.Content);
        Assert.Contains("上线灰度", xlsxDoc.Content);
        Assert.Contains("| --- |", xlsxDoc.Content);

        var pptxDoc = batch1.Documents.Single(d => d.Name == "slides.pptx");
        Assert.Contains("第 1 页", pptxDoc.Content);
        Assert.Contains("Plana 路线图", pptxDoc.Content);
        Assert.Contains("第 2 页", pptxDoc.Content);
        Assert.Contains("下周行动项", pptxDoc.Content);

        var epubDoc = batch1.Documents.Single(d => d.Name == "book.epub");
        Assert.Contains("第一章：时间管理原则", epubDoc.Content);

        var rtfDoc = batch2.Documents.Single(d => d.Name == "memo.rtf");
        Assert.Contains("Weekly Review", rtfDoc.Content);
        Assert.Contains("Finish API migration", rtfDoc.Content);

        var icsDoc = batch2.Documents.Single(d => d.Name == "event.ics");
        Assert.Contains("项目复盘会", icsDoc.Content);

        var yamlDoc = batch2.Documents.Single(d => d.Name == "config.yaml");
        Assert.Contains("2026-12-01", yamlDoc.Content);

        var csDoc = batch2.Documents.Single(d => d.Name == "Sample.cs");
        Assert.Contains("TodoItem", csDoc.Content);
    }

    private static void CreateSampleXlsx(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteZipEntry(zip, "xl/workbook.xml",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheets><sheet name="排期表" sheetId="1" /></sheets>
            </workbook>
            """);
        WriteZipEntry(zip, "xl/sharedStrings.xml",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <si><t>任务名称</t></si>
              <si><t>交付日期</t></si>
              <si><t>上线灰度</t></si>
              <si><t>11月25日</t></si>
            </sst>
            """);
        WriteZipEntry(zip, "xl/worksheets/sheet1.xml",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" t="s"><v>0</v></c>
                  <c r="B1" t="s"><v>1</v></c>
                </row>
                <row r="2">
                  <c r="A2" t="s"><v>2</v></c>
                  <c r="B2" t="s"><v>3</v></c>
                </row>
              </sheetData>
            </worksheet>
            """);
    }

    private static void CreateSamplePptx(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteZipEntry(zip, "ppt/slides/slide1.xml",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                   xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <p:cSld><p:spTree><p:sp><p:txBody>
                <a:p><a:r><a:t>Plana 路线图</a:t></a:r></a:p>
              </p:txBody></p:sp></p:spTree></p:cSld>
            </p:sld>
            """);
        WriteZipEntry(zip, "ppt/slides/slide2.xml",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                   xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <p:cSld><p:spTree><p:sp><p:txBody>
                <a:p><a:r><a:t>下周行动项</a:t></a:r></a:p>
              </p:txBody></p:sp></p:spTree></p:cSld>
            </p:sld>
            """);
    }

    private static void CreateSampleEpub(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteZipEntry(zip, "OEBPS/ch1.xhtml",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml">
              <body><h2>第一章：时间管理原则</h2><p>优先完成高价值任务。</p></body>
            </html>
            """);
    }

    private static AttachmentRef ToRef(string path)
    {
        var fi = new FileInfo(path);
        return new AttachmentRef(fi.Name, fi.FullName, fi.Length, AttachmentFormats.GetKindLabel(fi.Extension));
    }

    private static void WriteZipEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static byte[] CreateMinimalPng() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");

    private sealed class PlainSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public bool TryUnprotect(string ciphertext, out string plaintext)
        {
            plaintext = ciphertext;
            return true;
        }
    }

    private sealed class DummyConnectionTester : IModelConnectionTester
    {
        public Task<ConnectionTestResult> TestAsync(
            string baseUrl,
            ModelProtocol protocol,
            string apiKey,
            string model,
            bool needsKey,
            CancellationToken ct = default) =>
            Task.FromResult(new ConnectionTestResult(true, "OK", 200, 50));

        public Task<ModelListResult> FetchModelsAsync(
            string baseUrl,
            ModelProtocol protocol,
            string apiKey,
            CancellationToken ct = default) =>
            Task.FromResult(new ModelListResult(true, []));
    }
}

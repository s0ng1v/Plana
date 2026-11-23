using System.Net;
using System.Security.Authentication;
using System.Text;
using VGList.App.Models;
using VGList.Desktop.Main.Settings;
using VGList.Desktop.Services;
using Xunit;

namespace VGList.Tests;

public sealed class ModelServicesTests : IDisposable
{
    private readonly string _tempDir;

    public ModelServicesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vglist-models-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void ProviderCatalog_IsValidAndContainsAllExpectedPresets()
    {
        var catalog = ProviderCatalog.Shared;
        Assert.True(catalog.Providers.Count >= 20);

        var ids = catalog.Providers.Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var p in catalog.Providers)
        {
            Assert.NotEmpty(p.Name);
            Assert.NotEmpty(p.Domain);
            Assert.NotEmpty(p.Variants);

            foreach (var v in p.Variants)
            {
                Assert.NotEmpty(v.Id);
                Assert.NotEmpty(v.Name);
                if (!string.IsNullOrWhiteSpace(v.BaseUrl))
                {
                    Assert.True(ProviderCatalog.IsValidBaseUrl(v.BaseUrl, out var err), $"{p.Id}/{v.Id}: {err}");
                    Assert.False(string.IsNullOrWhiteSpace(v.SourceUrl), $"{p.Id}/{v.Id} 缺少 sourceUrl");
                    Assert.False(string.IsNullOrWhiteSpace(v.VerifiedAt), $"{p.Id}/{v.Id} 缺少 verifiedAt");
                }
            }
        }

        // 核验关键厂商存在且 Ollama 免 Key
        Assert.NotNull(catalog.FindById("deepseek"));
        Assert.NotNull(catalog.FindById("kimi"));
        Assert.NotNull(catalog.FindById("volcengine"));
        Assert.NotNull(catalog.FindById("zhipu-glm"));
        var ollama = catalog.FindById("ollama");
        Assert.NotNull(ollama);
        Assert.False(ollama!.NeedsKey);
    }

    [Fact]
    public void BaseUrlValidation_AllowsHttpsAndLocalhostHttpOnly()
    {
        Assert.True(ProviderCatalog.IsValidBaseUrl("https://api.deepseek.com", out _));
        Assert.True(ProviderCatalog.IsValidBaseUrl("http://localhost:11434/v1", out _));
        Assert.True(ProviderCatalog.IsValidBaseUrl("http://127.0.0.1:8080/v1", out _));

        Assert.False(ProviderCatalog.IsValidBaseUrl("http://api.example.com/v1", out _));
        Assert.False(ProviderCatalog.IsValidBaseUrl("not-a-url", out _));
        Assert.False(ProviderCatalog.IsValidBaseUrl("", out _));
    }

    [Fact]
    public void ModelProfileStore_EncryptsKeyWithDpapi_NeverWritesPlaintext_AndRecoversFromCorruption()
    {
        var file = Path.Combine(_tempDir, "models.json");
        var protector = new WindowsSecretProtector();
        const string secretKey = "sk-super-secret-plana-key-987654321";

        var store = new ModelProfileStore(protector, file);
        var saved = store.SaveProfile(
            new ModelProfile
            {
                PresetId = "deepseek",
                VariantId = "openai",
                Name = "DeepSeek",
                BaseUrl = "https://api.deepseek.com",
                Protocol = ModelProtocol.OpenAi,
                Model = "deepseek-chat",
                LastTestPassed = true,
            },
            secretKey,
            setAsActive: true);

        Assert.Equal("••••4321", saved.MaskedKey);

        // 文件里绝对不能出现明文 Key
        var rawFile = File.ReadAllText(file);
        Assert.DoesNotContain(secretKey, rawFile, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", rawFile, StringComparison.Ordinal);

        // 重新加载后可完整解密
        var reloaded = new ModelProfileStore(protector, file);
        Assert.True(reloaded.TryResolveActive(out var resolved, out var err));
        Assert.Null(err);
        Assert.Equal(secretKey, resolved!.ApiKey);

        // 删除当前激活项后回到未配置
        reloaded.DeleteProfile(saved.Id);
        Assert.Null(reloaded.ActiveProfile);

        // 损坏文件恢复
        File.WriteAllText(file, "{ broken json !!!");
        var recovered = new ModelProfileStore(protector, file);
        Assert.True(recovered.RecoveredFromCorruption);
        Assert.Empty(recovered.Profiles);
        Assert.Contains(Directory.GetFiles(_tempDir), f => f.Contains(".bad-"));
    }

    [Fact]
    public async Task ConnectionTester_MapsAllErrorKinds_RetriesMaxCompletionTokens_AndRedactsKey()
    {
        const string fakeKey = "sk-test-redact-me-123456";
        var callCount = 0;

        // 1. 400 -> max_completion_tokens 自动切换重试成功
        var retryTester = new ModelConnectionTester(new HttpClient(new StubHandler((req, body) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return JsonResponse(HttpStatusCode.BadRequest, """{"error":{"message":"use max_completion_tokens instead"}}""");
            }

            Assert.Contains("max_completion_tokens", body);
            return JsonResponse(HttpStatusCode.OK, """{"id":"ok"}""");
        })));
        var ok = await retryTester.TestAsync("https://api.openai.com/v1", ModelProtocol.OpenAi, fakeKey, "o3-mini", true);
        Assert.True(ok.Success);
        Assert.Equal(2, callCount);

        // 2. 401 Key 无效且脱敏 Key
        var unauth = await new ModelConnectionTester(new HttpClient(new StubHandler((_, _) =>
            JsonResponse(HttpStatusCode.Unauthorized, $$$"""{"error":{"message":"Invalid key {{{fakeKey}}}"}}"""))))
            .TestAsync("https://api.deepseek.com", ModelProtocol.OpenAi, fakeKey, "deepseek-chat", true);
        Assert.False(unauth.Success);
        Assert.Contains("无效", unauth.Message);
        Assert.DoesNotContain(fakeKey, unauth.Message);

        // 3. 404 模型不存在
        var notFound = await new ModelConnectionTester(new HttpClient(new StubHandler((_, _) =>
            JsonResponse(HttpStatusCode.NotFound, """{"error":{"message":"model not found"}}"""))))
            .TestAsync("https://api.deepseek.com", ModelProtocol.OpenAi, fakeKey, "bad-model", true);
        Assert.False(notFound.Success);
        Assert.Contains("不存在", notFound.Message);

        // 4. 429 限流/额度不足
        var rateLimited = await new ModelConnectionTester(new HttpClient(new StubHandler((_, _) =>
            JsonResponse(HttpStatusCode.TooManyRequests, """{"error":{"message":"quota exceeded"}}"""))))
            .TestAsync("https://api.deepseek.com", ModelProtocol.OpenAi, fakeKey, "deepseek-chat", true);
        Assert.False(rateLimited.Success);
        Assert.Contains("额度", rateLimited.Message);

        // 5. Anthropic 协议请求头检查
        string? seenApiKeyHeader = null;
        string? seenVersionHeader = null;
        var anthropicRes = await new ModelConnectionTester(new HttpClient(new StubHandler((req, _) =>
        {
            seenApiKeyHeader = req.Headers.TryGetValues("x-api-key", out var k) ? k.FirstOrDefault() : null;
            seenVersionHeader = req.Headers.TryGetValues("anthropic-version", out var v) ? v.FirstOrDefault() : null;
            return JsonResponse(HttpStatusCode.OK, """{"id":"msg_1"}""");
        }))).TestAsync("https://api.anthropic.com", ModelProtocol.Anthropic, fakeKey, "claude-sonnet-4-5", true);
        Assert.True(anthropicRes.Success);
        Assert.Equal(fakeKey, seenApiKeyHeader);
        Assert.Equal("2023-06-01", seenVersionHeader);

        // 6. 证书错误映射
        var certRes = await new ModelConnectionTester(new HttpClient(new StubHandler((_, _) =>
            throw new HttpRequestException("ssl", new AuthenticationException("cert")))))
            .TestAsync("https://api.deepseek.com", ModelProtocol.OpenAi, fakeKey, "deepseek-chat", true);
        Assert.False(certRes.Success);
        Assert.Contains("证书", certRes.Message);
    }

    [Fact]
    public async Task ModelServicesViewModel_BlocksActivationUntilTestPasses()
    {
        var store = new ModelProfileStore(new WindowsSecretProtector(), Path.Combine(_tempDir, "vm-models.json"));
        var tester = new ModelConnectionTester(new HttpClient(new StubHandler((_, _) =>
            JsonResponse(HttpStatusCode.OK, """{"id":"ok"}"""))));
        var vm = new ModelServicesViewModel(store, tester, new ToastService());

        var deepseekCard = vm.ProviderCards.Single(c => c.Id == "deepseek");
        vm.OpenCard(deepseekCard);
        Assert.True(vm.IsDrawerOpen);
        Assert.False(vm.CanActivate);

        vm.ApiKeyInput = "sk-demo-key-9999";
        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.True(vm.TestPassed);
        Assert.True(vm.CanActivate);

        vm.ActivateProfileCommand.Execute(null);
        Assert.True(vm.HasActiveProfile);
        Assert.Equal("DeepSeek", vm.ActiveProviderName);
        Assert.True(deepseekCard.IsActive);

        // 修改模型名后必须重新测试才能激活
        vm.ModelName = "deepseek-reasoner";
        Assert.False(vm.CanActivate);
    }

    [Fact]
    public void LazyLoadingInvariant_ResidentAssembliesDoNotReferenceAgentOrAiSdks()
    {
        // 常驻层（Core / Data / App / Widgets / Platform.Win）严禁直接引用 VGList.Agent / MEAI / PdfPig / 厂商 SDK
        var residentAssemblies = new[]
        {
            typeof(VGList.Core.Models.TaskItem).Assembly,
            typeof(VGList.Data.SqliteDatabase).Assembly,
            typeof(ProviderCatalog).Assembly,
            typeof(VGList.Widgets.Today.TodayWidgetViewModel).Assembly,
            typeof(VGList.Platform.Win.DpapiSecretProtector).Assembly,
        };

        foreach (var asm in residentAssemblies)
        {
            var refs = asm.GetReferencedAssemblies().Select(r => r.Name ?? string.Empty).ToList();
            Assert.DoesNotContain(refs, n => n.StartsWith("VGList.Agent", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(refs, n => n.StartsWith("Microsoft.Extensions.AI", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(refs, n => n.Contains("PdfPig", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(refs, n => n.Equals("OpenAI", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(refs, n => n.Equals("Anthropic", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(refs, n => n.StartsWith("ModelContextProtocol", StringComparison.OrdinalIgnoreCase));
        }

        // Desktop 层仅允许通过 AssistantHostFactory（NoInlining）隔离懒加载 VGList.Agent
        var factoryCreate = typeof(VGList.Desktop.Services.AssistantHostFactory).GetMethod("Create");
        Assert.NotNull(factoryCreate);
        Assert.True(factoryCreate!.MethodImplementationFlags.HasFlag(System.Reflection.MethodImplAttributes.NoInlining));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

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

    private sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request, body);
        }
    }
}

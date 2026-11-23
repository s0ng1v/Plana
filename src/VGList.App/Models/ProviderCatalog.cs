using System.Reflection;
using System.Text.Json;

namespace VGList.App.Models;

/// <summary>随包厂商目录（Assets/Providers/providers.json），数据驱动，新增厂商无需改代码。</summary>
public sealed class ProviderCatalog
{
    private static readonly Lazy<ProviderCatalog> SharedLazy = new(LoadEmbedded);

    private ProviderCatalog(int schema, IReadOnlyList<ProviderPreset> providers)
    {
        Schema = schema;
        Providers = providers;
    }

    public static ProviderCatalog Shared => SharedLazy.Value;

    public int Schema { get; }

    public IReadOnlyList<ProviderPreset> Providers { get; }

    public ProviderPreset? FindById(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : Providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 按官方目录与模型族命名规范自动识别模型是否支持图片输入（多模态）。
    /// 返回 true 表示有明确依据（目录精确匹配或已知官方模型族），false 表示未知自定义模型。
    /// </summary>
    public bool TryGetKnownSupportsImages(string? presetId, string? modelId, out bool supportsImages)
    {
        supportsImages = false;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        var trimmed = modelId.Trim();

        // 1. 优先在指定厂商预设目录中精确匹配
        if (!string.IsNullOrWhiteSpace(presetId) && FindById(presetId) is { } preset)
        {
            foreach (var v in preset.Variants)
            {
                foreach (var m in v.EffectiveModels)
                {
                    if (string.Equals(m.Id, trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        supportsImages = m.SupportsImages;
                        return true;
                    }
                }
            }
        }

        // 2. 在全部官方预设目录中精确匹配（兼容 OpenRouter / 自定义中转站复用官方模型 ID）
        var normalizedBare = trimmed.Contains('/') ? trimmed[(trimmed.LastIndexOf('/') + 1)..] : trimmed;
        foreach (var p in Providers)
        {
            foreach (var v in p.Variants)
            {
                foreach (var m in v.EffectiveModels)
                {
                    if (string.Equals(m.Id, trimmed, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(m.Id, normalizedBare, StringComparison.OrdinalIgnoreCase))
                    {
                        supportsImages = m.SupportsImages;
                        return true;
                    }
                }
            }
        }

        // 3. 官方明确文档记载的模型族前缀与模态特征（先判明确纯文本，再判原生多模态）
        var lower = normalizedBare.ToLowerInvariant();

        string[] knownTextOnlyPrefixes =
        [
            "deepseek-v4-pro", "deepseek-pro", "deepseek-chat", "deepseek-reasoner", "deepseek-r1", "deepseek-v3",
            "ernie-x1", "step-3.5-flash", "minimax-m1", "minimax-m2", "longcat-flash-chat", "longcat-flash-thinking",
            "glm-4.5-air", "glm-4-air", "kimi-k2-0905", "o1-mini", "o3-mini",
        ];
        if (knownTextOnlyPrefixes.Any(k => lower.StartsWith(k, StringComparison.Ordinal)))
        {
            supportsImages = false;
            return true;
        }

        // 明确的纯文本旗舰 GLM-5.3 / GLM-5.2 / GLM-5.1（排除 flash / v 视觉版）
        if ((lower is "glm-5.3" or "glm-5.2" or "glm-5.1" or "glm-4.7" or "glm-4.6") &&
            !lower.Contains("flash", StringComparison.Ordinal) &&
            !lower.Contains("v", StringComparison.Ordinal))
        {
            supportsImages = false;
            return true;
        }

        string[] knownVisionPatterns =
        [
            "deepseek-flash", "deepseek-v4-flash",
            "gpt-4o", "gpt-4.1", "gpt-5", "gpt-6", "o3", "o4-mini",
            "claude-",
            "gemini-",
            "qwen4", "qwen3.5", "qwen3.7", "qwen3.8", "qwen-vl", "qwen2.5vl", "qwen2.5-vl", "qwen-plus", "qwen-max",
            "glm-5.3-flash", "glm-5v", "glm-4.6v", "glm-4v",
            "kimi-k3", "kimi-k2.5",
            "doubao-seed", "skylark-pro",
            "mimo-v2.6", "mimo-v2-omni",
            "step-3.7", "step-5", "step-1o",
            "minimax-m3", "minimax-h3",
            "longcat-2.5", "longcat-flash-omni",
            "ming-",
            "ernie-5", "ernie-4.5", "qianfan-vl",
            "hunyuan-vision", "hunyuan-t1-vision", "hunyuan-turbos",
            "llama-4", "llama3.2-vision", "gemma3", "llava", "pixtral", "internvl",
            "-vl", "-vision", "-omni",
        ];
        if (knownVisionPatterns.Any(k => lower.Contains(k, StringComparison.Ordinal)))
        {
            supportsImages = true;
            return true;
        }

        return false;
    }

    public bool InferSupportsImages(string? presetId, string? modelId, bool fallback = false) =>
        TryGetKnownSupportsImages(presetId, modelId, out var known) ? known : fallback;

    public static ProviderCatalog LoadFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var schema = root.TryGetProperty("schema", out var sEl) ? sEl.GetInt32() : 1;
        var list = new List<ProviderPreset>();

        if (root.TryGetProperty("providers", out var providersEl) && providersEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in providersEl.EnumerateArray())
            {
                var variants = new List<ProviderVariant>();
                if (p.TryGetProperty("variants", out var vArr) && vArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in vArr.EnumerateArray())
                    {
                        var protoStr = GetStr(v, "protocol");
                        var proto = string.Equals(protoStr, "anthropic", StringComparison.OrdinalIgnoreCase)
                            ? ModelProtocol.Anthropic
                            : ModelProtocol.OpenAi;

                        var defaultModel = GetStr(v, "defaultModel");
                        var variantSupportsImages = v.TryGetProperty("supportsImages", out var img) && img.GetBoolean();
                        var models = new List<PresetModel>();
                        if (v.TryGetProperty("models", out var mArr) && mArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var m in mArr.EnumerateArray())
                            {
                                var mId = GetStr(m, "id");
                                if (string.IsNullOrWhiteSpace(mId))
                                {
                                    continue;
                                }

                                var mImg = m.TryGetProperty("supportsImages", out var mImgEl)
                                    ? mImgEl.GetBoolean()
                                    : variantSupportsImages;
                                var mNote = m.TryGetProperty("note", out var mNoteEl) && mNoteEl.ValueKind == JsonValueKind.String
                                    ? mNoteEl.GetString()
                                    : null;
                                models.Add(new PresetModel(mId, mImg, mNote));
                            }
                        }

                        if (models.Count == 0 && !string.IsNullOrWhiteSpace(defaultModel))
                        {
                            models.Add(new PresetModel(defaultModel, variantSupportsImages));
                        }

                        variants.Add(new ProviderVariant(
                            Id: GetStr(v, "id"),
                            Name: GetStr(v, "name"),
                            Protocol: proto,
                            BaseUrl: GetStr(v, "baseUrl"),
                            DefaultModel: defaultModel,
                            SupportsImages: variantSupportsImages,
                            SourceUrl: v.TryGetProperty("sourceUrl", out var src) && src.ValueKind == JsonValueKind.String ? src.GetString() : null,
                            VerifiedAt: v.TryGetProperty("verifiedAt", out var ver) && ver.ValueKind == JsonValueKind.String ? ver.GetString() : null,
                            Models: models));
                    }
                }

                list.Add(new ProviderPreset(
                    Id: GetStr(p, "id"),
                    Name: GetStr(p, "name"),
                    Domain: GetStr(p, "domain"),
                    ApiKeyUrl: GetStr(p, "apiKeyUrl"),
                    Color: GetStr(p, "color", "#8F7BE5"),
                    Monogram: GetStr(p, "monogram", "A"),
                    NeedsKey: !p.TryGetProperty("needsKey", out var nk) || nk.GetBoolean(),
                    Variants: variants));
            }
        }

        return new ProviderCatalog(schema, list);
    }

    public static bool IsValidBaseUrl(string? url, out string? error)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            error = "请填写接口地址（Base URL）";
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            error = "接口地址格式不正确，例如 https://api.example.com/v1";
            return false;
        }

        if (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            error = null;
            return true;
        }

        if (string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase)))
        {
            error = null;
            return true;
        }

        error = "接口地址必须使用 https（仅本机 localhost / 127.0.0.1 允许 http）";
        return false;
    }

    private static ProviderCatalog LoadEmbedded()
    {
        var asm = Assembly.GetExecutingAssembly();
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("providers.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("未找到内置模型厂商目录 providers.json。");

        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return LoadFromJson(reader.ReadToEnd());
    }

    private static string GetStr(JsonElement el, string prop, string fallback = "") =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? fallback
            : fallback;
}

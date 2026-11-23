namespace VGList.App.Models;

/// <summary>模型 API 协议格式。</summary>
public enum ModelProtocol
{
    OpenAi,
    Anthropic,
}

/// <summary>厂商预设模型项（含官方模型 ID、是否原生支持图片输入与简短说明）。</summary>
public sealed record PresetModel(
    string Id,
    bool SupportsImages,
    string? Note = null);

/// <summary>单个厂商接入版本（如国内站 / 国际站 / Coding 套餐）。</summary>
public sealed record ProviderVariant(
    string Id,
    string Name,
    ModelProtocol Protocol,
    string BaseUrl,
    string DefaultModel,
    bool SupportsImages,
    string? SourceUrl,
    string? VerifiedAt,
    IReadOnlyList<PresetModel>? Models = null)
{
    public IReadOnlyList<PresetModel> EffectiveModels =>
        Models is { Count: > 0 }
            ? Models
            : (string.IsNullOrWhiteSpace(DefaultModel) ? [] : [new PresetModel(DefaultModel, SupportsImages)]);
}

/// <summary>随包目录中的单个预设厂商条目。</summary>
public sealed record ProviderPreset(
    string Id,
    string Name,
    string Domain,
    string ApiKeyUrl,
    string Color,
    string Monogram,
    bool NeedsKey,
    IReadOnlyList<ProviderVariant> Variants)
{
    public bool RequiresManualUrl => Variants.All(v => string.IsNullOrWhiteSpace(v.BaseUrl));

    public string SubtitleText => Variants.Count > 1
        ? $"{Domain} · {Variants.Count} 个版本"
        : Domain;
}

/// <summary>持久化在 models.json 中的用户模型配置实例（仅存密文与末 4 位，永不存明文 Key）。</summary>
public sealed class ModelProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string? PresetId { get; set; }

    public string? VariantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public ModelProtocol Protocol { get; set; } = ModelProtocol.OpenAi;

    public string Model { get; set; } = string.Empty;

    public bool SupportsImages { get; set; }

    public string ApiKeyCipher { get; set; } = string.Empty;

    public string ApiKeyLast4 { get; set; } = string.Empty;

    public bool LastTestPassed { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public bool IsCustom => string.IsNullOrWhiteSpace(PresetId);

    public string MaskedKey => string.IsNullOrEmpty(ApiKeyLast4) ? string.Empty : $"••••{ApiKeyLast4}";
}

/// <summary>调用时解密后的当前激活模型配置（用完即弃，禁止写日志）。</summary>
public sealed record ResolvedModel(
    string ProfileId,
    string Name,
    string? PresetId,
    string BaseUrl,
    ModelProtocol Protocol,
    string ApiKey,
    string Model,
    bool SupportsImages);

public sealed record ConnectionTestResult(
    bool Success,
    string Message,
    int? StatusCode = null,
    int LatencyMs = 0);

public sealed record ModelListResult(
    bool Success,
    IReadOnlyList<string> Models,
    string? Error = null);

/// <summary>获取当前激活模型配置的唯一只读入口，供 A3 助理内核及对话预览使用。</summary>
public interface IModelProfileProvider
{
    ModelProfile? ActiveProfile { get; }

    bool TryResolveActive(out ResolvedModel? resolved, out string? error);

    event EventHandler? ActiveChanged;
}

public interface IModelConnectionTester
{
    Task<ConnectionTestResult> TestAsync(
        string baseUrl,
        ModelProtocol protocol,
        string apiKey,
        string model,
        bool needsKey,
        CancellationToken ct = default);

    Task<ModelListResult> FetchModelsAsync(
        string baseUrl,
        ModelProtocol protocol,
        string apiKey,
        CancellationToken ct = default);
}

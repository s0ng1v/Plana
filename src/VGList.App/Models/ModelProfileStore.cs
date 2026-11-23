using System.Text.Json;
using System.Text.Json.Serialization;
using VGList.Platform;

namespace VGList.App.Models;

/// <summary>
/// 模型服务配置持久化仓储：%LOCALAPPDATA%\VGList\models.json（独立于 settings.json）。
/// API Key 仅保存密文与末 4 位；文件损坏自动保留 .bad 副本并空配置启动。
/// </summary>
public sealed class ModelProfileStore : IModelProfileProvider
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _filePath;
    private readonly ISecretProtector _protector;
    private readonly Lock _gate = new();
    private ModelStoreFile _state;

    public ModelProfileStore(ISecretProtector protector, string? filePath = null)
    {
        _protector = protector;
        _filePath = string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath : filePath;
        _state = LoadFromDisk(out var corrupted);
        RecoveredFromCorruption = corrupted;
    }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VGList",
        "models.json");

    public string FilePath => _filePath;

    public bool RecoveredFromCorruption { get; }

    public event EventHandler? Changed;

    public event EventHandler? ActiveChanged;

    public IReadOnlyList<ModelProfile> Profiles
    {
        get
        {
            lock (_gate)
            {
                return _state.Profiles.Select(CloneProfile).ToList();
            }
        }
    }

    public string? ActiveProfileId
    {
        get
        {
            lock (_gate)
            {
                return _state.ActiveProfileId;
            }
        }
    }

    public ModelProfile? ActiveProfile
    {
        get
        {
            lock (_gate)
            {
                if (string.IsNullOrEmpty(_state.ActiveProfileId))
                {
                    return null;
                }

                var found = _state.Profiles.FirstOrDefault(p => p.Id == _state.ActiveProfileId);
                return found is null ? null : CloneProfile(found);
            }
        }
    }

    /// <summary>新增或更新模型配置；若 plainApiKey 非 null 则重新加密，为 null 则保留原有密文。</summary>
    public ModelProfile SaveProfile(ModelProfile profile, string? plainApiKey, bool setAsActive = false)
    {
        ArgumentNullException.ThrowIfNull(profile);

        bool activeChanged;
        ModelProfile saved;
        lock (_gate)
        {
            var idx = _state.Profiles.FindIndex(p => p.Id == profile.Id);
            var target = CloneProfile(profile);

            if (plainApiKey is not null)
            {
                var trimmed = plainApiKey.Trim();
                if (trimmed.Length == 0)
                {
                    target.ApiKeyCipher = string.Empty;
                    target.ApiKeyLast4 = string.Empty;
                }
                else
                {
                    target.ApiKeyCipher = _protector.Protect(trimmed);
                    target.ApiKeyLast4 = trimmed.Length >= 4 ? trimmed[^4..] : trimmed;
                }
            }
            else if (idx >= 0)
            {
                target.ApiKeyCipher = _state.Profiles[idx].ApiKeyCipher;
                target.ApiKeyLast4 = _state.Profiles[idx].ApiKeyLast4;
            }

            if (idx >= 0)
            {
                _state.Profiles[idx] = target;
            }
            else
            {
                _state.Profiles.Add(target);
            }

            var prevActive = _state.ActiveProfileId;
            if (setAsActive)
            {
                _state.ActiveProfileId = target.Id;
            }

            activeChanged = !string.Equals(prevActive, _state.ActiveProfileId, StringComparison.Ordinal) ||
                            string.Equals(_state.ActiveProfileId, target.Id, StringComparison.Ordinal);

            WriteToDisk(_state);
            saved = CloneProfile(target);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        if (activeChanged)
        {
            ActiveChanged?.Invoke(this, EventArgs.Empty);
        }

        return saved;
    }

    public bool SetActive(string? profileId)
    {
        bool changed;
        lock (_gate)
        {
            if (profileId is not null && _state.Profiles.All(p => p.Id != profileId))
            {
                return false;
            }

            changed = !string.Equals(_state.ActiveProfileId, profileId, StringComparison.Ordinal);
            _state.ActiveProfileId = profileId;
            WriteToDisk(_state);
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            ActiveChanged?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    public void DeleteProfile(string profileId)
    {
        bool removed;
        bool activeChanged;
        lock (_gate)
        {
            removed = _state.Profiles.RemoveAll(p => p.Id == profileId) > 0;
            if (!removed)
            {
                return;
            }

            activeChanged = string.Equals(_state.ActiveProfileId, profileId, StringComparison.Ordinal);
            if (activeChanged)
            {
                _state.ActiveProfileId = null;
            }

            WriteToDisk(_state);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        if (activeChanged)
        {
            ActiveChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool TryResolveKey(string profileId, out string apiKey)
    {
        apiKey = string.Empty;
        string? cipher;
        lock (_gate)
        {
            cipher = _state.Profiles.FirstOrDefault(p => p.Id == profileId)?.ApiKeyCipher;
        }

        if (string.IsNullOrEmpty(cipher))
        {
            return true;
        }

        return _protector.TryUnprotect(cipher, out apiKey);
    }

    public bool TryResolveActive(out ResolvedModel? resolved, out string? error)
    {
        resolved = null;
        var active = ActiveProfile;
        if (active is null)
        {
            error = "尚未配置模型服务";
            return false;
        }

        if (!_protector.TryUnprotect(active.ApiKeyCipher, out var key))
        {
            error = "API Key 无法解密（可能是更换了系统用户），请重新输入 Key";
            return false;
        }

        var effectiveSupportsImages = active.SupportsImages;
        if (!effectiveSupportsImages &&
            ProviderCatalog.Shared.TryGetKnownSupportsImages(active.PresetId, active.Model, out var knownVision) &&
            knownVision)
        {
            effectiveSupportsImages = true;
        }

        resolved = new ResolvedModel(
            active.Id,
            active.Name,
            active.PresetId,
            active.BaseUrl,
            active.Protocol,
            key,
            active.Model,
            effectiveSupportsImages);
        error = null;
        return true;
    }

    public IReadOnlyList<string> GetCustomModels(string providerKey)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            return [];
        }

        lock (_gate)
        {
            return _state.CustomModelsByProvider.TryGetValue(providerKey, out var list)
                ? list.ToList()
                : [];
        }
    }

    public bool AddCustomModel(string providerKey, string modelId)
    {
        if (string.IsNullOrWhiteSpace(providerKey) || string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        var trimmed = modelId.Trim();
        lock (_gate)
        {
            if (!_state.CustomModelsByProvider.TryGetValue(providerKey, out var list))
            {
                list = [];
                _state.CustomModelsByProvider[providerKey] = list;
            }

            if (list.Any(m => string.Equals(m, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            list.Add(trimmed);
            WriteToDisk(_state);
            return true;
        }
    }

    public bool RemoveCustomModel(string providerKey, string modelId)
    {
        if (string.IsNullOrWhiteSpace(providerKey) || string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        lock (_gate)
        {
            if (!_state.CustomModelsByProvider.TryGetValue(providerKey, out var list))
            {
                return false;
            }

            var removed = list.RemoveAll(m => string.Equals(m, modelId.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                WriteToDisk(_state);
            }

            return removed;
        }
    }

    private ModelStoreFile LoadFromDisk(out bool recoveredFromCorruption)
    {
        recoveredFromCorruption = false;
        if (!File.Exists(_filePath))
        {
            return new ModelStoreFile();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var parsed = JsonSerializer.Deserialize<ModelStoreFile>(json, JsonOpts);
            if (parsed is null)
            {
                throw new JsonException("空配置");
            }

            parsed.Profiles ??= [];
            parsed.CustomModelsByProvider ??= new(StringComparer.OrdinalIgnoreCase);

            // 自动修正旧版配置中被误标为不支持图片的官方多模态模型（如 deepseek-flash）
            var upgraded = false;
            foreach (var p in parsed.Profiles)
            {
                if (!p.SupportsImages &&
                    ProviderCatalog.Shared.TryGetKnownSupportsImages(p.PresetId, p.Model, out var knownVision) &&
                    knownVision)
                {
                    p.SupportsImages = true;
                    upgraded = true;
                }
            }

            if (upgraded)
            {
                try
                {
                    WriteToDisk(parsed);
                }
                catch
                {
                }
            }

            return parsed;
        }
        catch
        {
            recoveredFromCorruption = true;
            try
            {
                var bad = $"{_filePath}.bad-{DateTime.Now:yyyyMMddHHmmss}";
                File.Copy(_filePath, bad, overwrite: true);
            }
            catch
            {
            }

            return new ModelStoreFile();
        }
    }

    private void WriteToDisk(ModelStoreFile file)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = _filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(file, JsonOpts));
        File.Move(tmp, _filePath, overwrite: true);
    }

    private static ModelProfile CloneProfile(ModelProfile p) => new()
    {
        Id = p.Id,
        PresetId = p.PresetId,
        VariantId = p.VariantId,
        Name = p.Name,
        BaseUrl = p.BaseUrl,
        Protocol = p.Protocol,
        Model = p.Model,
        SupportsImages = p.SupportsImages,
        ApiKeyCipher = p.ApiKeyCipher,
        ApiKeyLast4 = p.ApiKeyLast4,
        LastTestPassed = p.LastTestPassed,
        CreatedAt = p.CreatedAt,
    };

    private sealed class ModelStoreFile
    {
        public int Schema { get; set; } = 1;
        public string? ActiveProfileId { get; set; }
        public List<ModelProfile> Profiles { get; set; } = [];
        public Dictionary<string, List<string>> CustomModelsByProvider { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

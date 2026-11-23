using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VGList.App.Models;
using VGList.Desktop.Services;
using VGList.Widgets.Support;

namespace VGList.Desktop.Main.Settings;

/// <summary>模型厂商筛选分类。</summary>
public enum ProviderFilterKind
{
    All,
    Domestic,
    Global,
    Configured,
}

/// <summary>厂商网格中的单张卡片模型（预设厂商或「自定义」入口）。</summary>
public sealed partial class ProviderCardViewModel : ObservableObject
{
    private static readonly HashSet<string> GlobalProviderIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "openai", "anthropic", "gemini", "openrouter", "ollama", "meta", "byteplus", "qwencloud",
    };

    public ProviderCardViewModel(ProviderPreset? preset)
    {
        Preset = preset;
        IsCustom = preset is null;
        Id = preset?.Id ?? "__custom__";
        Name = preset?.Name ?? "自定义模型服务";
        Subtitle = preset?.SubtitleText ?? "任意 OpenAI / Anthropic 兼容 URL 与 Key";
        Monogram = preset?.Monogram ?? "+";
        BrandBrush = CreateBrush(preset?.Color ?? "#E46B92");
        LogoBitmap = ProviderLogoLoader.TryLoad(preset?.Id);
        IsGlobal = !IsCustom && GlobalProviderIds.Contains(Id);
        IsDomestic = !IsCustom && !IsGlobal;
    }

    public ProviderPreset? Preset { get; }

    public bool IsCustom { get; }

    public bool IsDomestic { get; }

    public bool IsGlobal { get; }

    public string Id { get; }

    public string Name { get; }

    public string Subtitle { get; }

    public string Monogram { get; }

    public IBrush BrandBrush { get; }

    public Bitmap? LogoBitmap { get; }

    public bool HasLogo => LogoBitmap is not null;

    [ObservableProperty]
    private int _configuredCount;

    [ObservableProperty]
    private bool _isActive;

    public bool IsConfigured => ConfiguredCount > 0;

    public string BadgeText => IsActive
        ? "使用中"
        : ConfiguredCount > 1 ? $"已配置 {ConfiguredCount}" : "已配置";

    partial void OnConfiguredCountChanged(int value)
    {
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(BadgeText));
    }

    partial void OnIsActiveChanged(bool value) => OnPropertyChanged(nameof(BadgeText));

    internal static IBrush CreateBrush(string hex)
    {
        try
        {
            return new SolidColorBrush(Color.Parse(hex));
        }
        catch
        {
            return new SolidColorBrush(Color.Parse("#E46B92"));
        }
    }
}

/// <summary>抽屉内同一厂商多实例切换胶囊。</summary>
public sealed partial class ProfileInstanceChipViewModel : ObservableObject
{
    public ProfileInstanceChipViewModel(ModelProfile profile, bool isSelected, bool isActive)
    {
        Profile = profile;
        Id = profile.Id;
        Label = string.IsNullOrWhiteSpace(profile.Model) ? profile.Name : $"{profile.Name} · {profile.Model}";
        _isSelected = isSelected;
        _isActive = isActive;
    }

    public ModelProfile Profile { get; }
    public string Id { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>抽屉内官方预设或用户新增的模型快选胶囊。</summary>
public sealed partial class PresetModelChipViewModel : ObservableObject
{
    public PresetModelChipViewModel(string id, bool supportsImages, string? note, bool isCustomAdded, bool isSelected)
    {
        Id = id;
        SupportsImages = supportsImages;
        Note = note ?? string.Empty;
        IsCustomAdded = isCustomAdded;
        _isSelected = isSelected;
    }

    public string Id { get; }
    public bool SupportsImages { get; }
    public string Note { get; }
    public bool IsCustomAdded { get; }
    public bool HasNote => !string.IsNullOrEmpty(Note);
    public string Tooltip => HasNote
        ? $"{Id} — {Note}"
        : (SupportsImages ? $"{Id}（支持图片输入）" : $"{Id}（纯文本）");

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>设置页「模型服务」完整视图模型：当前使用条、厂商卡片网格、窗内配置抽屉与连通性测试。</summary>
public sealed partial class ModelServicesViewModel : ObservableObject, IDisposable
{
    private readonly ProviderCatalog _catalog;
    private readonly ModelProfileStore _store;
    private readonly IModelConnectionTester _tester;
    private readonly ToastService _toast;
    private CancellationTokenSource? _testCts;
    private CancellationTokenSource? _fetchCts;
    private ProviderPreset? _drawerPreset;
    private string? _editingProfileId;
    private bool _hasSavedKey;
    private bool _suppressDirty;
    private bool _disposed;

    public ModelServicesViewModel(
        ModelProfileStore store,
        IModelConnectionTester tester,
        ToastService toast,
        ProviderCatalog? catalog = null)
    {
        _store = store;
        _tester = tester;
        _toast = toast;
        _catalog = catalog ?? ProviderCatalog.Shared;

        foreach (var preset in _catalog.Providers)
        {
            ProviderCards.Add(new ProviderCardViewModel(preset));
        }

        // 末尾固定追加「自定义」卡片
        ProviderCards.Add(new ProviderCardViewModel(null));

        if (_store.RecoveredFromCorruption)
        {
            StoreWarning = "模型配置文件异常，已自动备份原文件并重置为空配置";
        }

        _store.Changed += OnStoreChanged;
        RefreshCardsAndActive();
    }

    public ObservableCollection<ProviderCardViewModel> ProviderCards { get; } = [];

    public ObservableCollection<ProviderCardViewModel> VisibleProviderCards { get; } = [];

    public ObservableCollection<ProfileInstanceChipViewModel> DrawerInstances { get; } = [];

    public ObservableCollection<ProviderVariant> DrawerVariants { get; } = [];

    public ObservableCollection<PresetModelChipViewModel> PresetModels { get; } = [];

    public ObservableCollection<string> FetchedModels { get; } = [];

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private ProviderFilterKind _filter = ProviderFilterKind.All;

    public bool IsFilterAll => Filter == ProviderFilterKind.All;
    public bool IsFilterDomestic => Filter == ProviderFilterKind.Domestic;
    public bool IsFilterGlobal => Filter == ProviderFilterKind.Global;
    public bool IsFilterConfigured => Filter == ProviderFilterKind.Configured;
    public bool HasNoFilteredCards => VisibleProviderCards.Count == 0;
    public int ConfiguredProfilesCount => _store.Profiles.Count;

    [ObservableProperty]
    private string? _storeWarning;

    public bool HasStoreWarning => !string.IsNullOrEmpty(StoreWarning);

    // ===== 当前使用条 =====
    [ObservableProperty]
    private bool _hasActiveProfile;

    [ObservableProperty]
    private string _activeProviderName = string.Empty;

    [ObservableProperty]
    private string _activeModelName = string.Empty;

    [ObservableProperty]
    private string _activeMonogram = "P";

    [ObservableProperty]
    private IBrush _activeBrandBrush = ProviderCardViewModel.CreateBrush("#E46B92");

    [ObservableProperty]
    private Bitmap? _activeLogoBitmap;

    public bool HasActiveLogo => ActiveLogoBitmap is not null;

    [ObservableProperty]
    private string _activeMaskedKey = string.Empty;

    // ===== 右侧窗内配置抽屉 =====
    [ObservableProperty]
    private bool _isDrawerOpen;

    [ObservableProperty]
    private bool _isCustomDrawer;

    [ObservableProperty]
    private string _drawerTitle = string.Empty;

    [ObservableProperty]
    private string _drawerDomain = string.Empty;

    [ObservableProperty]
    private string _drawerApiKeyUrl = string.Empty;

    [ObservableProperty]
    private string _drawerMonogram = "+";

    [ObservableProperty]
    private IBrush _drawerBrandBrush = ProviderCardViewModel.CreateBrush("#E46B92");

    [ObservableProperty]
    private Bitmap? _drawerLogoBitmap;

    public bool HasDrawerLogo => DrawerLogoBitmap is not null;

    [ObservableProperty]
    private bool _drawerNeedsKey = true;

    [ObservableProperty]
    private ProviderVariant? _selectedVariant;

    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    private string? _baseUrlError;

    [ObservableProperty]
    private ModelProtocol _protocol = ModelProtocol.OpenAi;

    [ObservableProperty]
    private string _apiKeyInput = string.Empty;

    [ObservableProperty]
    private string _apiKeyPlaceholder = "输入 API Key";

    [ObservableProperty]
    private bool _isApiKeyVisible;

    [ObservableProperty]
    private string _modelName = string.Empty;

    [ObservableProperty]
    private string _newModelInput = string.Empty;

    [ObservableProperty]
    private bool _supportsImages;

    [ObservableProperty]
    private bool _isSupportsImagesAutoDetected;

    [ObservableProperty]
    private bool _isAdvancedExpanded;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private bool _testPassed;

    [ObservableProperty]
    private string? _testMessage;

    [ObservableProperty]
    private bool _testIsError;

    [ObservableProperty]
    private bool _isFetchingModels;

    [ObservableProperty]
    private string? _fetchModelsHint;

    public bool HasDrawerApiKeyUrl => !string.IsNullOrWhiteSpace(DrawerApiKeyUrl);
    public bool HasDrawerInstances => DrawerInstances.Count > 0;
    public bool HasMultipleVariants => !IsCustomDrawer && DrawerVariants.Count > 1;
    public bool HasBaseUrlError => !string.IsNullOrEmpty(BaseUrlError);
    public bool HasTestMessage => !string.IsNullOrEmpty(TestMessage);
    public bool HasPresetModels => PresetModels.Count > 0;
    public bool HasFetchedModels => FetchedModels.Count > 0;
    public bool HasFetchModelsHint => !string.IsNullOrEmpty(FetchModelsHint);
    public bool CanAddCustomModel => !string.IsNullOrWhiteSpace(NewModelInput);
    public string VisionCapabilitySummary => IsSupportsImagesAutoDetected
        ? (SupportsImages ? "已自动识别：支持图片输入" : "已自动识别：仅支持文本输入")
        : (SupportsImages ? "支持图片输入" : "仅支持文本输入");
    public bool CanDeleteProfile => !string.IsNullOrEmpty(_editingProfileId);
    public bool IsEditingActiveProfile => !string.IsNullOrEmpty(_editingProfileId) && _editingProfileId == _store.ActiveProfileId;
    public bool IsOpenAiProtocol => Protocol == ModelProtocol.OpenAi;
    public bool IsAnthropicProtocol => Protocol == ModelProtocol.Anthropic;
    public char ApiKeyPasswordChar => IsApiKeyVisible ? '\0' : '•';

    public bool CanActivate => TestPassed && !IsTesting;

    public string TestButtonLabel => IsTesting ? "测试中…" : "测试连接";

    public string ActivateButtonLabel => IsEditingActiveProfile ? "当前使用中" : "设为当前使用";

    public void OpenCard(ProviderCardViewModel card)
    {
        CancelPendingNetwork();
        _drawerPreset = card.Preset;
        IsCustomDrawer = card.IsCustom;
        DrawerTitle = card.Name;
        DrawerDomain = card.Preset?.Domain ?? "支持任意 OpenAI / Anthropic 兼容端点";
        DrawerApiKeyUrl = card.Preset?.ApiKeyUrl ?? string.Empty;
        DrawerMonogram = card.Monogram;
        DrawerBrandBrush = card.BrandBrush;
        DrawerLogoBitmap = card.LogoBitmap;
        DrawerNeedsKey = card.Preset?.NeedsKey ?? true;

        DrawerVariants.Clear();
        if (card.Preset is not null)
        {
            foreach (var v in card.Preset.Variants)
            {
                DrawerVariants.Add(v);
            }
        }

        RebuildDrawerInstances();

        // 若该厂商已有配置，默认加载当前激活或第一条；否则进入新建态
        var existing = DrawerInstances.FirstOrDefault(i => i.IsActive) ?? DrawerInstances.FirstOrDefault();
        if (existing is not null)
        {
            LoadProfileIntoDrawer(existing.Profile);
        }
        else
        {
            LoadNewInstanceIntoDrawer();
        }

        NotifyDrawerComputed();
        IsDrawerOpen = true;
    }

    public void OpenCustomCard()
    {
        var custom = ProviderCards.FirstOrDefault(c => c.IsCustom);
        if (custom is not null)
        {
            OpenCard(custom);
        }
    }

    public void SetFilter(ProviderFilterKind kind) => Filter = kind;

    public void OpenActiveInDrawer()
    {
        var active = _store.ActiveProfile;
        if (active is null)
        {
            return;
        }

        var card = active.IsCustom
            ? ProviderCards.FirstOrDefault(c => c.IsCustom)
            : ProviderCards.FirstOrDefault(c => c.Id == active.PresetId);
        if (card is not null)
        {
            OpenCard(card);
        }
    }

    [RelayCommand]
    public void CloseDrawer()
    {
        CancelPendingNetwork();
        IsDrawerOpen = false;
    }

    [RelayCommand]
    public void AddAnotherInstance()
    {
        CancelPendingNetwork();
        LoadNewInstanceIntoDrawer();
        SyncInstanceChips();
        NotifyDrawerComputed();
    }

    public void SelectInstance(ProfileInstanceChipViewModel chip)
    {
        CancelPendingNetwork();
        LoadProfileIntoDrawer(chip.Profile);
        SyncInstanceChips();
        NotifyDrawerComputed();
    }

    public void SelectProtocol(ModelProtocol protocol)
    {
        if (!IsCustomDrawer)
        {
            return;
        }

        Protocol = protocol;
    }

    [RelayCommand]
    private void ToggleApiKeyVisibility() => IsApiKeyVisible = !IsApiKeyVisible;

    [RelayCommand]
    private void ToggleAdvanced() => IsAdvancedExpanded = !IsAdvancedExpanded;

    [RelayCommand]
    private void OpenApiKeyUrl()
    {
        if (string.IsNullOrWhiteSpace(DrawerApiKeyUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(DrawerApiKeyUrl) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    public void PickFetchedModel(string model)
    {
        if (!string.IsNullOrWhiteSpace(model))
        {
            ModelName = model.Trim();
        }
    }

    public void PickPresetModel(PresetModelChipViewModel chip)
    {
        if (chip is null || string.IsNullOrWhiteSpace(chip.Id))
        {
            return;
        }

        ModelName = chip.Id;
    }

    public void AddCustomModel()
    {
        var trimmed = NewModelInput?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        var key = CurrentProviderKey;
        var added = _store.AddCustomModel(key, trimmed);
        NewModelInput = string.Empty;
        ModelName = trimmed;
        RebuildPresetModels();
        if (added)
        {
            _toast.Show($"已新增模型「{trimmed}」");
        }
    }

    public void RemoveCustomModel(PresetModelChipViewModel chip)
    {
        if (chip is null || !chip.IsCustomAdded)
        {
            return;
        }

        if (_store.RemoveCustomModel(CurrentProviderKey, chip.Id))
        {
            RebuildPresetModels();
        }
    }

    private string CurrentProviderKey => _drawerPreset?.Id ?? "__custom__";

    private void RebuildPresetModels()
    {
        PresetModels.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (SelectedVariant is not null)
        {
            foreach (var m in SelectedVariant.EffectiveModels)
            {
                if (seen.Add(m.Id))
                {
                    PresetModels.Add(new PresetModelChipViewModel(
                        m.Id,
                        m.SupportsImages,
                        m.Note,
                        isCustomAdded: false,
                        isSelected: string.Equals(m.Id, ModelName?.Trim(), StringComparison.OrdinalIgnoreCase)));
                }
            }
        }

        foreach (var customId in _store.GetCustomModels(CurrentProviderKey))
        {
            if (seen.Add(customId))
            {
                var vision = _catalog.InferSupportsImages(_drawerPreset?.Id, customId, fallback: SelectedVariant?.SupportsImages ?? true);
                PresetModels.Add(new PresetModelChipViewModel(
                    customId,
                    vision,
                    note: "自定义添加",
                    isCustomAdded: true,
                    isSelected: string.Equals(customId, ModelName?.Trim(), StringComparison.OrdinalIgnoreCase)));
            }
        }

        OnPropertyChanged(nameof(HasPresetModels));
    }

    private void SyncPresetModelSelection()
    {
        var current = ModelName?.Trim() ?? string.Empty;
        foreach (var chip in PresetModels)
        {
            chip.IsSelected = string.Equals(chip.Id, current, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void SyncVisionCapabilityFromModel(string? modelId, bool allowFallbackToVariant = false)
    {
        if (_catalog.TryGetKnownSupportsImages(_drawerPreset?.Id, modelId, out var known))
        {
            SupportsImages = known;
            IsSupportsImagesAutoDetected = true;
            return;
        }

        IsSupportsImagesAutoDetected = false;
        if (allowFallbackToVariant && SelectedVariant is not null)
        {
            SupportsImages = SelectedVariant.SupportsImages;
        }
    }

    [RelayCommand]
    private async Task FetchModelsAsync()
    {
        if (IsFetchingModels)
        {
            return;
        }

        if (!ValidateBaseUrl())
        {
            return;
        }

        var key = ResolveEffectiveApiKey(out var decryptErr);
        if (decryptErr is not null)
        {
            FetchModelsHint = decryptErr;
            return;
        }

        _fetchCts?.Cancel();
        _fetchCts = new CancellationTokenSource();
        IsFetchingModels = true;
        FetchModelsHint = null;
        FetchedModels.Clear();
        OnPropertyChanged(nameof(HasFetchedModels));
        OnPropertyChanged(nameof(HasFetchModelsHint));

        try
        {
            var result = await _tester.FetchModelsAsync(BaseUrl, Protocol, key, _fetchCts.Token);
            if (result.Success)
            {
                foreach (var m in result.Models)
                {
                    FetchedModels.Add(m);
                }

                FetchModelsHint = $"已拉取 {result.Models.Count} 个可用模型，点击即可选用";
            }
            else
            {
                FetchModelsHint = result.Error ?? "不支持自动拉取，请手动填写模型名";
            }
        }
        finally
        {
            IsFetchingModels = false;
            OnPropertyChanged(nameof(HasFetchedModels));
            OnPropertyChanged(nameof(HasFetchModelsHint));
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (IsTesting)
        {
            return;
        }

        if (!ValidateBaseUrl())
        {
            TestPassed = false;
            TestIsError = true;
            TestMessage = BaseUrlError;
            return;
        }

        var key = ResolveEffectiveApiKey(out var decryptErr);
        if (decryptErr is not null)
        {
            TestPassed = false;
            TestIsError = true;
            TestMessage = decryptErr;
            return;
        }

        _testCts?.Cancel();
        _testCts = new CancellationTokenSource();
        IsTesting = true;
        TestMessage = null;
        TestIsError = false;
        OnPropertyChanged(nameof(CanActivate));
        OnPropertyChanged(nameof(TestButtonLabel));

        try
        {
            var result = await _tester.TestAsync(BaseUrl, Protocol, key, ModelName, DrawerNeedsKey, _testCts.Token);
            TestPassed = result.Success;
            TestIsError = !result.Success;
            TestMessage = result.Message;
        }
        finally
        {
            IsTesting = false;
            OnPropertyChanged(nameof(CanActivate));
            OnPropertyChanged(nameof(TestButtonLabel));
        }
    }

    [RelayCommand]
    private void SaveProfile()
    {
        if (!TryPersist(setAsActive: false, out var saved))
        {
            return;
        }

        _toast.Show($"已保存模型配置「{saved!.Name}」");
    }

    [RelayCommand]
    private void ActivateProfile()
    {
        if (!TestPassed)
        {
            TestIsError = true;
            TestMessage = "请先点击「测试连接」验证通过后再设为当前使用";
            return;
        }

        if (!TryPersist(setAsActive: true, out var saved))
        {
            return;
        }

        _toast.Show($"已切换当前模型为 {saved!.Name} · {saved.Model}");
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (string.IsNullOrEmpty(_editingProfileId))
        {
            return;
        }

        var name = ProfileName;
        _store.DeleteProfile(_editingProfileId);
        RebuildDrawerInstances();
        var next = DrawerInstances.FirstOrDefault();
        if (next is not null)
        {
            LoadProfileIntoDrawer(next.Profile);
        }
        else
        {
            LoadNewInstanceIntoDrawer();
        }

        SyncInstanceChips();
        NotifyDrawerComputed();
        _toast.Show($"已删除模型配置「{name}」");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelPendingNetwork();
        _store.Changed -= OnStoreChanged;
    }

    private bool TryPersist(bool setAsActive, out ModelProfile? saved)
    {
        saved = null;
        if (!ValidateBaseUrl())
        {
            TestIsError = true;
            TestMessage = BaseUrlError;
            return false;
        }

        if (string.IsNullOrWhiteSpace(ModelName))
        {
            TestIsError = true;
            TestMessage = "请填写模型名称";
            return false;
        }

        if (DrawerNeedsKey && !_hasSavedKey && string.IsNullOrWhiteSpace(ApiKeyInput))
        {
            TestIsError = true;
            TestMessage = "请填写 API Key";
            return false;
        }

        var finalName = string.IsNullOrWhiteSpace(ProfileName)
            ? (_drawerPreset?.Name ?? "自定义模型")
            : ProfileName.Trim();

        var profile = new ModelProfile
        {
            Id = string.IsNullOrEmpty(_editingProfileId) ? Guid.NewGuid().ToString("N") : _editingProfileId,
            PresetId = _drawerPreset?.Id,
            VariantId = SelectedVariant?.Id,
            Name = finalName,
            BaseUrl = BaseUrl.Trim(),
            Protocol = Protocol,
            Model = ModelName.Trim(),
            SupportsImages = SupportsImages,
            LastTestPassed = TestPassed,
        };

        // 仅当用户重新输入了 Key（或是免鉴权新建）时覆写密文；否则传 null 保留原有密文
        string? plainKey = !string.IsNullOrWhiteSpace(ApiKeyInput)
            ? ApiKeyInput.Trim()
            : (!DrawerNeedsKey && !_hasSavedKey ? string.Empty : null);

        saved = _store.SaveProfile(profile, plainKey, setAsActive);
        _editingProfileId = saved.Id;
        _hasSavedKey = !string.IsNullOrEmpty(saved.ApiKeyLast4);
        _suppressDirty = true;
        try
        {
            ApiKeyInput = string.Empty;
            ApiKeyPlaceholder = _hasSavedKey
                ? $"{saved.MaskedKey}（留空保持不变）"
                : (DrawerNeedsKey ? "输入 API Key" : "本地模型无需填写 Key");
        }
        finally
        {
            _suppressDirty = false;
        }

        RebuildDrawerInstances();
        SyncInstanceChips();
        NotifyDrawerComputed();
        return true;
    }

    private string ResolveEffectiveApiKey(out string? error)
    {
        error = null;
        if (!string.IsNullOrWhiteSpace(ApiKeyInput))
        {
            return ApiKeyInput.Trim();
        }

        if (_hasSavedKey && !string.IsNullOrEmpty(_editingProfileId))
        {
            if (_store.TryResolveKey(_editingProfileId, out var existing))
            {
                return existing;
            }

            error = "已存 API Key 无法解密（可能更换了 Windows 用户），请重新输入 Key";
            return string.Empty;
        }

        return string.Empty;
    }

    private bool ValidateBaseUrl()
    {
        var ok = ProviderCatalog.IsValidBaseUrl(BaseUrl, out var err);
        BaseUrlError = err;
        OnPropertyChanged(nameof(HasBaseUrlError));
        return ok;
    }

    private void LoadProfileIntoDrawer(ModelProfile profile)
    {
        _suppressDirty = true;
        try
        {
            _editingProfileId = profile.Id;
            _hasSavedKey = !string.IsNullOrEmpty(profile.ApiKeyLast4);
            ProfileName = profile.Name;
            SelectedVariant = DrawerVariants.FirstOrDefault(v => v.Id == profile.VariantId) ?? DrawerVariants.FirstOrDefault();
            BaseUrl = profile.BaseUrl;
            BaseUrlError = null;
            Protocol = profile.Protocol;
            ModelName = profile.Model;
            SupportsImages = profile.SupportsImages;
            if (_catalog.TryGetKnownSupportsImages(_drawerPreset?.Id, profile.Model, out var known))
            {
                IsSupportsImagesAutoDetected = true;
                if (known && !SupportsImages)
                {
                    SupportsImages = true;
                }
            }
            else
            {
                IsSupportsImagesAutoDetected = false;
            }

            NewModelInput = string.Empty;
            ApiKeyInput = string.Empty;
            ApiKeyPlaceholder = _hasSavedKey
                ? $"{profile.MaskedKey}（留空保持不变）"
                : (DrawerNeedsKey ? "输入 API Key" : "本地模型无需填写 Key");
            IsApiKeyVisible = false;
            IsAdvancedExpanded = IsCustomDrawer || string.IsNullOrWhiteSpace(BaseUrl);
            TestPassed = profile.LastTestPassed;
            TestMessage = profile.LastTestPassed ? "上次连接测试已通过" : null;
            TestIsError = false;
            FetchedModels.Clear();
            FetchModelsHint = null;
            RebuildPresetModels();
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    private void LoadNewInstanceIntoDrawer()
    {
        _suppressDirty = true;
        try
        {
            _editingProfileId = null;
            _hasSavedKey = false;
            var firstVariant = DrawerVariants.FirstOrDefault();
            SelectedVariant = firstVariant;
            ProfileName = _drawerPreset?.Name ?? string.Empty;
            BaseUrl = firstVariant?.BaseUrl ?? string.Empty;
            BaseUrlError = null;
            Protocol = firstVariant?.Protocol ?? ModelProtocol.OpenAi;
            ModelName = firstVariant?.DefaultModel ?? string.Empty;
            NewModelInput = string.Empty;
            SyncVisionCapabilityFromModel(ModelName, allowFallbackToVariant: true);
            if (!IsSupportsImagesAutoDetected && firstVariant is null)
            {
                SupportsImages = true;
            }

            ApiKeyInput = string.Empty;
            ApiKeyPlaceholder = DrawerNeedsKey ? "粘贴你的 API Key" : "本地模型无需填写 Key";
            IsApiKeyVisible = false;
            IsAdvancedExpanded = IsCustomDrawer || string.IsNullOrWhiteSpace(BaseUrl);
            TestPassed = false;
            TestMessage = null;
            TestIsError = false;
            FetchedModels.Clear();
            FetchModelsHint = null;
            RebuildPresetModels();
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    private void RebuildDrawerInstances()
    {
        DrawerInstances.Clear();
        var activeId = _store.ActiveProfileId;
        var matching = _store.Profiles.Where(p =>
            IsCustomDrawer ? p.IsCustom : string.Equals(p.PresetId, _drawerPreset?.Id, StringComparison.Ordinal));

        foreach (var p in matching)
        {
            DrawerInstances.Add(new ProfileInstanceChipViewModel(p, p.Id == _editingProfileId, p.Id == activeId));
        }

        OnPropertyChanged(nameof(HasDrawerInstances));
    }

    private void SyncInstanceChips()
    {
        var activeId = _store.ActiveProfileId;
        foreach (var chip in DrawerInstances)
        {
            chip.IsSelected = chip.Id == _editingProfileId;
            chip.IsActive = chip.Id == activeId;
        }
    }

    private void OnStoreChanged(object? sender, EventArgs e) => UiDispatcher.PostIfNeeded(() =>
    {
        if (!_disposed)
        {
            RefreshCardsAndActive();
        }
    });

    private void RefreshCardsAndActive()
    {
        var all = _store.Profiles;
        var activeId = _store.ActiveProfileId;

        foreach (var card in ProviderCards)
        {
            var matched = all.Where(p =>
                card.IsCustom ? p.IsCustom : string.Equals(p.PresetId, card.Id, StringComparison.Ordinal)).ToList();
            card.ConfiguredCount = matched.Count;
            card.IsActive = activeId is not null && matched.Any(p => p.Id == activeId);
        }

        var active = _store.ActiveProfile;
        HasActiveProfile = active is not null;
        if (active is not null)
        {
            var preset = _catalog.FindById(active.PresetId);
            ActiveProviderName = active.Name;
            ActiveModelName = active.Model;
            ActiveMonogram = preset?.Monogram ?? "自";
            ActiveBrandBrush = ProviderCardViewModel.CreateBrush(preset?.Color ?? "#E46B92");
            ActiveLogoBitmap = ProviderLogoLoader.TryLoad(preset?.Id);
            ActiveMaskedKey = string.IsNullOrEmpty(active.MaskedKey) ? "免鉴权" : active.MaskedKey;
        }
        else
        {
            ActiveProviderName = string.Empty;
            ActiveModelName = string.Empty;
            ActiveLogoBitmap = null;
            ActiveMaskedKey = string.Empty;
        }

        ApplyCardFilter();
        OnPropertyChanged(nameof(ConfiguredProfilesCount));
        OnPropertyChanged(nameof(IsEditingActiveProfile));
        OnPropertyChanged(nameof(ActivateButtonLabel));
    }

    private void ApplyCardFilter()
    {
        var q = SearchQuery?.Trim() ?? string.Empty;
        VisibleProviderCards.Clear();
        foreach (var card in ProviderCards)
        {
            var matchKind = Filter switch
            {
                ProviderFilterKind.Domestic => card.IsDomestic,
                ProviderFilterKind.Global => card.IsGlobal,
                ProviderFilterKind.Configured => card.IsConfigured,
                _ => true,
            };

            if (!matchKind)
            {
                continue;
            }

            if (q.Length > 0 &&
                !card.Name.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !card.Subtitle.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !card.Id.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            VisibleProviderCards.Add(card);
        }

        OnPropertyChanged(nameof(HasNoFilteredCards));
    }

    private void MarkDirty()
    {
        if (_suppressDirty)
        {
            return;
        }

        if (TestPassed)
        {
            TestPassed = false;
            TestMessage = null;
            OnPropertyChanged(nameof(CanActivate));
        }
    }

    private void NotifyDrawerComputed()
    {
        OnPropertyChanged(nameof(HasDrawerApiKeyUrl));
        OnPropertyChanged(nameof(HasDrawerInstances));
        OnPropertyChanged(nameof(HasMultipleVariants));
        OnPropertyChanged(nameof(HasBaseUrlError));
        OnPropertyChanged(nameof(HasTestMessage));
        OnPropertyChanged(nameof(HasPresetModels));
        OnPropertyChanged(nameof(HasFetchedModels));
        OnPropertyChanged(nameof(HasFetchModelsHint));
        OnPropertyChanged(nameof(CanAddCustomModel));
        OnPropertyChanged(nameof(VisionCapabilitySummary));
        OnPropertyChanged(nameof(CanDeleteProfile));
        OnPropertyChanged(nameof(IsEditingActiveProfile));
        OnPropertyChanged(nameof(CanActivate));
        OnPropertyChanged(nameof(ActivateButtonLabel));
        OnPropertyChanged(nameof(IsOpenAiProtocol));
        OnPropertyChanged(nameof(IsAnthropicProtocol));
        OnPropertyChanged(nameof(ApiKeyPasswordChar));
    }

    private void CancelPendingNetwork()
    {
        _testCts?.Cancel();
        _testCts?.Dispose();
        _testCts = null;
        _fetchCts?.Cancel();
        _fetchCts?.Dispose();
        _fetchCts = null;
    }

    partial void OnSelectedVariantChanged(ProviderVariant? value)
    {
        if (_suppressDirty || value is null || IsCustomDrawer)
        {
            return;
        }

        _suppressDirty = true;
        try
        {
            BaseUrl = value.BaseUrl;
            Protocol = value.Protocol;
            if (string.IsNullOrWhiteSpace(ModelName) || DrawerVariants.Any(v => v.DefaultModel == ModelName))
            {
                ModelName = value.DefaultModel;
            }

            SyncVisionCapabilityFromModel(ModelName, allowFallbackToVariant: true);
            RebuildPresetModels();
        }
        finally
        {
            _suppressDirty = false;
        }

        ValidateBaseUrl();
        MarkDirty();
    }

    partial void OnBaseUrlChanged(string value)
    {
        if (!_suppressDirty)
        {
            ValidateBaseUrl();
            MarkDirty();
        }
    }

    partial void OnApiKeyInputChanged(string value) => MarkDirty();

    partial void OnModelNameChanged(string value)
    {
        SyncPresetModelSelection();
        if (!_suppressDirty)
        {
            SyncVisionCapabilityFromModel(value, allowFallbackToVariant: false);
            MarkDirty();
        }
    }

    partial void OnNewModelInputChanged(string value) => OnPropertyChanged(nameof(CanAddCustomModel));

    partial void OnSupportsImagesChanged(bool value) => OnPropertyChanged(nameof(VisionCapabilitySummary));

    partial void OnIsSupportsImagesAutoDetectedChanged(bool value) => OnPropertyChanged(nameof(VisionCapabilitySummary));

    partial void OnProtocolChanged(ModelProtocol value)
    {
        OnPropertyChanged(nameof(IsOpenAiProtocol));
        OnPropertyChanged(nameof(IsAnthropicProtocol));
        MarkDirty();
    }

    partial void OnIsApiKeyVisibleChanged(bool value) => OnPropertyChanged(nameof(ApiKeyPasswordChar));

    partial void OnTestMessageChanged(string? value) => OnPropertyChanged(nameof(HasTestMessage));

    partial void OnTestPassedChanged(bool value) => OnPropertyChanged(nameof(CanActivate));

    partial void OnFetchModelsHintChanged(string? value) => OnPropertyChanged(nameof(HasFetchModelsHint));

    partial void OnStoreWarningChanged(string? value) => OnPropertyChanged(nameof(HasStoreWarning));

    partial void OnActiveLogoBitmapChanged(Bitmap? value) => OnPropertyChanged(nameof(HasActiveLogo));

    partial void OnDrawerLogoBitmapChanged(Bitmap? value) => OnPropertyChanged(nameof(HasDrawerLogo));

    partial void OnSearchQueryChanged(string value) => ApplyCardFilter();

    partial void OnFilterChanged(ProviderFilterKind value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterDomestic));
        OnPropertyChanged(nameof(IsFilterGlobal));
        OnPropertyChanged(nameof(IsFilterConfigured));
        ApplyCardFilter();
    }
}

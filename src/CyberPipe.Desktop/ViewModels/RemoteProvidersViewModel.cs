using System.Collections.ObjectModel;
using CyberPipe.Contracts;
using CyberPipe.Desktop.Common;
using CyberPipe.Desktop.Services;

namespace CyberPipe.Desktop.ViewModels;

public sealed record RemoteProtocolOption(RemoteProviderProtocol Protocol, string Label);

/// <summary>Модель внутри профиля облачного провайдера.</summary>
public sealed class RemoteProviderModelItemViewModel : ViewModelBase
{
    private string _displayName = string.Empty;
    private bool _isEnabled = true;
    private ModelTaskCategory _recommendedFor = ModelTaskCategory.HeavyReasoningTier3;

    public required string ModelId { get; init; }

    public int ContextWindowTokens { get; init; } = 128000;

    public decimal? InputPricePer1mUsd { get; init; }

    public decimal? OutputPricePer1mUsd { get; init; }

    public string? Description { get; init; }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public ModelTaskCategory RecommendedFor
    {
        get => _recommendedFor;
        set
        {
            if (SetProperty(ref _recommendedFor, value))
            {
                OnPropertyChanged(nameof(IsFastRecommendation));
            }
        }
    }

    public bool IsFastRecommendation
    {
        get => RecommendedFor == ModelTaskCategory.LightFastTier2;
        set => RecommendedFor = value ? ModelTaskCategory.LightFastTier2 : ModelTaskCategory.HeavyReasoningTier3;
    }

    public RelayCommand? AssignFastCommand { get; set; }

    public RelayCommand? AssignExpertCommand { get; set; }

    public RelayCommand? TestCommand { get; set; }

    public RelayCommand? RemoveCommand { get; set; }
}

/// <summary>Редактируемый профиль облачного провайдера.</summary>
public sealed class RemoteProviderItemViewModel : ViewModelBase
{
    private string _name = string.Empty;
    private RemoteProviderProtocol _protocol;
    private string _baseUrl = string.Empty;
    private bool _isEnabled = true;
    private bool _hasSavedApiKey;
    private string _apiKeyInput = string.Empty;
    private string _probeStatusText = string.Empty;
    private string _modelFilterText = string.Empty;
    private string _newModelId = string.Empty;
    private string _newModelDisplayName = string.Empty;

    public required string Id { get; init; }

    public ObservableCollection<RemoteProviderModelItemViewModel> Models { get; } = [];

    public ObservableCollection<RemoteProviderModelItemViewModel> FilteredModels { get; } = [];

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public RemoteProviderProtocol Protocol
    {
        get => _protocol;
        set
        {
            if (SetProperty(ref _protocol, value))
            {
                OnPropertyChanged(nameof(ProtocolIndex));
                OnPropertyChanged(nameof(ProtocolLabel));
            }
        }
    }

    public int ProtocolIndex
    {
        get => (int)Protocol;
        set => Protocol = Enum.IsDefined(typeof(RemoteProviderProtocol), value)
            ? (RemoteProviderProtocol)value
            : RemoteProviderProtocol.OpenAiCompatible;
    }

    public string ProtocolLabel => Protocol switch
    {
        RemoteProviderProtocol.Anthropic => "Anthropic",
        RemoteProviderProtocol.GoogleGemini => "Google Gemini",
        _ => "OpenAI-совместимый",
    };

    public string BaseUrl
    {
        get => _baseUrl;
        set => SetProperty(ref _baseUrl, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public bool HasSavedApiKey
    {
        get => _hasSavedApiKey;
        set
        {
            if (SetProperty(ref _hasSavedApiKey, value))
            {
                OnPropertyChanged(nameof(KeyStatusText));
                OnPropertyChanged(nameof(KeyBadgeText));
            }
        }
    }

    public string KeyStatusText => HasSavedApiKey
        ? "✓ Ключ сохранён в защищённом хранилище"
        : "Ключ ещё не сохранён";

    public string KeyBadgeText => HasSavedApiKey ? "🟢 Ключ настроен" : "⚪ Нет ключа";

    public string ApiKeyInput
    {
        get => _apiKeyInput;
        set => SetProperty(ref _apiKeyInput, value);
    }

    public string ProbeStatusText
    {
        get => _probeStatusText;
        set => SetProperty(ref _probeStatusText, value);
    }

    public string ModelFilterText
    {
        get => _modelFilterText;
        set
        {
            if (SetProperty(ref _modelFilterText, value))
            {
                ApplyModelFilter();
            }
        }
    }

    public string NewModelId
    {
        get => _newModelId;
        set => SetProperty(ref _newModelId, value);
    }

    public string NewModelDisplayName
    {
        get => _newModelDisplayName;
        set => SetProperty(ref _newModelDisplayName, value);
    }

    public AsyncRelayCommand? SaveCommand { get; set; }

    public AsyncRelayCommand? DeleteKeyCommand { get; set; }

    public AsyncRelayCommand? ProbeCommand { get; set; }

    public RelayCommand? AddModelCommand { get; set; }

    public RelayCommand? RemoveProviderCommand { get; set; }

    public void ReplaceModels(IEnumerable<RemoteProviderModelItemViewModel> models)
    {
        Models.Clear();
        foreach (RemoteProviderModelItemViewModel model in models)
        {
            Models.Add(model);
        }

        ApplyModelFilter();
    }

    public void ApplyModelFilter()
    {
        IEnumerable<RemoteProviderModelItemViewModel> query = Models;
        if (!string.IsNullOrWhiteSpace(ModelFilterText))
        {
            string term = ModelFilterText.Trim();
            query = query.Where(model =>
                model.ModelId.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                model.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        FilteredModels.Clear();
        foreach (RemoteProviderModelItemViewModel model in query)
        {
            FilteredModels.Add(model);
        }
    }
}

/// <summary>Окно динамических облачных провайдеров.</summary>
public sealed class RemoteProvidersViewModel : ViewModelBase
{
    private readonly IRemoteProviderService _service;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private RemoteProviderItemViewModel? _selectedProvider;
    private bool _loaded;

    public RemoteProvidersViewModel(IRemoteProviderService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Presets = service.GetBuiltInPresets();
        AddPresetCommand = new RelayCommand(param =>
        {
            if (param is string presetId && !string.IsNullOrWhiteSpace(presetId))
            {
                AddPreset(presetId);
            }
        });
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
        _ = LoadAsync();
    }

    public IReadOnlyList<RemoteProviderProfileDto> Presets { get; }

    public IReadOnlyList<RemoteProtocolOption> ProtocolOptions { get; } =
    [
        new(RemoteProviderProtocol.OpenAiCompatible, "OpenAI-совместимый"),
        new(RemoteProviderProtocol.Anthropic, "Anthropic"),
        new(RemoteProviderProtocol.GoogleGemini, "Google Gemini"),
    ];

    public ObservableCollection<RemoteProviderItemViewModel> Providers { get; } = [];

    public RemoteProviderItemViewModel? SelectedProvider
    {
        get => _selectedProvider;
        set => SetProperty(ref _selectedProvider, value);
    }

    public Action? OnProvidersUpdated { get; set; }

    public Action<IntentTier, string, string>? OnAssignRemoteModelToTier { get; set; }

    public Action<string, string, string>? OnTestRemoteModelRequested { get; set; }

    public Action? RequestClose { get; set; }

    public RelayCommand AddPresetCommand { get; }

    public RelayCommand CloseCommand { get; }

    public async Task LoadAsync()
    {
        await _loadGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_loaded)
            {
                return;
            }

            Providers.Clear();
            foreach (RemoteProviderProfileDto profile in _service.LoadProviders())
            {
                RemoteProviderItemViewModel item = CreateItem(profile);
                item.HasSavedApiKey = await _service.HasApiKeyAsync(profile.Id).ConfigureAwait(true);
                Providers.Add(item);
            }

            SelectedProvider ??= Providers.FirstOrDefault();
            _loaded = true;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public void AddPreset(string presetId)
    {
        RemoteProviderProfileDto? preset = Presets.FirstOrDefault(item =>
            string.Equals(item.Id, presetId, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            return;
        }

        string id = NextId(preset.Id);
        var created = preset with
        {
            Id = id,
            Name = string.Equals(id, preset.Id, StringComparison.OrdinalIgnoreCase) ? preset.Name : $"{preset.Name} {id}",
            ApiKeySecretName = SecretKeys.ForModelProvider(id),
            IsEnabled = true,
            Models = preset.Models.Select(model => model with { }).ToList(),
        };
        RemoteProviderItemViewModel item = CreateItem(created);
        Providers.Add(item);
        SelectedProvider = item;
        _service.SaveProviders(Providers.Select(ToDto));
        OnProvidersUpdated?.Invoke();
    }

    public async Task SaveAsync()
    {
        foreach (RemoteProviderItemViewModel provider in Providers)
        {
            if (string.IsNullOrWhiteSpace(provider.ApiKeyInput))
            {
                continue;
            }

            await _service.SaveApiKeyAsync(provider.Id, provider.ApiKeyInput).ConfigureAwait(true);
            provider.ApiKeyInput = string.Empty;
            provider.HasSavedApiKey = true;
        }

        _service.SaveProviders(Providers.Select(ToDto));
        OnProvidersUpdated?.Invoke();
    }

    public async Task DeleteKeyAsync(RemoteProviderItemViewModel provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        await _service.DeleteApiKeyAsync(provider.Id).ConfigureAwait(true);
        provider.HasSavedApiKey = false;
        provider.ApiKeyInput = string.Empty;
    }

    public async Task ProbeAsync(RemoteProviderItemViewModel provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        provider.ProbeStatusText = "Проверка связи...";
        string? unsaved = string.IsNullOrWhiteSpace(provider.ApiKeyInput) ? null : provider.ApiKeyInput.Trim();
        RemoteProviderProbeResult result = await _service.ProbeAndDiscoverModelsAsync(ToDto(provider), unsaved).ConfigureAwait(true);
        provider.ProbeStatusText = result.Message ?? (result.Success ? "Готово." : "Ошибка проверки.");
        if (!result.Success)
        {
            return;
        }

        provider.ReplaceModels(result.DiscoveredModels.Select(model => CreateModel(provider, model)));
        _service.SaveProviders(Providers.Select(ToDto));
        OnProvidersUpdated?.Invoke();
    }

    public void AddModel(RemoteProviderItemViewModel provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(provider.NewModelId))
        {
            return;
        }

        string modelId = provider.NewModelId.Trim();
        if (provider.Models.Any(model => string.Equals(model.ModelId, modelId, StringComparison.OrdinalIgnoreCase)))
        {
            provider.NewModelId = string.Empty;
            return;
        }

        var dto = new RemoteModelEntryDto
        {
            ModelId = modelId,
            DisplayName = string.IsNullOrWhiteSpace(provider.NewModelDisplayName) ? modelId : provider.NewModelDisplayName.Trim(),
            IsEnabled = true,
        };
        RemoteProviderModelItemViewModel model = CreateModel(provider, dto);
        provider.Models.Add(model);
        provider.ApplyModelFilter();
        provider.NewModelId = string.Empty;
        provider.NewModelDisplayName = string.Empty;
        _service.SaveProviders(Providers.Select(ToDto));
        OnProvidersUpdated?.Invoke();
    }

    public void RemoveProvider(RemoteProviderItemViewModel provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Providers.Remove(provider);
        if (ReferenceEquals(SelectedProvider, provider))
        {
            SelectedProvider = Providers.FirstOrDefault();
        }

        _service.SaveProviders(Providers.Select(ToDto));
        OnProvidersUpdated?.Invoke();
    }

    private RemoteProviderItemViewModel CreateItem(RemoteProviderProfileDto profile)
    {
        var item = new RemoteProviderItemViewModel
        {
            Id = profile.Id,
            Name = profile.Name,
            Protocol = profile.Protocol,
            BaseUrl = profile.BaseUrl,
            IsEnabled = profile.IsEnabled,
        };
        item.SaveCommand = new AsyncRelayCommand(SaveAsync);
        item.DeleteKeyCommand = new AsyncRelayCommand(() => DeleteKeyAsync(item));
        item.ProbeCommand = new AsyncRelayCommand(() => ProbeAsync(item));
        item.AddModelCommand = new RelayCommand(() => AddModel(item));
        item.RemoveProviderCommand = new RelayCommand(() => RemoveProvider(item));
        item.ReplaceModels(profile.Models.Select(model => CreateModel(item, model)));
        return item;
    }

    private RemoteProviderModelItemViewModel CreateModel(RemoteProviderItemViewModel provider, RemoteModelEntryDto dto)
    {
        var model = new RemoteProviderModelItemViewModel
        {
            ModelId = dto.ModelId,
            DisplayName = dto.DisplayName,
            IsEnabled = dto.IsEnabled,
            RecommendedFor = dto.RecommendedFor,
            ContextWindowTokens = dto.ContextWindowTokens,
            InputPricePer1mUsd = dto.InputPricePer1mUsd,
            OutputPricePer1mUsd = dto.OutputPricePer1mUsd,
            Description = dto.Description,
        };
        model.AssignFastCommand = new RelayCommand(() =>
        {
            model.RecommendedFor = ModelTaskCategory.LightFastTier2;
            OnAssignRemoteModelToTier?.Invoke(IntentTier.Tier2, model.ModelId, provider.Name);
            _service.SaveProviders(Providers.Select(ToDto));
            OnProvidersUpdated?.Invoke();
        });
        model.AssignExpertCommand = new RelayCommand(() =>
        {
            model.RecommendedFor = ModelTaskCategory.HeavyReasoningTier3;
            OnAssignRemoteModelToTier?.Invoke(IntentTier.Tier3, model.ModelId, provider.Name);
            _service.SaveProviders(Providers.Select(ToDto));
            OnProvidersUpdated?.Invoke();
        });
        model.TestCommand = new RelayCommand(() =>
            OnTestRemoteModelRequested?.Invoke(model.ModelId, provider.Name, model.DisplayName));
        model.RemoveCommand = new RelayCommand(() =>
        {
            provider.Models.Remove(model);
            provider.ApplyModelFilter();
            _service.SaveProviders(Providers.Select(ToDto));
            OnProvidersUpdated?.Invoke();
        });
        return model;
    }

    private string NextId(string baseId)
    {
        if (Providers.All(provider => !string.Equals(provider.Id, baseId, StringComparison.OrdinalIgnoreCase)))
        {
            return baseId;
        }

        int index = 2;
        while (Providers.Any(provider => string.Equals(provider.Id, $"{baseId}-{index}", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        return $"{baseId}-{index}";
    }

    private static RemoteProviderProfileDto ToDto(RemoteProviderItemViewModel provider) =>
        new()
        {
            Id = provider.Id,
            Name = provider.Name.Trim(),
            Protocol = provider.Protocol,
            BaseUrl = provider.BaseUrl.Trim(),
            ApiKeySecretName = SecretKeys.ForModelProvider(provider.Id),
            IsEnabled = provider.IsEnabled,
            Models = provider.Models.Select(model => new RemoteModelEntryDto
            {
                ModelId = model.ModelId,
                DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? model.ModelId : model.DisplayName.Trim(),
                RecommendedFor = model.RecommendedFor,
                IsEnabled = model.IsEnabled,
                ContextWindowTokens = model.ContextWindowTokens,
                InputPricePer1mUsd = model.InputPricePer1mUsd,
                OutputPricePer1mUsd = model.OutputPricePer1mUsd,
                Description = model.Description,
            }).ToList(),
        };
}

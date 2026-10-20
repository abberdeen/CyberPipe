using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CyberPipe.Contracts;
using CyberPipe.Desktop.Common;
using CyberPipe.Desktop.Localization;
using CyberPipe.Desktop.Services;
using CyberPipe.Models;

namespace CyberPipe.Desktop.ViewModels;

public sealed class TierModelAssignmentViewModel : ViewModelBase
{
    private string _modelId = string.Empty;
    private ModelHostingType? _hostingType;
    private string _selectedModel = string.Empty;

    public required IntentTier Tier { get; init; }
    public required string TierName { get; init; }
    public string Description { get; init; } = string.Empty;
    public bool IsEditable => Tier != IntentTier.Tier0;
    public bool HasHostingSelector => Tier == IntentTier.Tier2 || Tier == IntentTier.Tier3;
    public RelayCommand? TestCommand { get; set; }

    public Action<TierModelAssignmentViewModel>? OnHostingTypeChanged { get; set; }

    public string ModelId
    {
        get => _modelId;
        set
        {
            if (SetProperty(ref _modelId, value))
            {
                if (_selectedModel != value)
                {
                    _selectedModel = value;
                    OnPropertyChanged(nameof(SelectedModel));
                }
            }
        }
    }

    public ModelHostingType? HostingType
    {
        get => _hostingType;
        set
        {
            if (SetProperty(ref _hostingType, value))
            {
                OnPropertyChanged(nameof(IsRemoteHosting));
                OnPropertyChanged(nameof(IsLocalHosting));
                OnPropertyChanged(nameof(HostingTypeText));
                OnHostingTypeChanged?.Invoke(this);
            }
        }
    }

    public string HostingTypeText => HostingType switch
    {
        ModelHostingType.Local => "Локальный (Local)",
        ModelHostingType.RemoteCloud => "Удаленный (Cloud)",
        _ => HostingType?.ToString() ?? "—"
    };

    public bool IsRemoteHosting
    {
        get => HostingType == ModelHostingType.RemoteCloud;
        set
        {
            ModelHostingType target = value ? ModelHostingType.RemoteCloud : ModelHostingType.Local;
            if (HostingType != target)
            {
                HostingType = target;
            }
        }
    }

    public bool IsLocalHosting
    {
        get => HostingType == ModelHostingType.Local;
        set
        {
            ModelHostingType target = value ? ModelHostingType.Local : ModelHostingType.RemoteCloud;
            if (HostingType != target)
            {
                HostingType = target;
            }
        }
    }

    public ObservableCollection<string> AvailableModels { get; } = [];

    public string SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (SetProperty(ref _selectedModel, value))
            {
                if (!string.IsNullOrWhiteSpace(value) && _modelId != value)
                {
                    _modelId = value;
                    OnPropertyChanged(nameof(ModelId));
                }
            }
        }
    }
}

public sealed class CatalogItemViewModel : ViewModelBase
{
    public required ModelDescriptorDto Model { get; init; }
    public required RelayCommand AssignCommand { get; init; }
    public RelayCommand? TestCommand { get; init; }

    public string Name => Model.Name;
    public string ParamsSummary => Model.ParamsSummary;
    public string BestFor => Model.BestFor;
    public ModelHostingType HostingType => Model.HostingType;
    public string Provider => Model.Provider;
}

public sealed class ModelsViewModel : ViewModelBase
{
    private readonly IDesktopNotificationService? _notifications;
    private readonly LocalizationManager _localization;
    private readonly IModelCatalog? _modelCatalog;
    private readonly string _uiOverridesPath;
    private readonly IModelTestService _modelTestService;
    private readonly IClipboardService _clipboardService;

    private string _installerNotice;
    private string _statusMessage = string.Empty;

    public ModelsViewModel(
        IEnumerable<TierModelAssignment>? assignments = null,
        IEnumerable<ModelDescriptorDto>? catalog = null,
        IDesktopNotificationService? notifications = null,
        LocalizationManager? localization = null,
        LocalModelInstallerViewModel? installer = null,
        IModelCatalog? modelCatalog = null,
        CyberPipeOptions? options = null,
        string? uiOverridesPath = null,
        IModelTestService? modelTestService = null,
        IClipboardService? clipboardService = null)
    {
        _notifications = notifications;
        _localization = localization ?? LocalizationManager.Instance;
        _installerNotice = _localization["Models.InstallerNotice"];
        _modelCatalog = modelCatalog;
        Installer = installer;
        _uiOverridesPath = uiOverridesPath ?? CyberPipeConfigurationLoader.GetDefaultUiOverridesPath();
        _modelTestService = modelTestService ?? new ModelTestService();
        _clipboardService = clipboardService ?? AvaloniaClipboardService.Default;

        LaunchInstallerCommand = new RelayCommand(LaunchInstaller);
        SaveTierAssignmentsCommand = new RelayCommand(SaveTierAssignments);
        ResetTierAssignmentsCommand = new RelayCommand(ResetTierAssignmentsToDefaults);
        AssignCatalogModelCommand = new RelayCommand(param =>
        {
            if (param is ModelDescriptorDto model)
            {
                AssignCatalogModel(model);
            }
        });
        AssignToTier1Command = new RelayCommand(param => AssignModelToTier(IntentTier.Tier1, param as string ?? ""));
        AssignToTier2Command = new RelayCommand(param => AssignModelToTier(IntentTier.Tier2, param as string ?? ""));
        AssignToTier3Command = new RelayCommand(param => AssignModelToTier(IntentTier.Tier3, param as string ?? ""));

        TestModelCommand = new RelayCommand(param =>
        {
            if (param is ModelDescriptorDto model)
            {
                OpenTestForModel(model);
            }
            else if (param is CatalogItemViewModel catalogItem)
            {
                OpenTestForModel(catalogItem.Model);
            }
            else if (param is LocalModelItemViewModel localItem)
            {
                OpenTestForModel(localItem.Model);
            }
            else if (param is TierModelAssignmentViewModel tierItem)
            {
                OpenTestForTier(tierItem);
            }
            else if (param is string modelId && !string.IsNullOrWhiteSpace(modelId))
            {
                OpenTestForModelId(modelId, ModelHostingType.Local);
            }
        });

        // Wire up installer assignment and test callbacks
        if (Installer != null)
        {
            Installer.OnAssignModel = model => AssignCatalogModel(model);
            Installer.OnTestModel = model => OpenTestForModel(model);
        }

        // Populate catalog
        if (catalog != null)
        {
            foreach (ModelDescriptorDto item in catalog)
            {
                Catalog.Add(item);
                CatalogItems.Add(new CatalogItemViewModel
                {
                    Model = item,
                    AssignCommand = new RelayCommand(() => AssignCatalogModel(item)),
                    TestCommand = new RelayCommand(() => OpenTestForModel(item)),
                });
            }
        }

        // Initialize assignments
        InitializeTierAssignments(assignments, options);
    }

    public LocalModelInstallerViewModel? Installer { get; }

    public bool HasInstaller => Installer != null;

    public ObservableCollection<TierModelAssignmentViewModel> Assignments { get; } = [];
    public ObservableCollection<ModelDescriptorDto> Catalog { get; } = [];
    public ObservableCollection<CatalogItemViewModel> CatalogItems { get; } = [];

    public string InstallerNotice
    {
        get => _installerNotice;
        set => SetProperty(ref _installerNotice, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(_statusMessage);

    public RelayCommand LaunchInstallerCommand { get; }
    public RelayCommand SaveTierAssignmentsCommand { get; }
    public RelayCommand ResetTierAssignmentsCommand { get; }
    public RelayCommand AssignCatalogModelCommand { get; }
    public RelayCommand AssignToTier1Command { get; }
    public RelayCommand AssignToTier2Command { get; }
    public RelayCommand AssignToTier3Command { get; }
    public RelayCommand TestModelCommand { get; }

    public Func<ModelTestViewModel, Task>? OpenModelTestRequested { get; set; }

    public void OpenTestForModel(ModelDescriptorDto model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var vm = new ModelTestViewModel(model, _modelTestService, _clipboardService);
        OpenModelTestRequested?.Invoke(vm);
    }

    public void OpenTestForModelId(string modelId, ModelHostingType hosting, string? provider = null, string? name = null)
    {
        var vm = new ModelTestViewModel(modelId, hosting, provider ?? "Ollama", name ?? modelId, _modelTestService, _clipboardService);
        OpenModelTestRequested?.Invoke(vm);
    }

    public void OpenTestForTier(TierModelAssignmentViewModel tierItem)
    {
        if (tierItem == null) return;
        string modelId = string.IsNullOrWhiteSpace(tierItem.SelectedModel) ? tierItem.ModelId : tierItem.SelectedModel;
        ModelHostingType hosting = tierItem.HostingType ?? (tierItem.Tier == IntentTier.Tier3 ? ModelHostingType.RemoteCloud : ModelHostingType.Local);
        string provider = hosting == ModelHostingType.RemoteCloud ? "Cloud" : "Ollama";
        if (tierItem.Tier == IntentTier.Tier1) provider = "ONNX";
        if (tierItem.Tier == IntentTier.Tier0) provider = "Regex";

        ModelDescriptorDto? descriptor = Catalog.FirstOrDefault(m =>
            string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
        string runtimeId = string.IsNullOrWhiteSpace(descriptor?.ModelId) ? modelId : descriptor.ModelId;
        OpenTestForModelId(runtimeId, hosting, provider, $"{tierItem.TierName}: {descriptor?.Name ?? runtimeId}");
    }

    private void InitializeTierAssignments(
        IEnumerable<TierModelAssignment>? assignments,
        CyberPipeOptions? options)
    {
        Assignments.Clear();

        string tier0Model = "builtin-regex-rules";
        string tier1Model = options?.Models?.EmbeddingModelId ?? "multilingual-e5-small";
        string tier2Model = options?.Router?.Tier2ModelId ?? "qwen2.5:3b";
        string tier3Model = options?.Router?.Tier3ModelId ?? "claude-3-5-sonnet";
        ModelHostingType tier2Hosting = options?.Router?.Tier2HostingType ?? ModelHostingType.Local;
        ModelHostingType tier3Hosting = options?.Router?.Tier3HostingType ?? ModelHostingType.RemoteCloud;

        if (assignments != null)
        {
            foreach (TierModelAssignment a in assignments)
            {
                switch (a.Tier)
                {
                    case IntentTier.Tier0:
                        tier0Model = a.ModelId;
                        break;
                    case IntentTier.Tier1:
                        tier1Model = a.ModelId;
                        break;
                    case IntentTier.Tier2:
                        tier2Model = a.ModelId;
                        if (a.HostingType.HasValue) tier2Hosting = a.HostingType.Value;
                        break;
                    case IntentTier.Tier3:
                        tier3Model = a.ModelId;
                        if (a.HostingType.HasValue) tier3Hosting = a.HostingType.Value;
                        break;
                }
            }
        }

        // Tier 1 Candidates
        var tier1Candidates = new List<string> { "multilingual-e5-small" };
        foreach (ModelDescriptorDto item in Catalog)
        {
            if (item.RecommendedFor == ModelTaskCategory.EmbeddingTier1 || string.Equals(item.Provider, "ONNX", StringComparison.OrdinalIgnoreCase))
            {
                if (!tier1Candidates.Contains(item.Id))
                    tier1Candidates.Add(item.Id);
            }
        }

        // Tier 0
        var t0Vm = new TierModelAssignmentViewModel
        {
            Tier = IntentTier.Tier0,
            TierName = "Tier 0 (Regex)",
            Description = "Детерминированные правила regex (фиксировано)",
            ModelId = tier0Model,
            SelectedModel = tier0Model,
            HostingType = ModelHostingType.Local,
        };
        t0Vm.TestCommand = new RelayCommand(() => OpenTestForTier(t0Vm));
        Assignments.Add(t0Vm);

        // Tier 1
        var t1Vm = new TierModelAssignmentViewModel
        {
            Tier = IntentTier.Tier1,
            TierName = "Tier 1 (Semantic)",
            Description = "Семантический роутер и эмбеддинг векторной базы",
            ModelId = tier1Model,
            SelectedModel = tier1Model,
            HostingType = ModelHostingType.Local,
        };
        foreach (string c in tier1Candidates) t1Vm.AvailableModels.Add(c);
        t1Vm.TestCommand = new RelayCommand(() => OpenTestForTier(t1Vm));
        Assignments.Add(t1Vm);

        // Tier 2
        var t2Vm = new TierModelAssignmentViewModel
        {
            Tier = IntentTier.Tier2,
            TierName = "Tier 2 (Small LLM)",
            Description = "Быстрая LLM (локальная или облачная)",
            ModelId = tier2Model,
            SelectedModel = tier2Model,
            HostingType = tier2Hosting,
            OnHostingTypeChanged = HandleTierHostingTypeChanged,
        };
        var t2Candidates = GetCandidatesForHosting(tier2Hosting, IntentTier.Tier2);
        if (!t2Candidates.Contains(tier2Model)) t2Candidates.Insert(0, tier2Model);
        foreach (string c in t2Candidates) t2Vm.AvailableModels.Add(c);
        t2Vm.TestCommand = new RelayCommand(() => OpenTestForTier(t2Vm));
        Assignments.Add(t2Vm);

        // Tier 3
        var t3Vm = new TierModelAssignmentViewModel
        {
            Tier = IntentTier.Tier3,
            TierName = "Tier 3 (Large LLM)",
            Description = "Мощная рассуждающая LLM (облачная или локальная)",
            ModelId = tier3Model,
            SelectedModel = tier3Model,
            HostingType = tier3Hosting,
            OnHostingTypeChanged = HandleTierHostingTypeChanged,
        };
        var t3Candidates = GetCandidatesForHosting(tier3Hosting, IntentTier.Tier3);
        if (!t3Candidates.Contains(tier3Model)) t3Candidates.Insert(0, tier3Model);
        foreach (string c in t3Candidates) t3Vm.AvailableModels.Add(c);
        t3Vm.TestCommand = new RelayCommand(() => OpenTestForTier(t3Vm));
        Assignments.Add(t3Vm);
    }

    public List<string> GetCandidatesForHosting(ModelHostingType hosting, IntentTier tier)
    {
        var candidates = new List<string>();
        ModelTaskCategory targetCategory = tier == IntentTier.Tier3
            ? ModelTaskCategory.HeavyReasoningTier3
            : ModelTaskCategory.LightFastTier2;

        // First add catalog models that match both hosting and tier recommendation
        foreach (ModelDescriptorDto item in Catalog)
        {
            if (item.RecommendedFor == ModelTaskCategory.EmbeddingTier1 ||
                string.Equals(item.Provider, "ONNX", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (item.HostingType == hosting && item.RecommendedFor == targetCategory && !candidates.Contains(item.Id))
            {
                candidates.Add(item.Id);
            }
        }

        // Then add other catalog models matching hosting
        foreach (ModelDescriptorDto item in Catalog)
        {
            if (item.RecommendedFor == ModelTaskCategory.EmbeddingTier1 ||
                string.Equals(item.Provider, "ONNX", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (item.HostingType == hosting && !candidates.Contains(item.Id))
            {
                candidates.Add(item.Id);
            }
        }

        if (hosting == ModelHostingType.Local)
        {
            string[] defaults = tier == IntentTier.Tier3
                ? ["qwen2.5:7b", "llama3.1:8b", "qwen2.5:3b", "llama3.2:3b", "mistral"]
                : ["qwen2.5:3b", "llama3.2:3b", "qwen2.5:7b", "gemma2:2b", "mistral"];
            foreach (string d in defaults)
            {
                if (!candidates.Contains(d))
                    candidates.Add(d);
            }
        }
        else // RemoteCloud
        {
            string[] defaults = tier == IntentTier.Tier2
                ? ["gpt-4o-mini", "claude-3-haiku", "gemini-1.5-flash", "gpt-4o", "claude-3-5-sonnet"]
                : ["claude-3-5-sonnet", "gpt-4o", "gpt-4o-mini", "claude-3-haiku", "gemini-1.5-pro"];
            foreach (string d in defaults)
            {
                if (!candidates.Contains(d))
                    candidates.Add(d);
            }
        }

        return candidates;
    }

    private void HandleTierHostingTypeChanged(TierModelAssignmentViewModel vm)
    {
        if (vm.Tier != IntentTier.Tier2 && vm.Tier != IntentTier.Tier3)
            return;

        ModelHostingType hosting = vm.HostingType ?? (vm.Tier == IntentTier.Tier2 ? ModelHostingType.Local : ModelHostingType.RemoteCloud);
        var candidates = GetCandidatesForHosting(hosting, vm.Tier);

        vm.AvailableModels.Clear();
        foreach (var c in candidates)
        {
            vm.AvailableModels.Add(c);
        }

        bool currentMatches = candidates.Contains(vm.ModelId);
        if (!currentMatches)
        {
            var catalogItem = Catalog.FirstOrDefault(c => string.Equals(c.Id, vm.ModelId, StringComparison.OrdinalIgnoreCase));
            if (catalogItem != null && catalogItem.HostingType == hosting)
            {
                currentMatches = true;
                vm.AvailableModels.Insert(0, vm.ModelId);
            }
        }

        if (!currentMatches)
        {
            string fallback = candidates.Count > 0 ? candidates[0] : (hosting == ModelHostingType.Local ? "qwen2.5:3b" : "gpt-4o-mini");
            vm.ModelId = fallback;
            vm.SelectedModel = fallback;
        }

        SaveTierAssignments();
    }

    public void SaveTierAssignments()
    {
        try
        {
            string? tier1 = Assignments.FirstOrDefault(a => a.Tier == IntentTier.Tier1)?.ModelId?.Trim();
            string? tier2 = Assignments.FirstOrDefault(a => a.Tier == IntentTier.Tier2)?.ModelId?.Trim();
            string? tier3 = Assignments.FirstOrDefault(a => a.Tier == IntentTier.Tier3)?.ModelId?.Trim();

            TierModelAssignmentViewModel? t2 = Assignments.FirstOrDefault(a => a.Tier == IntentTier.Tier2);
            TierModelAssignmentViewModel? t3 = Assignments.FirstOrDefault(a => a.Tier == IntentTier.Tier3);

            if (t2 != null && t2.HostingType == null)
            {
                t2.HostingType = ResolveHostingType(t2.ModelId, ModelHostingType.Local);
            }
            if (t3 != null && t3.HostingType == null)
            {
                t3.HostingType = ResolveHostingType(t3.ModelId, ModelHostingType.RemoteCloud);
            }

            string path = _uiOverridesPath;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            JsonObject root = File.Exists(path)
                ? (JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject())
                : new JsonObject();

            if (root["Router"] is not JsonObject routerObj)
            {
                routerObj = new JsonObject();
                root["Router"] = routerObj;
            }

            if (!string.IsNullOrWhiteSpace(tier2))
            {
                routerObj["Tier2ModelId"] = tier2;
            }

            if (!string.IsNullOrWhiteSpace(tier3))
            {
                routerObj["Tier3ModelId"] = tier3;
            }

            if (t2?.HostingType != null)
            {
                routerObj["Tier2HostingType"] = t2.HostingType.Value.ToString();
            }

            if (t3?.HostingType != null)
            {
                routerObj["Tier3HostingType"] = t3.HostingType.Value.ToString();
            }

            if (root["Models"] is not JsonObject modelsObj)
            {
                modelsObj = new JsonObject();
                root["Models"] = modelsObj;
            }

            if (!string.IsNullOrWhiteSpace(tier1))
            {
                modelsObj["EmbeddingModelId"] = tier1;
            }

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, root.ToJsonString(jsonOptions));

            try
            {
                Program.Options = Program.Options with
                {
                    Router = Program.Options.Router with
                    {
                        Tier2ModelId = tier2 ?? Program.Options.Router.Tier2ModelId,
                        Tier3ModelId = tier3 ?? Program.Options.Router.Tier3ModelId,
                        Tier2HostingType = t2?.HostingType ?? Program.Options.Router.Tier2HostingType,
                        Tier3HostingType = t3?.HostingType ?? Program.Options.Router.Tier3HostingType,
                    },
                    Models = Program.Options.Models with
                    {
                        EmbeddingModelId = tier1 ?? Program.Options.Models.EmbeddingModelId,
                    }
                };
            }
            catch
            {
                // In testing environments Program.Options may not be initialized or modifiable
            }

            StatusMessage = $"✓ Назначения сохранены в {Path.GetFileName(path)} (Tier 1: {tier1}, Tier 2: {tier2} [{t2?.HostingType}], Tier 3: {tier3} [{t3?.HostingType}])";
            _notifications?.ShowNotification(
                "Назначение моделей",
                $"Назначения ярусов успешно сохранены:\nTier 1: {tier1}\nTier 2: {tier2} ({t2?.HostingType})\nTier 3: {tier3} ({t3?.HostingType})",
                NotificationSeverity.Success);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка сохранения назначений: {ex.Message}";
            _notifications?.ShowNotification(
                "Ошибка сохранения",
                ex.Message,
                NotificationSeverity.Error);
        }
    }

    public void ResetTierAssignmentsToDefaults()
    {
        TierModelAssignmentViewModel? t1 = Assignments.FirstOrDefault(a => a.Tier == IntentTier.Tier1);
        if (t1 != null) { t1.ModelId = "multilingual-e5-small"; t1.SelectedModel = "multilingual-e5-small"; }

        TierModelAssignmentViewModel? t2 = Assignments.FirstOrDefault(a => a.Tier == IntentTier.Tier2);
        if (t2 != null)
        {
            t2.HostingType = ModelHostingType.Local;
            var c2 = GetCandidatesForHosting(ModelHostingType.Local, IntentTier.Tier2);
            t2.AvailableModels.Clear();
            foreach (var m in c2) t2.AvailableModels.Add(m);
            t2.ModelId = "qwen2.5:3b";
            t2.SelectedModel = "qwen2.5:3b";
        }

        TierModelAssignmentViewModel? t3 = Assignments.FirstOrDefault(a => a.Tier == IntentTier.Tier3);
        if (t3 != null)
        {
            t3.HostingType = ModelHostingType.RemoteCloud;
            var c3 = GetCandidatesForHosting(ModelHostingType.RemoteCloud, IntentTier.Tier3);
            t3.AvailableModels.Clear();
            foreach (var m in c3) t3.AvailableModels.Add(m);
            t3.ModelId = "claude-3-5-sonnet";
            t3.SelectedModel = "claude-3-5-sonnet";
        }

        SaveTierAssignments();
    }

    public void AssignModelToTier(IntentTier tier, string modelId, ModelHostingType? hostingType = null)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return;

        TierModelAssignmentViewModel? target = Assignments.FirstOrDefault(a => a.Tier == tier);
        if (target != null && target.IsEditable)
        {
            if (hostingType.HasValue && target.HasHostingSelector && target.HostingType != hostingType.Value)
            {
                target.HostingType = hostingType.Value;
            }

            if (!target.AvailableModels.Contains(modelId))
            {
                target.AvailableModels.Insert(0, modelId);
            }
            target.ModelId = modelId;
            target.SelectedModel = modelId;
            SaveTierAssignments();
        }
    }

    public void AssignCatalogModel(ModelDescriptorDto? model)
    {
        if (model == null) return;

        if (model.RecommendedFor == ModelTaskCategory.EmbeddingTier1 || string.Equals(model.Provider, "ONNX", StringComparison.OrdinalIgnoreCase))
        {
            AssignModelToTier(IntentTier.Tier1, model.Id, ModelHostingType.Local);
        }
        else if (model.RecommendedFor == ModelTaskCategory.LightFastTier2)
        {
            AssignModelToTier(IntentTier.Tier2, model.Id, model.HostingType);
        }
        else if (model.RecommendedFor == ModelTaskCategory.HeavyReasoningTier3)
        {
            AssignModelToTier(IntentTier.Tier3, model.Id, model.HostingType);
        }
        else if (model.HostingType == ModelHostingType.Local)
        {
            AssignModelToTier(IntentTier.Tier2, model.Id, ModelHostingType.Local);
        }
        else
        {
            AssignModelToTier(IntentTier.Tier3, model.Id, ModelHostingType.RemoteCloud);
        }
    }

    private ModelHostingType ResolveHostingType(string modelId, ModelHostingType fallback)
    {
        ModelDescriptorDto? descriptor = Catalog.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
        return descriptor?.HostingType ?? fallback;
    }

    public void LaunchInstaller()
    {
        if (Installer != null)
        {
            _ = Installer.RefreshAsync();
            _notifications?.ShowNotification(
                "Model Installer",
                "Ollama runtime & local models installer refreshed.",
                NotificationSeverity.Info);
        }
        else
        {
            _notifications?.ShowNotification(
                "Model Installer",
                "Local model installer is unavailable.",
                NotificationSeverity.Warning);
        }
    }
}

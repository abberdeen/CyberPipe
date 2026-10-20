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
        set => SetProperty(ref _hostingType, value);
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

public sealed class ModelsViewModel : ViewModelBase
{
    private readonly IDesktopNotificationService? _notifications;
    private readonly LocalizationManager _localization;
    private readonly IModelCatalog? _modelCatalog;
    private readonly string _uiOverridesPath;

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
        string? uiOverridesPath = null)
    {
        _notifications = notifications;
        _localization = localization ?? LocalizationManager.Instance;
        _installerNotice = _localization["Models.InstallerNotice"];
        _modelCatalog = modelCatalog;
        Installer = installer;
        _uiOverridesPath = uiOverridesPath ?? CyberPipeConfigurationLoader.GetDefaultUiOverridesPath();

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

        // Populate catalog
        if (catalog != null)
        {
            foreach (ModelDescriptorDto item in catalog)
            {
                Catalog.Add(item);
            }
        }

        // Initialize assignments
        InitializeTierAssignments(assignments, options);
    }

    public LocalModelInstallerViewModel? Installer { get; }

    public bool HasInstaller => Installer != null;

    public ObservableCollection<TierModelAssignmentViewModel> Assignments { get; } = [];
    public ObservableCollection<ModelDescriptorDto> Catalog { get; } = [];

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

    private void InitializeTierAssignments(
        IEnumerable<TierModelAssignment>? assignments,
        CyberPipeOptions? options)
    {
        Assignments.Clear();

        string tier0Model = "builtin-regex-rules";
        string tier1Model = options?.Models?.EmbeddingModelId ?? "multilingual-e5-small";
        string tier2Model = options?.Router?.Tier2ModelId ?? "qwen2.5:3b";
        string tier3Model = options?.Router?.Tier3ModelId ?? "claude-3-5-sonnet";

        if (assignments != null)
        {
            foreach (TierModelAssignment a in assignments)
            {
                switch (a.Tier)
                {
                    case IntentTier.Tier0: tier0Model = a.ModelId; break;
                    case IntentTier.Tier1: tier1Model = a.ModelId; break;
                    case IntentTier.Tier2: tier2Model = a.ModelId; break;
                    case IntentTier.Tier3: tier3Model = a.ModelId; break;
                }
            }
        }

        // Tier 1 Candidates
        var tier1Candidates = new List<string> { "multilingual-e5-small" };
        var tier2Candidates = new List<string>();
        var tier3Candidates = new List<string>();

        foreach (ModelDescriptorDto item in Catalog)
        {
            if (item.RecommendedFor == ModelTaskCategory.EmbeddingTier1 || string.Equals(item.Provider, "ONNX", StringComparison.OrdinalIgnoreCase))
            {
                if (!tier1Candidates.Contains(item.Id))
                    tier1Candidates.Add(item.Id);
            }
            if (item.RecommendedFor == ModelTaskCategory.LightFastTier2 || item.HostingType == ModelHostingType.Local)
            {
                if (!tier2Candidates.Contains(item.Id))
                    tier2Candidates.Add(item.Id);
            }
            if (item.RecommendedFor == ModelTaskCategory.HeavyReasoningTier3 || item.HostingType == ModelHostingType.RemoteCloud)
            {
                if (!tier3Candidates.Contains(item.Id))
                    tier3Candidates.Add(item.Id);
            }
        }

        if (!tier2Candidates.Contains("qwen2.5:3b")) tier2Candidates.Insert(0, "qwen2.5:3b");
        if (!tier3Candidates.Contains("claude-3-5-sonnet")) tier3Candidates.Insert(0, "claude-3-5-sonnet");

        // Tier 0
        Assignments.Add(new TierModelAssignmentViewModel
        {
            Tier = IntentTier.Tier0,
            TierName = "Tier 0 (Regex)",
            Description = "Детерминированные правила regex (фиксировано)",
            ModelId = tier0Model,
            SelectedModel = tier0Model,
            HostingType = ModelHostingType.Local,
        });

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
        Assignments.Add(t1Vm);

        // Tier 2
        var t2Vm = new TierModelAssignmentViewModel
        {
            Tier = IntentTier.Tier2,
            TierName = "Tier 2 (Small LLM)",
            Description = "Быстрая локальная LLM для простых интентов",
            ModelId = tier2Model,
            SelectedModel = tier2Model,
            HostingType = ResolveHostingType(tier2Model, ModelHostingType.Local),
        };
        foreach (string c in tier2Candidates) t2Vm.AvailableModels.Add(c);
        Assignments.Add(t2Vm);

        // Tier 3
        var t3Vm = new TierModelAssignmentViewModel
        {
            Tier = IntentTier.Tier3,
            TierName = "Tier 3 (Large LLM)",
            Description = "Мощная рассуждающая LLM для сложных сценариев",
            ModelId = tier3Model,
            SelectedModel = tier3Model,
            HostingType = ResolveHostingType(tier3Model, ModelHostingType.RemoteCloud),
        };
        foreach (string c in tier3Candidates) t3Vm.AvailableModels.Add(c);
        Assignments.Add(t3Vm);
    }

    public void SaveTierAssignments()
    {
        try
        {
            string? tier1 = Assignments.FirstOrDefault(a => a.Tier == IntentTier.Tier1)?.ModelId?.Trim();
            string? tier2 = Assignments.FirstOrDefault(a => a.Tier == IntentTier.Tier2)?.ModelId?.Trim();
            string? tier3 = Assignments.FirstOrDefault(a => a.Tier == IntentTier.Tier3)?.ModelId?.Trim();

            foreach (TierModelAssignmentViewModel a in Assignments)
            {
                if (a.Tier == IntentTier.Tier2)
                    a.HostingType = ResolveHostingType(a.ModelId, ModelHostingType.Local);
                else if (a.Tier == IntentTier.Tier3)
                    a.HostingType = ResolveHostingType(a.ModelId, ModelHostingType.RemoteCloud);
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

            StatusMessage = $"✓ Назначения сохранены в {Path.GetFileName(path)} (Tier 1: {tier1}, Tier 2: {tier2}, Tier 3: {tier3})";
            _notifications?.ShowNotification(
                "Назначение моделей",
                $"Назначения ярусов успешно сохранены:\nTier 1: {tier1}\nTier 2: {tier2}\nTier 3: {tier3}",
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
        if (t2 != null) { t2.ModelId = "qwen2.5:3b"; t2.SelectedModel = "qwen2.5:3b"; }

        TierModelAssignmentViewModel? t3 = Assignments.FirstOrDefault(a => a.Tier == IntentTier.Tier3);
        if (t3 != null) { t3.ModelId = "claude-3-5-sonnet"; t3.SelectedModel = "claude-3-5-sonnet"; }

        SaveTierAssignments();
    }

    public void AssignModelToTier(IntentTier tier, string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return;

        TierModelAssignmentViewModel? target = Assignments.FirstOrDefault(a => a.Tier == tier);
        if (target != null && target.IsEditable)
        {
            if (!target.AvailableModels.Contains(modelId))
            {
                target.AvailableModels.Add(modelId);
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
            AssignModelToTier(IntentTier.Tier1, model.Id);
        }
        else if (model.RecommendedFor == ModelTaskCategory.LightFastTier2)
        {
            AssignModelToTier(IntentTier.Tier2, model.Id);
        }
        else if (model.RecommendedFor == ModelTaskCategory.HeavyReasoningTier3)
        {
            AssignModelToTier(IntentTier.Tier3, model.Id);
        }
        else if (model.HostingType == ModelHostingType.Local)
        {
            AssignModelToTier(IntentTier.Tier2, model.Id);
        }
        else
        {
            AssignModelToTier(IntentTier.Tier3, model.Id);
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

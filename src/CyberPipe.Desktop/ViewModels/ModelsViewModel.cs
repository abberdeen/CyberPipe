using System.Collections.ObjectModel;
using CyberPipe.Contracts;
using CyberPipe.Desktop.Common;
using CyberPipe.Desktop.Localization;
using CyberPipe.Desktop.Services;

namespace CyberPipe.Desktop.ViewModels;

public sealed class TierModelAssignmentViewModel : ViewModelBase
{
    public required IntentTier Tier { get; init; }
    public required string TierName { get; init; }
    public required string ModelId { get; init; }
    public ModelHostingType? HostingType { get; init; }
}

public sealed class ModelsViewModel : ViewModelBase
{
    private readonly IDesktopNotificationService? _notifications;
    private readonly LocalizationManager _localization;

    private string _installerNotice;

    public ModelsViewModel(
        IEnumerable<TierModelAssignment>? assignments = null,
        IEnumerable<ModelDescriptorDto>? catalog = null,
        IDesktopNotificationService? notifications = null,
        LocalizationManager? localization = null)
    {
        _notifications = notifications;
        _localization = localization ?? LocalizationManager.Instance;
        _installerNotice = _localization["Models.InstallerNotice"];

        LaunchInstallerCommand = new RelayCommand(LaunchInstaller);

        // Populate tier assignments
        if (assignments != null)
        {
            foreach (TierModelAssignment a in assignments)
            {
                Assignments.Add(new TierModelAssignmentViewModel
                {
                    Tier = a.Tier,
                    TierName = a.Tier.ToString(),
                    ModelId = a.ModelId,
                    HostingType = a.HostingType,
                });
            }
        }
        else
        {
            // Default expected assignments
            Assignments.Add(new TierModelAssignmentViewModel { Tier = IntentTier.Tier0, TierName = "Tier 0 (Regex)", ModelId = "builtin-regex-rules", HostingType = ModelHostingType.Local });
            Assignments.Add(new TierModelAssignmentViewModel { Tier = IntentTier.Tier1, TierName = "Tier 1 (Semantic)", ModelId = "bge-m3-onnx", HostingType = ModelHostingType.Local });
            Assignments.Add(new TierModelAssignmentViewModel { Tier = IntentTier.Tier2, TierName = "Tier 2 (Small LLM)", ModelId = "qwen2.5-coder:7b", HostingType = ModelHostingType.Local });
            Assignments.Add(new TierModelAssignmentViewModel { Tier = IntentTier.Tier3, TierName = "Tier 3 (Large LLM)", ModelId = "claude-3-5-sonnet", HostingType = ModelHostingType.RemoteCloud });
        }

        // Populate catalog
        if (catalog != null)
        {
            foreach (ModelDescriptorDto item in catalog)
            {
                Catalog.Add(item);
            }
        }
    }

    public ObservableCollection<TierModelAssignmentViewModel> Assignments { get; } = [];
    public ObservableCollection<ModelDescriptorDto> Catalog { get; } = [];

    public string InstallerNotice
    {
        get => _installerNotice;
        set => SetProperty(ref _installerNotice, value);
    }

    public RelayCommand LaunchInstallerCommand { get; }

    public void LaunchInstaller()
    {
        _notifications?.ShowNotification(
            "Model Installer",
            "Ollama installer and hardware advisor will be initialized in TASK-081 & TASK-082.",
            NotificationSeverity.Info);
    }
}

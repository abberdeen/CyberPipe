using CyberPipe.Desktop.Common;
using CyberPipe.Desktop.Localization;
using CyberPipe.Desktop.Services;

namespace CyberPipe.Desktop.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private int _selectedTabIndex;
    private bool _isFirstRun;
    private string? _setupWizardNotice;

    public MainWindowViewModel(
        ChatViewModel chat,
        ChainsViewModel chains,
        HitlViewModel hitl,
        ServiceViewModel service,
        SchedulesViewModel schedules,
        ModelsViewModel models,
        IDesktopNotificationService? notifications = null,
        IThemeService? themeService = null,
        LocalizationManager? localization = null,
        bool isFirstRun = false)
    {
        Chat = chat ?? throw new ArgumentNullException(nameof(chat));
        Chains = chains ?? throw new ArgumentNullException(nameof(chains));
        Hitl = hitl ?? throw new ArgumentNullException(nameof(hitl));
        Service = service ?? throw new ArgumentNullException(nameof(service));
        Schedules = schedules ?? throw new ArgumentNullException(nameof(schedules));
        Models = models ?? throw new ArgumentNullException(nameof(models));
        Notifications = notifications ?? new DesktopNotificationService();
        ThemeService = themeService ?? new ThemeService();
        Localization = localization ?? LocalizationManager.Instance;
        _isFirstRun = isFirstRun;

        ToggleThemeCommand = new RelayCommand(() => ThemeService.ToggleTheme());
        SwitchLanguageCommand = new RelayCommand(param =>
        {
            if (param is string lang)
            {
                Localization.CurrentCulture = lang;
            }
            else
            {
                Localization.CurrentCulture = Localization.CurrentCulture == "ru" ? "en" : "ru";
            }
        });

        LaunchSetupWizardCommand = new RelayCommand(LaunchSetupWizard);
        DismissSetupBannerCommand = new RelayCommand(() => IsFirstRun = false);
    }

    public ChatViewModel Chat { get; }
    public ChainsViewModel Chains { get; }
    public HitlViewModel Hitl { get; }
    public ServiceViewModel Service { get; }
    public SchedulesViewModel Schedules { get; }
    public ModelsViewModel Models { get; }
    public IDesktopNotificationService Notifications { get; }
    public IThemeService ThemeService { get; }
    public LocalizationManager Localization { get; }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public bool IsFirstRun
    {
        get => _isFirstRun;
        set => SetProperty(ref _isFirstRun, value);
    }

    public string? SetupWizardNotice
    {
        get => _setupWizardNotice;
        set => SetProperty(ref _setupWizardNotice, value);
    }

    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand SwitchLanguageCommand { get; }
    public RelayCommand LaunchSetupWizardCommand { get; }
    public RelayCommand DismissSetupBannerCommand { get; }

    public void LaunchSetupWizard()
    {
        SetupWizardNotice = "Launching Setup Wizard (TASK-063)...";
        Notifications.ShowNotification(
            "Setup Wizard",
            "First-run setup wizard will guide through database, owner credentials, and model initialization (TASK-063).",
            NotificationSeverity.Info);
    }
}

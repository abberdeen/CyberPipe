using CyberPipe.Contracts;
using CyberPipe.Desktop.Services;
using CyberPipe.Models;
using CyberPipe.ServiceControl;
using Microsoft.Extensions.Logging;

namespace CyberPipe.Desktop.ViewModels;

/// <summary>
/// Фабрика создания MainWindowViewModel для автономного запуска Desktop и UI-тестов.
/// Панель Service подключается к реальной службе через клиент управляющего канала
/// TASK-021 (Named Pipe + SCM-fallback); фейков здесь нет.
/// </summary>
public static class MainWindowViewModelFactory
{
    public static MainWindowViewModel CreateDefault()
    {
        CyberPipeOptions options = Program.Options;
        ILogger? controlLogger = null;
        try
        {
            controlLogger = Program.LoggerFactory.CreateLogger("CyberPipe.Desktop.ServiceControl");
        }
        catch (InvalidOperationException)
        {
            // Дизайнер/тесты без Main: действия панели логировать некуда, молчим.
        }

        // Все экраны ходят в настоящую службу по Named Pipe RPC (FIX-02):
        // Standalone* фейки удалены — при остановленной службе UI честно покажет недоступность.
        var pipeClient = new ServiceControlPipeClient(options.Hosting.PipeName);
        IServiceController serviceController = new ServiceControllerManager(
            pipeClient,
            new WindowsScmManager(),
            options,
            controlLogger);
        IIntentRouter router = new PipeIntentRouter(pipeClient);
        ITokenBudgetGovernor budgetGovernor = new PipeBudgetGovernor(pipeClient);
        IChainDefinitionRepository chainRepo = new PipeChainDefinitionRepository(pipeClient);
        IChainExecutionRepository executionRepo = new PipeChainExecutionRepository(pipeClient);
        ITriggerInvoker triggerInvoker = new PipeTriggerInvoker(pipeClient);
        IHitlApprovalService hitlService = new PipeHitlApprovalService(pipeClient);
        IChainSchedulerService scheduler = new PipeChainSchedulerService(pipeClient);
        var notifications = new DesktopNotificationService();
        var themeService = new ThemeService();

        var chat = new ChatViewModel(router, budgetGovernor, notifications);
        var chains = new ChainsViewModel(chainRepo, executionRepo, triggerInvoker, notifications);
        var hitl = new HitlViewModel(hitlService, notifications);
        var service = new ServiceViewModel(serviceController, logger: controlLogger);
        var schedules = new SchedulesViewModel(scheduler, notifications);
        IModelCatalog? catalog = null;
        ILocalModelInstaller? localModelInstaller = null;
        ILocalRuntimeInstaller? localRuntimeInstaller = null;
        IModelCompatibilityAdvisor? hardwareAdvisor = null;

        try
        {
            string baseDir = AppContext.BaseDirectory;
            string localPath = Path.Combine(baseDir, "models", "local-models.json");
            string cloudPath = Path.Combine(baseDir, "models", "cloud-models.json");
            string embeddingPath = Path.Combine(baseDir, "models", "embedding-models.json");

            if (!File.Exists(localPath) || !File.Exists(cloudPath) || !File.Exists(embeddingPath))
            {
                string candidateDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
                if (Directory.Exists(candidateDir))
                {
                    localPath = Path.Combine(candidateDir, "local-models.json");
                    cloudPath = Path.Combine(candidateDir, "cloud-models.json");
                    embeddingPath = Path.Combine(candidateDir, "embedding-models.json");
                }
            }

            if (File.Exists(localPath) && File.Exists(cloudPath) && File.Exists(embeddingPath))
            {
                catalog = FileModelCatalog.Load(localPath, cloudPath, embeddingPath);
                var httpClient = new HttpClient();
                localRuntimeInstaller = new OllamaLocalRuntimeInstaller(httpClient);
                localModelInstaller = new OllamaLocalModelInstaller(httpClient, catalog);
                var hardwareProbe = new SystemHardwareProbe();
                hardwareAdvisor = new ModelCompatibilityAdvisor(catalog, hardwareProbe);
            }
        }
        catch (Exception ex)
        {
            controlLogger?.LogWarning(ex, "Failed to initialize local model catalog / installer in Desktop.");
        }

        LocalModelInstallerViewModel? localInstallerVm = null;
        if (catalog is not null && localModelInstaller is not null && localRuntimeInstaller is not null)
        {
            IReadOnlyList<ModelDescriptorDto> localModels = catalog.Find(hostingType: ModelHostingType.Local);
            localInstallerVm = new LocalModelInstallerViewModel(
                localModels,
                localModelInstaller,
                localRuntimeInstaller,
                hardwareAdvisor,
                notifications);
        }

        var models = new ModelsViewModel(
            assignments: null,
            catalog: catalog?.Find(),
            notifications: notifications,
            installer: localInstallerVm);
        var hostSkills = HostSkillsTestViewModel.CreateProbe();

        return new MainWindowViewModel(
            chat,
            chains,
            hitl,
            service,
            schedules,
            models,
            notifications,
            themeService,
            hostSkills: hostSkills);
    }
}

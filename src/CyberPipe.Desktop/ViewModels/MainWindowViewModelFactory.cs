using CyberPipe.Contracts;
using CyberPipe.Desktop.Services;
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
        ModelsViewModel models;
        try
        {
            models = DesktopModelScreen.Create(notifications, controlLogger);
        }
        catch (Exception ex)
        {
            controlLogger?.LogWarning(ex, "Failed to initialize models screen; using safe fallback.");
            models = new ModelsViewModel(notifications: notifications);
        }

        HostSkillsTestViewModel hostSkills;
        try
        {
            hostSkills = HostSkillsTestViewModel.CreateProbe();
        }
        catch (Exception ex)
        {
            controlLogger?.LogWarning(ex, "Failed to initialize host skills probe; using safe fallback.");
            hostSkills = HostSkillsTestViewModel.CreateFallback();
        }

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

    public static MainWindowViewModel CreateFallback(string? failureReason = null)
    {
        var notifications = new DesktopNotificationService();
        if (!string.IsNullOrEmpty(failureReason))
        {
            notifications.ShowNotification("Предупреждение", failureReason, NotificationSeverity.Warning);
        }

        var pipeClient = new ServiceControlPipeClient();
        var scm = new WindowsScmManager();
        var controller = new ServiceControllerManager(pipeClient, scm, new CyberPipeOptions());
        var router = new PipeIntentRouter(pipeClient);
        var budget = new PipeBudgetGovernor(pipeClient);
        var chainRepo = new PipeChainDefinitionRepository(pipeClient);
        var execRepo = new PipeChainExecutionRepository(pipeClient);
        var trigger = new PipeTriggerInvoker(pipeClient);
        var hitl = new PipeHitlApprovalService(pipeClient);
        var sched = new PipeChainSchedulerService(pipeClient);

        return new MainWindowViewModel(
            new ChatViewModel(router, budget, notifications),
            new ChainsViewModel(chainRepo, execRepo, trigger, notifications),
            new HitlViewModel(hitl, notifications),
            new ServiceViewModel(controller),
            new SchedulesViewModel(sched, notifications),
            new ModelsViewModel(notifications: notifications),
            notifications,
            new ThemeService());
    }
}

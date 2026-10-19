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

        IServiceController serviceController = new ServiceControllerManager(
            new ServiceControlPipeClient(options.Hosting.PipeName),
            new WindowsScmManager(),
            options,
            controlLogger);
        var router = new StandaloneIntentRouter();
        var budgetGovernor = new StandaloneBudgetGovernor();
        var chainRepo = new StandaloneChainDefinitionRepository();
        var executionRepo = new StandaloneChainExecutionRepository();
        var triggerInvoker = new StandaloneTriggerInvoker();
        var hitlService = new StandaloneHitlApprovalService();
        var scheduler = new StandaloneChainSchedulerService();
        var notifications = new DesktopNotificationService();
        var themeService = new ThemeService();

        var chat = new ChatViewModel(router, budgetGovernor, notifications);
        var chains = new ChainsViewModel(chainRepo, executionRepo, triggerInvoker, notifications);
        var hitl = new HitlViewModel(hitlService, notifications);
        var service = new ServiceViewModel(serviceController);
        var schedules = new SchedulesViewModel(scheduler, notifications);
        var models = new ModelsViewModel(null, null, notifications);

        return new MainWindowViewModel(
            chat,
            chains,
            hitl,
            service,
            schedules,
            models,
            notifications,
            themeService);
    }
}

internal sealed class StandaloneIntentRouter : IIntentRouter
{
    public Task<RouteResult> RouteAsync(RouteRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RouteResult
        {
            Status = RouteStatus.Matched,
            Tier = IntentTier.Tier0,
            Intent = $"Echo: {request.Text}",
            Confidence = 1.0,
            MatchedRuleName = "standalone-echo",
        });
    }
}

internal sealed class StandaloneBudgetGovernor : ITokenBudgetGovernor
{
    public Task<BudgetStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new BudgetStatusDto
        {
            Limits = new BudgetLimitsDto { DailyCapUsd = 5.0m, SoftTokenLimit = 4000, HardTokenLimit = 12000, MaxCloudTurns = 3, MaxLocalTurns = 5 },
            SpentTodayUsd = 0.12m,
            RemainingTodayUsd = 4.88m,
            TokensUsedToday = 1450,
            BudgetDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
    }

    public Task<BudgetDecision> TryAuthorizeAsync(BudgetRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new BudgetDecision { IsAllowed = true });
    }

    public Task RecordUsageAsync(TokenUsageDto usage, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

internal sealed class StandaloneChainDefinitionRepository : IChainDefinitionRepository
{
    private readonly List<ChainDefinitionDto> _definitions =
    [
        new ChainDefinitionDto
        {
            Id = new ChainId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            Name = "System Health Check",
            IsEnabled = true,
            Trigger = new ChainTriggerDto
            {
                Kind = ChainTriggerKind.Cron,
                CronExpression = "0 */15 * * * ?",
            },
            Skill = new ChainSkillStepDto { SkillId = "system.status" },
            Script = new ChainScriptStepDto { PackageSlug = "health-report", PackageVersion = "1.0.0" },
            Delivery = new ChainDeliveryStepDto { Channel = ChannelKind.Desktop, Target = "desktop-ui", MessageTemplate = "Result: {result}" },
            RequiresHitl = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        }
    ];

    public Task<ChainDefinitionDto?> GetByIdAsync(ChainId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_definitions.FirstOrDefault(d => d.Id == id));

    public Task<IReadOnlyList<ChainDefinitionDto>> ListEnabledAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ChainDefinitionDto>>(_definitions.Where(d => d.IsEnabled).ToList());

    public Task<ChainDefinitionDto> UpsertAsync(ChainDefinitionDto definition, CancellationToken cancellationToken = default) =>
        Task.FromResult(definition);

    public Task<bool> SetEnabledAsync(ChainId id, bool isEnabled, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

internal sealed class StandaloneChainExecutionRepository : IChainExecutionRepository
{
    public Task<ChainRunState?> GetRunAsync(RunId runId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ChainRunState?>(null);

    public Task<RunId?> FindRunByIdempotencyKeyAsync(ChainId chainId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<RunId?>(null);

    public Task<ChainRunState> CreateRunAsync(RunId runId, ChainId chainId, TriggerEvent trigger, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ChainRunState
        {
            RunId = runId,
            ChainId = chainId,
            Status = ChainRunStatus.Running,
            Trigger = trigger,
            StartedAtUtc = DateTimeOffset.UtcNow,
        });

    public Task UpdateRunStatusAsync(RunId runId, ChainRunStatus status, ErrorDto? error = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SaveCheckpointAsync(ChainCheckpointDto checkpoint, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<ChainCheckpointDto>> GetCheckpointsAsync(RunId runId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ChainCheckpointDto>>([]);

    public Task<IReadOnlyList<RunId>> ListUnfinishedRunsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RunId>>([]);
}

internal sealed class StandaloneTriggerInvoker : ITriggerInvoker
{
    public Task<RunId> InvokeFunctionCallAsync(ChainId chainId, string payloadJson, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RunId(Guid.NewGuid()));
}

internal sealed class StandaloneHitlApprovalService : IHitlApprovalService
{
    public Task<HitlApprovalRecord> CreateAsync(HitlApprovalRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new HitlApprovalRecord
        {
            ApprovalId = request.ApprovalId,
            RunId = request.RunId,
            ChainId = request.ChainId,
            Title = request.Title,
            PreviewText = request.PreviewText,
            PayloadJson = request.PayloadJson,
            CreatedAtUtc = request.CreatedAtUtc,
            ExpiresAtUtc = request.ExpiresAtUtc,
        });
    }

    public Task<HitlApprovalResult> ApproveAsync(Guid approvalId, string ownerId, ChannelKind channel, CancellationToken cancellationToken = default) =>
        Task.FromResult(new HitlApprovalResult
        {
            ApprovalId = approvalId,
            Decision = HitlDecision.Approved,
            OwnerId = ownerId,
            Channel = channel,
            DecidedAtUtc = DateTimeOffset.UtcNow,
        });

    public Task<HitlApprovalResult> EditAsync(Guid approvalId, string ownerId, string editedPayloadJson, ChannelKind channel, CancellationToken cancellationToken = default) =>
        Task.FromResult(new HitlApprovalResult
        {
            ApprovalId = approvalId,
            Decision = HitlDecision.Edited,
            EditedPayloadJson = editedPayloadJson,
            OwnerId = ownerId,
            Channel = channel,
            DecidedAtUtc = DateTimeOffset.UtcNow,
        });

    public Task<HitlApprovalResult> RejectAsync(Guid approvalId, string ownerId, ChannelKind channel, string? reason = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new HitlApprovalResult
        {
            ApprovalId = approvalId,
            Decision = HitlDecision.Rejected,
            Reason = reason,
            OwnerId = ownerId,
            Channel = channel,
            DecidedAtUtc = DateTimeOffset.UtcNow,
        });

    public Task<int> ExpireOverdueAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}

internal sealed class StandaloneChainSchedulerService : IChainSchedulerService
{
    private readonly List<ScheduledChainInfo> _schedules =
    [
        new ScheduledChainInfo
        {
            ChainId = new ChainId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            CronExpression = "0 */15 * * * ?",
            NextFireTimeUtc = DateTimeOffset.UtcNow.AddMinutes(15),
            IsPaused = false,
        }
    ];

    public Task ScheduleChainAsync(ChainId chainId, string cronExpression, ChainMisfirePolicy misfirePolicy = ChainMisfirePolicy.FireOnceNow, CancellationToken cancellationToken = default)
    {
        if (cronExpression.Contains("invalid", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidCronExpressionException(cronExpression, "Specified cron expression syntax is invalid.");
        }

        ScheduledChainInfo? existing = _schedules.FirstOrDefault(s => s.ChainId == chainId);
        if (existing != null)
        {
            _schedules.Remove(existing);
        }

        _schedules.Add(new ScheduledChainInfo
        {
            ChainId = chainId,
            CronExpression = cronExpression,
            NextFireTimeUtc = DateTimeOffset.UtcNow.AddHours(1),
            IsPaused = false,
            MisfirePolicy = misfirePolicy,
        });

        return Task.CompletedTask;
    }

    public Task PauseChainAsync(ChainId chainId, CancellationToken cancellationToken = default)
    {
        ScheduledChainInfo? existing = _schedules.FirstOrDefault(s => s.ChainId == chainId);
        if (existing != null)
        {
            _schedules.Remove(existing);
            _schedules.Add(existing with { IsPaused = true });
        }
        return Task.CompletedTask;
    }

    public Task ResumeChainAsync(ChainId chainId, CancellationToken cancellationToken = default)
    {
        ScheduledChainInfo? existing = _schedules.FirstOrDefault(s => s.ChainId == chainId);
        if (existing != null)
        {
            _schedules.Remove(existing);
            _schedules.Add(existing with { IsPaused = false });
        }
        return Task.CompletedTask;
    }

    public Task UnscheduleChainAsync(ChainId chainId, CancellationToken cancellationToken = default)
    {
        _schedules.RemoveAll(s => s.ChainId == chainId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScheduledChainInfo>> ListScheduledAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ScheduledChainInfo>>(_schedules.ToList());

    public Task TriggerChainNowAsync(ChainId chainId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SyncWithRepositoryAsync(IChainDefinitionRepository repository, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

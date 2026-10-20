using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.ServiceControl;

/// <summary>
/// Клиентские адаптеры контрактов службы поверх Named Pipe RPC (TASK-062/061):
/// CLI и Desktop получают IIntentRouter/ITokenBudgetGovernor/репозитории/HITL/планировщик
/// без ссылки на CyberPipe.Service. Запись вне сценариев клиентов (Upsert, Create и т.п.)
/// намеренно не поддерживается — такие вызовы бросают NotSupportedException, а не фейковый успех.
/// </summary>
public sealed class PipeIntentRouter : IIntentRouter
{
    private readonly ServiceControlPipeClient _client;
    public PipeIntentRouter(ServiceControlPipeClient client) => _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<RouteResult> RouteAsync(RouteRequest request, CancellationToken cancellationToken = default)
    {
        string json = await _client.CallRpcAsync(ServicePipeCommands.Route, JsonSerializer.Serialize(request), cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<RouteResult>(json) ?? throw new InvalidOperationException("Empty route response from service.");
    }
}

/// <inheritdoc />
public sealed class PipeBudgetGovernor : ITokenBudgetGovernor
{
    private readonly ServiceControlPipeClient _client;
    public PipeBudgetGovernor(ServiceControlPipeClient client) => _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<BudgetDecision> TryAuthorizeAsync(BudgetRequest request, CancellationToken cancellationToken = default)
    {
        string json = await _client.CallRpcAsync(ServicePipeCommands.BudgetAuthorize, JsonSerializer.Serialize(request), cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<BudgetDecision>(json) ?? throw new InvalidOperationException("Empty budget response from service.");
    }

    public async Task RecordUsageAsync(TokenUsageDto usage, CancellationToken cancellationToken = default)
    {
        await _client.CallRpcAsync(ServicePipeCommands.BudgetRecord, JsonSerializer.Serialize(usage), cancellationToken).ConfigureAwait(false);
    }

    public async Task<BudgetStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        string json = await _client.CallRpcAsync(ServicePipeCommands.BudgetStatus, "{}", cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<BudgetStatusDto>(json) ?? throw new InvalidOperationException("Empty budget status from service.");
    }
}

/// <inheritdoc />
public sealed class PipeChainDefinitionRepository : IChainDefinitionRepository
{
    private readonly ServiceControlPipeClient _client;
    public PipeChainDefinitionRepository(ServiceControlPipeClient client) => _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<ChainDefinitionDto?> GetByIdAsync(ChainId id, CancellationToken cancellationToken = default)
    {
        string json = await _client.CallRpcAsync(ServicePipeCommands.ChainGet, JsonSerializer.Serialize(new GuidPipeRequest(id.Value)), cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ChainDefinitionDto>(json);
    }

    public async Task<IReadOnlyList<ChainDefinitionDto>> ListEnabledAsync(CancellationToken cancellationToken = default)
    {
        string json = await _client.CallRpcAsync(ServicePipeCommands.ChainList, "{}", cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<List<ChainDefinitionDto>>(json) ?? [];
    }

    public Task<ChainDefinitionDto> UpsertAsync(ChainDefinitionDto definition, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Chain definitions are read-only over the pipe client; edit them on the service host.");

    public Task<bool> SetEnabledAsync(ChainId id, bool isEnabled, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Chain definitions are read-only over the pipe client; edit them on the service host.");
}

/// <inheritdoc />
public sealed class PipeChainExecutionRepository : IChainExecutionRepository
{
    private readonly ServiceControlPipeClient _client;
    public PipeChainExecutionRepository(ServiceControlPipeClient client) => _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<ChainRunState?> GetRunAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        string json = await _client.CallRpcAsync(ServicePipeCommands.ChainRunsGet, JsonSerializer.Serialize(new GuidPipeRequest(runId.Value)), cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ChainRunState>(json);
    }

    public async Task<IReadOnlyList<RunId>> ListUnfinishedRunsAsync(CancellationToken cancellationToken = default)
    {
        string json = await _client.CallRpcAsync(ServicePipeCommands.ChainRunsUnfinished, "{}", cancellationToken).ConfigureAwait(false);
        List<Guid> ids = JsonSerializer.Deserialize<List<Guid>>(json) ?? [];
        return ids.Select(g => new RunId(g)).ToList();
    }

    public Task<RunId?> FindRunByIdempotencyKeyAsync(ChainId chainId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Execution repository writes/lookups beyond run inspection are not exposed over the pipe client.");

    public Task<ChainRunState> CreateRunAsync(RunId runId, ChainId chainId, TriggerEvent trigger, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Execution repository writes are not exposed over the pipe client.");

    public Task UpdateRunStatusAsync(RunId runId, ChainRunStatus status, ErrorDto? error = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Execution repository writes are not exposed over the pipe client.");

    public Task SaveCheckpointAsync(ChainCheckpointDto checkpoint, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Execution repository writes are not exposed over the pipe client.");

    public Task<IReadOnlyList<ChainCheckpointDto>> GetCheckpointsAsync(RunId runId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Use GetRunAsync over the pipe client; checkpoints are included in ChainRunState.");
}

/// <inheritdoc />
public sealed class PipeTriggerInvoker : ITriggerInvoker
{
    private readonly ServiceControlPipeClient _client;
    public PipeTriggerInvoker(ServiceControlPipeClient client) => _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<RunId> InvokeFunctionCallAsync(ChainId chainId, string payloadJson, CancellationToken cancellationToken = default)
    {
        string json = await _client.CallRpcAsync(ServicePipeCommands.ChainRun,
            JsonSerializer.Serialize(new ChainRunPipeRequest(chainId.Value, payloadJson)), cancellationToken).ConfigureAwait(false);
        Guid id = JsonSerializer.Deserialize<Guid>(json);
        return new RunId(id);
    }
}

/// <inheritdoc />
public sealed class PipeHitlApprovalService : IHitlApprovalService
{
    private readonly ServiceControlPipeClient _client;
    public PipeHitlApprovalService(ServiceControlPipeClient client) => _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<HitlApprovalResult> ApproveAsync(Guid approvalId, string ownerId, ChannelKind channel, CancellationToken cancellationToken = default)
    {
        string json = await _client.CallRpcAsync(ServicePipeCommands.HitlApprove,
            JsonSerializer.Serialize(new HitlDecisionPipeRequest(approvalId, ownerId, channel)), cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<HitlApprovalResult>(json) ?? throw new InvalidOperationException("Empty HITL response from service.");
    }

    public async Task<HitlApprovalResult> RejectAsync(Guid approvalId, string ownerId, ChannelKind channel, string? reason = null, CancellationToken cancellationToken = default)
    {
        string json = await _client.CallRpcAsync(ServicePipeCommands.HitlReject,
            JsonSerializer.Serialize(new HitlDecisionPipeRequest(approvalId, ownerId, channel, reason)), cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<HitlApprovalResult>(json) ?? throw new InvalidOperationException("Empty HITL response from service.");
    }

    public Task<HitlApprovalRecord> CreateAsync(HitlApprovalRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("HITL requests are created by the service, not by pipe clients.");

    public Task<HitlApprovalResult> EditAsync(Guid approvalId, string ownerId, string editedPayloadJson, ChannelKind channel, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("HITL edit is not exposed over the pipe client.");

    public Task<int> ExpireOverdueAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("HITL expiry runs on the service, not by pipe clients.");
}

/// <inheritdoc />
public sealed class PipeChainSchedulerService : IChainSchedulerService
{
    private readonly ServiceControlPipeClient _client;
    public PipeChainSchedulerService(ServiceControlPipeClient client) => _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task ScheduleChainAsync(ChainId chainId, string cronExpression, ChainMisfirePolicy misfirePolicy = ChainMisfirePolicy.FireOnceNow, CancellationToken cancellationToken = default)
    {
        await _client.CallRpcAsync(ServicePipeCommands.ChainSchedule,
            JsonSerializer.Serialize(new ChainSchedulePipeRequest(chainId.Value, cronExpression)), cancellationToken).ConfigureAwait(false);
    }

    public async Task PauseChainAsync(ChainId chainId, CancellationToken cancellationToken = default) =>
        await _client.CallRpcAsync(ServicePipeCommands.ChainPause, JsonSerializer.Serialize(new GuidPipeRequest(chainId.Value)), cancellationToken).ConfigureAwait(false);

    public async Task ResumeChainAsync(ChainId chainId, CancellationToken cancellationToken = default) =>
        await _client.CallRpcAsync(ServicePipeCommands.ChainResume, JsonSerializer.Serialize(new GuidPipeRequest(chainId.Value)), cancellationToken).ConfigureAwait(false);

    public async Task UnscheduleChainAsync(ChainId chainId, CancellationToken cancellationToken = default) =>
        await _client.CallRpcAsync(ServicePipeCommands.ChainUnschedule, JsonSerializer.Serialize(new GuidPipeRequest(chainId.Value)), cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<ScheduledChainInfo>> ListScheduledAsync(CancellationToken cancellationToken = default)
    {
        string json = await _client.CallRpcAsync(ServicePipeCommands.ChainSchedules, "{}", cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<List<ScheduledChainInfo>>(json) ?? [];
    }

    public async Task TriggerChainNowAsync(ChainId chainId, CancellationToken cancellationToken = default) =>
        await _client.CallRpcAsync(ServicePipeCommands.ChainTriggerNow, JsonSerializer.Serialize(new GuidPipeRequest(chainId.Value)), cancellationToken).ConfigureAwait(false);

    public Task SyncWithRepositoryAsync(IChainDefinitionRepository repository, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Scheduler sync runs on the service at startup, not by pipe clients.");
}

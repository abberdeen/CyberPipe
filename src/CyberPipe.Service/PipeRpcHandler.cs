using System.Text.Json;
using CyberPipe.Contracts;
using CyberPipe.Data;
using Microsoft.Extensions.DependencyInjection;

namespace CyberPipe.Service;

/// <summary>
/// Серверная сторона pipe-RPC (TASK-062/061): команды route/budget/chain/hitl
/// диспатчатся в настоящие контракты композиции службы.
/// </summary>
public static class PipeRpcHandler
{
    public static Func<string, string, Task<string>> Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return async (command, payload) =>
        {
            switch (command)
            {
                case ServicePipeCommands.Route:
                {
                    RouteRequest request = JsonSerializer.Deserialize<RouteRequest>(payload)
                        ?? throw new InvalidOperationException("Empty route request.");
                    RouteResult result = await services.GetRequiredService<IIntentRouter>().RouteAsync(request).ConfigureAwait(false);
                    if (result.Status == RouteStatus.Matched && string.IsNullOrWhiteSpace(result.ResponseText))
                    {
                        result = await CyberPipe.Router.SkillIntentDispatcher.DispatchAsync(services.GetService<ISkillInvoker>(), result).ConfigureAwait(false);
                    }
                    return JsonSerializer.Serialize(result);
                }
                case ServicePipeCommands.BudgetStatus:
                {
                    BudgetStatusDto status = await services.GetRequiredService<ITokenBudgetGovernor>().GetStatusAsync().ConfigureAwait(false);
                    return JsonSerializer.Serialize(status);
                }
                case ServicePipeCommands.BudgetAuthorize:
                {
                    BudgetRequest request = JsonSerializer.Deserialize<BudgetRequest>(payload)
                        ?? throw new InvalidOperationException("Empty budget request.");
                    BudgetDecision decision = await services.GetRequiredService<ITokenBudgetGovernor>().TryAuthorizeAsync(request).ConfigureAwait(false);
                    return JsonSerializer.Serialize(decision);
                }
                case ServicePipeCommands.BudgetRecord:
                {
                    TokenUsageDto usage = JsonSerializer.Deserialize<TokenUsageDto>(payload)
                        ?? throw new InvalidOperationException("Empty usage payload.");
                    await services.GetRequiredService<ITokenBudgetGovernor>().RecordUsageAsync(usage).ConfigureAwait(false);
                    return "{}";
                }
                case ServicePipeCommands.ChainList:
                {
                    IReadOnlyList<ChainDefinitionDto> list = await services.GetRequiredService<IChainCatalogService>().ListEnabledAsync().ConfigureAwait(false);
                    return JsonSerializer.Serialize(list);
                }
                case ServicePipeCommands.ChainGet:
                {
                    GuidPipeRequest req = Deserialize<GuidPipeRequest>(payload);
                    ChainDefinitionDto? def = await services.GetRequiredService<IChainCatalogService>().GetByIdAsync(new ChainId(req.Id)).ConfigureAwait(false);
                    return JsonSerializer.Serialize(def);
                }
                case ServicePipeCommands.ChainRunsGet:
                {
                    GuidPipeRequest req = Deserialize<GuidPipeRequest>(payload);
                    ChainRunState? state = await services.GetRequiredService<IChainExecutionStoreService>().GetRunAsync(new RunId(req.Id)).ConfigureAwait(false);
                    return JsonSerializer.Serialize(state);
                }
                case ServicePipeCommands.ChainRunsUnfinished:
                {
                    IReadOnlyList<RunId> runs = await services.GetRequiredService<IChainExecutionStoreService>().ListUnfinishedRunsAsync().ConfigureAwait(false);
                    return JsonSerializer.Serialize(runs.Select(r => r.Value).ToList());
                }
                case ServicePipeCommands.ChainRun:
                {
                    ChainRunPipeRequest req = Deserialize<ChainRunPipeRequest>(payload);
                    RunId runId = await services.GetRequiredService<ITriggerInvoker>().InvokeFunctionCallAsync(new ChainId(req.ChainId), req.PayloadJson).ConfigureAwait(false);
                    return JsonSerializer.Serialize(runId.Value);
                }
                case ServicePipeCommands.ChainSchedule:
                {
                    ChainSchedulePipeRequest req = Deserialize<ChainSchedulePipeRequest>(payload);
                    await services.GetRequiredService<IChainSchedulerService>().ScheduleChainAsync(new ChainId(req.ChainId), req.CronExpression).ConfigureAwait(false);
                    return "{}";
                }
                case ServicePipeCommands.ChainSchedules:
                {
                    IReadOnlyList<ScheduledChainInfo> list = await services.GetRequiredService<IChainSchedulerService>().ListScheduledAsync().ConfigureAwait(false);
                    return JsonSerializer.Serialize(list);
                }
                case ServicePipeCommands.ChainUnschedule:
                {
                    GuidPipeRequest req = Deserialize<GuidPipeRequest>(payload);
                    await services.GetRequiredService<IChainSchedulerService>().UnscheduleChainAsync(new ChainId(req.Id)).ConfigureAwait(false);
                    return "{}";
                }
                case ServicePipeCommands.ChainPause:
                {
                    GuidPipeRequest req = Deserialize<GuidPipeRequest>(payload);
                    await services.GetRequiredService<IChainSchedulerService>().PauseChainAsync(new ChainId(req.Id)).ConfigureAwait(false);
                    return "{}";
                }
                case ServicePipeCommands.ChainResume:
                {
                    GuidPipeRequest req = Deserialize<GuidPipeRequest>(payload);
                    await services.GetRequiredService<IChainSchedulerService>().ResumeChainAsync(new ChainId(req.Id)).ConfigureAwait(false);
                    return "{}";
                }
                case ServicePipeCommands.ChainTriggerNow:
                {
                    GuidPipeRequest req = Deserialize<GuidPipeRequest>(payload);
                    await services.GetRequiredService<IChainSchedulerService>().TriggerChainNowAsync(new ChainId(req.Id)).ConfigureAwait(false);
                    return "{}";
                }
                case ServicePipeCommands.HitlApprove:
                {
                    HitlDecisionPipeRequest req = Deserialize<HitlDecisionPipeRequest>(payload);
                    HitlApprovalResult result = await services.GetRequiredService<IHitlApprovalService>()
                        .ApproveAsync(req.ApprovalId, req.OwnerId, req.Channel).ConfigureAwait(false);
                    return JsonSerializer.Serialize(result);
                }
                case ServicePipeCommands.HitlReject:
                {
                    HitlDecisionPipeRequest req = Deserialize<HitlDecisionPipeRequest>(payload);
                    HitlApprovalResult result = await services.GetRequiredService<IHitlApprovalService>()
                        .RejectAsync(req.ApprovalId, req.OwnerId, req.Channel, req.Reason).ConfigureAwait(false);
                    return JsonSerializer.Serialize(result);
                }
                default:
                    throw new InvalidOperationException($"Unknown pipe command '{command}'.");
            }
        };
    }

    private static T Deserialize<T>(string payload) =>
        JsonSerializer.Deserialize<T>(payload) ?? throw new InvalidOperationException($"Empty payload for {typeof(T).Name}.");
}

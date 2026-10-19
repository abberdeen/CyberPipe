using CyberPipe.Contracts;
using Quartz;

namespace CyberPipe.Chains.Scheduling;

/// <summary>
/// Quartz-задание для отложенного возобновления запуска цепочки (TASK-044, TASK-045).
/// Вызывается по истечении задержки DeferRetry и передаёт управление IChainExecutor.ResumeAsync.
/// </summary>
[DisallowConcurrentExecution]
public sealed class ChainResumeJob : IJob
{
    private readonly IChainExecutor? _executor;

    /// <summary>Параметризованный конструктор для JobFactory.</summary>
    public ChainResumeJob(IChainExecutor? executor = null)
    {
        _executor = executor;
    }

    /// <inheritdoc />
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        JobDataMap data = context.MergedJobDataMap;
        string? runIdString = data.GetString("RunId");
        if (string.IsNullOrWhiteSpace(runIdString))
        {
            throw new InvalidOperationException("ChainResumeJob: RunId is missing from JobDataMap.");
        }

        var runId = new RunId(Guid.Parse(runIdString));

        IChainExecutor? executor = _executor ?? context.Scheduler.Context.Get("ChainExecutor") as IChainExecutor;
        if (executor is null)
        {
            throw new InvalidOperationException("ChainResumeJob: No IChainExecutor configured in Job or SchedulerContext.");
        }

        await executor.ResumeAsync(runId, cancellationToken: context.CancellationToken).ConfigureAwait(false);
    }
}

using CyberPipe.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace CyberPipe.Chains.Hitl;

/// <summary>
/// Отложенный <see cref="IChainExecutor" /> для разрыва цикла композиции (TASK-090):
/// <c>HitlApprovalService → IChainExecutor → IHitlApprovalGate → IHitlApprovalService</c>.
/// Циклы через фабричные лямбды MS.DI не детектирует, поэтому реальный исполнитель
/// резолвится только при первом вызове, когда граф уже полностью построен.
/// Первый вызов вне построения графа потокобезопасен (<see cref="Lazy{T}" />).
/// </summary>
internal sealed class DeferredChainExecutor(IServiceProvider services) : IChainExecutor
{
    private readonly Lazy<IChainExecutor> _real = new(services.GetRequiredService<IChainExecutor>);

    /// <inheritdoc />
    public Task<RunId> StartAsync(TriggerEvent triggerEvent, CancellationToken cancellationToken = default) =>
        _real.Value.StartAsync(triggerEvent, cancellationToken);

    /// <inheritdoc />
    public Task ResumeAsync(RunId runId, CancellationToken cancellationToken = default) =>
        _real.Value.ResumeAsync(runId, cancellationToken);

    /// <inheritdoc />
    public Task CancelAsync(RunId runId, CancellationToken cancellationToken = default) =>
        _real.Value.CancelAsync(runId, cancellationToken);

    /// <inheritdoc />
    public Task<ChainRunState?> GetRunAsync(RunId runId, CancellationToken cancellationToken = default) =>
        _real.Value.GetRunAsync(runId, cancellationToken);
}

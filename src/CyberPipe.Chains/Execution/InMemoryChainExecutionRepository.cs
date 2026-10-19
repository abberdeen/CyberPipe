using System.Collections.Concurrent;
using CyberPipe.Contracts;

namespace CyberPipe.Chains.Execution;

/// <summary>
/// Потокобезопасная in-memory реализация репозитория запусков и чекпоинтов цепочек (TASK-044).
/// Обеспечивает строгую идемпотентность по (ChainId, IdempotencyKey) и хранение истории чекпоинтов.
/// </summary>
public sealed class InMemoryChainExecutionRepository : IChainExecutionRepository
{
    private readonly ConcurrentDictionary<RunId, ChainRunState> _runs = new();
    private readonly ConcurrentDictionary<string, RunId> _idempotencyKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<RunId, ConcurrentDictionary<ChainStepKind, ChainCheckpointDto>> _checkpoints = new();

    /// <inheritdoc />
    public Task<ChainRunState?> GetRunAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        _runs.TryGetValue(runId, out ChainRunState? state);
        if (state is not null && _checkpoints.TryGetValue(runId, out var steps))
        {
            state = state with { Checkpoints = steps.Values.OrderBy(c => c.Step).ToArray() };
        }
        return Task.FromResult(state);
    }

    /// <inheritdoc />
    public Task<RunId?> FindRunByIdempotencyKeyAsync(ChainId chainId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        string compositeKey = MakeIdempotencyKey(chainId, idempotencyKey);
        _idempotencyKeys.TryGetValue(compositeKey, out RunId runId);
        return Task.FromResult(runId == default ? (RunId?)null : runId);
    }

    /// <inheritdoc />
    public Task<ChainRunState> CreateRunAsync(RunId runId, ChainId chainId, TriggerEvent trigger, CancellationToken cancellationToken = default)
    {
        string compositeKey = MakeIdempotencyKey(chainId, trigger.IdempotencyKey);
        RunId actualRunId = _idempotencyKeys.GetOrAdd(compositeKey, runId);

        var state = new ChainRunState
        {
            RunId = actualRunId,
            ChainId = chainId,
            Status = ChainRunStatus.Running,
            Trigger = trigger,
            StartedAtUtc = DateTimeOffset.UtcNow,
        };

        _runs[actualRunId] = state;
        _checkpoints.GetOrAdd(actualRunId, _ => new ConcurrentDictionary<ChainStepKind, ChainCheckpointDto>());

        return Task.FromResult(state);
    }

    /// <inheritdoc />
    public Task UpdateRunStatusAsync(RunId runId, ChainRunStatus status, ErrorDto? error = null, CancellationToken cancellationToken = default)
    {
        _runs.AddOrUpdate(runId,
            _ => throw new KeyNotFoundException($"Run '{runId}' not found."),
            (_, existing) => existing with
            {
                Status = status,
                Error = error ?? existing.Error,
                FinishedAtUtc = status is ChainRunStatus.Completed or ChainRunStatus.Failed or ChainRunStatus.Cancelled or ChainRunStatus.CancelledByCrash
                    ? DateTimeOffset.UtcNow
                    : existing.FinishedAtUtc,
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SaveCheckpointAsync(ChainCheckpointDto checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var steps = _checkpoints.GetOrAdd(checkpoint.RunId, _ => new ConcurrentDictionary<ChainStepKind, ChainCheckpointDto>());
        steps[checkpoint.Step] = checkpoint;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ChainCheckpointDto>> GetCheckpointsAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        if (_checkpoints.TryGetValue(runId, out var steps))
        {
            IReadOnlyList<ChainCheckpointDto> list = steps.Values.OrderBy(c => c.Step).ToArray();
            return Task.FromResult(list);
        }

        return Task.FromResult<IReadOnlyList<ChainCheckpointDto>>([]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RunId>> ListUnfinishedRunsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RunId> list = _runs.Values
            .Where(r => r.Status is ChainRunStatus.Running or ChainRunStatus.WaitingForHitl or ChainRunStatus.WaitingForRetry)
            .Select(r => r.RunId)
            .ToArray();

        return Task.FromResult(list);
    }

    private static string MakeIdempotencyKey(ChainId chainId, string idempotencyKey) =>
        $"{chainId.Value:D}::{idempotencyKey}";
}

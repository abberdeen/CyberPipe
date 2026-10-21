using CyberPipe.Contracts;

namespace CyberPipe.Data;

/// <summary>
/// SQLite реализация <see cref="IChainExecutionRepository"/> (TASK-044) поверх
/// существующих IChainRunRepository/ICheckpointRepository: запуски и чекпоинты переживают
/// рестарт службы. InMemory вариант Chains остаётся fallback-ом standalone-композиций.
/// </summary>
public sealed class SqliteChainExecutionRepository : IChainExecutionRepository
{
    private readonly IChainRunRepository _runs;
    private readonly ICheckpointRepository _checkpoints;

    public SqliteChainExecutionRepository(IChainRunRepository runs, ICheckpointRepository checkpoints)
    {
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
    }

    public async Task<ChainRunState?> GetRunAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        ChainRunRecord? record = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);
        if (record is null)
            return null;
        IReadOnlyList<ChainCheckpointDto> checkpoints = await GetCheckpointsAsync(runId, cancellationToken).ConfigureAwait(false);
        return ToState(record, checkpoints);
    }

    public async Task<RunId?> FindRunByIdempotencyKeyAsync(ChainId chainId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await foreach (ChainRunRecord record in EnumerateRunsAsync(
            new PagedQuery { SortBy = "created_at", Descending = false }, chainId, cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(record.IdempotencyKey, idempotencyKey, StringComparison.Ordinal))
                return record.Id;
        }

        return null;
    }

    public async Task<ChainRunState> CreateRunAsync(RunId runId, ChainId chainId, TriggerEvent trigger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        RunId? existing = await FindRunByIdempotencyKeyAsync(chainId, trigger.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (existing is { } existingId)
        {
            ChainRunState? existingState = await GetRunAsync(existingId, cancellationToken).ConfigureAwait(false);
            if (existingState is not null)
                return existingState;
        }

        var record = new ChainRunRecord
        {
            Id = runId,
            ChainId = chainId,
            TriggerEventId = trigger.EventId,
            IdempotencyKey = trigger.IdempotencyKey,
            Status = ChainRunStatus.Running,
            Source = trigger.Kind.ToString(),
            PayloadJson = trigger.PayloadJson,
            StartedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        ChainRunRecord saved = await _runs.AddAsync(record, cancellationToken).ConfigureAwait(false);
        return ToState(saved, []);
    }

    public async Task UpdateRunStatusAsync(RunId runId, ChainRunStatus status, ErrorDto? error = null, CancellationToken cancellationToken = default)
    {
        ChainRunRecord? record = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Run '{runId}' not found.");
        var updated = record with
        {
            Status = status,
            Error = error ?? record.Error,
            FinishedAtUtc = status is ChainRunStatus.Completed or ChainRunStatus.Failed or ChainRunStatus.Cancelled or ChainRunStatus.CancelledByCrash
                ? DateTimeOffset.UtcNow
                : record.FinishedAtUtc,
        };
        await _runs.UpdateAsync(updated, record.RowVersion, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveCheckpointAsync(ChainCheckpointDto checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        CheckpointRecord? existing = await _checkpoints.GetByRunAndStepAsync(checkpoint.RunId, checkpoint.Step, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            await _checkpoints.AddAsync(checkpoint, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _checkpoints.UpdateAsync(existing with { Checkpoint = checkpoint }, existing.RowVersion, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<ChainCheckpointDto>> GetCheckpointsAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        PagedResult<CheckpointRecord> page = await _checkpoints.ListAsync(
            new PagedQuery { Offset = 0, Limit = 100, SortBy = "checkpoint_at", Descending = false }, runId, cancellationToken).ConfigureAwait(false);
        return page.Items.Select(r => r.Checkpoint).OrderBy(c => c.Step).ToList();
    }

    public async Task<IReadOnlyList<RunId>> ListUnfinishedRunsAsync(CancellationToken cancellationToken = default)
    {
        var ids = new List<RunId>();
        await foreach (ChainRunRecord record in EnumerateRunsAsync(
            new PagedQuery { SortBy = "started_at", Descending = true }, chainId: null, cancellationToken).ConfigureAwait(false))
        {
            if (record.Status is ChainRunStatus.Running or ChainRunStatus.WaitingForHitl or ChainRunStatus.WaitingForRetry)
                ids.Add(record.Id);
        }

        return ids;
    }

    /// <summary>
    /// Читает запуски страницами не больше <see cref="RepositoryOptions.MaxPageLimit"/>.
    /// Прямой Limit 500 отклоняется репозиторием и роняет обязательный шаг ChainRecovery.
    /// </summary>
    private async IAsyncEnumerable<ChainRunRecord> EnumerateRunsAsync(
        PagedQuery query,
        ChainId? chainId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const int pageSize = 200;
        int offset = 0;
        while (true)
        {
            PagedResult<ChainRunRecord> page = await _runs.ListAsync(
                query with { Offset = offset, Limit = pageSize }, chainId, cancellationToken).ConfigureAwait(false);
            foreach (ChainRunRecord record in page.Items)
                yield return record;

            offset += page.Items.Count;
            if (page.Items.Count == 0 || offset >= page.TotalCount)
                yield break;
        }
    }

    private static ChainRunState ToState(ChainRunRecord record, IReadOnlyList<ChainCheckpointDto> checkpoints) => new()
    {
        RunId = record.Id,
        ChainId = record.ChainId,
        Status = record.Status,
        Trigger = new TriggerEvent
        {
            EventId = record.TriggerEventId ?? Guid.Empty,
            ChainId = record.ChainId,
            Kind = Enum.TryParse<ChainTriggerKind>(record.Source, ignoreCase: true, out ChainTriggerKind kind) ? kind : ChainTriggerKind.FunctionCall,
            OccurredAtUtc = record.StartedAtUtc,
            PayloadJson = record.PayloadJson,
            Source = record.Source,
            IdempotencyKey = record.IdempotencyKey,
        },
        StartedAtUtc = record.StartedAtUtc,
        FinishedAtUtc = record.FinishedAtUtc,
        Error = record.Error,
        Checkpoints = checkpoints,
    };
}

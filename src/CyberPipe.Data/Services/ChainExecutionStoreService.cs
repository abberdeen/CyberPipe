using CyberPipe.Contracts;

namespace CyberPipe.Data;

/// <summary>
/// Хранилище выполнения цепочек: запуски и чекпоинты переживают рестарт службы.
/// Реализовано поверх сервисов записей запусков и чекпоинтов (та же SQL-логика
/// их методов, по одному контексту на операцию); сырых SQL-строк нет.
/// </summary>
public sealed class ChainExecutionStoreService
{
    private readonly ChainRunRecordService _runs;
    private readonly CheckpointStoreService _checkpoints;

    /// <summary>Создаёт хранилище выполнения поверх сервисов записей.</summary>
    /// <param name="runs">Сервис записей запусков.</param>
    /// <param name="checkpoints">Сервис контрольных точек.</param>
    public ChainExecutionStoreService(ChainRunRecordService runs, CheckpointStoreService checkpoints)
    {
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
    }

    /// <summary>Возвращает состояние запуска с чекпоинтами; отсутствие — null.</summary>
    /// <param name="runId">Идентификатор запуска.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Состояние запуска или null.</returns>
    public async Task<ChainRunState?> GetRunAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        ChainRunRecord? record = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);
        if (record is null)
            return null;
        IReadOnlyList<ChainCheckpointDto> checkpoints = await GetCheckpointsAsync(runId, cancellationToken).ConfigureAwait(false);
        return ToState(record, checkpoints);
    }

    /// <summary>Ищет запуск по ключу идемпотентности внутри цепочки.</summary>
    /// <param name="chainId">Идентификатор цепочки.</param>
    /// <param name="idempotencyKey">Ключ идемпотентности.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор запуска или null.</returns>
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

    /// <summary>Создаёт запуск; повтор с тем же ключом идемпотентности возвращает существующий.</summary>
    /// <param name="runId">Идентификатор нового запуска.</param>
    /// <param name="chainId">Идентификатор цепочки.</param>
    /// <param name="trigger">Событие-триггер запуска.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Состояние созданного или существующего запуска.</returns>
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

    /// <summary>Обновляет статус запуска с проставлением времени завершения для терминальных статусов.</summary>
    /// <param name="runId">Идентификатор запуска.</param>
    /// <param name="status">Новый статус.</param>
    /// <param name="error">Ошибка запуска; отсутствует при успехе.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача обновления.</returns>
    /// <exception cref="KeyNotFoundException">Запуск отсутствует.</exception>
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

    /// <summary>Сохраняет чекпоинт: создаёт новый или обновляет существующий по (RunId, Step).</summary>
    /// <param name="checkpoint">DTO чекпоинта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача сохранения.</returns>
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

    /// <summary>Возвращает чекпоинты запуска, упорядоченные по шагу.</summary>
    /// <param name="runId">Идентификатор запуска.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Чекпоинты запуска.</returns>
    public async Task<IReadOnlyList<ChainCheckpointDto>> GetCheckpointsAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        PagedResult<CheckpointRecord> page = await _checkpoints.ListAsync(
            new PagedQuery { Offset = 0, Limit = 100, SortBy = "checkpoint_at", Descending = false }, runId, cancellationToken).ConfigureAwait(false);
        return page.Items.Select(r => r.Checkpoint).OrderBy(c => c.Step).ToList();
    }

    /// <summary>Возвращает идентификаторы незавершённых запусков (для восстановления после рестарта).</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификаторы незавершённых запусков.</returns>
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
    /// Читает запуски страницами не больше <see cref="RepositoryOptions.MaxPageLimit" />.
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

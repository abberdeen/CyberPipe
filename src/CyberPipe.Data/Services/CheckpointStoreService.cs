using CyberPipe.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>
/// Хранилище контрольных точек (таблица app.chain_checkpoints):
/// идемпотентность по (run_id, step). Все чтения — LINQ с AsNoTracking,
/// сырых SQL-строк нет.
/// </summary>
public sealed class CheckpointStoreService : ICheckpointStoreService
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["created_at"] = nameof(CheckpointEntity.CreatedAt),
        ["updated_at"] = nameof(CheckpointEntity.UpdatedAt),
        ["checkpoint_at"] = nameof(CheckpointEntity.CheckpointAt),
        ["step"] = nameof(CheckpointEntity.Step),
    };

    private readonly IDbContextFactory<CyberPipeDbContext> _dbFactory;
    private readonly RepositoryOptions _options;
    private readonly ILogger _logger;
    private readonly DataServiceMetrics _metrics;

    /// <summary>Создаёт хранилище контрольных точек поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public CheckpointStoreService(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger<CheckpointStoreService>? logger = null,
        DataServiceMetrics? metrics = null)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _options = options ?? new RepositoryOptions();
        IReadOnlyList<string> errors = _options.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(options));
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _metrics = metrics ?? new DataServiceMetrics();
    }

    /// <summary>Возвращает чекпоинт по идентификатору строки; отсутствие — null.</summary>
    /// <param name="id">Идентификатор строки чекпоинта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Чекпоинт или null.</returns>
    public Task<CheckpointRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync<CheckpointRecord?>("checkpoints.get", async (db, token) =>
        {
            string key = EfServiceHelper.GuidKey(id);
            CheckpointEntity? entity = await db.Checkpoints
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <summary>Возвращает чекпоинт запуска по шагу; отсутствие — null.</summary>
    /// <param name="runId">Идентификатор запуска.</param>
    /// <param name="step">Шаг цепочки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Чекпоинт или null.</returns>
    public Task<CheckpointRecord?> GetByRunAndStepAsync(RunId runId, ChainStepKind step, CancellationToken cancellationToken = default) =>
        ExecuteAsync<CheckpointRecord?>("checkpoints.get_by_run_step", async (db, token) =>
        {
            string runKey = EfServiceHelper.GuidKey(runId.Value);
            string stepKey = step.ToString();
            CheckpointEntity? entity = await db.Checkpoints
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.RunId == runKey && e.Step == stepKey, token)
                .ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <summary>Страница чекпоинтов; сортировка из белого списка.</summary>
    /// <param name="query">Запрос страницы.</param>
    /// <param name="runId">Необязательный фильтр по запуску.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница чекпоинтов.</returns>
    public Task<PagedResult<CheckpointRecord>> ListAsync(PagedQuery query, RunId? runId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("checkpoints.list", async (db, token) =>
        {
            string sort = EfServiceHelper.ResolveSortKey(query, Sorts, _options.MaxPageLimit);
            string? runKey = runId is null ? null : EfServiceHelper.GuidKey(runId.Value.Value);
            IQueryable<CheckpointEntity> filtered = db.Checkpoints
                .AsNoTracking()
                .Where(e => runKey == null || e.RunId == runKey);
            IQueryable<CheckpointEntity> ordered = EfServiceHelper.ApplyOrdering(filtered, sort, query.Descending, nameof(CheckpointEntity.Id));
            long total = await filtered.LongCountAsync(token).ConfigureAwait(false);
            List<CheckpointEntity> entities = await ordered
                .Skip(query.Offset).Take(query.Limit)
                .ToListAsync(token).ConfigureAwait(false);
            return new PagedResult<CheckpointRecord>
            {
                Items = entities.Select(Map).ToList(),
                TotalCount = total,
                Offset = query.Offset,
                Limit = query.Limit,
            };
        }, cancellationToken);

    /// <summary>
    /// Добавляет чекпоинт. Идемпотентность: уникальный ключ (RunId, Step) — повтор обновляет
    /// состояние существующей строки и возвращает её, а не создаёт дубль.
    /// </summary>
    /// <param name="checkpoint">DTO чекпоинта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённый чекпоинт с идентификатором и версией.</returns>
    public Task<CheckpointRecord> AddAsync(ChainCheckpointDto checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.AttemptCount < 0)
            throw new ArgumentException("AttemptCount must not be negative.", nameof(checkpoint));
        string? input = checkpoint.InputJson is null ? null : RepositoryJson.EnsureJson(checkpoint.InputJson, nameof(checkpoint.InputJson));
        string? output = checkpoint.OutputJson is null ? null : RepositoryJson.EnsureJson(checkpoint.OutputJson, nameof(checkpoint.OutputJson));
        return ExecuteAsync("checkpoints.add", async (db, token) =>
        {
            string runKey = EfServiceHelper.GuidKey(checkpoint.RunId.Value);
            string stepKey = checkpoint.Step.ToString();
            CheckpointEntity? existing = await db.Checkpoints
                .FirstOrDefaultAsync(e => e.RunId == runKey && e.Step == stepKey, token)
                .ConfigureAwait(false);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset checkpointAt = checkpoint.CheckpointAtUtc == default ? now : checkpoint.CheckpointAtUtc;
            string? errorJson = checkpoint.Error is null ? null : RepositoryJson.Serialize(checkpoint.Error);
            if (existing is not null)
            {
                existing.State = checkpoint.State.ToString();
                existing.AttemptCount = checkpoint.AttemptCount;
                existing.InputJson = input;
                existing.OutputJson = output;
                existing.ErrorJson = errorJson;
                existing.CheckpointAt = checkpointAt;
                existing.UpdatedAt = now;
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return Map(existing);
            }

            var entity = new CheckpointEntity
            {
                Id = Guid.NewGuid().ToString("D"),
                RunId = runKey,
                ChainId = EfServiceHelper.GuidKey(checkpoint.ChainId.Value),
                Step = stepKey,
                State = checkpoint.State.ToString(),
                AttemptCount = checkpoint.AttemptCount,
                InputJson = input,
                OutputJson = output,
                ErrorJson = errorJson,
                CheckpointAt = checkpointAt,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Checkpoints.Add(entity);
            try
            {
                await db.SaveChangesAsync(token).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                CheckpointEntity? raced = await db.Checkpoints
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.RunId == runKey && e.Step == stepKey, token)
                    .ConfigureAwait(false);
                if (raced is null)
                    throw;
                return Map(raced);
            }

            return Map(entity);
        }, cancellationToken);
    }

    /// <summary>Обновляет чекпоинт при совпадении expectedRowVersion с updated_at записи.</summary>
    /// <param name="record">Новое состояние чекпоинта.</param>
    /// <param name="expectedRowVersion">Версия строки, полученная при чтении.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Обновлённый чекпоинт с новой версией.</returns>
    /// <exception cref="OptimisticConcurrencyException">Версия устарела или запись отсутствует.</exception>
    public Task<CheckpointRecord> UpdateAsync(CheckpointRecord record, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Checkpoint);
        ChainCheckpointDto checkpoint = record.Checkpoint;
        string? input = checkpoint.InputJson is null ? null : RepositoryJson.EnsureJson(checkpoint.InputJson, nameof(checkpoint.InputJson));
        string? output = checkpoint.OutputJson is null ? null : RepositoryJson.EnsureJson(checkpoint.OutputJson, nameof(checkpoint.OutputJson));
        return ExecuteAsync("checkpoints.update", async (db, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string key = EfServiceHelper.GuidKey(record.Id);
            int affected = await db.Checkpoints
                .Where(e => e.Id == key && e.UpdatedAt == expectedRowVersion)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.State, checkpoint.State.ToString())
                    .SetProperty(e => e.AttemptCount, checkpoint.AttemptCount)
                    .SetProperty(e => e.InputJson, input)
                    .SetProperty(e => e.OutputJson, output)
                    .SetProperty(e => e.ErrorJson, checkpoint.Error == null ? null : RepositoryJson.Serialize(checkpoint.Error))
                    .SetProperty(e => e.CheckpointAt, checkpoint.CheckpointAtUtc)
                    .SetProperty(e => e.UpdatedAt, now), token).ConfigureAwait(false);
            if (affected == 0)
                throw new OptimisticConcurrencyException("chain_checkpoints", record.Id.ToString());
            CheckpointEntity? updated = await db.Checkpoints
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            return updated is null
                ? record with { RowVersion = now }
                : Map(updated);
        }, cancellationToken);
    }

    /// <summary>Удаляет чекпоинт; отсутствие — false.</summary>
    /// <param name="id">Идентификатор строки чекпоинта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись была удалена.</returns>
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("checkpoints.delete", async (db, token) =>
        {
            string key = EfServiceHelper.GuidKey(id);
            int affected = await db.Checkpoints
                .Where(e => e.Id == key)
                .ExecuteDeleteAsync(token).ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);

    internal static CheckpointRecord Map(CheckpointEntity entity) => new()
    {
        Id = Guid.Parse(entity.Id),
        Checkpoint = new ChainCheckpointDto
        {
            RunId = new RunId(Guid.Parse(entity.RunId)),
            ChainId = new ChainId(Guid.Parse(entity.ChainId)),
            Step = Enum.Parse<ChainStepKind>(entity.Step),
            State = Enum.Parse<ChainStepState>(entity.State),
            AttemptCount = entity.AttemptCount,
            InputJson = entity.InputJson,
            OutputJson = entity.OutputJson,
            Error = entity.ErrorJson is null ? null : RepositoryJson.Deserialize<ErrorDto>(entity.ErrorJson),
            CheckpointAtUtc = entity.CheckpointAt,
        },
        CreatedAtUtc = entity.CreatedAt,
        RowVersion = entity.UpdatedAt,
    };

    private Task<T> ExecuteAsync<T>(
        string operation,
        Func<CyberPipeDbContext, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken) =>
        EfServiceHelper.ExecuteAsync(_dbFactory, _options, _logger, _metrics, operation, body, cancellationToken);
}

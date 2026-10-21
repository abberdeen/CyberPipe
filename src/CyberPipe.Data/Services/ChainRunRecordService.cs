using CyberPipe.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>
/// Журнал запусков цепочек (таблица app.chain_runs): идемпотентность по
/// (ChainId, IdempotencyKey), оптимистичная конкуренция по updated_at.
/// Все чтения — LINQ с AsNoTracking, сырых SQL-строк нет.
/// </summary>
public sealed class ChainRunRecordService : IChainRunRecordService
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>
    {
        ["created_at"] = nameof(ChainRunEntity.CreatedAt),
        ["updated_at"] = nameof(ChainRunEntity.UpdatedAt),
        ["started_at"] = nameof(ChainRunEntity.StartedAt),
        ["status"] = nameof(ChainRunEntity.Status),
    };

    private readonly IDbContextFactory<CyberPipeDbContext> _dbFactory;
    private readonly RepositoryOptions _options;
    private readonly ILogger _logger;
    private readonly DataServiceMetrics _metrics;

    /// <summary>Создаёт сервис запусков поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public ChainRunRecordService(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger<ChainRunRecordService>? logger = null,
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

    /// <summary>Возвращает запуск; отсутствие — null.</summary>
    /// <param name="id">Идентификатор запуска.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Запуск или null.</returns>
    public Task<ChainRunRecord?> GetAsync(RunId id, CancellationToken cancellationToken = default) =>
        ExecuteAsync<ChainRunRecord?>("chain_runs.get", async (db, token) =>
        {
            string key = EfServiceHelper.GuidKey(id.Value);
            ChainRunEntity? entity = await db.ChainRuns
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <summary>Страница запусков; сортировка из белого списка.</summary>
    /// <param name="query">Запрос страницы.</param>
    /// <param name="chainId">Необязательный фильтр по цепочке.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница запусков.</returns>
    public Task<PagedResult<ChainRunRecord>> ListAsync(PagedQuery query, ChainId? chainId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("chain_runs.list", async (db, token) =>
        {
            string sort = EfServiceHelper.ResolveSortKey(query, Sorts, _options.MaxPageLimit);
            string? chainKey = chainId is null ? null : EfServiceHelper.GuidKey(chainId.Value.Value);
            IQueryable<ChainRunEntity> filtered = db.ChainRuns
                .AsNoTracking()
                .Where(e => chainKey == null || e.ChainId == chainKey);
            IQueryable<ChainRunEntity> ordered = EfServiceHelper.ApplyOrdering(filtered, sort, query.Descending, nameof(ChainRunEntity.Id));
            long total = await filtered.LongCountAsync(token).ConfigureAwait(false);
            List<ChainRunEntity> entities = await ordered
                .Skip(query.Offset).Take(query.Limit)
                .ToListAsync(token).ConfigureAwait(false);
            return new PagedResult<ChainRunRecord>
            {
                Items = entities.Select(Map).ToList(),
                TotalCount = total,
                Offset = query.Offset,
                Limit = query.Limit,
            };
        }, cancellationToken);

    /// <summary>
    /// Добавляет запуск. Идемпотентность: уникальный ключ (ChainId, IdempotencyKey) —
    /// повтор возвращает существующую запись без создания второй.
    /// </summary>
    /// <param name="run">Запуск.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённый или существующий запуск.</returns>
    public Task<ChainRunRecord> AddAsync(ChainRunRecord run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        return ExecuteAsync("chain_runs.add", async (db, token) =>
        {
            string chainKey = EfServiceHelper.GuidKey(run.ChainId.Value);
            string key = run.Id.Value == Guid.Empty ? Guid.NewGuid().ToString("D") : EfServiceHelper.GuidKey(run.Id.Value);
            ChainRunEntity? existing = await db.ChainRuns
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    e => e.ChainId == chainKey && e.IdempotencyKey == run.IdempotencyKey, token)
                .ConfigureAwait(false);
            if (existing is not null)
                return Map(existing);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset started = run.StartedAtUtc == default ? now : run.StartedAtUtc;
            var entity = new ChainRunEntity
            {
                Id = key,
                ChainId = chainKey,
                TriggerEventId = run.TriggerEventId.HasValue ? EfServiceHelper.GuidKey(run.TriggerEventId.Value) : null,
                IdempotencyKey = run.IdempotencyKey,
                Status = run.Status.ToString(),
                Source = run.Source,
                PayloadJson = RepositoryJson.EnsureJson(run.PayloadJson, "payload"),
                StartedAt = started,
                FinishedAt = run.FinishedAtUtc,
                ErrorJson = run.Error is null ? null : RepositoryJson.Serialize(run.Error),
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.ChainRuns.Add(entity);
            try
            {
                await db.SaveChangesAsync(token).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                ChainRunEntity? raced = await db.ChainRuns
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        e => e.ChainId == chainKey && e.IdempotencyKey == run.IdempotencyKey, token)
                    .ConfigureAwait(false);
                if (raced is null)
                    throw;
                return Map(raced);
            }

            return run with { Id = new RunId(Guid.Parse(key)), StartedAtUtc = started };
        }, cancellationToken);
    }

    /// <summary>Обновляет запуск при совпадении expectedRowVersion с updated_at записи.</summary>
    /// <param name="run">Новое состояние запуска.</param>
    /// <param name="expectedRowVersion">Версия строки, полученная при чтении.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Обновлённый запуск с новой версией.</returns>
    /// <exception cref="OptimisticConcurrencyException">Версия устарела или запись отсутствует.</exception>
    public Task<ChainRunRecord> UpdateAsync(ChainRunRecord run, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        return ExecuteAsync("chain_runs.update", async (db, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string key = EfServiceHelper.GuidKey(run.Id.Value);
            int affected = await db.ChainRuns
                .Where(e => e.Id == key && e.UpdatedAt == expectedRowVersion)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Status, run.Status.ToString())
                    .SetProperty(e => e.Source, run.Source.ToString())
                    .SetProperty(e => e.PayloadJson, RepositoryJson.EnsureJson(run.PayloadJson, "payload"))
                    .SetProperty(e => e.FinishedAt, run.FinishedAtUtc)
                    .SetProperty(e => e.ErrorJson, run.Error == null ? null : RepositoryJson.Serialize(run.Error))
                    .SetProperty(e => e.UpdatedAt, now), token).ConfigureAwait(false);
            if (affected == 0)
                throw new OptimisticConcurrencyException("chain_runs", run.Id.ToString());
            return run with { RowVersion = now };
        }, cancellationToken);
    }

    /// <summary>Удаляет запуск; отсутствие — false.</summary>
    /// <param name="id">Идентификатор запуска.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись была удалена.</returns>
    public Task<bool> DeleteAsync(RunId id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("chain_runs.delete", async (db, token) =>
        {
            string key = EfServiceHelper.GuidKey(id.Value);
            int affected = await db.ChainRuns
                .Where(e => e.Id == key)
                .ExecuteDeleteAsync(token).ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);

    internal static ChainRunRecord Map(ChainRunEntity entity) => new()
    {
        Id = new RunId(Guid.Parse(entity.Id)),
        ChainId = new ChainId(Guid.Parse(entity.ChainId)),
        TriggerEventId = entity.TriggerEventId is null ? null : Guid.Parse(entity.TriggerEventId),
        IdempotencyKey = entity.IdempotencyKey,
        Status = Enum.Parse<ChainRunStatus>(entity.Status),
        Source = entity.Source,
        PayloadJson = entity.PayloadJson,
        StartedAtUtc = entity.StartedAt,
        FinishedAtUtc = entity.FinishedAt,
        Error = entity.ErrorJson is null ? null : RepositoryJson.Deserialize<ErrorDto>(entity.ErrorJson),
        CreatedAtUtc = entity.CreatedAt,
        RowVersion = entity.UpdatedAt,
    };

    private Task<T> ExecuteAsync<T>(
        string operation,
        Func<CyberPipeDbContext, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken) =>
        EfServiceHelper.ExecuteAsync(_dbFactory, _options, _logger, _metrics, operation, body, cancellationToken);
}

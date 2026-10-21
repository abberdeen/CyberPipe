using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>
/// Журнал аудита (таблица app.audit_log, append-only по смыслу).
/// Все чтения — LINQ с AsNoTracking, сырых SQL-строк нет.
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["created_at"] = nameof(AuditEntity.CreatedAt),
        ["id"] = nameof(AuditEntity.Id),
        ["action"] = nameof(AuditEntity.Action),
        ["actor"] = nameof(AuditEntity.Actor),
    };

    private readonly IDbContextFactory<CyberPipeDbContext> _dbFactory;
    private readonly RepositoryOptions _options;
    private readonly ILogger _logger;
    private readonly DataServiceMetrics _metrics;

    /// <summary>Создаёт журнал аудита поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public AuditLogService(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger<AuditLogService>? logger = null,
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

    /// <summary>Возвращает запись аудита; отсутствие — null.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Запись или null.</returns>
    public Task<AuditLogRecord?> GetAsync(long id, CancellationToken cancellationToken = default) =>
        ExecuteAsync<AuditLogRecord?>("audit.get", async (db, token) =>
        {
            AuditEntity? entity = await db.AuditLog
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id, token).ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <summary>Страница аудита; сортировка из белого списка.</summary>
    /// <param name="query">Запрос страницы.</param>
    /// <param name="actor">Необязательный фильтр по исполнителю.</param>
    /// <param name="action">Необязательный фильтр по действию.</param>
    /// <param name="entityType">Необязательный фильтр по типу сущности.</param>
    /// <param name="entityId">Необязательный фильтр по идентификатору сущности.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница записей аудита.</returns>
    public Task<PagedResult<AuditLogRecord>> ListAsync(PagedQuery query, string? actor = null, string? action = null, string? entityType = null, string? entityId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("audit.list", async (db, token) =>
        {
            string sort = EfServiceHelper.ResolveSortKey(query, Sorts, _options.MaxPageLimit);
            string? actorFilter = EfServiceHelper.OptionalFilter(actor);
            string? actionFilter = EfServiceHelper.OptionalFilter(action);
            string? typeFilter = EfServiceHelper.OptionalFilter(entityType);
            string? idFilter = EfServiceHelper.OptionalFilter(entityId);
            IQueryable<AuditEntity> filtered = db.AuditLog
                .AsNoTracking()
                .Where(e => actorFilter == null || e.Actor == actorFilter)
                .Where(e => actionFilter == null || e.Action == actionFilter)
                .Where(e => typeFilter == null || e.EntityType == typeFilter)
                .Where(e => idFilter == null || e.EntityId == idFilter);
            IQueryable<AuditEntity> ordered = EfServiceHelper.ApplyOrdering(filtered, sort, query.Descending, nameof(AuditEntity.Id));
            long total = await filtered.LongCountAsync(token).ConfigureAwait(false);
            List<AuditEntity> entities = await ordered
                .Skip(query.Offset).Take(query.Limit)
                .ToListAsync(token).ConfigureAwait(false);
            return new PagedResult<AuditLogRecord>
            {
                Items = entities.Select(Map).ToList(),
                TotalCount = total,
                Offset = query.Offset,
                Limit = query.Limit,
            };
        }, cancellationToken);

    /// <summary>Добавляет запись аудита; идентификатор генерирует база.</summary>
    /// <param name="record">Запись аудита без идентификатора.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённая запись с идентификатором.</returns>
    public Task<AuditLogRecord> AddAsync(AuditLogRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return ExecuteAsync("audit.add", async (db, token) =>
        {
            var entity = new AuditEntity
            {
                Actor = record.Actor,
                Action = record.Action,
                EntityType = record.EntityType,
                EntityId = record.EntityId,
                DetailsJson = RepositoryJson.EnsureJson(record.DetailsJson, nameof(record.DetailsJson)),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.AuditLog.Add(entity);
            await db.SaveChangesAsync(token).ConfigureAwait(false);
            return record with { Id = entity.Id, CreatedAtUtc = entity.CreatedAt };
        }, cancellationToken);
    }

    /// <summary>Обновляет поля записи аудита по идентификатору (техническая правка; у таблицы нет updated_at).</summary>
    /// <param name="record">Новое состояние записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись существовала и обновлена.</returns>
    public Task<bool> UpdateAsync(AuditLogRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return ExecuteAsync("audit.update", async (db, token) =>
        {
            int affected = await db.AuditLog
                .Where(e => e.Id == record.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Actor, record.Actor)
                    .SetProperty(e => e.Action, record.Action)
                    .SetProperty(e => e.EntityType, record.EntityType)
                    .SetProperty(e => e.EntityId, record.EntityId)
                    .SetProperty(e => e.DetailsJson, RepositoryJson.EnsureJson(record.DetailsJson, nameof(record.DetailsJson))), token)
                .ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);
    }

    /// <summary>Удаляет запись аудита; отсутствие — false.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись была удалена.</returns>
    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("audit.delete", async (db, token) =>
        {
            int affected = await db.AuditLog
                .Where(e => e.Id == id)
                .ExecuteDeleteAsync(token).ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);

    internal static AuditLogRecord Map(AuditEntity entity) => new() { Id = entity.Id, Actor = entity.Actor, Action = entity.Action, EntityType = entity.EntityType, EntityId = entity.EntityId, DetailsJson = entity.DetailsJson, CreatedAtUtc = entity.CreatedAt };

    private Task<T> ExecuteAsync<T>(
        string operation,
        Func<CyberPipeDbContext, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken) =>
        EfServiceHelper.ExecuteAsync(_dbFactory, _options, _logger, _metrics, operation, body, cancellationToken);
}

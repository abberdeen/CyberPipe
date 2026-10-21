using CyberPipe.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>
/// Каталог цепочек (таблица app.chains): определения целиком в definition_json.
/// Объединяет операции определений и каталога; все чтения — LINQ с AsNoTracking,
/// записи — ExecuteUpdate/ExecuteDelete, сырых SQL-строк нет.
/// </summary>
public sealed class ChainCatalogService : IChainCatalogService
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>
    {
        ["created_at"] = nameof(ChainEntity.CreatedAt),
        ["updated_at"] = nameof(ChainEntity.UpdatedAt),
        ["name"] = nameof(ChainEntity.Name),
        ["version"] = nameof(ChainEntity.Version),
    };

    private readonly IDbContextFactory<CyberPipeDbContext> _dbFactory;
    private readonly RepositoryOptions _options;
    private readonly ILogger _logger;
    private readonly DataServiceMetrics _metrics;

    /// <summary>Создаёт каталог цепочек поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public ChainCatalogService(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger<ChainCatalogService>? logger = null,
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

    /// <summary>Возвращает цепочку по идентификатору; отсутствие — null без исключения.</summary>
    /// <param name="id">Идентификатор цепочки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Цепочка или null.</returns>
    public Task<ChainDefinitionDto?> GetAsync(ChainId id, CancellationToken cancellationToken = default) =>
        ExecuteAsync<ChainDefinitionDto?>("chains.get", async (db, token) =>
        {
            string key = EfServiceHelper.GuidKey(id.Value);
            ChainEntity? entity = await db.Chains
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <summary>Возвращает определение цепочки по идентификатору; отсутствие — null.</summary>
    /// <param name="id">Идентификатор цепочки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Определение или null.</returns>
    public Task<ChainDefinitionDto?> GetByIdAsync(ChainId id, CancellationToken cancellationToken = default) =>
        GetAsync(id, cancellationToken);

    /// <summary>Страница цепочек с обязательной сортировкой из белого списка.</summary>
    /// <param name="query">Запрос страницы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница цепочек.</returns>
    public Task<PagedResult<ChainDefinitionDto>> ListAsync(PagedQuery query, CancellationToken cancellationToken = default) =>
        ExecuteAsync("chains.list", async (db, token) =>
        {
            string sort = EfServiceHelper.ResolveSortKey(query, Sorts, _options.MaxPageLimit);
            IQueryable<ChainEntity> source = EfServiceHelper.ApplyOrdering(db.Chains.AsNoTracking(), sort, query.Descending, nameof(ChainEntity.Id));
            long total = await source.LongCountAsync(token).ConfigureAwait(false);
            List<ChainEntity> entities = await source
                .Skip(query.Offset).Take(query.Limit)
                .ToListAsync(token).ConfigureAwait(false);
            return new PagedResult<ChainDefinitionDto>
            {
                Items = entities.Select(Map).ToList(),
                TotalCount = total,
                Offset = query.Offset,
                Limit = query.Limit,
            };
        }, cancellationToken);

    /// <summary>
    /// Добавляет цепочку. Идемпотентность: повтор с тем же Id возвращает существующую запись
    /// без перезаписи.
    /// </summary>
    /// <param name="chain">Определение цепочки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённое определение с фактическими метками времени.</returns>
    public Task<ChainDefinitionDto> AddAsync(ChainDefinitionDto chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chain);
        return ExecuteAsync("chains.add", async (db, token) =>
        {
            string key = EfServiceHelper.GuidKey(chain.Id.Value);
            ChainEntity? existing = await db.Chains
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            if (existing is not null)
                return Map(existing);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset created = chain.CreatedAtUtc == default ? now : chain.CreatedAtUtc;
            DateTimeOffset updated = chain.UpdatedAtUtc == default ? created : chain.UpdatedAtUtc;
            var entity = new ChainEntity
            {
                Id = key,
                Name = chain.Name,
                DefinitionJson = RepositoryJson.Serialize(chain),
                IsEnabled = chain.IsEnabled,
                RequiresHitl = chain.RequiresHitl,
                Version = chain.Version,
                CreatedAt = created,
                UpdatedAt = updated,
            };
            db.Chains.Add(entity);
            try
            {
                await db.SaveChangesAsync(token).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                ChainEntity? raced = await db.Chains
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
                if (raced is null)
                    throw;
                return Map(raced);
            }

            return chain with { CreatedAtUtc = created, UpdatedAtUtc = updated };
        }, cancellationToken);
    }

    /// <summary>Обновляет цепочку целиком; updated_at выставляет база.</summary>
    /// <param name="chain">Новое определение.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Обновлённое определение.</returns>
    /// <exception cref="OptimisticConcurrencyException">Запись отсутствует (трактуется как конфликт версий).</exception>
    public Task<ChainDefinitionDto> UpdateAsync(ChainDefinitionDto chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chain);
        return ExecuteAsync("chains.update", async (db, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string json = RepositoryJson.Serialize(chain);
            int affected = await db.Chains
                .Where(e => e.Id == EfServiceHelper.GuidKey(chain.Id.Value))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Name, chain.Name)
                    .SetProperty(e => e.DefinitionJson, json)
                    .SetProperty(e => e.IsEnabled, chain.IsEnabled)
                    .SetProperty(e => e.RequiresHitl, chain.RequiresHitl)
                    .SetProperty(e => e.Version, chain.Version)
                    .SetProperty(e => e.UpdatedAt, now), token).ConfigureAwait(false);
            if (affected == 0)
                throw new OptimisticConcurrencyException("chains", chain.Id.ToString());
            return chain with { UpdatedAtUtc = now };
        }, cancellationToken);
    }

    /// <summary>Удаляет цепочку; отсутствие — false без исключения, повторный вызов — false.</summary>
    /// <param name="id">Идентификатор цепочки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись была удалена.</returns>
    public Task<bool> DeleteAsync(ChainId id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("chains.delete", async (db, token) =>
        {
            string key = EfServiceHelper.GuidKey(id.Value);
            int affected = await db.Chains
                .Where(e => e.Id == key)
                .ExecuteDeleteAsync(token).ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);

    /// <summary>Создаёт определение или обновляет существующее с инкрементом версии.</summary>
    /// <param name="definition">Определение цепочки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённое определение.</returns>
    public async Task<ChainDefinitionDto> UpsertAsync(ChainDefinitionDto definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ChainDefinitionDto? existing = await GetAsync(definition.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return await AddAsync(definition, cancellationToken).ConfigureAwait(false);

        int nextVersion = existing.IsEnabled ? existing.Version + 1 : existing.Version;
        return await UpdateAsync(definition with { Version = nextVersion }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Возвращает все включённые цепочки по имени.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Включённые определения.</returns>
    public Task<IReadOnlyList<ChainDefinitionDto>> ListEnabledAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync<IReadOnlyList<ChainDefinitionDto>>("chains.list_enabled", async (db, token) =>
        {
            List<ChainEntity> entities = await db.Chains
                .AsNoTracking()
                .Where(e => e.IsEnabled)
                .OrderBy(e => e.Name)
                .ToListAsync(token).ConfigureAwait(false);
            return entities.Select(Map).ToList();
        }, cancellationToken);

    /// <summary>Включает или выключает цепочку.</summary>
    /// <param name="id">Идентификатор цепочки.</param>
    /// <param name="isEnabled">Новое состояние флага.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись существовала и обновлена.</returns>
    public Task<bool> SetEnabledAsync(ChainId id, bool isEnabled, CancellationToken cancellationToken = default) =>
        ExecuteAsync("chains.set_enabled", async (db, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string key = EfServiceHelper.GuidKey(id.Value);
            ChainEntity? entity = await db.Chains
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            if (entity is null)
            {
                return false;
            }

            // Колонка IsEnabled — для фильтра ListEnabledAsync, JSON — источник DTO:
            // обновляем оба, иначе чтения через Map вернут устаревший флаг.
            ChainDefinitionDto dto = RepositoryJson.Deserialize<ChainDefinitionDto>(entity.DefinitionJson);
            entity.IsEnabled = isEnabled;
            entity.DefinitionJson = RepositoryJson.Serialize(dto with { IsEnabled = isEnabled });
            entity.UpdatedAt = now;
            await db.SaveChangesAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    internal static ChainDefinitionDto Map(ChainEntity entity)
    {
        ChainDefinitionDto dto = RepositoryJson.Deserialize<ChainDefinitionDto>(entity.DefinitionJson);
        return dto.UpdatedAtUtc == entity.UpdatedAt ? dto : dto with { UpdatedAtUtc = entity.UpdatedAt };
    }

    private Task<T> ExecuteAsync<T>(
        string operation,
        Func<CyberPipeDbContext, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken) =>
        EfServiceHelper.ExecuteAsync(_dbFactory, _options, _logger, _metrics, operation, body, cancellationToken);
}

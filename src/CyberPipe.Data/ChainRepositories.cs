using CyberPipe.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>Репозиторий цепочек на EF Core: определение целиком в definition_json.</summary>
public sealed class ChainRepository : RepositoryBase, IChainRepository, IChainDefinitionRepository
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>
    {
        ["created_at"] = nameof(ChainEntity.CreatedAt),
        ["updated_at"] = nameof(ChainEntity.UpdatedAt),
        ["name"] = nameof(ChainEntity.Name),
        ["version"] = nameof(ChainEntity.Version),
    };

    /// <summary>Создаёт репозиторий цепочек поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции репозиториев; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public ChainRepository(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger? logger = null,
        UnitOfWorkMetrics? metrics = null)
        : base(dbFactory, options, logger, metrics)
    {
    }

    /// <inheritdoc />
    public Task<ChainDefinitionDto?> GetAsync(ChainId id, CancellationToken cancellationToken = default) =>
        ExecuteAsync<ChainDefinitionDto?>("chains.get", async (db, token) =>
        {
            string key = GuidKey(id.Value);
            ChainEntity? entity = await db.Chains
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<ChainDefinitionDto>> ListAsync(PagedQuery query, CancellationToken cancellationToken = default) =>
        ExecuteAsync("chains.list", async (db, token) =>
        {
            string sort = ResolveSortKey(query, Sorts);
            IQueryable<ChainEntity> source = ApplyOrdering(db.Chains.AsNoTracking(), sort, query.Descending, nameof(ChainEntity.Id));
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

    /// <inheritdoc />
    public Task<ChainDefinitionDto> AddAsync(ChainDefinitionDto chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chain);
        return ExecuteAsync("chains.add", async (db, token) =>
        {
            string key = GuidKey(chain.Id.Value);
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
                await SaveAsync(db, token).ConfigureAwait(false);
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

    /// <inheritdoc />
    public Task<ChainDefinitionDto> UpdateAsync(ChainDefinitionDto chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chain);
        return ExecuteAsync("chains.update", async (db, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string json = RepositoryJson.Serialize(chain);
            int affected = await db.Chains
                .Where(e => e.Id == GuidKey(chain.Id.Value))
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

    /// <inheritdoc />
    public Task<bool> DeleteAsync(ChainId id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("chains.delete", async (db, token) =>
        {
            string key = GuidKey(id.Value);
            int affected = await db.Chains
                .Where(e => e.Id == key)
                .ExecuteDeleteAsync(token).ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);

    /// <inheritdoc />
    Task<ChainDefinitionDto?> IChainDefinitionRepository.GetByIdAsync(ChainId id, CancellationToken cancellationToken) =>
        GetAsync(id, cancellationToken);

    /// <inheritdoc />
    public async Task<ChainDefinitionDto> UpsertAsync(ChainDefinitionDto definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ChainDefinitionDto? existing = await GetAsync(definition.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return await AddAsync(definition, cancellationToken).ConfigureAwait(false);

        int nextVersion = existing.IsEnabled ? existing.Version + 1 : existing.Version;
        return await UpdateAsync(definition with { Version = nextVersion }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Task<bool> SetEnabledAsync(ChainId id, bool isEnabled, CancellationToken cancellationToken = default) =>
        ExecuteAsync("chains.set_enabled", async (db, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string key = GuidKey(id.Value);
            int affected = await db.Chains
                .Where(e => e.Id == key)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.IsEnabled, isEnabled)
                    .SetProperty(e => e.UpdatedAt, now), token).ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);

    internal static ChainDefinitionDto Map(ChainEntity entity)
    {
        ChainDefinitionDto dto = RepositoryJson.Deserialize<ChainDefinitionDto>(entity.DefinitionJson);
        return dto.UpdatedAtUtc == entity.UpdatedAt ? dto : dto with { UpdatedAtUtc = entity.UpdatedAt };
    }
}

/// <summary>Репозиторий запусков на EF Core.</summary>
public sealed class ChainRunRepository : RepositoryBase, IChainRunRepository
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>
    {
        ["created_at"] = nameof(ChainRunEntity.CreatedAt),
        ["updated_at"] = nameof(ChainRunEntity.UpdatedAt),
        ["started_at"] = nameof(ChainRunEntity.StartedAt),
        ["status"] = nameof(ChainRunEntity.Status),
    };

    /// <summary>Создаёт репозиторий запусков поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции репозиториев; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public ChainRunRepository(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger? logger = null,
        UnitOfWorkMetrics? metrics = null)
        : base(dbFactory, options, logger, metrics)
    {
    }

    /// <inheritdoc />
    public Task<ChainRunRecord?> GetAsync(RunId id, CancellationToken cancellationToken = default) =>
        ExecuteAsync<ChainRunRecord?>("chain_runs.get", async (db, token) =>
        {
            string key = GuidKey(id.Value);
            ChainRunEntity? entity = await db.ChainRuns
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<ChainRunRecord>> ListAsync(PagedQuery query, ChainId? chainId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("chain_runs.list", async (db, token) =>
        {
            string sort = ResolveSortKey(query, Sorts);
            string? chainKey = chainId is null ? null : GuidKey(chainId.Value.Value);
            IQueryable<ChainRunEntity> filtered = db.ChainRuns
                .AsNoTracking()
                .Where(e => chainKey == null || e.ChainId == chainKey);
            IQueryable<ChainRunEntity> ordered = ApplyOrdering(filtered, sort, query.Descending, nameof(ChainRunEntity.Id));
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

    /// <inheritdoc />
    public Task<ChainRunRecord> AddAsync(ChainRunRecord run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        return ExecuteAsync("chain_runs.add", async (db, token) =>
        {
            string chainKey = GuidKey(run.ChainId.Value);
            string key = run.Id.Value == Guid.Empty ? Guid.NewGuid().ToString("D") : GuidKey(run.Id.Value);
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
                TriggerEventId = run.TriggerEventId.HasValue ? GuidKey(run.TriggerEventId.Value) : null,
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
                await SaveAsync(db, token).ConfigureAwait(false);
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

    /// <inheritdoc />
    public Task<ChainRunRecord> UpdateAsync(ChainRunRecord run, DateTimeOffset expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        return ExecuteAsync("chain_runs.update", async (db, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string key = GuidKey(run.Id.Value);
            int affected = await db.ChainRuns
                .Where(e => e.Id == key && e.UpdatedAt == expectedVersion)
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

    /// <inheritdoc />
    public Task<bool> DeleteAsync(RunId id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("chain_runs.delete", async (db, token) =>
        {
            string key = GuidKey(id.Value);
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
}

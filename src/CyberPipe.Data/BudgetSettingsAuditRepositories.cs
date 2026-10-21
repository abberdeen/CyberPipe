using CyberPipe.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>Репозиторий книги бюджета на EF Core; итоги дня считаются LINQ-агрегатами.</summary>
public sealed class TokenBudgetRepository : RepositoryBase, ITokenBudgetRepository
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["created_at"] = nameof(TokenBudgetEntity.CreatedAt),
        ["updated_at"] = nameof(TokenBudgetEntity.UpdatedAt),
        ["budget_date"] = nameof(TokenBudgetEntity.BudgetDate),
        ["cost_usd"] = nameof(TokenBudgetEntity.CostUsd),
    };

    /// <summary>Создаёт репозиторий книги бюджета поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции репозиториев; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public TokenBudgetRepository(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger? logger = null,
        UnitOfWorkMetrics? metrics = null)
        : base(dbFactory, options, logger, metrics)
    {
    }

    /// <inheritdoc />
    public Task<TokenBudgetEntryRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync<TokenBudgetEntryRecord?>("token_budget.get", async (db, token) =>
        {
            string key = GuidKey(id);
            TokenBudgetEntity? entity = await db.TokenBudgets
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<TokenBudgetEntryRecord>> ListAsync(PagedQuery query, DateOnly? budgetDate = null, string? modelId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("token_budget.list", async (db, token) =>
        {
            string sort = ResolveSortKey(query, Sorts);
            string? modelFilter = OptionalFilter(modelId);
            IQueryable<TokenBudgetEntity> filtered = db.TokenBudgets
                .AsNoTracking()
                .Where(e => budgetDate == null || e.BudgetDate == budgetDate.Value)
                .Where(e => modelFilter == null || e.ModelId == modelFilter);
            IQueryable<TokenBudgetEntity> ordered = ApplyOrdering(filtered, sort, query.Descending, nameof(TokenBudgetEntity.Id));
            long total = await filtered.LongCountAsync(token).ConfigureAwait(false);
            List<TokenBudgetEntity> entities = await ordered
                .Skip(query.Offset).Take(query.Limit)
                .ToListAsync(token).ConfigureAwait(false);
            return new PagedResult<TokenBudgetEntryRecord>
            {
                Items = entities.Select(Map).ToList(),
                TotalCount = total,
                Offset = query.Offset,
                Limit = query.Limit,
            };
        }, cancellationToken);

    /// <inheritdoc />
    public Task<TokenBudgetEntryRecord> AddAsync(TokenBudgetEntryRecord entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ExecuteAsync("token_budget.add", async (db, token) =>
        {
            string key = entry.Id == Guid.Empty ? Guid.NewGuid().ToString("D") : GuidKey(entry.Id);
            TokenBudgetEntity? existing = await db.TokenBudgets
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            if (existing is not null)
                return Map(existing);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            var entity = new TokenBudgetEntity
            {
                Id = key,
                BudgetDate = entry.BudgetDate,
                ModelId = entry.ModelId,
                HostingType = entry.HostingType.ToString(),
                EntryType = entry.EntryType,
                RequestId = entry.RequestId,
                InputTokens = entry.InputTokens,
                OutputTokens = entry.OutputTokens,
                CostUsd = entry.CostUsd,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.TokenBudgets.Add(entity);
            try
            {
                await SaveAsync(db, token).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                TokenBudgetEntity? raced = await db.TokenBudgets
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
                if (raced is null)
                    throw;
                return Map(raced);
            }

            return Map(entity);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TokenBudgetEntryRecord> UpdateAsync(TokenBudgetEntryRecord entry, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ExecuteAsync("token_budget.update", async (db, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string key = GuidKey(entry.Id);
            int affected = await db.TokenBudgets
                .Where(e => e.Id == key && e.UpdatedAt == expectedRowVersion)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.BudgetDate, entry.BudgetDate)
                    .SetProperty(e => e.ModelId, entry.ModelId)
                    .SetProperty(e => e.HostingType, entry.HostingType.ToString())
                    .SetProperty(e => e.EntryType, entry.EntryType)
                    .SetProperty(e => e.RequestId, entry.RequestId)
                    .SetProperty(e => e.InputTokens, entry.InputTokens)
                    .SetProperty(e => e.OutputTokens, entry.OutputTokens)
                    .SetProperty(e => e.CostUsd, entry.CostUsd)
                    .SetProperty(e => e.UpdatedAt, now), token).ConfigureAwait(false);
            if (affected == 0)
                throw new OptimisticConcurrencyException("token_budget", entry.Id.ToString());
            return entry with { RowVersion = now };
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("token_budget.delete", async (db, token) =>
        {
            string key = GuidKey(id);
            int affected = await db.TokenBudgets
                .Where(e => e.Id == key)
                .ExecuteDeleteAsync(token).ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);

    /// <inheritdoc />
    public Task<TokenBudgetDailySummary> GetDailySummaryAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        ExecuteAsync("token_budget.daily_summary", async (db, token) =>
        {
            IQueryable<TokenBudgetEntity> filtered = db.TokenBudgets
                .AsNoTracking()
                .Where(e => e.BudgetDate == date);
            decimal cost = await filtered.SumAsync(e => (decimal?)e.CostUsd, token).ConfigureAwait(false) ?? 0m;
            long input = await filtered.SumAsync(e => (long?)e.InputTokens, token).ConfigureAwait(false) ?? 0L;
            long output = await filtered.SumAsync(e => (long?)e.OutputTokens, token).ConfigureAwait(false) ?? 0L;
            long count = await filtered.LongCountAsync(token).ConfigureAwait(false);
            return new TokenBudgetDailySummary
            {
                BudgetDate = date,
                TotalCostUsd = cost,
                TotalInputTokens = input,
                TotalOutputTokens = output,
                EntryCount = count,
            };
        }, cancellationToken);

    /// <inheritdoc />
    public Task<TokenBudgetEntryRecord> AddUsageAsync(TokenUsageDto usage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usage);
        return ExecuteAsync("token_budget.add_usage", async (db, token) =>
        {
            DateOnly today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var entity = new TokenBudgetEntity
            {
                Id = Guid.NewGuid().ToString("D"),
                BudgetDate = today,
                ModelId = usage.ModelId,
                HostingType = usage.HostingType.ToString(),
                EntryType = "usage",
                RequestId = usage.RequestId,
                InputTokens = usage.InputTokens,
                OutputTokens = usage.OutputTokens,
                CostUsd = usage.CostUsd,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.TokenBudgets.Add(entity);
            await SaveAsync(db, token).ConfigureAwait(false);
            return Map(entity);
        }, cancellationToken);
    }

    internal static TokenBudgetEntryRecord Map(TokenBudgetEntity entity) => new()
    {
        Id = Guid.Parse(entity.Id),
        BudgetDate = entity.BudgetDate,
        ModelId = entity.ModelId,
        HostingType = Enum.Parse<ModelHostingType>(entity.HostingType),
        EntryType = entity.EntryType,
        RequestId = entity.RequestId,
        InputTokens = entity.InputTokens,
        OutputTokens = entity.OutputTokens,
        CostUsd = entity.CostUsd,
        CreatedAtUtc = entity.CreatedAt,
        RowVersion = entity.UpdatedAt,
    };
}

/// <summary>Репозиторий настроек на EF Core; SetAsync — upsert по ключу.</summary>
public sealed class KeyValueSettingsRepository : RepositoryBase, IKeyValueSettingsRepository
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["key"] = nameof(SettingEntity.Key),
        ["created_at"] = nameof(SettingEntity.CreatedAt),
        ["updated_at"] = nameof(SettingEntity.UpdatedAt),
    };

    /// <summary>Создаёт репозиторий настроек поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции репозиториев; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public KeyValueSettingsRepository(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger? logger = null,
        UnitOfWorkMetrics? metrics = null)
        : base(dbFactory, options, logger, metrics)
    {
    }

    /// <inheritdoc />
    public Task<KeyValueSettingRecord?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        ExecuteAsync<KeyValueSettingRecord?>("settings.get", async (db, token) =>
        {
            SettingEntity? entity = await db.Settings
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Key == key, token).ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<KeyValueSettingRecord>> ListAsync(PagedQuery query, CancellationToken cancellationToken = default) =>
        ExecuteAsync("settings.list", async (db, token) =>
        {
            string sort = ResolveSortKey(query, Sorts);
            IQueryable<SettingEntity> source = db.Settings.AsNoTracking();
            IQueryable<SettingEntity> ordered = ApplyOrdering(source, sort, query.Descending, nameof(SettingEntity.Key));
            long total = await source.LongCountAsync(token).ConfigureAwait(false);
            List<SettingEntity> entities = await ordered
                .Skip(query.Offset).Take(query.Limit)
                .ToListAsync(token).ConfigureAwait(false);
            return new PagedResult<KeyValueSettingRecord>
            {
                Items = entities.Select(Map).ToList(),
                TotalCount = total,
                Offset = query.Offset,
                Limit = query.Limit,
            };
        }, cancellationToken);

    /// <inheritdoc />
    public Task<KeyValueSettingRecord> AddAsync(KeyValueSettingRecord setting, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return ExecuteAsync("settings.add", async (db, token) =>
        {
            SettingEntity? existing = await db.Settings
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Key == setting.Key, token).ConfigureAwait(false);
            if (existing is not null)
                return Map(existing);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            var entity = new SettingEntity
            {
                Key = setting.Key,
                ValueJson = RepositoryJson.EnsureJson(setting.ValueJson, nameof(setting.ValueJson)),
                Description = setting.Description,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Settings.Add(entity);
            try
            {
                await SaveAsync(db, token).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                SettingEntity? raced = await db.Settings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Key == setting.Key, token).ConfigureAwait(false);
                if (raced is null)
                    throw;
                return Map(raced);
            }

            return Map(entity);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<KeyValueSettingRecord> SetAsync(KeyValueSettingRecord setting, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return ExecuteAsync("settings.set", async (db, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            SettingEntity? existing = await db.Settings
                .FirstOrDefaultAsync(e => e.Key == setting.Key, token).ConfigureAwait(false);
            if (existing is null)
            {
                var created = new SettingEntity
                {
                    Key = setting.Key,
                    ValueJson = RepositoryJson.EnsureJson(setting.ValueJson, nameof(setting.ValueJson)),
                    Description = setting.Description,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.Settings.Add(created);
                await SaveAsync(db, token).ConfigureAwait(false);
                return Map(created);
            }

            existing.ValueJson = RepositoryJson.EnsureJson(setting.ValueJson, nameof(setting.ValueJson));
            existing.Description = setting.Description;
            existing.UpdatedAt = now;
            await SaveAsync(db, token).ConfigureAwait(false);
            return Map(existing);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<KeyValueSettingRecord> UpdateAsync(KeyValueSettingRecord setting, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return ExecuteAsync("settings.update", async (db, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            int affected = await db.Settings
                .Where(e => e.Key == setting.Key && e.UpdatedAt == expectedRowVersion)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.ValueJson, RepositoryJson.EnsureJson(setting.ValueJson, nameof(setting.ValueJson)))
                    .SetProperty(e => e.Description, setting.Description)
                    .SetProperty(e => e.UpdatedAt, now), token).ConfigureAwait(false);
            if (affected == 0)
                throw new OptimisticConcurrencyException("kv_settings", setting.Key);
            return setting with { RowVersion = now };
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        ExecuteAsync("settings.delete", async (db, token) =>
        {
            int affected = await db.Settings
                .Where(e => e.Key == key)
                .ExecuteDeleteAsync(token).ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);

    internal static KeyValueSettingRecord Map(SettingEntity entity) => new()
    {
        Key = entity.Key,
        ValueJson = entity.ValueJson,
        Description = entity.Description,
        CreatedAtUtc = entity.CreatedAt,
        RowVersion = entity.UpdatedAt,
    };
}

/// <summary>Репозиторий аудита на EF Core; записи только добавляются и обновляют текст.</summary>
public sealed class AuditLogRepository : RepositoryBase, IAuditLogRepository
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["created_at"] = nameof(AuditEntity.CreatedAt),
        ["id"] = nameof(AuditEntity.Id),
        ["action"] = nameof(AuditEntity.Action),
        ["actor"] = nameof(AuditEntity.Actor),
    };

    /// <summary>Создаёт репозиторий аудита поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции репозиториев; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public AuditLogRepository(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger? logger = null,
        UnitOfWorkMetrics? metrics = null)
        : base(dbFactory, options, logger, metrics)
    {
    }

    /// <inheritdoc />
    public Task<AuditLogRecord?> GetAsync(long id, CancellationToken cancellationToken = default) =>
        ExecuteAsync<AuditLogRecord?>("audit.get", async (db, token) =>
        {
            AuditEntity? entity = await db.AuditLog
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id, token).ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<AuditLogRecord>> ListAsync(PagedQuery query, string? actor = null, string? action = null, string? entityType = null, string? entityId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("audit.list", async (db, token) =>
        {
            string sort = ResolveSortKey(query, Sorts);
            string? actorFilter = OptionalFilter(actor);
            string? actionFilter = OptionalFilter(action);
            string? typeFilter = OptionalFilter(entityType);
            string? idFilter = OptionalFilter(entityId);
            IQueryable<AuditEntity> filtered = db.AuditLog
                .AsNoTracking()
                .Where(e => actorFilter == null || e.Actor == actorFilter)
                .Where(e => actionFilter == null || e.Action == actionFilter)
                .Where(e => typeFilter == null || e.EntityType == typeFilter)
                .Where(e => idFilter == null || e.EntityId == idFilter);
            IQueryable<AuditEntity> ordered = ApplyOrdering(filtered, sort, query.Descending, nameof(AuditEntity.Id));
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

    /// <inheritdoc />
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
            await SaveAsync(db, token).ConfigureAwait(false);
            return record with { Id = entity.Id, CreatedAtUtc = entity.CreatedAt };
        }, cancellationToken);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("audit.delete", async (db, token) =>
        {
            int affected = await db.AuditLog
                .Where(e => e.Id == id)
                .ExecuteDeleteAsync(token).ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);

    internal static AuditLogRecord Map(AuditEntity entity) => new() { Id = entity.Id, Actor = entity.Actor, Action = entity.Action, EntityType = entity.EntityType, EntityId = entity.EntityId, DetailsJson = entity.DetailsJson, CreatedAtUtc = entity.CreatedAt };
}

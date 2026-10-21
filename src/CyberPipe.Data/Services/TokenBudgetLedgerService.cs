using CyberPipe.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>
/// Книга токен-бюджета (таблица app.token_budget_ledger): итоги дня считаются
/// LINQ-агрегатами. Все чтения — LINQ с AsNoTracking, сырых SQL-строк нет.
/// </summary>
public sealed class TokenBudgetLedgerService : ITokenBudgetLedgerService
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["created_at"] = nameof(TokenBudgetEntity.CreatedAt),
        ["updated_at"] = nameof(TokenBudgetEntity.UpdatedAt),
        ["budget_date"] = nameof(TokenBudgetEntity.BudgetDate),
        ["cost_usd"] = nameof(TokenBudgetEntity.CostUsd),
    };

    private readonly IDbContextFactory<CyberPipeDbContext> _dbFactory;
    private readonly RepositoryOptions _options;
    private readonly ILogger _logger;
    private readonly DataServiceMetrics _metrics;

    /// <summary>Создаёт книгу бюджета поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public TokenBudgetLedgerService(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger<TokenBudgetLedgerService>? logger = null,
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

    /// <summary>Возвращает запись книги; отсутствие — null.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Запись или null.</returns>
    public Task<TokenBudgetEntryRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync<TokenBudgetEntryRecord?>("token_budget.get", async (db, token) =>
        {
            string key = EfServiceHelper.GuidKey(id);
            TokenBudgetEntity? entity = await db.TokenBudgets
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <summary>Страница записей книги; сортировка из белого списка.</summary>
    /// <param name="query">Запрос страницы.</param>
    /// <param name="budgetDate">Необязательный фильтр по дате бюджета.</param>
    /// <param name="modelId">Необязательный фильтр по модели.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница записей.</returns>
    public Task<PagedResult<TokenBudgetEntryRecord>> ListAsync(PagedQuery query, DateOnly? budgetDate = null, string? modelId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("token_budget.list", async (db, token) =>
        {
            string sort = EfServiceHelper.ResolveSortKey(query, Sorts, _options.MaxPageLimit);
            string? modelFilter = EfServiceHelper.OptionalFilter(modelId);
            IQueryable<TokenBudgetEntity> filtered = db.TokenBudgets
                .AsNoTracking()
                .Where(e => budgetDate == null || e.BudgetDate == budgetDate.Value)
                .Where(e => modelFilter == null || e.ModelId == modelFilter);
            IQueryable<TokenBudgetEntity> ordered = EfServiceHelper.ApplyOrdering(filtered, sort, query.Descending, nameof(TokenBudgetEntity.Id));
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

    /// <summary>Добавляет запись книги; повтор с тем же непустым Id возвращает существующую запись.</summary>
    /// <param name="entry">Запись книги.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённая или существующая запись.</returns>
    public Task<TokenBudgetEntryRecord> AddAsync(TokenBudgetEntryRecord entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ExecuteAsync("token_budget.add", async (db, token) =>
        {
            string key = entry.Id == Guid.Empty ? Guid.NewGuid().ToString("D") : EfServiceHelper.GuidKey(entry.Id);
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
                await db.SaveChangesAsync(token).ConfigureAwait(false);
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

    /// <summary>Обновляет запись книги при совпадении expectedRowVersion с updated_at.</summary>
    /// <param name="entry">Новое состояние записи.</param>
    /// <param name="expectedRowVersion">Версия строки, полученная при чтении.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Обновлённая запись с новой версией.</returns>
    /// <exception cref="OptimisticConcurrencyException">Версия устарела или запись отсутствует.</exception>
    public Task<TokenBudgetEntryRecord> UpdateAsync(TokenBudgetEntryRecord entry, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ExecuteAsync("token_budget.update", async (db, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string key = EfServiceHelper.GuidKey(entry.Id);
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

    /// <summary>Удаляет запись книги; отсутствие — false.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись была удалена.</returns>
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("token_budget.delete", async (db, token) =>
        {
            string key = EfServiceHelper.GuidKey(id);
            int affected = await db.TokenBudgets
                .Where(e => e.Id == key)
                .ExecuteDeleteAsync(token).ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);

    /// <summary>Дневной итог книги: суммарная стоимость и токены за дату.</summary>
    /// <param name="budgetDate">Дата бюджета.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Итог дня; нули, если записей нет.</returns>
    public Task<TokenBudgetDailySummary> GetDailySummaryAsync(DateOnly budgetDate, CancellationToken cancellationToken = default) =>
        ExecuteAsync("token_budget.daily_summary", async (db, token) =>
        {
            IQueryable<TokenBudgetEntity> filtered = db.TokenBudgets
                .AsNoTracking()
                .Where(e => e.BudgetDate == budgetDate);
            decimal cost = await filtered.SumAsync(e => (decimal?)e.CostUsd, token).ConfigureAwait(false) ?? 0m;
            long input = await filtered.SumAsync(e => (long?)e.InputTokens, token).ConfigureAwait(false) ?? 0L;
            long output = await filtered.SumAsync(e => (long?)e.OutputTokens, token).ConfigureAwait(false) ?? 0L;
            long count = await filtered.LongCountAsync(token).ConfigureAwait(false);
            return new TokenBudgetDailySummary
            {
                BudgetDate = budgetDate,
                TotalCostUsd = cost,
                TotalInputTokens = input,
                TotalOutputTokens = output,
                EntryCount = count,
            };
        }, cancellationToken);

    /// <summary>Добавляет фактический расход модели как запись книги вида usage.</summary>
    /// <param name="usage">Фактический расход вызова модели.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённая запись книги.</returns>
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
            await db.SaveChangesAsync(token).ConfigureAwait(false);
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

    private Task<T> ExecuteAsync<T>(
        string operation,
        Func<CyberPipeDbContext, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken) =>
        EfServiceHelper.ExecuteAsync(_dbFactory, _options, _logger, _metrics, operation, body, cancellationToken);
}

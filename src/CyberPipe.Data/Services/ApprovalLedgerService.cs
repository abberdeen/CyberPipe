using CyberPipe.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>Соответствие статуса одобрения значению колонки status (строчные буквы).</summary>
public static class ApprovalStatusMapping
{
    /// <summary>Преобразует статус в значение колонки (строчными буквами).</summary>
    /// <param name="status">Статус одобрения.</param>
    /// <returns>Значение колонки status.</returns>
    public static string ToDbValue(ApprovalStatus status) => status switch
    {
        ApprovalStatus.Pending => "pending",
        ApprovalStatus.Approved => "approved",
        ApprovalStatus.Rejected => "rejected",
        ApprovalStatus.Expired => "expired",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown approval status."),
    };

    /// <summary>Разбирает значение колонки status в статус.</summary>
    /// <param name="value">Значение колонки.</param>
    /// <returns>Статус одобрения.</returns>
    /// <exception cref="InvalidOperationException">Неизвестное значение в базе.</exception>
    public static ApprovalStatus Parse(string value) => value switch
    {
        "pending" => ApprovalStatus.Pending,
        "approved" => ApprovalStatus.Approved,
        "rejected" => ApprovalStatus.Rejected,
        "expired" => ApprovalStatus.Expired,
        _ => throw new InvalidOperationException($"Unknown approval status '{value}' in database."),
    };
}

/// <summary>
/// Журнал HITL-одобрений (таблица app.approvals): фильтр статуса — точное совпадение
/// lowercase-строки. Все чтения — LINQ с AsNoTracking, сырых SQL-строк нет.
/// </summary>
public sealed class ApprovalLedgerService : IApprovalLedgerService
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["created_at"] = nameof(ApprovalEntity.CreatedAt),
        ["updated_at"] = nameof(ApprovalEntity.UpdatedAt),
        ["expires_at"] = nameof(ApprovalEntity.ExpiresAt),
        ["status"] = nameof(ApprovalEntity.Status),
    };

    private readonly IDbContextFactory<CyberPipeDbContext> _dbFactory;
    private readonly RepositoryOptions _options;
    private readonly ILogger _logger;
    private readonly DataServiceMetrics _metrics;

    /// <summary>Создаёт журнал одобрений поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public ApprovalLedgerService(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger<ApprovalLedgerService>? logger = null,
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

    /// <summary>Возвращает одобрение; отсутствие — null.</summary>
    /// <param name="id">Идентификатор одобрения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Одобрение или null.</returns>
    public Task<ApprovalRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync<ApprovalRecord?>("approvals.get", async (db, token) =>
        {
            string key = EfServiceHelper.GuidKey(id);
            ApprovalEntity? entity = await db.Approvals
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <summary>Страница одобрений; сортировка из белого списка.</summary>
    /// <param name="query">Запрос страницы.</param>
    /// <param name="status">Необязательный фильтр по статусу.</param>
    /// <param name="runId">Необязательный фильтр по запуску.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница одобрений.</returns>
    public Task<PagedResult<ApprovalRecord>> ListAsync(PagedQuery query, ApprovalStatus? status = null, RunId? runId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("approvals.list", async (db, token) =>
        {
            string sort = EfServiceHelper.ResolveSortKey(query, Sorts, _options.MaxPageLimit);
            string? statusFilter = status?.ToString().ToLowerInvariant();
            string? runKey = runId is null ? null : EfServiceHelper.GuidKey(runId.Value.Value);
            IQueryable<ApprovalEntity> filtered = db.Approvals
                .AsNoTracking()
                .Where(e => statusFilter == null || e.Status == statusFilter)
                .Where(e => runKey == null || e.RunId == runKey);
            IQueryable<ApprovalEntity> ordered = EfServiceHelper.ApplyOrdering(filtered, sort, query.Descending, nameof(ApprovalEntity.Id));
            long total = await filtered.LongCountAsync(token).ConfigureAwait(false);
            List<ApprovalEntity> entities = await ordered
                .Skip(query.Offset).Take(query.Limit)
                .ToListAsync(token).ConfigureAwait(false);
            return new PagedResult<ApprovalRecord>
            {
                Items = entities.Select(Map).ToList(),
                TotalCount = total,
                Offset = query.Offset,
                Limit = query.Limit,
            };
        }, cancellationToken);

    /// <summary>
    /// Добавляет одобрение. Идемпотентность: повтор с тем же непустым Id возвращает существующую
    /// запись без перезаписи.
    /// </summary>
    /// <param name="approval">Одобрение.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённое или существующее одобрение.</returns>
    public Task<ApprovalRecord> AddAsync(ApprovalRecord approval, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ValidateApproval(approval);
        return ExecuteAsync("approvals.add", async (db, token) =>
        {
            string key = approval.Id == Guid.Empty ? Guid.NewGuid().ToString("D") : EfServiceHelper.GuidKey(approval.Id);
            ApprovalEntity? existing = await db.Approvals
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            if (existing is not null)
                return Map(existing);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            var entity = new ApprovalEntity
            {
                Id = key,
                RunId = approval.RunId is null ? null : EfServiceHelper.GuidKey(approval.RunId.Value.Value),
                ChainId = approval.ChainId is null ? null : EfServiceHelper.GuidKey(approval.ChainId.Value.Value),
                Step = approval.Step,
                Status = ApprovalStatusMapping.ToDbValue(approval.Status),
                PayloadJson = RepositoryJson.EnsureJson(approval.PayloadJson, nameof(approval.PayloadJson)),
                Decision = approval.Decision?.ToString(),
                DecidedAt = approval.DecidedAtUtc,
                ExpiresAt = approval.ExpiresAtUtc,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Approvals.Add(entity);
            try
            {
                await db.SaveChangesAsync(token).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                ApprovalEntity? raced = await db.Approvals
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
                if (raced is null)
                    throw;
                return Map(raced);
            }

            return Map(entity);
        }, cancellationToken);
    }

    /// <summary>Обновляет одобрение при совпадении expectedRowVersion с updated_at записи.</summary>
    /// <param name="approval">Новое состояние одобрения.</param>
    /// <param name="expectedRowVersion">Версия строки, полученная при чтении.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Обновлённое одобрение с новой версией.</returns>
    /// <exception cref="OptimisticConcurrencyException">Версия устарела или запись отсутствует.</exception>
    public Task<ApprovalRecord> UpdateAsync(ApprovalRecord approval, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ValidateApproval(approval);
        return ExecuteAsync("approvals.update", async (db, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string key = EfServiceHelper.GuidKey(approval.Id);
            int affected = await db.Approvals
                .Where(e => e.Id == key && e.UpdatedAt == expectedRowVersion)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Step, approval.Step)
                    .SetProperty(e => e.Status, ApprovalStatusMapping.ToDbValue(approval.Status))
                    .SetProperty(e => e.PayloadJson, RepositoryJson.EnsureJson(approval.PayloadJson, nameof(approval.PayloadJson)))
                    .SetProperty(e => e.Decision, approval.Decision == null ? null : approval.Decision.ToString())
                    .SetProperty(e => e.DecidedAt, approval.DecidedAtUtc)
                    .SetProperty(e => e.ExpiresAt, approval.ExpiresAtUtc)
                    .SetProperty(e => e.UpdatedAt, now), token).ConfigureAwait(false);
            if (affected == 0)
                throw new OptimisticConcurrencyException("approvals", approval.Id.ToString());
            return approval with { RowVersion = now };
        }, cancellationToken);
    }

    /// <summary>Удаляет одобрение; отсутствие — false.</summary>
    /// <param name="id">Идентификатор одобрения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись была удалена.</returns>
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("approvals.delete", async (db, token) =>
        {
            string key = EfServiceHelper.GuidKey(id);
            int affected = await db.Approvals
                .Where(e => e.Id == key)
                .ExecuteDeleteAsync(token).ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);

    private static void ValidateApproval(ApprovalRecord approval)
    {
        EfServiceHelper.RequireText(approval.Step, nameof(approval.Step));
        _ = RepositoryJson.EnsureJson(approval.PayloadJson, nameof(approval.PayloadJson));
    }

    internal static ApprovalRecord Map(ApprovalEntity entity) => new()
    {
        Id = Guid.Parse(entity.Id),
        RunId = entity.RunId is null ? null : new RunId(Guid.Parse(entity.RunId)),
        ChainId = entity.ChainId is null ? null : new ChainId(Guid.Parse(entity.ChainId)),
        Step = entity.Step,
        Status = ApprovalStatusMapping.Parse(entity.Status),
        PayloadJson = entity.PayloadJson,
        Decision = entity.Decision is null ? null : Enum.Parse<HitlDecision>(entity.Decision),
        DecidedAtUtc = entity.DecidedAt,
        ExpiresAtUtc = entity.ExpiresAt,
        CreatedAtUtc = entity.CreatedAt,
        RowVersion = entity.UpdatedAt,
    };

    private Task<T> ExecuteAsync<T>(
        string operation,
        Func<CyberPipeDbContext, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken) =>
        EfServiceHelper.ExecuteAsync(_dbFactory, _options, _logger, _metrics, operation, body, cancellationToken);
}

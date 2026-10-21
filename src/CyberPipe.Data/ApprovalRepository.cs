using CyberPipe.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

/// <summary>Репозиторий одобрений на EF Core; фильтр статуса — точное совпадение lowercase-строки.</summary>
public sealed class ApprovalRepository : RepositoryBase, IApprovalRepository
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["created_at"] = nameof(ApprovalEntity.CreatedAt),
        ["updated_at"] = nameof(ApprovalEntity.UpdatedAt),
        ["expires_at"] = nameof(ApprovalEntity.ExpiresAt),
        ["status"] = nameof(ApprovalEntity.Status),
    };

    /// <summary>Создаёт репозиторий одобрений поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции репозиториев; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public ApprovalRepository(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger? logger = null,
        UnitOfWorkMetrics? metrics = null)
        : base(dbFactory, options, logger, metrics)
    {
    }

    /// <inheritdoc />
    public Task<ApprovalRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync<ApprovalRecord?>("approvals.get", async (db, token) =>
        {
            string key = GuidKey(id);
            ApprovalEntity? entity = await db.Approvals
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<ApprovalRecord>> ListAsync(PagedQuery query, ApprovalStatus? status = null, RunId? runId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("approvals.list", async (db, token) =>
        {
            string sort = ResolveSortKey(query, Sorts);
            string? statusFilter = status?.ToString().ToLowerInvariant();
            string? runKey = runId is null ? null : GuidKey(runId.Value.Value);
            IQueryable<ApprovalEntity> filtered = db.Approvals
                .AsNoTracking()
                .Where(e => statusFilter == null || e.Status == statusFilter)
                .Where(e => runKey == null || e.RunId == runKey);
            IQueryable<ApprovalEntity> ordered = ApplyOrdering(filtered, sort, query.Descending, nameof(ApprovalEntity.Id));
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

    /// <inheritdoc />
    public Task<ApprovalRecord> AddAsync(ApprovalRecord approval, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ValidateApproval(approval);
        return ExecuteAsync("approvals.add", async (db, token) =>
        {
            string key = approval.Id == Guid.Empty ? Guid.NewGuid().ToString("D") : GuidKey(approval.Id);
            ApprovalEntity? existing = await db.Approvals
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            if (existing is not null)
                return Map(existing);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            var entity = new ApprovalEntity
            {
                Id = key,
                RunId = approval.RunId is null ? null : GuidKey(approval.RunId.Value.Value),
                ChainId = approval.ChainId is null ? null : GuidKey(approval.ChainId.Value.Value),
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
                await SaveAsync(db, token).ConfigureAwait(false);
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

    /// <inheritdoc />
    public Task<ApprovalRecord> UpdateAsync(ApprovalRecord approval, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ValidateApproval(approval);
        return ExecuteAsync("approvals.update", async (db, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string key = GuidKey(approval.Id);
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

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("approvals.delete", async (db, token) =>
        {
            string key = GuidKey(id);
            int affected = await db.Approvals
                .Where(e => e.Id == key)
                .ExecuteDeleteAsync(token).ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);

    private static void ValidateApproval(ApprovalRecord approval)
    {
        RequireText(approval.Step, nameof(approval.Step));
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
}

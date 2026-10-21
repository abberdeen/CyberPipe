using System.Text.Json;
using CyberPipe.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>SQLite реализация репозитория HITL-подтверждений (TASK-046) на EF Core.
/// Атомарное разрешение запросов: UPDATE выполняется одним оператором с проверкой
/// статуса 'Pending', поэтому побеждает ровно один конкурент.
/// </summary>
public sealed class SqliteHitlApprovalRepository : RepositoryBase, IHitlApprovalRepository
{
    /// <summary>Создаёт репозиторий HITL-подтверждений поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции репозиториев; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public SqliteHitlApprovalRepository(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger? logger = null,
        UnitOfWorkMetrics? metrics = null)
        : base(dbFactory, options, logger, metrics)
    {
    }

    /// <summary>
    /// Обеспечивает существование таблицы chain_hitl_approvals: схема покрыта EF-миграцией,
    /// поэтому метод доводит базу до актуального состояния миграций (идемпотентно).
    /// </summary>
    public Task EnsureTableCreatedAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync("hitl_approvals.ensure_table", async (db, token) =>
        {
            await EfBaseline.EnsureBaselineAsync(db, token).ConfigureAwait(false);
            await db.Database.MigrateAsync(token).ConfigureAwait(false);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<HitlApprovalRecord> CreateAsync(HitlApprovalRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return ExecuteAsync("hitl_approvals.create", async (db, token) =>
        {
            string key = GuidKey(record.ApprovalId);
            HitlEntity? existing = await db.HitlApprovals
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            var entity = new HitlEntity
            {
                Id = key,
                RunId = GuidKey(record.RunId.Value),
                ChainId = GuidKey(record.ChainId.Value),
                Title = record.Title,
                PreviewText = record.PreviewText,
                PayloadJson = record.PayloadJson,
                AttachFilesJson = JsonSerializer.Serialize(record.AttachFiles),
                Status = record.Status.ToString(),
                ResolutionJson = record.Resolution is null ? null : JsonSerializer.Serialize(record.Resolution),
                CreatedAt = record.CreatedAtUtc,
                ExpiresAt = record.ExpiresAtUtc,
            };
            if (existing is null)
            {
                db.HitlApprovals.Add(entity);
            }
            else
            {
                existing.RunId = entity.RunId;
                existing.ChainId = entity.ChainId;
                existing.Title = entity.Title;
                existing.PreviewText = entity.PreviewText;
                existing.PayloadJson = entity.PayloadJson;
                existing.AttachFilesJson = entity.AttachFilesJson;
                existing.Status = entity.Status;
                existing.ResolutionJson = entity.ResolutionJson;
                existing.CreatedAt = entity.CreatedAt;
                existing.ExpiresAt = entity.ExpiresAt;
            }

            await SaveAsync(db, token).ConfigureAwait(false);
            HitlEntity? stored = await db.HitlApprovals
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            return stored is null ? Map(entity) : Map(stored);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<HitlApprovalRecord?> GetAsync(Guid approvalId, CancellationToken cancellationToken = default) =>
        ExecuteAsync<HitlApprovalRecord?>("hitl_approvals.get", async (db, token) =>
        {
            string key = GuidKey(approvalId);
            HitlEntity? entity = await db.HitlApprovals
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<bool> TryResolveAsync(Guid approvalId, HitlApprovalResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        return ExecuteAsync("hitl_approvals.resolve", async (db, token) =>
        {
            string key = GuidKey(approvalId);
            HitlStatus newStatus = result.Decision == HitlDecision.Expired
                ? HitlStatus.Expired
                : HitlStatus.Resolved;
            string resolutionJson = JsonSerializer.Serialize(result);
            int affected = await db.HitlApprovals
                .Where(e => e.Id == key && e.Status == nameof(HitlStatus.Pending))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Status, newStatus.ToString())
                    .SetProperty(e => e.ResolutionJson, resolutionJson), token)
                .ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<HitlApprovalRecord>> ListPendingExpiredAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        ExecuteAsync<IReadOnlyList<HitlApprovalRecord>>("hitl_approvals.list_pending_expired", async (db, token) =>
        {
            List<HitlEntity> entities = await db.HitlApprovals
                .AsNoTracking()
                .Where(e => e.Status == nameof(HitlStatus.Pending) && e.ExpiresAt <= nowUtc)
                .OrderBy(e => e.ExpiresAt)
                .ToListAsync(token).ConfigureAwait(false);
            return entities.Select(Map).ToList();
        }, cancellationToken);

    internal static HitlApprovalRecord Map(HitlEntity entity)
    {
        IReadOnlyList<MessageAttachmentDto>? files = null;
        if (!string.IsNullOrWhiteSpace(entity.AttachFilesJson))
        {
            try
            {
                files = JsonSerializer.Deserialize<IReadOnlyList<MessageAttachmentDto>>(entity.AttachFilesJson);
            }
            catch (JsonException)
            {
                files = null;
            }
        }

        HitlApprovalResult? resolution = null;
        if (!string.IsNullOrWhiteSpace(entity.ResolutionJson))
        {
            try
            {
                resolution = JsonSerializer.Deserialize<HitlApprovalResult>(entity.ResolutionJson);
            }
            catch (JsonException)
            {
                resolution = null;
            }
        }

        HitlStatus status = Enum.TryParse<HitlStatus>(entity.Status, ignoreCase: true, out HitlStatus parsedStatus)
            ? parsedStatus
            : HitlStatus.Pending;

        return new HitlApprovalRecord
        {
            ApprovalId = Guid.Parse(entity.Id),
            RunId = new RunId(Guid.Parse(entity.RunId)),
            ChainId = new ChainId(Guid.Parse(entity.ChainId)),
            Title = entity.Title,
            PreviewText = entity.PreviewText,
            PayloadJson = entity.PayloadJson,
            AttachFiles = files ?? [],
            CreatedAtUtc = entity.CreatedAt,
            ExpiresAtUtc = entity.ExpiresAt,
            Status = status,
            Resolution = resolution,
        };
    }
}

using System.Text.Json;
using CyberPipe.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>
/// Хранилище HITL-подтверждений (таблица chain_hitl_approvals) на EF Core.
/// Атомарное разрешение запросов: UPDATE выполняется одним оператором с проверкой
/// статуса 'Pending', поэтому побеждает ровно один конкурент. Сырых SQL-строк нет.
/// </summary>
public sealed class HitlApprovalStoreService
{
    private readonly IDbContextFactory<CyberPipeDbContext> _dbFactory;
    private readonly RepositoryOptions _options;
    private readonly ILogger _logger;
    private readonly UnitOfWorkMetrics _metrics;

    /// <summary>Создаёт хранилище HITL-подтверждений поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public HitlApprovalStoreService(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger<HitlApprovalStoreService>? logger = null,
        UnitOfWorkMetrics? metrics = null)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _options = options ?? new RepositoryOptions();
        IReadOnlyList<string> errors = _options.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(options));
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _metrics = metrics ?? new UnitOfWorkMetrics();
    }

    /// <summary>
    /// Обеспечивает существование таблицы chain_hitl_approvals: схема покрыта EF-миграцией,
    /// поэтому метод доводит базу до актуального состояния миграций (идемпотентно).
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача подготовки таблицы.</returns>
    public Task EnsureTableCreatedAsync(CancellationToken cancellationToken = default) =>
        EfServiceHelper.ExecuteAsync(_dbFactory, _options, _logger, _metrics, "hitl_approvals.ensure_table", async (db, token) =>
        {
            await EfBaseline.EnsureBaselineAsync(db, token).ConfigureAwait(false);
            await db.Database.MigrateAsync(token).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>Создаёт запрос подтверждения; повтор того же идентификатора перезаписывает поля.</summary>
    /// <param name="record">Запись подтверждения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённая запись.</returns>
    public Task<HitlApprovalRecord> CreateAsync(HitlApprovalRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return ExecuteAsync("hitl_approvals.create", async (db, token) =>
        {
            string key = EfServiceHelper.GuidKey(record.ApprovalId);
            HitlEntity? existing = await db.HitlApprovals
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            var entity = new HitlEntity
            {
                Id = key,
                RunId = EfServiceHelper.GuidKey(record.RunId.Value),
                ChainId = EfServiceHelper.GuidKey(record.ChainId.Value),
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

            await db.SaveChangesAsync(token).ConfigureAwait(false);
            HitlEntity? stored = await db.HitlApprovals
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            return stored is null ? Map(entity) : Map(stored);
        }, cancellationToken);
    }

    /// <summary>Возвращает подтверждение; отсутствие — null.</summary>
    /// <param name="approvalId">Идентификатор подтверждения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Подтверждение или null.</returns>
    public Task<HitlApprovalRecord?> GetAsync(Guid approvalId, CancellationToken cancellationToken = default) =>
        ExecuteAsync<HitlApprovalRecord?>("hitl_approvals.get", async (db, token) =>
        {
            string key = EfServiceHelper.GuidKey(approvalId);
            HitlEntity? entity = await db.HitlApprovals
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <summary>
    /// Атомарно разрешает подтверждение одним условным UPDATE вне транзакций:
    /// побеждает ровно один конкурент, остальные получают false.
    /// </summary>
    /// <param name="approvalId">Идентификатор подтверждения.</param>
    /// <param name="result">Результат решения владельца.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если статус перешёл из Pending в разрешённый.</returns>
    public Task<bool> TryResolveAsync(Guid approvalId, HitlApprovalResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        return ExecuteAsync("hitl_approvals.resolve", async (db, token) =>
        {
            string key = EfServiceHelper.GuidKey(approvalId);
            HitlStatus newStatus = result.Decision == HitlDecision.Expired
                ? HitlStatus.Expired
                : HitlStatus.Resolved;
            string resolutionJson = JsonSerializer.Serialize(result);
            // При решении Edited нагрузка подтверждения заменяется отредактированной,
            // как делал предыдущий репозиторий: чтения GetAsync видят итог.
            string? editedPayload = result.Decision == HitlDecision.Edited
                ? result.EditedPayloadJson
                : null;
            int affected = await db.HitlApprovals
                .Where(e => e.Id == key && e.Status == nameof(HitlStatus.Pending))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Status, newStatus.ToString())
                    .SetProperty(e => e.ResolutionJson, resolutionJson)
                    .SetProperty(e => e.PayloadJson, e => editedPayload ?? e.PayloadJson), token)
                .ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);
    }

    /// <summary>Возвращает ожидающие подтверждения с истёкшим сроком, по возрастанию срока.</summary>
    /// <param name="nowUtc">Текущий момент (UTC).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Просроченные ожидающие подтверждения.</returns>
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

    private Task<T> ExecuteAsync<T>(
        string operation,
        Func<CyberPipeDbContext, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken) =>
        EfServiceHelper.ExecuteAsync(_dbFactory, _options, _logger, _metrics, operation, body, cancellationToken);
}

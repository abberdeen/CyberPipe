using CyberPipe.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>Репозиторий контрольных точек на EF Core; идемпотентность по (run_id, step).</summary>
public sealed class CheckpointRepository : RepositoryBase, ICheckpointRepository
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["created_at"] = nameof(CheckpointEntity.CreatedAt),
        ["updated_at"] = nameof(CheckpointEntity.UpdatedAt),
        ["checkpoint_at"] = nameof(CheckpointEntity.CheckpointAt),
        ["step"] = nameof(CheckpointEntity.Step),
    };

    /// <summary>Создаёт репозиторий контрольных точек поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции репозиториев; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public CheckpointRepository(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger? logger = null,
        UnitOfWorkMetrics? metrics = null)
        : base(dbFactory, options, logger, metrics)
    {
    }

    /// <inheritdoc />
    public Task<CheckpointRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync<CheckpointRecord?>("checkpoints.get", async (db, token) =>
        {
            string key = GuidKey(id);
            CheckpointEntity? entity = await db.Checkpoints
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<CheckpointRecord?> GetByRunAndStepAsync(RunId runId, ChainStepKind step, CancellationToken cancellationToken = default) =>
        ExecuteAsync<CheckpointRecord?>("checkpoints.get_by_run_step", async (db, token) =>
        {
            string runKey = GuidKey(runId.Value);
            string stepKey = step.ToString();
            CheckpointEntity? entity = await db.Checkpoints
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.RunId == runKey && e.Step == stepKey, token)
                .ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<CheckpointRecord>> ListAsync(PagedQuery query, RunId? runId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("checkpoints.list", async (db, token) =>
        {
            string sort = ResolveSortKey(query, Sorts);
            string? runKey = runId is null ? null : GuidKey(runId.Value.Value);
            IQueryable<CheckpointEntity> filtered = db.Checkpoints
                .AsNoTracking()
                .Where(e => runKey == null || e.RunId == runKey);
            IQueryable<CheckpointEntity> ordered = ApplyOrdering(filtered, sort, query.Descending, nameof(CheckpointEntity.Id));
            long total = await filtered.LongCountAsync(token).ConfigureAwait(false);
            List<CheckpointEntity> entities = await ordered
                .Skip(query.Offset).Take(query.Limit)
                .ToListAsync(token).ConfigureAwait(false);
            return new PagedResult<CheckpointRecord>
            {
                Items = entities.Select(Map).ToList(),
                TotalCount = total,
                Offset = query.Offset,
                Limit = query.Limit,
            };
        }, cancellationToken);

    /// <inheritdoc />
    public Task<CheckpointRecord> AddAsync(ChainCheckpointDto checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.AttemptCount < 0)
            throw new ArgumentException("AttemptCount must not be negative.", nameof(checkpoint));
        string? input = checkpoint.InputJson is null ? null : RepositoryJson.EnsureJson(checkpoint.InputJson, nameof(checkpoint.InputJson));
        string? output = checkpoint.OutputJson is null ? null : RepositoryJson.EnsureJson(checkpoint.OutputJson, nameof(checkpoint.OutputJson));
        return ExecuteAsync("checkpoints.add", async (db, token) =>
        {
            string runKey = GuidKey(checkpoint.RunId.Value);
            string stepKey = checkpoint.Step.ToString();
            CheckpointEntity? existing = await db.Checkpoints
                .FirstOrDefaultAsync(e => e.RunId == runKey && e.Step == stepKey, token)
                .ConfigureAwait(false);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset checkpointAt = checkpoint.CheckpointAtUtc == default ? now : checkpoint.CheckpointAtUtc;
            string? errorJson = checkpoint.Error is null ? null : RepositoryJson.Serialize(checkpoint.Error);
            if (existing is not null)
            {
                existing.State = checkpoint.State.ToString();
                existing.AttemptCount = checkpoint.AttemptCount;
                existing.InputJson = input;
                existing.OutputJson = output;
                existing.ErrorJson = errorJson;
                existing.CheckpointAt = checkpointAt;
                existing.UpdatedAt = now;
                await SaveAsync(db, token).ConfigureAwait(false);
                return Map(existing);
            }

            var entity = new CheckpointEntity
            {
                Id = Guid.NewGuid().ToString("D"),
                RunId = runKey,
                ChainId = GuidKey(checkpoint.ChainId.Value),
                Step = stepKey,
                State = checkpoint.State.ToString(),
                AttemptCount = checkpoint.AttemptCount,
                InputJson = input,
                OutputJson = output,
                ErrorJson = errorJson,
                CheckpointAt = checkpointAt,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Checkpoints.Add(entity);
            try
            {
                await SaveAsync(db, token).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                CheckpointEntity? raced = await db.Checkpoints
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.RunId == runKey && e.Step == stepKey, token)
                    .ConfigureAwait(false);
                if (raced is null)
                    throw;
                return Map(raced);
            }

            return Map(entity);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CheckpointRecord> UpdateAsync(CheckpointRecord record, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Checkpoint);
        ChainCheckpointDto checkpoint = record.Checkpoint;
        string? input = checkpoint.InputJson is null ? null : RepositoryJson.EnsureJson(checkpoint.InputJson, nameof(checkpoint.InputJson));
        string? output = checkpoint.OutputJson is null ? null : RepositoryJson.EnsureJson(checkpoint.OutputJson, nameof(checkpoint.OutputJson));
        return ExecuteAsync("checkpoints.update", async (db, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string key = GuidKey(record.Id);
            int affected = await db.Checkpoints
                .Where(e => e.Id == key && e.UpdatedAt == expectedRowVersion)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.State, checkpoint.State.ToString())
                    .SetProperty(e => e.AttemptCount, checkpoint.AttemptCount)
                    .SetProperty(e => e.InputJson, input)
                    .SetProperty(e => e.OutputJson, output)
                    .SetProperty(e => e.ErrorJson, checkpoint.Error == null ? null : RepositoryJson.Serialize(checkpoint.Error))
                    .SetProperty(e => e.CheckpointAt, checkpoint.CheckpointAtUtc)
                    .SetProperty(e => e.UpdatedAt, now), token).ConfigureAwait(false);
            if (affected == 0)
                throw new OptimisticConcurrencyException("chain_checkpoints", record.Id.ToString());
            CheckpointEntity? updated = await db.Checkpoints
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            return updated is null
                ? record with { RowVersion = now }
                : Map(updated);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("checkpoints.delete", async (db, token) =>
        {
            string key = GuidKey(id);
            int affected = await db.Checkpoints
                .Where(e => e.Id == key)
                .ExecuteDeleteAsync(token).ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);

    internal static CheckpointRecord Map(CheckpointEntity entity) => new()
    {
        Id = Guid.Parse(entity.Id),
        Checkpoint = new ChainCheckpointDto
        {
            RunId = new RunId(Guid.Parse(entity.RunId)),
            ChainId = new ChainId(Guid.Parse(entity.ChainId)),
            Step = Enum.Parse<ChainStepKind>(entity.Step),
            State = Enum.Parse<ChainStepState>(entity.State),
            AttemptCount = entity.AttemptCount,
            InputJson = entity.InputJson,
            OutputJson = entity.OutputJson,
            Error = entity.ErrorJson is null ? null : RepositoryJson.Deserialize<ErrorDto>(entity.ErrorJson),
            CheckpointAtUtc = entity.CheckpointAt,
        },
        CreatedAtUtc = entity.CreatedAt,
        RowVersion = entity.UpdatedAt,
    };
}

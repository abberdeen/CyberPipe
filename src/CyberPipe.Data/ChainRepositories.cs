using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CyberPipe.Data;

/// <summary>Репозиторий app.chains: определение цепочки целиком в definition_json, колонки — проекция для поиска.</summary>
public sealed class ChainRepository : RepositoryBase, IChainRepository, IChainDefinitionRepository
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["created_at"] = "created_at",
        ["updated_at"] = "updated_at",
        ["name"] = "name",
        ["version"] = "version",
    };

    private const string SelectColumns = "definition_json, updated_at";

    /// <summary>Создаёт репозиторий цепочек.</summary>
    /// <param name="connectionFactory">Фабрика соединений TASK-010.</param>
    /// <param name="options">Опции репозиториев.</param>
    /// <param name="logger">Логгер медленных запросов.</param>
    /// <param name="metrics">Общие счётчики.</param>
    public ChainRepository(IPostgresConnectionFactory connectionFactory, RepositoryOptions? options = null, ILogger? logger = null, UnitOfWorkMetrics? metrics = null)
        : base(connectionFactory, options, logger, metrics)
    {
    }

    /// <inheritdoc />
    public Task<ChainDefinitionDto?> GetAsync(ChainId id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("chains.get", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, $"SELECT {SelectColumns} FROM app.chains WHERE id = @id");
            Param(command, "id", id.Value);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                return null;
            return Map(reader);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<ChainDefinitionDto>> ListAsync(PagedQuery query, CancellationToken cancellationToken = default) =>
        ExecuteAsync("chains.list", async (connection, transaction, token) =>
        {
            string orderBy = OrderBy(query, Sorts, "id");
            await using var countCommand = Command(connection, transaction, "SELECT count(*) FROM app.chains");
            long total = Convert.ToInt64(await countCommand.ExecuteScalarAsync(token).ConfigureAwait(false));

            await using var command = Command(connection, transaction,
                $"SELECT {SelectColumns} FROM app.chains ORDER BY {orderBy} LIMIT @limit OFFSET @offset");
            Param(command, "limit", query.Limit);
            Param(command, "offset", query.Offset);
            var items = new List<ChainDefinitionDto>();
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                items.Add(Map(reader));
            return new PagedResult<ChainDefinitionDto> { Items = items, TotalCount = total, Offset = query.Offset, Limit = query.Limit };
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ChainDefinitionDto> AddAsync(ChainDefinitionDto chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chain);
        RequireText(chain.Name, nameof(chain.Name));
        return ExecuteAsync("chains.add", async (connection, transaction, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset created = chain.CreatedAtUtc == default ? now : chain.CreatedAtUtc;
            DateTimeOffset updated = chain.UpdatedAtUtc == default ? created : chain.UpdatedAtUtc;
            string json = RepositoryJson.Serialize(chain with { CreatedAtUtc = created, UpdatedAtUtc = updated });
            await using var command = Command(connection, transaction, """
                INSERT INTO app.chains (id, name, definition_json, is_enabled, requires_hitl, version, created_at, updated_at)
                VALUES (@id, @name, @definition, @is_enabled, @requires_hitl, @version, @created_at, @updated_at)
                ON CONFLICT (id) DO NOTHING
                RETURNING updated_at
                """);
            Param(command, "id", chain.Id.Value);
            Param(command, "name", chain.Name);
            JsonParam(command, "definition", json);
            Param(command, "is_enabled", chain.IsEnabled);
            Param(command, "requires_hitl", chain.RequiresHitl);
            Param(command, "version", chain.Version);
            TimeParam(command, "created_at", created);
            TimeParam(command, "updated_at", updated);
            object? inserted = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
            if (inserted is not null)
                return chain with { CreatedAtUtc = created, UpdatedAtUtc = updated };
            // AlreadyExists: существующая запись возвращается без перезаписи.
            return await GetAsync(chain.Id, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Chain disappeared after conflict.");
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ChainDefinitionDto> UpdateAsync(ChainDefinitionDto chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chain);
        RequireText(chain.Name, nameof(chain.Name));
        return ExecuteAsync("chains.update", async (connection, transaction, token) =>
        {
            string json = RepositoryJson.Serialize(chain);
            await using var command = Command(connection, transaction, """
                UPDATE app.chains
                SET name = @name, definition_json = @definition, is_enabled = @is_enabled,
                    requires_hitl = @requires_hitl, version = @version, updated_at = now()
                WHERE id = @id
                RETURNING updated_at
                """);
            Param(command, "id", chain.Id.Value);
            Param(command, "name", chain.Name);
            JsonParam(command, "definition", json);
            Param(command, "is_enabled", chain.IsEnabled);
            Param(command, "requires_hitl", chain.RequiresHitl);
            Param(command, "version", chain.Version);
            object? result = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
            if (result is null)
                throw new OptimisticConcurrencyException("chains", chain.Id.ToString());
            DateTimeOffset updatedAt = result is DateTimeOffset dto
                ? dto
                : new DateTimeOffset(DateTime.SpecifyKind((DateTime)result, DateTimeKind.Utc));
            return chain with { UpdatedAtUtc = updatedAt };
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(ChainId id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("chains.delete", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, "DELETE FROM app.chains WHERE id = @id");
            Param(command, "id", id.Value);
            return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0;
        }, cancellationToken);

    /// <inheritdoc />
    Task<ChainDefinitionDto?> IChainDefinitionRepository.GetByIdAsync(ChainId id, CancellationToken cancellationToken) =>
        GetAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ChainDefinitionDto>> ListEnabledAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync("chains.list_enabled", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction,
                $"SELECT {SelectColumns} FROM app.chains WHERE is_enabled = true ORDER BY name");
            var items = new List<ChainDefinitionDto>();
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                items.Add(Map(reader));
            return (IReadOnlyList<ChainDefinitionDto>)items;
        }, cancellationToken);

    /// <inheritdoc />
    public async Task<ChainDefinitionDto> UpsertAsync(ChainDefinitionDto definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ChainDefinitionDto? existing = await GetAsync(definition.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return await AddAsync(definition, cancellationToken).ConfigureAwait(false);
        }

        int nextVersion = existing.IsEnabled ? existing.Version + 1 : existing.Version;
        return await UpdateAsync(definition with { Version = nextVersion }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> SetEnabledAsync(ChainId id, bool isEnabled, CancellationToken cancellationToken = default) =>
        ExecuteAsync("chains.set_enabled", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction,
                "UPDATE app.chains SET is_enabled = @is_enabled, updated_at = now() WHERE id = @id");
            Param(command, "id", id.Value);
            Param(command, "is_enabled", isEnabled);
            return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0;
        }, cancellationToken);

    internal static ChainDefinitionDto Map(NpgsqlDataReader reader)
    {
        ChainDefinitionDto dto = RepositoryJson.Deserialize<ChainDefinitionDto>(reader.GetString(0));
        DateTimeOffset updatedAt = ReadRequiredTime(reader, 1);
        return dto.UpdatedAtUtc == updatedAt ? dto : dto with { UpdatedAtUtc = updatedAt };
    }
}

/// <summary>Репозиторий app.chain_runs с идемпотентностью по (chain_id, idempotency_key).</summary>
public sealed class ChainRunRepository : RepositoryBase, IChainRunRepository
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["created_at"] = "created_at",
        ["updated_at"] = "updated_at",
        ["started_at"] = "started_at",
        ["status"] = "status",
    };

    private const string Columns =
        "id, chain_id, trigger_event_id, idempotency_key, status, source, payload_json, started_at, finished_at, error_json, created_at, updated_at";

    /// <summary>Создаёт репозиторий запусков.</summary>
    /// <param name="connectionFactory">Фабрика соединений TASK-010.</param>
    /// <param name="options">Опции репозиториев.</param>
    /// <param name="logger">Логгер медленных запросов.</param>
    /// <param name="metrics">Общие счётчики.</param>
    public ChainRunRepository(IPostgresConnectionFactory connectionFactory, RepositoryOptions? options = null, ILogger? logger = null, UnitOfWorkMetrics? metrics = null)
        : base(connectionFactory, options, logger, metrics)
    {
    }

    /// <inheritdoc />
    public Task<ChainRunRecord?> GetAsync(RunId id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("chain_runs.get", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, $"SELECT {Columns} FROM app.chain_runs WHERE id = @id");
            Param(command, "id", id.Value);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false) ? Map(reader) : null;
        }, cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<ChainRunRecord>> ListAsync(PagedQuery query, ChainId? chainId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("chain_runs.list", async (connection, transaction, token) =>
        {
            string orderBy = OrderBy(query, Sorts, "id");
            const string where = "WHERE (@chain_id IS NULL OR chain_id = @chain_id)";
            await using var countCommand = Command(connection, transaction, $"SELECT count(*) FROM app.chain_runs {where}");
            UuidParam(countCommand, "chain_id", chainId?.Value);
            long total = Convert.ToInt64(await countCommand.ExecuteScalarAsync(token).ConfigureAwait(false));

            await using var command = Command(connection, transaction,
                $"SELECT {Columns} FROM app.chain_runs {where} ORDER BY {orderBy} LIMIT @limit OFFSET @offset");
            UuidParam(command, "chain_id", chainId?.Value);
            Param(command, "limit", query.Limit);
            Param(command, "offset", query.Offset);
            var items = new List<ChainRunRecord>();
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                items.Add(Map(reader));
            return new PagedResult<ChainRunRecord> { Items = items, TotalCount = total, Offset = query.Offset, Limit = query.Limit };
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ChainRunRecord> AddAsync(ChainRunRecord run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        RequireText(run.IdempotencyKey, nameof(run.IdempotencyKey));
        RequireText(run.Source, nameof(run.Source));
        string payload = RepositoryJson.EnsureJson(run.PayloadJson, nameof(run.PayloadJson));
        return ExecuteAsync("chain_runs.add", async (connection, transaction, token) =>
        {
            Guid id = run.Id.Value == Guid.Empty ? Guid.NewGuid() : run.Id.Value;
            DateTimeOffset started = run.StartedAtUtc == default ? DateTimeOffset.UtcNow : run.StartedAtUtc;
            await using var command = Command(connection, transaction, $"""
                INSERT INTO app.chain_runs (id, chain_id, trigger_event_id, idempotency_key, status, source, payload_json, started_at, finished_at, error_json)
                VALUES (@id, @chain_id, @trigger_event_id, @idempotency_key, @status, @source, @payload, @started_at, @finished_at, @error)
                ON CONFLICT (chain_id, idempotency_key) DO NOTHING
                RETURNING {Columns}
                """);
            Param(command, "id", id);
            Param(command, "chain_id", run.ChainId.Value);
            Param(command, "trigger_event_id", run.TriggerEventId);
            Param(command, "idempotency_key", run.IdempotencyKey);
            Param(command, "status", run.Status.ToString());
            Param(command, "source", run.Source);
            JsonParam(command, "payload", payload);
            TimeParam(command, "started_at", started);
            TimeParam(command, "finished_at", run.FinishedAtUtc);
            JsonParam(command, "error", run.Error is null ? null : RepositoryJson.Serialize(run.Error));
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(token).ConfigureAwait(false))
                    return Map(reader);
            }

            // AlreadyExists по идемпотентному ключу: вернуть существующий запуск.
            await using var select = Command(connection, transaction,
                $"SELECT {Columns} FROM app.chain_runs WHERE chain_id = @chain_id AND idempotency_key = @idempotency_key");
            Param(select, "chain_id", run.ChainId.Value);
            Param(select, "idempotency_key", run.IdempotencyKey);
            await using NpgsqlDataReader existing = await select.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (await existing.ReadAsync(token).ConfigureAwait(false))
                return Map(existing);
            throw new InvalidOperationException("Chain run disappeared after idempotency conflict.");
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ChainRunRecord> UpdateAsync(ChainRunRecord run, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        string payload = RepositoryJson.EnsureJson(run.PayloadJson, nameof(run.PayloadJson));
        return ExecuteAsync("chain_runs.update", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, $"""
                UPDATE app.chain_runs
                SET status = @status, source = @source, payload_json = @payload, finished_at = @finished_at,
                    error_json = @error, updated_at = now()
                WHERE id = @id AND updated_at = @expected_version
                RETURNING {Columns}
                """);
            Param(command, "id", run.Id.Value);
            Param(command, "status", run.Status.ToString());
            Param(command, "source", run.Source);
            JsonParam(command, "payload", payload);
            TimeParam(command, "finished_at", run.FinishedAtUtc);
            JsonParam(command, "error", run.Error is null ? null : RepositoryJson.Serialize(run.Error));
            TimeParam(command, "expected_version", expectedRowVersion);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
                return Map(reader);
            throw new OptimisticConcurrencyException("chain_runs", run.Id.ToString());
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(RunId id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("chain_runs.delete", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, "DELETE FROM app.chain_runs WHERE id = @id");
            Param(command, "id", id.Value);
            return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0;
        }, cancellationToken);

    internal static ChainRunRecord Map(NpgsqlDataReader reader) => new()
    {
        Id = new RunId(reader.GetGuid(0)),
        ChainId = new ChainId(reader.GetGuid(1)),
        TriggerEventId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
        IdempotencyKey = reader.GetString(3),
        Status = Enum.Parse<ChainRunStatus>(reader.GetString(4)),
        Source = reader.GetString(5),
        PayloadJson = reader.GetString(6),
        StartedAtUtc = ReadRequiredTime(reader, 7),
        FinishedAtUtc = ReadTime(reader, 8),
        Error = reader.IsDBNull(9) ? null : RepositoryJson.Deserialize<ErrorDto>(reader.GetString(9)),
        CreatedAtUtc = ReadRequiredTime(reader, 10),
        RowVersion = ReadRequiredTime(reader, 11),
    };
}

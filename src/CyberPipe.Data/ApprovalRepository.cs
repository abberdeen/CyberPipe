using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CyberPipe.Data;

/// <summary>Маппинг статуса одобрения в текстовую колонку app.approvals.status.</summary>
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

/// <summary>Репозиторий app.approvals с оптимистичной конкуренцией по updated_at.</summary>
public sealed class ApprovalRepository : RepositoryBase, IApprovalRepository
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["created_at"] = "created_at",
        ["updated_at"] = "updated_at",
        ["expires_at"] = "expires_at",
        ["status"] = "status",
    };

    private const string Columns =
        "id, run_id, chain_id, step, status, payload_json, decision, decided_at, expires_at, created_at, updated_at";

    /// <summary>Создаёт репозиторий одобрений.</summary>
    /// <param name="connectionFactory">Фабрика соединений TASK-010.</param>
    /// <param name="options">Опции репозиториев.</param>
    /// <param name="logger">Логгер медленных запросов.</param>
    /// <param name="metrics">Общие счётчики.</param>
    public ApprovalRepository(IPostgresConnectionFactory connectionFactory, RepositoryOptions? options = null, ILogger? logger = null, UnitOfWorkMetrics? metrics = null)
        : base(connectionFactory, options, logger, metrics)
    {
    }

    /// <inheritdoc />
    public Task<ApprovalRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("approvals.get", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, $"SELECT {Columns} FROM app.approvals WHERE id = @id");
            Param(command, "id", id);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false) ? Map(reader) : null;
        }, cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<ApprovalRecord>> ListAsync(PagedQuery query, ApprovalStatus? status = null, RunId? runId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("approvals.list", async (connection, transaction, token) =>
        {
            string orderBy = OrderBy(query, Sorts, "id");
            const string where = "WHERE (@status IS NULL OR status = @status) AND (@run_id IS NULL OR run_id = @run_id)";
            await using var countCommand = Command(connection, transaction, $"SELECT count(*) FROM app.approvals {where}");
            TextParam(countCommand, "status", status is null ? null : ApprovalStatusMapping.ToDbValue(status.Value));
            UuidParam(countCommand, "run_id", runId?.Value);
            long total = Convert.ToInt64(await countCommand.ExecuteScalarAsync(token).ConfigureAwait(false));

            await using var command = Command(connection, transaction,
                $"SELECT {Columns} FROM app.approvals {where} ORDER BY {orderBy} LIMIT @limit OFFSET @offset");
            TextParam(command, "status", status is null ? null : ApprovalStatusMapping.ToDbValue(status.Value));
            UuidParam(command, "run_id", runId?.Value);
            Param(command, "limit", query.Limit);
            Param(command, "offset", query.Offset);
            var items = new List<ApprovalRecord>();
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                items.Add(Map(reader));
            return new PagedResult<ApprovalRecord> { Items = items, TotalCount = total, Offset = query.Offset, Limit = query.Limit };
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ApprovalRecord> AddAsync(ApprovalRecord approval, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        RequireText(approval.Step, nameof(approval.Step));
        string payload = RepositoryJson.EnsureJson(approval.PayloadJson, nameof(approval.PayloadJson));
        return ExecuteAsync("approvals.add", async (connection, transaction, token) =>
        {
            Guid id = approval.Id == Guid.Empty ? Guid.NewGuid() : approval.Id;
            await using var command = Command(connection, transaction, $"""
                INSERT INTO app.approvals (id, run_id, chain_id, step, status, payload_json, decision, decided_at, expires_at)
                VALUES (@id, @run_id, @chain_id, @step, @status, @payload, @decision, @decided_at, @expires_at)
                ON CONFLICT (id) DO NOTHING
                RETURNING {Columns}
                """);
            Param(command, "id", id);
            UuidParam(command, "run_id", approval.RunId?.Value);
            UuidParam(command, "chain_id", approval.ChainId?.Value);
            Param(command, "step", approval.Step);
            Param(command, "status", ApprovalStatusMapping.ToDbValue(approval.Status));
            JsonParam(command, "payload", payload);
            TextParam(command, "decision", approval.Decision?.ToString());
            TimeParam(command, "decided_at", approval.DecidedAtUtc);
            TimeParam(command, "expires_at", approval.ExpiresAtUtc);
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(token).ConfigureAwait(false))
                    return Map(reader);
            }

            return await GetAsync(id, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Approval disappeared after conflict.");
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ApprovalRecord> UpdateAsync(ApprovalRecord approval, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        RequireText(approval.Step, nameof(approval.Step));
        string payload = RepositoryJson.EnsureJson(approval.PayloadJson, nameof(approval.PayloadJson));
        return ExecuteAsync("approvals.update", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, $"""
                UPDATE app.approvals
                SET step = @step, status = @status, payload_json = @payload, decision = @decision,
                    decided_at = @decided_at, expires_at = @expires_at, updated_at = now()
                WHERE id = @id AND updated_at = @expected_version
                RETURNING {Columns}
                """);
            Param(command, "id", approval.Id);
            Param(command, "step", approval.Step);
            Param(command, "status", ApprovalStatusMapping.ToDbValue(approval.Status));
            JsonParam(command, "payload", payload);
            TextParam(command, "decision", approval.Decision?.ToString());
            TimeParam(command, "decided_at", approval.DecidedAtUtc);
            TimeParam(command, "expires_at", approval.ExpiresAtUtc);
            TimeParam(command, "expected_version", expectedRowVersion);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
                return Map(reader);
            throw new OptimisticConcurrencyException("approvals", approval.Id.ToString());
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("approvals.delete", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, "DELETE FROM app.approvals WHERE id = @id");
            Param(command, "id", id);
            return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0;
        }, cancellationToken);

    internal static ApprovalRecord Map(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        RunId = reader.IsDBNull(1) ? null : new RunId(reader.GetGuid(1)),
        ChainId = reader.IsDBNull(2) ? null : new ChainId(reader.GetGuid(2)),
        Step = reader.GetString(3),
        Status = ApprovalStatusMapping.Parse(reader.GetString(4)),
        PayloadJson = reader.GetString(5),
        Decision = reader.IsDBNull(6) ? null : Enum.Parse<HitlDecision>(reader.GetString(6)),
        DecidedAtUtc = ReadTime(reader, 7),
        ExpiresAtUtc = ReadTime(reader, 8),
        CreatedAtUtc = ReadRequiredTime(reader, 9),
        RowVersion = ReadRequiredTime(reader, 10),
    };
}

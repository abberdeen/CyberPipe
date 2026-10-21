using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;

namespace CyberPipe.Data;

/// <summary>Репозиторий chain_checkpoints с идемпотентностью по (run_id, step) и оптимистичной конкуренцией по updated_at.</summary>
public sealed class CheckpointRepository : RepositoryBase, ICheckpointRepository
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["created_at"] = "created_at",
        ["updated_at"] = "updated_at",
        ["checkpoint_at"] = "checkpoint_at",
        ["step"] = "step",
    };

    private const string Columns =
        "id, run_id, chain_id, step, state, attempt_count, input_json, output_json, error_json, checkpoint_at, created_at, updated_at";

    /// <summary>Создаёт репозиторий контрольных точек.</summary>
    /// <param name="connectionFactory">Фабрика соединений TASK-010.</param>
    /// <param name="options">Опции репозиториев.</param>
    /// <param name="logger">Логгер медленных запросов.</param>
    /// <param name="metrics">Общие счётчики.</param>
    public CheckpointRepository(IDatabaseConnectionFactory connectionFactory, RepositoryOptions? options = null, ILogger? logger = null, UnitOfWorkMetrics? metrics = null)
        : base(connectionFactory, options, logger, metrics)
    {
    }

    /// <inheritdoc />
    public Task<CheckpointRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("checkpoints.get", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, $"SELECT {Columns} FROM chain_checkpoints WHERE id = @id");
            Param(command, "id", id);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false) ? Map(reader) : null;
        }, cancellationToken);

    /// <inheritdoc />
    public Task<CheckpointRecord?> GetByRunAndStepAsync(RunId runId, ChainStepKind step, CancellationToken cancellationToken = default) =>
        ExecuteAsync("checkpoints.get_by_run_step", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction,
                $"SELECT {Columns} FROM chain_checkpoints WHERE run_id = @run_id AND step = @step");
            Param(command, "run_id", runId.Value);
            Param(command, "step", step.ToString());
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false) ? Map(reader) : null;
        }, cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<CheckpointRecord>> ListAsync(PagedQuery query, RunId? runId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("checkpoints.list", async (connection, transaction, token) =>
        {
            string orderBy = OrderBy(query, Sorts, "id");
            await using var countCommand = Command(connection, transaction,
                "SELECT count(*) FROM chain_checkpoints WHERE (@run_id IS NULL OR run_id = @run_id)");
            UuidParam(countCommand, "run_id", runId?.Value);
            long total = Convert.ToInt64(await countCommand.ExecuteScalarAsync(token).ConfigureAwait(false));

            await using var command = Command(connection, transaction,
                $"SELECT {Columns} FROM chain_checkpoints WHERE (@run_id IS NULL OR run_id = @run_id) ORDER BY {orderBy} LIMIT @limit OFFSET @offset");
            UuidParam(command, "run_id", runId?.Value);
            Param(command, "limit", query.Limit);
            Param(command, "offset", query.Offset);
            var items = new List<CheckpointRecord>();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                items.Add(Map(reader));
            return new PagedResult<CheckpointRecord> { Items = items, TotalCount = total, Offset = query.Offset, Limit = query.Limit };
        }, cancellationToken);

    /// <inheritdoc />
    public Task<CheckpointRecord> AddAsync(ChainCheckpointDto checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.AttemptCount < 0)
            throw new ArgumentException("AttemptCount must not be negative.", nameof(checkpoint));
        string? input = checkpoint.InputJson is null ? null : RepositoryJson.EnsureJson(checkpoint.InputJson, nameof(checkpoint.InputJson));
        string? output = checkpoint.OutputJson is null ? null : RepositoryJson.EnsureJson(checkpoint.OutputJson, nameof(checkpoint.OutputJson));
        return ExecuteAsync("checkpoints.add", async (connection, transaction, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await using var command = Command(connection, transaction, $"""
                INSERT INTO chain_checkpoints (id, run_id, chain_id, step, state, attempt_count, input_json, output_json, error_json, checkpoint_at, created_at, updated_at)
                VALUES (@id, @run_id, @chain_id, @step, @state, @attempt_count, @input, @output, @error, @checkpoint_at, @now, @now)
                ON CONFLICT (run_id, step) DO UPDATE
                SET state = EXCLUDED.state, attempt_count = EXCLUDED.attempt_count,
                    input_json = EXCLUDED.input_json, output_json = EXCLUDED.output_json,
                    error_json = EXCLUDED.error_json, checkpoint_at = EXCLUDED.checkpoint_at, updated_at = @now
                RETURNING {Columns}
                """);
            TimeParam(command, "now", now);
            Param(command, "id", Guid.NewGuid());
            Param(command, "run_id", checkpoint.RunId.Value);
            Param(command, "chain_id", checkpoint.ChainId.Value);
            Param(command, "step", checkpoint.Step.ToString());
            Param(command, "state", checkpoint.State.ToString());
            Param(command, "attempt_count", checkpoint.AttemptCount);
            JsonParam(command, "input", input);
            JsonParam(command, "output", output);
            JsonParam(command, "error", checkpoint.Error is null ? null : RepositoryJson.Serialize(checkpoint.Error));
            TimeParam(command, "checkpoint_at", checkpoint.CheckpointAtUtc == default ? DateTimeOffset.UtcNow : checkpoint.CheckpointAtUtc);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            await reader.ReadAsync(token).ConfigureAwait(false);
            return Map(reader);
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
        return ExecuteAsync("checkpoints.update", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, $"""
                UPDATE chain_checkpoints
                SET state = @state, attempt_count = @attempt_count, input_json = @input, output_json = @output,
                    error_json = @error, checkpoint_at = @checkpoint_at, updated_at = @now
                WHERE id = @id AND updated_at = @expected_version
                RETURNING {Columns}
                """);
            TimeParam(command, "now", DateTimeOffset.UtcNow);
            Param(command, "id", record.Id);
            Param(command, "state", checkpoint.State.ToString());
            Param(command, "attempt_count", checkpoint.AttemptCount);
            JsonParam(command, "input", input);
            JsonParam(command, "output", output);
            JsonParam(command, "error", checkpoint.Error is null ? null : RepositoryJson.Serialize(checkpoint.Error));
            TimeParam(command, "checkpoint_at", checkpoint.CheckpointAtUtc);
            TimeParam(command, "expected_version", expectedRowVersion);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
                return Map(reader);
            throw new OptimisticConcurrencyException("chain_checkpoints", record.Id.ToString());
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("checkpoints.delete", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, "DELETE FROM chain_checkpoints WHERE id = @id");
            Param(command, "id", id);
            return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0;
        }, cancellationToken);

    internal static CheckpointRecord Map(SqliteDataReader reader)
    {
        var dto = new ChainCheckpointDto
        {
            RunId = new RunId(ReadGuid(reader, 1)),
            ChainId = new ChainId(ReadGuid(reader, 2)),
            Step = Enum.Parse<ChainStepKind>(reader.GetString(3)),
            State = Enum.Parse<ChainStepState>(reader.GetString(4)),
            AttemptCount = reader.GetInt32(5),
            InputJson = reader.IsDBNull(6) ? null : reader.GetString(6),
            OutputJson = reader.IsDBNull(7) ? null : reader.GetString(7),
            Error = reader.IsDBNull(8) ? null : RepositoryJson.Deserialize<ErrorDto>(reader.GetString(8)),
            CheckpointAtUtc = ReadRequiredTime(reader, 9),
        };
        return new CheckpointRecord
        {
            Id = ReadGuid(reader, 0),
            Checkpoint = dto,
            CreatedAtUtc = ReadRequiredTime(reader, 10),
            RowVersion = ReadRequiredTime(reader, 11),
        };
    }

}

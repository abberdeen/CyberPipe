using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;

namespace CyberPipe.Data;

/// <summary>Репозиторий token_budget_ledger с оптимистичной конкуренцией по updated_at.</summary>
public sealed class TokenBudgetRepository : RepositoryBase, ITokenBudgetRepository
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["created_at"] = "created_at",
        ["updated_at"] = "updated_at",
        ["budget_date"] = "budget_date",
        ["cost_usd"] = "cost_usd",
    };

    private const string Columns =
        "id, budget_date, model_id, hosting_type, entry_type, request_id, input_tokens, output_tokens, cost_usd, created_at, updated_at";

    /// <summary>Создаёт репозиторий книги бюджета.</summary>
    /// <param name="connectionFactory">Фабрика соединений TASK-010.</param>
    /// <param name="options">Опции репозиториев.</param>
    /// <param name="logger">Логгер медленных запросов.</param>
    /// <param name="metrics">Общие счётчики.</param>
    public TokenBudgetRepository(IDatabaseConnectionFactory connectionFactory, RepositoryOptions? options = null, ILogger? logger = null, UnitOfWorkMetrics? metrics = null)
        : base(connectionFactory, options, logger, metrics)
    {
    }

    /// <inheritdoc />
    public Task<TokenBudgetEntryRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("token_budget.get", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, $"SELECT {Columns} FROM token_budget_ledger WHERE id = @id");
            Param(command, "id", id);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false) ? Map(reader) : null;
        }, cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<TokenBudgetEntryRecord>> ListAsync(PagedQuery query, DateOnly? budgetDate = null, string? modelId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("token_budget.list", async (connection, transaction, token) =>
        {
            string orderBy = OrderBy(query, Sorts, "id");
            const string where = "WHERE (@budget_date IS NULL OR budget_date = @budget_date) AND (@model_id IS NULL OR model_id = @model_id)";
            await using var countCommand = Command(connection, transaction, $"SELECT count(*) FROM token_budget_ledger {where}");
            DateParam(countCommand, "budget_date", budgetDate);
            TextParam(countCommand, "model_id", modelId);
            long total = Convert.ToInt64(await countCommand.ExecuteScalarAsync(token).ConfigureAwait(false));

            await using var command = Command(connection, transaction,
                $"SELECT {Columns} FROM token_budget_ledger {where} ORDER BY {orderBy} LIMIT @limit OFFSET @offset");
            DateParam(command, "budget_date", budgetDate);
            TextParam(command, "model_id", modelId);
            Param(command, "limit", query.Limit);
            Param(command, "offset", query.Offset);
            var items = new List<TokenBudgetEntryRecord>();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                items.Add(Map(reader));
            return new PagedResult<TokenBudgetEntryRecord> { Items = items, TotalCount = total, Offset = query.Offset, Limit = query.Limit };
        }, cancellationToken);

    /// <inheritdoc />
    public Task<TokenBudgetEntryRecord> AddAsync(TokenBudgetEntryRecord entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        RequireText(entry.ModelId, nameof(entry.ModelId));
        RequireText(entry.EntryType, nameof(entry.EntryType));
        if (entry.InputTokens < 0 || entry.OutputTokens < 0 || entry.CostUsd < 0)
            throw new ArgumentException("Token counts and cost must not be negative.", nameof(entry));
        return ExecuteAsync("token_budget.add", async (connection, transaction, token) =>
        {
            Guid id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await using var command = Command(connection, transaction, $"""
                INSERT INTO token_budget_ledger (id, budget_date, model_id, hosting_type, entry_type, request_id, input_tokens, output_tokens, cost_usd, created_at, updated_at)
                VALUES (@id, @budget_date, @model_id, @hosting_type, @entry_type, @request_id, @input_tokens, @output_tokens, @cost_usd, @now, @now)
                ON CONFLICT (id) DO NOTHING
                RETURNING {Columns}
                """);
            TimeParam(command, "now", now);
            Param(command, "id", id);
            DateParam(command, "budget_date", entry.BudgetDate);
            Param(command, "model_id", entry.ModelId);
            Param(command, "hosting_type", entry.HostingType.ToString());
            Param(command, "entry_type", entry.EntryType);
            TextParam(command, "request_id", entry.RequestId);
            Param(command, "input_tokens", entry.InputTokens);
            Param(command, "output_tokens", entry.OutputTokens);
            Param(command, "cost_usd", entry.CostUsd);
            await using (SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(token).ConfigureAwait(false))
                    return Map(reader);
            }

            return await GetAsync(id, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Budget entry disappeared after conflict.");
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TokenBudgetEntryRecord> AddUsageAsync(TokenUsageDto usage, CancellationToken cancellationToken = default) =>
        AddAsync(TokenBudgetEntryRecord.FromUsage(usage), cancellationToken);

    /// <inheritdoc />
    public Task<TokenBudgetEntryRecord> UpdateAsync(TokenBudgetEntryRecord entry, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ExecuteAsync("token_budget.update", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, $"""
                UPDATE token_budget_ledger
                SET budget_date = @budget_date, model_id = @model_id, hosting_type = @hosting_type, entry_type = @entry_type,
                    request_id = @request_id, input_tokens = @input_tokens, output_tokens = @output_tokens, cost_usd = @cost_usd, updated_at = @now
                WHERE id = @id AND updated_at = @expected_version
                RETURNING {Columns}
                """);
            TimeParam(command, "now", DateTimeOffset.UtcNow);
            Param(command, "id", entry.Id);
            DateParam(command, "budget_date", entry.BudgetDate);
            Param(command, "model_id", entry.ModelId);
            Param(command, "hosting_type", entry.HostingType.ToString());
            Param(command, "entry_type", entry.EntryType);
            TextParam(command, "request_id", entry.RequestId);
            Param(command, "input_tokens", entry.InputTokens);
            Param(command, "output_tokens", entry.OutputTokens);
            Param(command, "cost_usd", entry.CostUsd);
            TimeParam(command, "expected_version", expectedRowVersion);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
                return Map(reader);
            throw new OptimisticConcurrencyException("token_budget_ledger", entry.Id.ToString());
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("token_budget.delete", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, "DELETE FROM token_budget_ledger WHERE id = @id");
            Param(command, "id", id);
            return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0;
        }, cancellationToken);

    /// <inheritdoc />
    public Task<TokenBudgetDailySummary> GetDailySummaryAsync(DateOnly budgetDate, CancellationToken cancellationToken = default) =>
        ExecuteAsync("token_budget.daily_summary", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, """
                SELECT COALESCE(sum(cost_usd), 0), COALESCE(sum(input_tokens), 0), COALESCE(sum(output_tokens), 0), count(*)
                FROM token_budget_ledger WHERE budget_date = @budget_date
                """);
            DateParam(command, "budget_date", budgetDate);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            await reader.ReadAsync(token).ConfigureAwait(false);
            return new TokenBudgetDailySummary
            {
                BudgetDate = budgetDate,
                TotalCostUsd = reader.GetDecimal(0),
                TotalInputTokens = reader.GetInt64(1),
                TotalOutputTokens = reader.GetInt64(2),
                EntryCount = reader.GetInt64(3),
            };
        }, cancellationToken);

    internal static TokenBudgetEntryRecord Map(SqliteDataReader reader) => new()
    {
        Id = ReadGuid(reader, 0),
        BudgetDate = ReadRequiredDate(reader, 1),
        ModelId = reader.GetString(2),
        HostingType = Enum.Parse<ModelHostingType>(reader.GetString(3)),
        EntryType = reader.GetString(4),
        RequestId = reader.IsDBNull(5) ? null : reader.GetString(5),
        InputTokens = reader.GetInt32(6),
        OutputTokens = reader.GetInt32(7),
        CostUsd = reader.GetDecimal(8),
        CreatedAtUtc = ReadRequiredTime(reader, 9),
        RowVersion = ReadRequiredTime(reader, 10),
    };
}

/// <summary>Репозиторий kv_settings с оптимистичной конкуренцией по updated_at.</summary>
public sealed class KeyValueSettingsRepository : RepositoryBase, IKeyValueSettingsRepository
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["key"] = "key",
        ["created_at"] = "created_at",
        ["updated_at"] = "updated_at",
    };

    private const string Columns = "key, value_json, description, created_at, updated_at";

    /// <summary>Создаёт репозиторий настроек.</summary>
    /// <param name="connectionFactory">Фабрика соединений TASK-010.</param>
    /// <param name="options">Опции репозиториев.</param>
    /// <param name="logger">Логгер медленных запросов.</param>
    /// <param name="metrics">Общие счётчики.</param>
    public KeyValueSettingsRepository(IDatabaseConnectionFactory connectionFactory, RepositoryOptions? options = null, ILogger? logger = null, UnitOfWorkMetrics? metrics = null)
        : base(connectionFactory, options, logger, metrics)
    {
    }

    /// <inheritdoc />
    public Task<KeyValueSettingRecord?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        RequireText(key, nameof(key));
        return ExecuteAsync("settings.get", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, $"SELECT {Columns} FROM kv_settings WHERE key = @key");
            Param(command, "key", key);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false) ? Map(reader) : null;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PagedResult<KeyValueSettingRecord>> ListAsync(PagedQuery query, CancellationToken cancellationToken = default) =>
        ExecuteAsync("settings.list", async (connection, transaction, token) =>
        {
            string orderBy = OrderBy(query, Sorts, "key");
            await using var countCommand = Command(connection, transaction, "SELECT count(*) FROM kv_settings");
            long total = Convert.ToInt64(await countCommand.ExecuteScalarAsync(token).ConfigureAwait(false));
            await using var command = Command(connection, transaction,
                $"SELECT {Columns} FROM kv_settings ORDER BY {orderBy} LIMIT @limit OFFSET @offset");
            Param(command, "limit", query.Limit);
            Param(command, "offset", query.Offset);
            var items = new List<KeyValueSettingRecord>();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                items.Add(Map(reader));
            return new PagedResult<KeyValueSettingRecord> { Items = items, TotalCount = total, Offset = query.Offset, Limit = query.Limit };
        }, cancellationToken);

    /// <inheritdoc />
    public Task<KeyValueSettingRecord> AddAsync(KeyValueSettingRecord setting, CancellationToken cancellationToken = default)
    {
        ValidateSetting(setting);
        return ExecuteAsync("settings.add", async (connection, transaction, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await using var command = Command(connection, transaction, $"""
                INSERT INTO kv_settings (key, value_json, description, created_at, updated_at)
                VALUES (@key, @value, @description, @now, @now)
                ON CONFLICT (key) DO NOTHING
                RETURNING {Columns}
                """);
            TimeParam(command, "now", now);
            Param(command, "key", setting.Key);
            JsonParam(command, "value", setting.ValueJson);
            TextParam(command, "description", setting.Description);
            await using (SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(token).ConfigureAwait(false))
                    return Map(reader);
            }

            return await GetAsync(setting.Key, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Setting disappeared after conflict.");
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<KeyValueSettingRecord> UpdateAsync(KeyValueSettingRecord setting, DateTimeOffset expectedRowVersion, CancellationToken cancellationToken = default)
    {
        ValidateSetting(setting);
        return ExecuteAsync("settings.update", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, $"""
                UPDATE kv_settings SET value_json = @value, description = @description, updated_at = @now
                WHERE key = @key AND updated_at = @expected_version
                RETURNING {Columns}
                """);
            TimeParam(command, "now", DateTimeOffset.UtcNow);
            Param(command, "key", setting.Key);
            JsonParam(command, "value", setting.ValueJson);
            TextParam(command, "description", setting.Description);
            TimeParam(command, "expected_version", expectedRowVersion);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
                return Map(reader);
            throw new OptimisticConcurrencyException("kv_settings", setting.Key);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<KeyValueSettingRecord> SetAsync(KeyValueSettingRecord setting, CancellationToken cancellationToken = default)
    {
        ValidateSetting(setting);
        return ExecuteAsync("settings.set", async (connection, transaction, token) =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await using var command = Command(connection, transaction, $"""
                INSERT INTO kv_settings (key, value_json, description, created_at, updated_at)
                VALUES (@key, @value, @description, @now, @now)
                ON CONFLICT (key) DO UPDATE SET value_json = EXCLUDED.value_json, description = EXCLUDED.description, updated_at = @now
                RETURNING {Columns}
                """);
            TimeParam(command, "now", now);
            Param(command, "key", setting.Key);
            JsonParam(command, "value", setting.ValueJson);
            TextParam(command, "description", setting.Description);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            await reader.ReadAsync(token).ConfigureAwait(false);
            return Map(reader);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        RequireText(key, nameof(key));
        return ExecuteAsync("settings.delete", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, "DELETE FROM kv_settings WHERE key = @key");
            Param(command, "key", key);
            return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0;
        }, cancellationToken);
    }

    internal static KeyValueSettingRecord Map(SqliteDataReader reader) => new()
    {
        Key = reader.GetString(0),
        ValueJson = reader.GetString(1),
        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
        CreatedAtUtc = ReadRequiredTime(reader, 3),
        RowVersion = ReadRequiredTime(reader, 4),
    };

    private static void ValidateSetting(KeyValueSettingRecord setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        RequireText(setting.Key, nameof(setting.Key));
        _ = RepositoryJson.EnsureJson(setting.ValueJson, nameof(setting.ValueJson));
    }
}

/// <summary>Репозиторий audit_log.</summary>
public sealed class AuditLogRepository : RepositoryBase, IAuditLogRepository
{
    private static readonly IReadOnlyDictionary<string, string> Sorts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["created_at"] = "created_at",
        ["id"] = "id",
        ["action"] = "action",
        ["actor"] = "actor",
    };

    private const string Columns = "id, actor, action, entity_type, entity_id, details_json, created_at";

    /// <summary>Создаёт репозиторий аудита.</summary>
    /// <param name="connectionFactory">Фабрика соединений TASK-010.</param>
    /// <param name="options">Опции репозиториев.</param>
    /// <param name="logger">Логгер медленных запросов.</param>
    /// <param name="metrics">Общие счётчики.</param>
    public AuditLogRepository(IDatabaseConnectionFactory connectionFactory, RepositoryOptions? options = null, ILogger? logger = null, UnitOfWorkMetrics? metrics = null)
        : base(connectionFactory, options, logger, metrics)
    {
    }

    /// <inheritdoc />
    public Task<AuditLogRecord?> GetAsync(long id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("audit.get", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, $"SELECT {Columns} FROM audit_log WHERE id = @id");
            Param(command, "id", id);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false) ? Map(reader) : null;
        }, cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<AuditLogRecord>> ListAsync(PagedQuery query, string? actor = null, string? action = null, string? entityType = null, string? entityId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("audit.list", async (connection, transaction, token) =>
        {
            string orderBy = OrderBy(query, Sorts, "id");
            const string where = """
                WHERE (@actor IS NULL OR actor = @actor) AND (@action IS NULL OR action = @action)
                  AND (@entity_type IS NULL OR entity_type = @entity_type) AND (@entity_id IS NULL OR entity_id = @entity_id)
                """;
            await using var countCommand = Command(connection, transaction, $"SELECT count(*) FROM audit_log {where}");
            AddFilters(countCommand, actor, action, entityType, entityId);
            long total = Convert.ToInt64(await countCommand.ExecuteScalarAsync(token).ConfigureAwait(false));

            await using var command = Command(connection, transaction,
                $"SELECT {Columns} FROM audit_log {where} ORDER BY {orderBy} LIMIT @limit OFFSET @offset");
            AddFilters(command, actor, action, entityType, entityId);
            Param(command, "limit", query.Limit);
            Param(command, "offset", query.Offset);
            var items = new List<AuditLogRecord>();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                items.Add(Map(reader));
            return new PagedResult<AuditLogRecord> { Items = items, TotalCount = total, Offset = query.Offset, Limit = query.Limit };
        }, cancellationToken);

    /// <inheritdoc />
    public Task<AuditLogRecord> AddAsync(AuditLogRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        RequireText(record.Actor, nameof(record.Actor));
        RequireText(record.Action, nameof(record.Action));
        string details = RepositoryJson.EnsureJson(record.DetailsJson, nameof(record.DetailsJson));
        return ExecuteAsync("audit.add", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, $"""
                INSERT INTO audit_log (actor, action, entity_type, entity_id, details_json)
                VALUES (@actor, @action, @entity_type, @entity_id, @details)
                RETURNING {Columns}
                """);
            Param(command, "actor", record.Actor);
            Param(command, "action", record.Action);
            TextParam(command, "entity_type", record.EntityType);
            TextParam(command, "entity_id", record.EntityId);
            JsonParam(command, "details", details);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            await reader.ReadAsync(token).ConfigureAwait(false);
            return Map(reader);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> UpdateAsync(AuditLogRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        RequireText(record.Actor, nameof(record.Actor));
        RequireText(record.Action, nameof(record.Action));
        string details = RepositoryJson.EnsureJson(record.DetailsJson, nameof(record.DetailsJson));
        return ExecuteAsync("audit.update", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, """
                UPDATE audit_log SET actor = @actor, action = @action, entity_type = @entity_type,
                    entity_id = @entity_id, details_json = @details WHERE id = @id
                """);
            Param(command, "id", record.Id);
            Param(command, "actor", record.Actor);
            Param(command, "action", record.Action);
            TextParam(command, "entity_type", record.EntityType);
            TextParam(command, "entity_id", record.EntityId);
            JsonParam(command, "details", details);
            return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("audit.delete", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, "DELETE FROM audit_log WHERE id = @id");
            Param(command, "id", id);
            return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0;
        }, cancellationToken);

    internal static AuditLogRecord Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Actor = reader.GetString(1),
        Action = reader.GetString(2),
        EntityType = reader.IsDBNull(3) ? null : reader.GetString(3),
        EntityId = reader.IsDBNull(4) ? null : reader.GetString(4),
        DetailsJson = reader.GetString(5),
        CreatedAtUtc = ReadRequiredTime(reader, 6),
    };

    private static void AddFilters(SqliteCommand command, string? actor, string? action, string? entityType, string? entityId)
    {
        TextParam(command, "actor", actor);
        TextParam(command, "action", action);
        TextParam(command, "entity_type", entityType);
        TextParam(command, "entity_id", entityId);
    }
}

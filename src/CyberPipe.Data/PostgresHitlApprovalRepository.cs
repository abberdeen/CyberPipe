using System.Text.Json;
using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CyberPipe.Data;

/// <summary>
/// PostgreSQL реализация репозитория HITL-подтверждений (TASK-046).
/// Реализует атомарное разрешение запросов (TryResolveAsync) с проверкой статуса 'Pending' на уровне SQL UPDATE.
/// </summary>
public sealed class PostgresHitlApprovalRepository : RepositoryBase, IHitlApprovalRepository
{
    private const string Columns = "id, run_id, chain_id, title, preview_text, payload_json, attach_files_json, status, resolution_json, created_at, expires_at";

    /// <summary>
    /// Создаёт репозиторий HITL-подтверждений на базе PostgreSQL.
    /// </summary>
    public PostgresHitlApprovalRepository(
        IPostgresConnectionFactory connectionFactory,
        RepositoryOptions? options = null,
        ILogger? logger = null,
        UnitOfWorkMetrics? metrics = null)
        : base(connectionFactory, options, logger, metrics)
    {
    }

    /// <summary>
    /// Обеспечивает существование таблицы app.chain_hitl_approvals в схеме app.
    /// </summary>
    public Task EnsureTableCreatedAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync("hitl_approvals.ensure_table", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, $"""
                CREATE TABLE IF NOT EXISTS app.chain_hitl_approvals (
                    id uuid PRIMARY KEY,
                    run_id uuid NOT NULL,
                    chain_id uuid NOT NULL,
                    title text NOT NULL,
                    preview_text text NOT NULL,
                    payload_json jsonb NOT NULL,
                    attach_files_json jsonb NOT NULL DEFAULT '[]'::jsonb,
                    status text NOT NULL DEFAULT 'Pending',
                    resolution_json jsonb,
                    created_at timestamptz NOT NULL,
                    expires_at timestamptz NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_chain_hitl_approvals_status_expires ON app.chain_hitl_approvals (status, expires_at);
                """);

            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    /// <inheritdoc />
    public Task<HitlApprovalRecord> CreateAsync(HitlApprovalRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        string filesJson = JsonSerializer.Serialize(record.AttachFiles);
        string payloadJson = RepositoryJson.EnsureJson(record.PayloadJson, nameof(record.PayloadJson));

        return ExecuteAsync("hitl_approvals.create", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, $"""
                INSERT INTO app.chain_hitl_approvals
                    (id, run_id, chain_id, title, preview_text, payload_json, attach_files_json, status, created_at, expires_at)
                VALUES
                    (@id, @run_id, @chain_id, @title, @preview_text, @payload::jsonb, @files::jsonb, @status, @created_at, @expires_at)
                ON CONFLICT (id) DO UPDATE SET
                    title = EXCLUDED.title,
                    preview_text = EXCLUDED.preview_text,
                    payload_json = EXCLUDED.payload_json,
                    attach_files_json = EXCLUDED.attach_files_json,
                    expires_at = EXCLUDED.expires_at
                RETURNING {Columns}
                """);

            Param(command, "id", record.ApprovalId);
            Param(command, "run_id", record.RunId.Value);
            Param(command, "chain_id", record.ChainId.Value);
            Param(command, "title", record.Title);
            Param(command, "preview_text", record.PreviewText);
            JsonParam(command, "payload", payloadJson);
            JsonParam(command, "files", filesJson);
            Param(command, "status", record.Status.ToString());
            TimeParam(command, "created_at", record.CreatedAtUtc);
            TimeParam(command, "expires_at", record.ExpiresAtUtc);

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                return Map(reader);
            }

            return record;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<HitlApprovalRecord?> GetAsync(Guid approvalId, CancellationToken cancellationToken = default) =>
        ExecuteAsync("hitl_approvals.get", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, $"SELECT {Columns} FROM app.chain_hitl_approvals WHERE id = @id");
            Param(command, "id", approvalId);

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false) ? Map(reader) : null;
        }, cancellationToken);

    /// <inheritdoc />
    public Task<bool> TryResolveAsync(Guid approvalId, HitlApprovalResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        HitlStatus newStatus = result.Decision == HitlDecision.Expired
            ? HitlStatus.Expired
            : HitlStatus.Resolved;

        string resolutionJson = JsonSerializer.Serialize(result);

        return ExecuteAsync("hitl_approvals.try_resolve", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, $"""
                UPDATE app.chain_hitl_approvals
                SET status = @status,
                    resolution_json = @resolution::jsonb,
                    payload_json = CASE WHEN @edited_payload IS NOT NULL THEN @edited_payload::jsonb ELSE payload_json END
                WHERE id = @id AND status = 'Pending'
                """);

            Param(command, "id", approvalId);
            Param(command, "status", newStatus.ToString());
            JsonParam(command, "resolution", resolutionJson);
            if (result.Decision == HitlDecision.Edited && !string.IsNullOrWhiteSpace(result.EditedPayloadJson))
            {
                JsonParam(command, "edited_payload", result.EditedPayloadJson);
            }
            else
            {
                JsonParam(command, "edited_payload", null);
            }

            int affected = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<HitlApprovalRecord>> ListPendingExpiredAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        ExecuteAsync("hitl_approvals.list_expired", async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, $"""
                SELECT {Columns} FROM app.chain_hitl_approvals
                WHERE status = 'Pending' AND expires_at <= @now
                ORDER BY expires_at ASC
                """);

            TimeParam(command, "now", nowUtc);

            var list = new List<HitlApprovalRecord>();
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                list.Add(Map(reader));
            }

            return (IReadOnlyList<HitlApprovalRecord>)list;
        }, cancellationToken);

    internal static HitlApprovalRecord Map(NpgsqlDataReader reader)
    {
        Guid id = reader.GetGuid(0);
        Guid runId = reader.GetGuid(1);
        Guid chainId = reader.GetGuid(2);
        string title = reader.GetString(3);
        string previewText = reader.GetString(4);
        string payloadJson = reader.GetString(5);
        string attachFilesJson = reader.GetString(6);
        string statusStr = reader.GetString(7);
        string? resolutionJson = reader.IsDBNull(8) ? null : reader.GetString(8);
        DateTimeOffset createdAt = ReadRequiredTime(reader, 9);
        DateTimeOffset expiresAt = ReadRequiredTime(reader, 10);

        IReadOnlyList<MessageAttachmentDto>? files = null;
        if (!string.IsNullOrWhiteSpace(attachFilesJson))
        {
            try
            {
                files = JsonSerializer.Deserialize<IReadOnlyList<MessageAttachmentDto>>(attachFilesJson);
            }
            catch
            {
                files = null;
            }
        }

        HitlApprovalResult? resolution = null;
        if (!string.IsNullOrWhiteSpace(resolutionJson))
        {
            try
            {
                resolution = JsonSerializer.Deserialize<HitlApprovalResult>(resolutionJson);
            }
            catch
            {
                resolution = null;
            }
        }

        HitlStatus status = Enum.TryParse<HitlStatus>(statusStr, ignoreCase: true, out var parsedStatus)
            ? parsedStatus
            : HitlStatus.Pending;

        return new HitlApprovalRecord
        {
            ApprovalId = id,
            RunId = new RunId(runId),
            ChainId = new ChainId(chainId),
            Title = title,
            PreviewText = previewText,
            PayloadJson = payloadJson,
            AttachFiles = files ?? [],
            Status = status,
            Resolution = resolution,
            CreatedAtUtc = createdAt,
            ExpiresAtUtc = expiresAt,
        };
    }
}

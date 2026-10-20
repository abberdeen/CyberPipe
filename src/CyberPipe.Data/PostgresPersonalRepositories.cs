using System.Text.Json;
using CyberPipe.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace CyberPipe.Data;

/// <summary>PostgreSQL заметки владельца (TASK-051).</summary>
public sealed class PostgresNoteRepository : INoteRepository
{
    private readonly IPostgresConnectionFactory _factory;
    private readonly TimeProvider _time;
    public PostgresNoteRepository(IPostgresConnectionFactory factory, TimeProvider? time = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _time = time ?? TimeProvider.System;
    }

    public async Task<NoteRecord> CreateAsync(string title, string content, IReadOnlyList<string>? tags = null, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        var record = new NoteRecord { NoteId = Guid.NewGuid(), Title = title, Content = content, Tags = tags ?? [], CreatedAtUtc = now, UpdatedAtUtc = now };
        await using NpgsqlConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("INSERT INTO app.personal_notes (id,title,content,tags,created_at,updated_at) VALUES (@id,@t,@c,@tags,@at,@at)", c);
        cmd.Parameters.AddWithValue("id", record.NoteId);
        cmd.Parameters.AddWithValue("t", title);
        cmd.Parameters.AddWithValue("c", content);
        cmd.Parameters.Add("tags", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = (tags ?? []).ToArray();
        cmd.Parameters.AddWithValue("at", now);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<NoteRecord?> GetAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT id,title,content,tags,created_at,updated_at FROM app.personal_notes WHERE id=@id AND NOT is_deleted", c);
        cmd.Parameters.AddWithValue("id", noteId);
        await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await r.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(r) : null;
    }

    public async Task<NoteRecord> UpdateAsync(Guid noteId, string? title, string? content, IReadOnlyList<string>? tags = null, CancellationToken cancellationToken = default)
    {
        NoteRecord existing = await GetAsync(noteId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{noteId}' not found.");
        DateTimeOffset now = _time.GetUtcNow();
        NoteRecord updated = existing with { Title = title ?? existing.Title, Content = content ?? existing.Content, Tags = tags ?? existing.Tags, UpdatedAtUtc = now };
        await using NpgsqlConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("UPDATE app.personal_notes SET title=@t,content=@c,tags=@tags,updated_at=@at WHERE id=@id", c);
        cmd.Parameters.AddWithValue("id", noteId);
        cmd.Parameters.AddWithValue("t", updated.Title);
        cmd.Parameters.AddWithValue("c", updated.Content);
        cmd.Parameters.Add("tags", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = updated.Tags.ToArray();
        cmd.Parameters.AddWithValue("at", now);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<bool> DeleteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("UPDATE app.personal_notes SET is_deleted=true,updated_at=now() WHERE id=@id AND NOT is_deleted", c);
        cmd.Parameters.AddWithValue("id", noteId);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<IReadOnlyList<NoteRecord>> SearchAsync(string query, IReadOnlyList<string>? tags = null, int limit = 20, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("""
            SELECT id,title,content,tags,created_at,updated_at FROM app.personal_notes
            WHERE NOT is_deleted AND (@q='' OR title ILIKE @like OR content ILIKE @like)
              AND (NOT @hasTags OR tags @> @tags)
            ORDER BY updated_at DESC LIMIT @limit
            """, c);
        cmd.Parameters.AddWithValue("q", query ?? string.Empty);
        cmd.Parameters.AddWithValue("like", $"%{query}%");
        cmd.Parameters.AddWithValue("hasTags", tags is { Count: > 0 });
        if (tags is { Count: > 0 })
            cmd.Parameters.Add("tags", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = tags.ToArray();
        else
            cmd.Parameters.Add("tags", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = Array.Empty<string>();
        cmd.Parameters.AddWithValue("limit", Math.Max(1, limit));
        var list = new List<NoteRecord>();
        await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(Map(r));
        return list;
    }

    private static NoteRecord Map(NpgsqlDataReader r) => new()
    {
        NoteId = r.GetGuid(0),
        Title = r.GetString(1),
        Content = r.GetString(2),
        Tags = r.GetFieldValue<string[]>(3),
        CreatedAtUtc = r.GetFieldValue<DateTimeOffset>(4),
        UpdatedAtUtc = r.GetFieldValue<DateTimeOffset>(5),
    };
}

/// <summary>PostgreSQL финансы владельца (TASK-051).</summary>
public sealed class PostgresFinanceRepository : IFinanceRepository
{
    private readonly IPostgresConnectionFactory _factory;
    private readonly TimeProvider _time;
    public PostgresFinanceRepository(IPostgresConnectionFactory factory, TimeProvider? time = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _time = time ?? TimeProvider.System;
    }

    public async Task<FinanceTransactionRecord> AddTransactionAsync(FinanceTransactionKind kind, decimal amount, string currency, string category, string? description = null, DateTimeOffset? occurredAtUtc = null, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        var record = new FinanceTransactionRecord
        {
            TransactionId = Guid.NewGuid(), Kind = kind, Amount = amount, Currency = currency,
            Category = category, Description = description, OccurredAtUtc = occurredAtUtc ?? now, CreatedAtUtc = now,
        };
        await using NpgsqlConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("INSERT INTO app.personal_finance (id,kind,amount,currency,category,description,occurred_at,created_at) VALUES (@id,@k,@a,@cur,@cat,@d,@occ,@at)", c);
        cmd.Parameters.AddWithValue("id", record.TransactionId);
        cmd.Parameters.AddWithValue("k", (int)kind);
        cmd.Parameters.AddWithValue("a", amount);
        cmd.Parameters.AddWithValue("cur", currency);
        cmd.Parameters.AddWithValue("cat", category);
        cmd.Parameters.AddWithValue("d", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("occ", record.OccurredAtUtc);
        cmd.Parameters.AddWithValue("at", now);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<decimal> GetBalanceAsync(string currency, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FinanceTransactionRecord> all = await ListTransactionsAsync(null, null, currency, int.MaxValue, cancellationToken).ConfigureAwait(false);
        return all.Sum(t => t.Kind == FinanceTransactionKind.Income ? t.Amount : -t.Amount);
    }

    public async Task<FinanceReportDto> GetReportAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, string currency, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FinanceTransactionRecord> items = await ListTransactionsAsync(fromUtc, toUtc, currency, int.MaxValue, cancellationToken).ConfigureAwait(false);
        var categories = items.GroupBy(t => t.Category).Select(g => new CategorySummaryDto
        {
            Category = g.Key,
            TotalExpense = g.Where(t => t.Kind == FinanceTransactionKind.Expense).Sum(t => t.Amount),
            TotalIncome = g.Where(t => t.Kind == FinanceTransactionKind.Income).Sum(t => t.Amount),
            TransactionCount = g.Count(),
        }).ToList();
        return new FinanceReportDto
        {
            FromUtc = fromUtc, ToUtc = toUtc, Currency = currency,
            TotalIncome = items.Where(t => t.Kind == FinanceTransactionKind.Income).Sum(t => t.Amount),
            TotalExpense = items.Where(t => t.Kind == FinanceTransactionKind.Expense).Sum(t => t.Amount),
            Categories = categories,
        };
    }

    public async Task<IReadOnlyList<FinanceTransactionRecord>> ListTransactionsAsync(DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, string? currency = null, int limit = 50, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("""
            SELECT id,kind,amount,currency,category,description,occurred_at,created_at FROM app.personal_finance
            WHERE (@from IS NULL OR occurred_at>=@from) AND (@to IS NULL OR occurred_at<=@to) AND (@cur IS NULL OR currency=@cur)
            ORDER BY occurred_at DESC LIMIT @limit
            """, c);
        cmd.Parameters.Add("from", NpgsqlDbType.TimestampTz).Value = (object?)fromUtc ?? DBNull.Value;
        cmd.Parameters.Add("to", NpgsqlDbType.TimestampTz).Value = (object?)toUtc ?? DBNull.Value;
        cmd.Parameters.Add("cur", NpgsqlDbType.Text).Value = (object?)currency ?? DBNull.Value;
        cmd.Parameters.AddWithValue("limit", limit == int.MaxValue ? int.MaxValue : Math.Max(1, limit));
        var list = new List<FinanceTransactionRecord>();
        await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new FinanceTransactionRecord
            {
                TransactionId = r.GetGuid(0), Kind = (FinanceTransactionKind)r.GetInt32(1), Amount = r.GetDecimal(2),
                Currency = r.GetString(3), Category = r.GetString(4), Description = r.IsDBNull(5) ? null : r.GetString(5),
                OccurredAtUtc = r.GetFieldValue<DateTimeOffset>(6), CreatedAtUtc = r.GetFieldValue<DateTimeOffset>(7),
            });
        }
        return list;
    }
}

/// <summary>PostgreSQL напоминания владельца (TASK-051).</summary>
public sealed class PostgresReminderRepository : IReminderRepository
{
    private readonly IPostgresConnectionFactory _factory;
    private readonly TimeProvider _time;
    public PostgresReminderRepository(IPostgresConnectionFactory factory, TimeProvider? time = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _time = time ?? TimeProvider.System;
    }

    public async Task<ReminderItemRecord> CreateAsync(string title, DateTimeOffset? dueTimeUtc, string? cronExpression = null, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        var record = new ReminderItemRecord { ReminderId = Guid.NewGuid(), Title = title, DueTimeUtc = dueTimeUtc, CronExpression = cronExpression, Status = ReminderStatus.Pending, CreatedAtUtc = now };
        await using NpgsqlConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("INSERT INTO app.personal_reminders (id,title,due_time,cron_expression,status,created_at) VALUES (@id,@t,@due,@cron,0,@at)", c);
        cmd.Parameters.AddWithValue("id", record.ReminderId);
        cmd.Parameters.AddWithValue("t", title);
        cmd.Parameters.AddWithValue("due", (object?)dueTimeUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cron", (object?)cronExpression ?? DBNull.Value);
        cmd.Parameters.AddWithValue("at", now);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<ReminderItemRecord?> GetAsync(Guid reminderId, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT id,title,due_time,cron_expression,status,created_at,completed_at FROM app.personal_reminders WHERE id=@id", c);
        cmd.Parameters.AddWithValue("id", reminderId);
        await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await r.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(r) : null;
    }

    public async Task<IReadOnlyList<ReminderItemRecord>> ListAsync(ReminderStatus? status = null, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT id,title,due_time,cron_expression,status,created_at,completed_at FROM app.personal_reminders WHERE (@s IS NULL OR status=@s) ORDER BY due_time NULLS LAST, created_at", c);
        cmd.Parameters.Add("s", NpgsqlDbType.Integer).Value = status.HasValue ? (object)(int)status.Value : DBNull.Value;
        var list = new List<ReminderItemRecord>();
        await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(Map(r));
        return list;
    }

    public Task<bool> CompleteAsync(Guid reminderId, CancellationToken cancellationToken = default) =>
        SetStatusAsync(reminderId, ReminderStatus.Completed, cancellationToken);

    public Task<bool> CancelAsync(Guid reminderId, CancellationToken cancellationToken = default) =>
        SetStatusAsync(reminderId, ReminderStatus.Cancelled, cancellationToken);

    private async Task<bool> SetStatusAsync(Guid id, ReminderStatus status, CancellationToken ct)
    {
        await using NpgsqlConnection c = await _factory.CreateOpenedConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("UPDATE app.personal_reminders SET status=@s, completed_at=CASE WHEN @s=1 THEN now() ELSE completed_at END WHERE id=@id AND status=0", c);
        cmd.Parameters.AddWithValue("s", (int)status);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    private static ReminderItemRecord Map(NpgsqlDataReader r) => new()
    {
        ReminderId = r.GetGuid(0), Title = r.GetString(1),
        DueTimeUtc = r.IsDBNull(2) ? null : r.GetFieldValue<DateTimeOffset>(2),
        CronExpression = r.IsDBNull(3) ? null : r.GetString(3),
        Status = (ReminderStatus)r.GetInt32(4),
        CreatedAtUtc = r.GetFieldValue<DateTimeOffset>(5),
        CompletedAtUtc = r.IsDBNull(6) ? null : r.GetFieldValue<DateTimeOffset>(6),
    };
}

/// <summary>PostgreSQL контакты/CRM владельца (TASK-051).</summary>
public sealed class PostgresContactRepository : IContactRepository
{
    private readonly IPostgresConnectionFactory _factory;
    private readonly TimeProvider _time;
    public PostgresContactRepository(IPostgresConnectionFactory factory, TimeProvider? time = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _time = time ?? TimeProvider.System;
    }

    public async Task<ContactRecord> UpsertContactAsync(string name, IReadOnlyDictionary<string, string>? channels = null, string? notes = null, Guid? contactId = null, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        ContactRecord? existing = contactId.HasValue
            ? await GetAsync(contactId.Value, cancellationToken).ConfigureAwait(false)
            : await FindByNameAsync(name, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            ContactRecord updated = existing with { Name = name, Channels = channels ?? existing.Channels, Notes = notes ?? existing.Notes, UpdatedAtUtc = now };
            await using NpgsqlConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand("UPDATE app.personal_contacts SET name=@n,channels=@ch,notes=@notes,updated_at=@at WHERE id=@id", c);
            cmd.Parameters.AddWithValue("id", existing.ContactId);
            cmd.Parameters.AddWithValue("n", updated.Name);
            cmd.Parameters.Add("ch", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(updated.Channels);
            cmd.Parameters.AddWithValue("notes", (object?)updated.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("at", now);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return updated;
        }

        var record = new ContactRecord
        {
            ContactId = contactId ?? Guid.NewGuid(), Name = name,
            Channels = channels ?? new Dictionary<string, string>(), Notes = notes, CreatedAtUtc = now, UpdatedAtUtc = now,
        };
        await using NpgsqlConnection conn = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var insert = new NpgsqlCommand("INSERT INTO app.personal_contacts (id,name,channels,notes,created_at,updated_at) VALUES (@id,@n,@ch,@notes,@at,@at)", conn);
        insert.Parameters.AddWithValue("id", record.ContactId);
        insert.Parameters.AddWithValue("n", name);
        insert.Parameters.Add("ch", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(record.Channels);
        insert.Parameters.AddWithValue("notes", (object?)notes ?? DBNull.Value);
        insert.Parameters.AddWithValue("at", now);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<ContactRecord?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT id,name,channels,notes,created_at,updated_at FROM app.personal_contacts WHERE lower(name)=lower(@n) AND NOT is_deleted LIMIT 1", c);
        cmd.Parameters.AddWithValue("n", name);
        await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await r.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(r) : null;
    }

    public async Task<ContactRecord?> GetAsync(Guid contactId, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT id,name,channels,notes,created_at,updated_at FROM app.personal_contacts WHERE id=@id AND NOT is_deleted", c);
        cmd.Parameters.AddWithValue("id", contactId);
        await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await r.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(r) : null;
    }

    public async Task<IReadOnlyList<ContactRecord>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT id,name,channels,notes,created_at,updated_at FROM app.personal_contacts WHERE NOT is_deleted AND (@q='' OR name ILIKE @like) ORDER BY name", c);
        cmd.Parameters.AddWithValue("q", query ?? string.Empty);
        cmd.Parameters.AddWithValue("like", $"%{query}%");
        var list = new List<ContactRecord>();
        await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(Map(r));
        return list;
    }

    public async Task<InteractionRecord> AddInteractionAsync(Guid contactId, string channel, string note, DateTimeOffset? occurredAtUtc = null, CancellationToken cancellationToken = default)
    {
        var record = new InteractionRecord { InteractionId = Guid.NewGuid(), ContactId = contactId, Channel = channel, Note = note, OccurredAtUtc = occurredAtUtc ?? _time.GetUtcNow() };
        await using NpgsqlConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("INSERT INTO app.personal_interactions (id,contact_id,channel,note,occurred_at) VALUES (@id,@cid,@ch,@n,@at)", c);
        cmd.Parameters.AddWithValue("id", record.InteractionId);
        cmd.Parameters.AddWithValue("cid", contactId);
        cmd.Parameters.AddWithValue("ch", channel);
        cmd.Parameters.AddWithValue("n", note);
        cmd.Parameters.AddWithValue("at", record.OccurredAtUtc);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<IReadOnlyList<InteractionRecord>> GetHistoryAsync(Guid contactId, int limit = 50, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT id,contact_id,channel,note,occurred_at FROM app.personal_interactions WHERE contact_id=@cid ORDER BY occurred_at DESC LIMIT @limit", c);
        cmd.Parameters.AddWithValue("cid", contactId);
        cmd.Parameters.AddWithValue("limit", Math.Max(1, limit));
        var list = new List<InteractionRecord>();
        await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new InteractionRecord
            {
                InteractionId = r.GetGuid(0), ContactId = r.GetGuid(1), Channel = r.GetString(2),
                Note = r.GetString(3), OccurredAtUtc = r.GetFieldValue<DateTimeOffset>(4),
            });
        }
        return list;
    }

    private static ContactRecord Map(NpgsqlDataReader r) => new()
    {
        ContactId = r.GetGuid(0), Name = r.GetString(1),
        Channels = JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(2)) ?? new Dictionary<string, string>(),
        Notes = r.IsDBNull(3) ? null : r.GetString(3),
        CreatedAtUtc = r.GetFieldValue<DateTimeOffset>(4), UpdatedAtUtc = r.GetFieldValue<DateTimeOffset>(5),
    };
}

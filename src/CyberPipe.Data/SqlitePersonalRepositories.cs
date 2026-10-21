using System.Globalization;
using System.Text.Json;
using CyberPipe.Contracts;
using Microsoft.Data.Sqlite;

namespace CyberPipe.Data;

/// <summary>SQLite заметки владельца (TASK-051).</summary>
public sealed class SqliteNoteRepository : INoteRepository
{
    private readonly IDatabaseConnectionFactory _factory;
    private readonly TimeProvider _time;
    public SqliteNoteRepository(IDatabaseConnectionFactory factory, TimeProvider? time = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _time = time ?? TimeProvider.System;
    }

    public async Task<NoteRecord> CreateAsync(string title, string content, IReadOnlyList<string>? tags = null, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        var record = new NoteRecord { NoteId = Guid.NewGuid(), Title = title, Content = content, Tags = tags ?? [], CreatedAtUtc = now, UpdatedAtUtc = now };
        await using SqliteConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("INSERT INTO personal_notes (id,title,content,tags,created_at,updated_at) VALUES (@id,@t,@c,@tags,@at,@at)", c);
        cmd.Parameters.AddWithValue("@id", SqliteRow.GuidText(record.NoteId));
        cmd.Parameters.AddWithValue("@t", title);
        cmd.Parameters.AddWithValue("@c", content);
        cmd.Parameters.AddWithValue("@tags", SqliteRow.FormatTags(tags ?? []));
        cmd.Parameters.AddWithValue("@at", SqliteRow.ToDbText(now));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<NoteRecord?> GetAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("SELECT id,title,content,tags,created_at,updated_at FROM personal_notes WHERE id=@id AND NOT is_deleted", c);
        cmd.Parameters.AddWithValue("@id", SqliteRow.GuidText(noteId));
        await using SqliteDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await r.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(r) : null;
    }

    public async Task<NoteRecord> UpdateAsync(Guid noteId, string? title, string? content, IReadOnlyList<string>? tags = null, CancellationToken cancellationToken = default)
    {
        NoteRecord existing = await GetAsync(noteId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{noteId}' not found.");
        DateTimeOffset now = _time.GetUtcNow();
        NoteRecord updated = existing with { Title = title ?? existing.Title, Content = content ?? existing.Content, Tags = tags ?? existing.Tags, UpdatedAtUtc = now };
        await using SqliteConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("UPDATE personal_notes SET title=@t,content=@c,tags=@tags,updated_at=@at WHERE id=@id", c);
        cmd.Parameters.AddWithValue("@id", SqliteRow.GuidText(noteId));
        cmd.Parameters.AddWithValue("@t", updated.Title);
        cmd.Parameters.AddWithValue("@c", updated.Content);
        cmd.Parameters.AddWithValue("@tags", SqliteRow.FormatTags(updated.Tags));
        cmd.Parameters.AddWithValue("@at", SqliteRow.ToDbText(now));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<bool> DeleteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("UPDATE personal_notes SET is_deleted=1,updated_at=CURRENT_TIMESTAMP WHERE id=@id AND NOT is_deleted", c);
        cmd.Parameters.AddWithValue("@id", SqliteRow.GuidText(noteId));
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<IReadOnlyList<NoteRecord>> SearchAsync(string query, IReadOnlyList<string>? tags = null, int limit = 20, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("""
            SELECT id,title,content,tags,created_at,updated_at FROM personal_notes
            WHERE NOT is_deleted AND (@q='' OR title LIKE @like COLLATE NOCASE OR content LIKE @like COLLATE NOCASE)
            ORDER BY updated_at DESC LIMIT @limit
            """, c);
        cmd.Parameters.AddWithValue("@q", query ?? string.Empty);
        cmd.Parameters.AddWithValue("@like", $"%{query}%");
        cmd.Parameters.AddWithValue("@limit", Math.Max(1, limit));
        var list = new List<NoteRecord>();
        await using SqliteDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(Map(r));
        if (tags is { Count: > 0 })
        {
            var required = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
            list = list.Where(n => n.Tags.Any(t => required.Contains(t))).ToList();
        }

        return list;
    }

    private static NoteRecord Map(SqliteDataReader r) => new()
    {
        NoteId = SqliteRow.ReadGuid(r, 0),
        Title = r.GetString(1),
        Content = r.GetString(2),
        Tags = SqliteRow.ParseTags(r.GetString(3)),
        CreatedAtUtc = SqliteRow.ReadRequiredTime(r, 4),
        UpdatedAtUtc = SqliteRow.ReadRequiredTime(r, 5),
    };
}

/// <summary>SQLite финансы владельца (TASK-051).</summary>
public sealed class SqliteFinanceRepository : IFinanceRepository
{
    private readonly IDatabaseConnectionFactory _factory;
    private readonly TimeProvider _time;
    public SqliteFinanceRepository(IDatabaseConnectionFactory factory, TimeProvider? time = null)
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
        await using SqliteConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("INSERT INTO personal_finance (id,kind,amount,currency,category,description,occurred_at,created_at) VALUES (@id,@k,@a,@cur,@cat,@d,@occ,@at)", c);
        cmd.Parameters.AddWithValue("@id", SqliteRow.GuidText(record.TransactionId));
        cmd.Parameters.AddWithValue("@k", (int)kind);
        cmd.Parameters.AddWithValue("@a", SqliteRow.ToDbText(amount));
        cmd.Parameters.AddWithValue("@cur", currency);
        cmd.Parameters.AddWithValue("@cat", category);
        cmd.Parameters.AddWithValue("@d", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@occ", SqliteRow.ToDbText(record.OccurredAtUtc));
        cmd.Parameters.AddWithValue("@at", SqliteRow.ToDbText(now));
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
        await using SqliteConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("""
            SELECT id,kind,amount,currency,category,description,occurred_at,created_at FROM personal_finance
            WHERE (@from IS NULL OR occurred_at>=@from) AND (@to IS NULL OR occurred_at<=@to) AND (@cur IS NULL OR currency=@cur)
            ORDER BY occurred_at DESC LIMIT @limit
            """, c);
        cmd.Parameters.AddWithValue("@from", fromUtc.HasValue ? SqliteRow.ToDbText(fromUtc.Value) : DBNull.Value);
        cmd.Parameters.AddWithValue("@to", toUtc.HasValue ? SqliteRow.ToDbText(toUtc.Value) : DBNull.Value);
        cmd.Parameters.AddWithValue("@cur", (object?)currency ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@limit", limit == int.MaxValue ? int.MaxValue : Math.Max(1, limit));
        var list = new List<FinanceTransactionRecord>();
        await using SqliteDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new FinanceTransactionRecord
            {
                TransactionId = SqliteRow.ReadGuid(r, 0), Kind = (FinanceTransactionKind)r.GetInt32(1), Amount = SqliteRow.ToDecimal(r.GetValue(2)),
                Currency = r.GetString(3), Category = r.GetString(4), Description = r.IsDBNull(5) ? null : r.GetString(5),
                OccurredAtUtc = SqliteRow.ReadRequiredTime(r, 6), CreatedAtUtc = SqliteRow.ReadRequiredTime(r, 7),
            });
        }

        return list;
    }
}

/// <summary>SQLite напоминания владельца (TASK-051).</summary>
public sealed class SqliteReminderRepository : IReminderRepository
{
    private readonly IDatabaseConnectionFactory _factory;
    private readonly TimeProvider _time;
    public SqliteReminderRepository(IDatabaseConnectionFactory factory, TimeProvider? time = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _time = time ?? TimeProvider.System;
    }

    public async Task<ReminderItemRecord> CreateAsync(string title, DateTimeOffset? dueTimeUtc, string? cronExpression = null, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        var record = new ReminderItemRecord { ReminderId = Guid.NewGuid(), Title = title, DueTimeUtc = dueTimeUtc, CronExpression = cronExpression, Status = ReminderStatus.Pending, CreatedAtUtc = now };
        await using SqliteConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("INSERT INTO personal_reminders (id,title,due_time,cron_expression,status,created_at) VALUES (@id,@t,@due,@cron,0,@at)", c);
        cmd.Parameters.AddWithValue("@id", SqliteRow.GuidText(record.ReminderId));
        cmd.Parameters.AddWithValue("@t", title);
        cmd.Parameters.AddWithValue("@due", dueTimeUtc.HasValue ? SqliteRow.ToDbText(dueTimeUtc.Value) : DBNull.Value);
        cmd.Parameters.AddWithValue("@cron", (object?)cronExpression ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@at", SqliteRow.ToDbText(now));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<ReminderItemRecord?> GetAsync(Guid reminderId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("SELECT id,title,due_time,cron_expression,status,created_at,completed_at FROM personal_reminders WHERE id=@id", c);
        cmd.Parameters.AddWithValue("@id", SqliteRow.GuidText(reminderId));
        await using SqliteDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await r.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(r) : null;
    }

    public async Task<IReadOnlyList<ReminderItemRecord>> ListAsync(ReminderStatus? status = null, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("SELECT id,title,due_time,cron_expression,status,created_at,completed_at FROM personal_reminders WHERE (@s IS NULL OR status=@s) ORDER BY due_time IS NULL, due_time, created_at", c);
        cmd.Parameters.AddWithValue("@s", status.HasValue ? (object)(int)status.Value : DBNull.Value);
        var list = new List<ReminderItemRecord>();
        await using SqliteDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
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
        await using SqliteConnection c = await _factory.CreateOpenedConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("UPDATE personal_reminders SET status=@s, completed_at=CASE WHEN @s=1 THEN CURRENT_TIMESTAMP ELSE completed_at END WHERE id=@id AND status=0", c);
        cmd.Parameters.AddWithValue("@s", (int)status);
        cmd.Parameters.AddWithValue("@id", SqliteRow.GuidText(id));
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    private static ReminderItemRecord Map(SqliteDataReader r) => new()
    {
        ReminderId = SqliteRow.ReadGuid(r, 0), Title = r.GetString(1),
        DueTimeUtc = SqliteRow.ReadTime(r, 2),
        CronExpression = r.IsDBNull(3) ? null : r.GetString(3),
        Status = (ReminderStatus)r.GetInt32(4),
        CreatedAtUtc = SqliteRow.ReadRequiredTime(r, 5),
        CompletedAtUtc = SqliteRow.ReadTime(r, 6),
    };
}

/// <summary>SQLite контакты/CRM владельца (TASK-051).</summary>
public sealed class SqliteContactRepository : IContactRepository
{
    private readonly IDatabaseConnectionFactory _factory;
    private readonly TimeProvider _time;
    public SqliteContactRepository(IDatabaseConnectionFactory factory, TimeProvider? time = null)
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
            await using SqliteConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new SqliteCommand("UPDATE personal_contacts SET name=@n,channels=@ch,notes=@notes,updated_at=@at WHERE id=@id", c);
            cmd.Parameters.AddWithValue("@id", SqliteRow.GuidText(existing.ContactId));
            cmd.Parameters.AddWithValue("@n", updated.Name);
            cmd.Parameters.AddWithValue("@ch", JsonSerializer.Serialize(updated.Channels));
            cmd.Parameters.AddWithValue("@notes", (object?)updated.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@at", SqliteRow.ToDbText(now));
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return updated;
        }

        var record = new ContactRecord
        {
            ContactId = contactId ?? Guid.NewGuid(), Name = name,
            Channels = channels ?? new Dictionary<string, string>(), Notes = notes, CreatedAtUtc = now, UpdatedAtUtc = now,
        };
        await using SqliteConnection conn = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var insert = new SqliteCommand("INSERT INTO personal_contacts (id,name,channels,notes,created_at,updated_at) VALUES (@id,@n,@ch,@notes,@at,@at)", conn);
        insert.Parameters.AddWithValue("@id", SqliteRow.GuidText(record.ContactId));
        insert.Parameters.AddWithValue("@n", name);
        insert.Parameters.AddWithValue("@ch", JsonSerializer.Serialize(record.Channels));
        insert.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        insert.Parameters.AddWithValue("@at", SqliteRow.ToDbText(now));
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<ContactRecord?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("SELECT id,name,channels,notes,created_at,updated_at FROM personal_contacts WHERE lower(name)=lower(@n) AND NOT is_deleted LIMIT 1", c);
        cmd.Parameters.AddWithValue("@n", name);
        await using SqliteDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await r.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(r) : null;
    }

    public async Task<ContactRecord?> GetAsync(Guid contactId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("SELECT id,name,channels,notes,created_at,updated_at FROM personal_contacts WHERE id=@id AND NOT is_deleted", c);
        cmd.Parameters.AddWithValue("@id", SqliteRow.GuidText(contactId));
        await using SqliteDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await r.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(r) : null;
    }

    public async Task<IReadOnlyList<ContactRecord>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("SELECT id,name,channels,notes,created_at,updated_at FROM personal_contacts WHERE NOT is_deleted AND (@q='' OR name LIKE @like COLLATE NOCASE) ORDER BY name", c);
        cmd.Parameters.AddWithValue("@q", query ?? string.Empty);
        cmd.Parameters.AddWithValue("@like", $"%{query}%");
        var list = new List<ContactRecord>();
        await using SqliteDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(Map(r));
        return list;
    }

    public async Task<InteractionRecord> AddInteractionAsync(Guid contactId, string channel, string note, DateTimeOffset? occurredAtUtc = null, CancellationToken cancellationToken = default)
    {
        var record = new InteractionRecord { InteractionId = Guid.NewGuid(), ContactId = contactId, Channel = channel, Note = note, OccurredAtUtc = occurredAtUtc ?? _time.GetUtcNow() };
        await using SqliteConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("INSERT INTO personal_interactions (id,contact_id,channel,note,occurred_at) VALUES (@id,@cid,@ch,@n,@at)", c);
        cmd.Parameters.AddWithValue("@id", SqliteRow.GuidText(record.InteractionId));
        cmd.Parameters.AddWithValue("@cid", SqliteRow.GuidText(contactId));
        cmd.Parameters.AddWithValue("@ch", channel);
        cmd.Parameters.AddWithValue("@n", note);
        cmd.Parameters.AddWithValue("@at", SqliteRow.ToDbText(record.OccurredAtUtc));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<IReadOnlyList<InteractionRecord>> GetHistoryAsync(Guid contactId, int limit = 50, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection c = await _factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("SELECT id,contact_id,channel,note,occurred_at FROM personal_interactions WHERE contact_id=@cid ORDER BY occurred_at DESC LIMIT @limit", c);
        cmd.Parameters.AddWithValue("@cid", SqliteRow.GuidText(contactId));
        cmd.Parameters.AddWithValue("@limit", Math.Max(1, limit));
        var list = new List<InteractionRecord>();
        await using SqliteDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new InteractionRecord
            {
                InteractionId = SqliteRow.ReadGuid(r, 0), ContactId = SqliteRow.ReadGuid(r, 1), Channel = r.GetString(2),
                Note = r.GetString(3), OccurredAtUtc = SqliteRow.ReadRequiredTime(r, 4),
            });
        }

        return list;
    }

    private static ContactRecord Map(SqliteDataReader r) => new()
    {
        ContactId = SqliteRow.ReadGuid(r, 0), Name = r.GetString(1),
        Channels = JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(2)) ?? new Dictionary<string, string>(),
        Notes = r.IsDBNull(3) ? null : r.GetString(3),
        CreatedAtUtc = SqliteRow.ReadRequiredTime(r, 4), UpdatedAtUtc = SqliteRow.ReadRequiredTime(r, 5),
    };
}

/// <summary>Локальные конвертеры строк SQLite для standalone-репозиториев.</summary>
internal static class SqliteRow
{
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";

    internal static string GuidText(Guid value) =>
        value.ToString("D", CultureInfo.InvariantCulture);

    internal static string ToDbText(DateTimeOffset value) =>
        value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    internal static string ToDbText(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    internal static string FormatTags(IReadOnlyList<string> tags) =>
        JsonSerializer.Serialize(tags);

    internal static IReadOnlyList<string> ParseTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(text) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static decimal ToDecimal(object value) =>
        Convert.ToDecimal(value, CultureInfo.InvariantCulture);

    internal static Guid ReadGuid(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            throw new InvalidOperationException("Unexpected NULL guid.");
        object value = reader.GetValue(ordinal);
        return value switch
        {
            Guid guid => guid,
            string s => Guid.Parse(s),
            byte[] bytes when bytes.Length == 16 => new Guid(bytes),
            _ => reader.GetGuid(ordinal),
        };
    }

    internal static DateTimeOffset? ReadTime(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;
        object value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset parsed) => parsed,
            string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out DateTime parsedDt) => new DateTimeOffset(DateTime.SpecifyKind(parsedDt, DateTimeKind.Utc)),
            long unix => DateTimeOffset.FromUnixTimeSeconds(unix),
            _ => throw new InvalidOperationException($"Unexpected timestamp value of type {value.GetType().Name}."),
        };
    }

    internal static DateTimeOffset ReadRequiredTime(SqliteDataReader reader, int ordinal) =>
        ReadTime(reader, ordinal) ?? throw new InvalidOperationException("Unexpected NULL timestamp.");
}

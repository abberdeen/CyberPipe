using System.Text.Json;
using CyberPipe.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>SQLite заметки владельца (TASK-051) на EF Core.</summary>
public sealed class SqliteNoteRepository : RepositoryBase, INoteRepository
{
    private readonly TimeProvider _time;

    /// <summary>Создаёт репозиторий заметок поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции репозиториев; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    /// <param name="time">Поставщик времени; null — системное время.</param>
    public SqliteNoteRepository(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger? logger = null,
        UnitOfWorkMetrics? metrics = null,
        TimeProvider? time = null)
        : base(dbFactory, options, logger, metrics)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<NoteRecord> CreateAsync(string title, string content, IReadOnlyList<string>? tags = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("notes.create", async (db, token) =>
        {
            DateTimeOffset now = _time.GetUtcNow();
            var entity = new NoteEntity
            {
                Id = Guid.NewGuid().ToString("D"),
                Title = title,
                Content = content,
                Tags = (tags ?? []).ToList(),
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Notes.Add(entity);
            await SaveAsync(db, token).ConfigureAwait(false);
            return Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<NoteRecord?> GetAsync(Guid noteId, CancellationToken cancellationToken = default) =>
        ExecuteAsync<NoteRecord?>("notes.get", async (db, token) =>
        {
            string key = GuidKey(noteId);
            NoteEntity? entity = await db.Notes
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key && !e.IsDeleted, token)
                .ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<NoteRecord> UpdateAsync(Guid noteId, string? title, string? content, IReadOnlyList<string>? tags = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("notes.update", async (db, token) =>
        {
            string key = GuidKey(noteId);
            NoteEntity? entity = await db.Notes
                .FirstOrDefaultAsync(e => e.Id == key && !e.IsDeleted, token)
                .ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Note '{noteId}' not found.");
            DateTimeOffset now = _time.GetUtcNow();
            entity.Title = title ?? entity.Title;
            entity.Content = content ?? entity.Content;
            if (tags is not null)
                entity.Tags = tags.ToList();
            entity.UpdatedAt = now;
            await SaveAsync(db, token).ConfigureAwait(false);
            return Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid noteId, CancellationToken cancellationToken = default) =>
        ExecuteAsync("notes.delete", async (db, token) =>
        {
            string key = GuidKey(noteId);
            int affected = await db.Notes
                .Where(e => e.Id == key && !e.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.IsDeleted, true)
                    .SetProperty(e => e.UpdatedAt, _time.GetUtcNow()), token)
                .ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<NoteRecord>> SearchAsync(string query, IReadOnlyList<string>? tags = null, int limit = 20, CancellationToken cancellationToken = default) =>
        ExecuteAsync<IReadOnlyList<NoteRecord>>("notes.search", async (db, token) =>
        {
            string pattern = query ?? string.Empty;
            List<NoteEntity> entities = await db.Notes
                .AsNoTracking()
                .Where(e => !e.IsDeleted && (pattern == "" || e.Title.Contains(pattern) || e.Content.Contains(pattern)))
                .OrderByDescending(e => e.UpdatedAt)
                .Take(Math.Max(1, limit))
                .ToListAsync(token).ConfigureAwait(false);
            IEnumerable<NoteRecord> results = entities.Select(Map);
            if (tags is { Count: > 0 })
            {
                var required = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
                results = results.Where(n => n.Tags.Any(required.Contains));
            }

            return results.ToList();
        }, cancellationToken);

    private static NoteRecord Map(NoteEntity entity) => new()
    {
        NoteId = Guid.Parse(entity.Id),
        Title = entity.Title,
        Content = entity.Content,
        Tags = entity.Tags,
        CreatedAtUtc = entity.CreatedAt,
        UpdatedAtUtc = entity.UpdatedAt,
    };
}

/// <summary>SQLite финансы владельца (TASK-051) на EF Core.</summary>
public sealed class SqliteFinanceRepository : RepositoryBase, IFinanceRepository
{
    private readonly TimeProvider _time;

    /// <summary>Создаёт репозиторий финансов поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции репозиториев; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    /// <param name="time">Поставщик времени; null — системное время.</param>
    public SqliteFinanceRepository(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger? logger = null,
        UnitOfWorkMetrics? metrics = null,
        TimeProvider? time = null)
        : base(dbFactory, options, logger, metrics)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<FinanceTransactionRecord> AddTransactionAsync(FinanceTransactionKind kind, decimal amount, string currency, string category, string? description = null, DateTimeOffset? occurredAtUtc = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("finance.add", async (db, token) =>
        {
            DateTimeOffset now = _time.GetUtcNow();
            var entity = new FinanceEntity
            {
                Id = Guid.NewGuid().ToString("D"),
                Kind = (int)kind,
                Amount = amount,
                Currency = currency,
                Category = category,
                Description = description,
                OccurredAt = occurredAtUtc ?? now,
                CreatedAt = now,
            };
            db.Finance.Add(entity);
            await SaveAsync(db, token).ConfigureAwait(false);
            return Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
    public async Task<decimal> GetBalanceAsync(string currency, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FinanceTransactionRecord> all = await ListTransactionsAsync(null, null, currency, int.MaxValue, cancellationToken).ConfigureAwait(false);
        return all.Sum(t => t.Kind == FinanceTransactionKind.Income ? t.Amount : -t.Amount);
    }

    /// <inheritdoc />
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
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Currency = currency,
            TotalIncome = items.Where(t => t.Kind == FinanceTransactionKind.Income).Sum(t => t.Amount),
            TotalExpense = items.Where(t => t.Kind == FinanceTransactionKind.Expense).Sum(t => t.Amount),
            Categories = categories,
        };
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FinanceTransactionRecord>> ListTransactionsAsync(DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, string? currency = null, int limit = 50, CancellationToken cancellationToken = default) =>
        ExecuteAsync<IReadOnlyList<FinanceTransactionRecord>>("finance.list", async (db, token) =>
        {
            string? currencyFilter = OptionalFilter(currency);
            List<FinanceEntity> entities = await db.Finance
                .AsNoTracking()
                .Where(e => (fromUtc == null || e.OccurredAt >= fromUtc.Value)
                    && (toUtc == null || e.OccurredAt <= toUtc.Value)
                    && (currencyFilter == null || e.Currency == currencyFilter))
                .OrderByDescending(e => e.OccurredAt)
                .Take(limit == int.MaxValue ? int.MaxValue : Math.Max(1, limit))
                .ToListAsync(token).ConfigureAwait(false);
            return entities.Select(Map).ToList();
        }, cancellationToken);

    private static FinanceTransactionRecord Map(FinanceEntity entity) => new()
    {
        TransactionId = Guid.Parse(entity.Id),
        Kind = (FinanceTransactionKind)entity.Kind,
        Amount = entity.Amount,
        Currency = entity.Currency,
        Category = entity.Category,
        Description = entity.Description,
        OccurredAtUtc = entity.OccurredAt,
        CreatedAtUtc = entity.CreatedAt,
    };
}

/// <summary>SQLite напоминания владельца (TASK-051) на EF Core.</summary>
public sealed class SqliteReminderRepository : RepositoryBase, IReminderRepository
{
    private readonly TimeProvider _time;

    /// <summary>Создаёт репозиторий напоминаний поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции репозиториев; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    /// <param name="time">Поставщик времени; null — системное время.</param>
    public SqliteReminderRepository(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger? logger = null,
        UnitOfWorkMetrics? metrics = null,
        TimeProvider? time = null)
        : base(dbFactory, options, logger, metrics)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<ReminderItemRecord> CreateAsync(string title, DateTimeOffset? dueTimeUtc, string? cronExpression = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("reminders.create", async (db, token) =>
        {
            DateTimeOffset now = _time.GetUtcNow();
            var entity = new ReminderEntity
            {
                Id = Guid.NewGuid().ToString("D"),
                Title = title,
                DueTime = dueTimeUtc,
                CronExpression = cronExpression,
                Status = (int)ReminderStatus.Pending,
                CreatedAt = now,
            };
            db.Reminders.Add(entity);
            await SaveAsync(db, token).ConfigureAwait(false);
            return Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ReminderItemRecord?> GetAsync(Guid reminderId, CancellationToken cancellationToken = default) =>
        ExecuteAsync<ReminderItemRecord?>("reminders.get", async (db, token) =>
        {
            string key = GuidKey(reminderId);
            ReminderEntity? entity = await db.Reminders
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key, token).ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ReminderItemRecord>> ListAsync(ReminderStatus? status = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync<IReadOnlyList<ReminderItemRecord>>("reminders.list", async (db, token) =>
        {
            List<ReminderEntity> entities = await db.Reminders
                .AsNoTracking()
                .Where(e => status == null || e.Status == (int)status.Value)
                .OrderBy(e => e.DueTime == null)
                .ThenBy(e => e.DueTime)
                .ThenBy(e => e.CreatedAt)
                .ToListAsync(token).ConfigureAwait(false);
            return entities.Select(Map).ToList();
        }, cancellationToken);

    /// <inheritdoc />
    public Task<bool> CompleteAsync(Guid reminderId, CancellationToken cancellationToken = default) =>
        SetStatusAsync(reminderId, ReminderStatus.Completed, cancellationToken);

    /// <inheritdoc />
    public Task<bool> CancelAsync(Guid reminderId, CancellationToken cancellationToken = default) =>
        SetStatusAsync(reminderId, ReminderStatus.Cancelled, cancellationToken);

    private Task<bool> SetStatusAsync(Guid id, ReminderStatus status, CancellationToken cancellationToken) =>
        ExecuteAsync("reminders.set_status", async (db, token) =>
        {
            string key = GuidKey(id);
            ReminderEntity? entity = await db.Reminders
                .FirstOrDefaultAsync(e => e.Id == key && e.Status == (int)ReminderStatus.Pending, token)
                .ConfigureAwait(false);
            if (entity is null)
                return false;
            DateTimeOffset now = _time.GetUtcNow();
            entity.Status = (int)status;
            if (status == ReminderStatus.Completed)
                entity.CompletedAt = now;
            await SaveAsync(db, token).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    private static ReminderItemRecord Map(ReminderEntity entity) => new()
    {
        ReminderId = Guid.Parse(entity.Id),
        Title = entity.Title,
        DueTimeUtc = entity.DueTime,
        CronExpression = entity.CronExpression,
        Status = (ReminderStatus)entity.Status,
        CreatedAtUtc = entity.CreatedAt,
        CompletedAtUtc = entity.CompletedAt,
    };
}

/// <summary>SQLite контакты/CRM владельца (TASK-051) на EF Core.</summary>
public sealed class SqliteContactRepository : RepositoryBase, IContactRepository
{
    private readonly TimeProvider _time;

    /// <summary>Создаёт репозиторий контактов поверх фабрики контекстов.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции репозиториев; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; null — без логирования.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    /// <param name="time">Поставщик времени; null — системное время.</param>
    public SqliteContactRepository(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        ILogger? logger = null,
        UnitOfWorkMetrics? metrics = null,
        TimeProvider? time = null)
        : base(dbFactory, options, logger, metrics)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<ContactRecord> UpsertContactAsync(string name, IReadOnlyDictionary<string, string>? channels = null, string? notes = null, Guid? contactId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("contacts.upsert", async (db, token) =>
        {
            DateTimeOffset now = _time.GetUtcNow();
            ContactEntity? existing = contactId.HasValue
                ? await db.Contacts.FirstOrDefaultAsync(
                    e => e.Id == GuidKey(contactId.Value) && !e.IsDeleted, token).ConfigureAwait(false)
                : await db.Contacts.FirstOrDefaultAsync(
                    e => e.Name.ToLower() == name.ToLower() && !e.IsDeleted, token).ConfigureAwait(false);
            if (existing is not null)
            {
                existing.Name = name;
                if (channels is not null)
                    existing.Channels = new Dictionary<string, string>(channels);
                existing.Notes = notes ?? existing.Notes;
                existing.UpdatedAt = now;
                await SaveAsync(db, token).ConfigureAwait(false);
                return Map(existing);
            }

            var entity = new ContactEntity
            {
                Id = GuidKey(contactId ?? Guid.NewGuid()),
                Name = name,
                Channels = new Dictionary<string, string>(channels ?? new Dictionary<string, string>()),
                Notes = notes,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Contacts.Add(entity);
            await SaveAsync(db, token).ConfigureAwait(false);
            return Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ContactRecord?> FindByNameAsync(string name, CancellationToken cancellationToken = default) =>
        ExecuteAsync<ContactRecord?>("contacts.find_by_name", async (db, token) =>
        {
            ContactEntity? entity = await db.Contacts
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Name.ToLower() == name.ToLower() && !e.IsDeleted, token)
                .ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ContactRecord?> GetAsync(Guid contactId, CancellationToken cancellationToken = default) =>
        ExecuteAsync<ContactRecord?>("contacts.get", async (db, token) =>
        {
            string key = GuidKey(contactId);
            ContactEntity? entity = await db.Contacts
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == key && !e.IsDeleted, token)
                .ConfigureAwait(false);
            return entity is null ? null : Map(entity);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ContactRecord>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        ExecuteAsync<IReadOnlyList<ContactRecord>>("contacts.search", async (db, token) =>
        {
            string pattern = query ?? string.Empty;
            List<ContactEntity> entities = await db.Contacts
                .AsNoTracking()
                .Where(e => !e.IsDeleted && (pattern == "" || e.Name.Contains(pattern)))
                .OrderBy(e => e.Name)
                .ToListAsync(token).ConfigureAwait(false);
            return entities.Select(Map).ToList();
        }, cancellationToken);

    /// <inheritdoc />
    public Task<InteractionRecord> AddInteractionAsync(Guid contactId, string channel, string note, DateTimeOffset? occurredAtUtc = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("contacts.add_interaction", async (db, token) =>
        {
            DateTimeOffset at = occurredAtUtc ?? _time.GetUtcNow();
            var entity = new InteractionEntity
            {
                Id = Guid.NewGuid().ToString("D"),
                ContactId = GuidKey(contactId),
                Channel = channel,
                Note = note,
                OccurredAt = at,
            };
            db.Interactions.Add(entity);
            await SaveAsync(db, token).ConfigureAwait(false);
            return new InteractionRecord
            {
                InteractionId = Guid.Parse(entity.Id),
                ContactId = contactId,
                Channel = channel,
                Note = note,
                OccurredAtUtc = at,
            };
        }, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<InteractionRecord>> GetHistoryAsync(Guid contactId, int limit = 50, CancellationToken cancellationToken = default) =>
        ExecuteAsync<IReadOnlyList<InteractionRecord>>("contacts.history", async (db, token) =>
        {
            string key = GuidKey(contactId);
            List<InteractionEntity> entities = await db.Interactions
                .AsNoTracking()
                .Where(e => e.ContactId == key)
                .OrderByDescending(e => e.OccurredAt)
                .Take(Math.Max(1, limit))
                .ToListAsync(token).ConfigureAwait(false);
            return entities.Select(e => new InteractionRecord
            {
                InteractionId = Guid.Parse(e.Id),
                ContactId = Guid.Parse(e.ContactId),
                Channel = e.Channel,
                Note = e.Note,
                OccurredAtUtc = e.OccurredAt,
            }).ToList();
        }, cancellationToken);

    private static ContactRecord Map(ContactEntity entity) => new()
    {
        ContactId = Guid.Parse(entity.Id),
        Name = entity.Name,
        Channels = entity.Channels,
        Notes = entity.Notes,
        CreatedAtUtc = entity.CreatedAt,
        UpdatedAtUtc = entity.UpdatedAt,
    };
}

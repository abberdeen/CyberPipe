using System.Collections.Concurrent;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Personal.Data;

/// <summary>
/// Репозиторий заметок в памяти (TASK-051).
/// </summary>
public sealed class InMemoryNoteRepository : INoteRepository
{
    private readonly ConcurrentDictionary<Guid, NoteRecord> _notes = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryNoteRepository(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<NoteRecord> CreateAsync(string title, string content, IReadOnlyList<string>? tags = null, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        var record = new NoteRecord
        {
            NoteId = Guid.NewGuid(),
            Title = title,
            Content = content,
            Tags = tags ?? [],
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsDeleted = false,
        };

        _notes[record.NoteId] = record;
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<NoteRecord?> GetAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        if (_notes.TryGetValue(noteId, out NoteRecord? note) && !note.IsDeleted)
        {
            return Task.FromResult<NoteRecord?>(note);
        }

        return Task.FromResult<NoteRecord?>(null);
    }

    /// <inheritdoc />
    public Task<NoteRecord> UpdateAsync(Guid noteId, string? title, string? content, IReadOnlyList<string>? tags = null, CancellationToken cancellationToken = default)
    {
        if (!_notes.TryGetValue(noteId, out NoteRecord? existing) || existing.IsDeleted)
        {
            throw new KeyNotFoundException($"Note '{noteId}' not found.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        var updated = existing with
        {
            Title = title ?? existing.Title,
            Content = content ?? existing.Content,
            Tags = tags ?? existing.Tags,
            UpdatedAtUtc = now,
        };

        _notes[noteId] = updated;
        return Task.FromResult(updated);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        if (_notes.TryGetValue(noteId, out NoteRecord? existing) && !existing.IsDeleted)
        {
            _notes[noteId] = existing with { IsDeleted = true, UpdatedAtUtc = _timeProvider.GetUtcNow() };
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<NoteRecord>> SearchAsync(string query, IReadOnlyList<string>? tags = null, int limit = 20, CancellationToken cancellationToken = default)
    {
        string q = query?.Trim() ?? string.Empty;
        var tagSet = tags is { Count: > 0 } ? new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase) : null;

        IReadOnlyList<NoteRecord> matched = _notes.Values
            .Where(n => !n.IsDeleted)
            .Where(n => string.IsNullOrWhiteSpace(q) ||
                        n.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        n.Content.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        n.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .Where(n => tagSet is null || n.Tags.Any(t => tagSet.Contains(t)))
            .OrderByDescending(n => n.UpdatedAtUtc)
            .Take(limit)
            .ToList();

        return Task.FromResult(matched);
    }
}

/// <summary>
/// Репозиторий учёта финансов в памяти (TASK-051).
/// </summary>
public sealed class InMemoryFinanceRepository : IFinanceRepository
{
    private readonly ConcurrentBag<FinanceTransactionRecord> _transactions = [];
    private readonly TimeProvider _timeProvider;

    public InMemoryFinanceRepository(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<FinanceTransactionRecord> AddTransactionAsync(
        FinanceTransactionKind kind,
        decimal amount,
        string currency,
        string category,
        string? description = null,
        DateTimeOffset? occurredAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be strictly positive.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        var record = new FinanceTransactionRecord
        {
            TransactionId = Guid.NewGuid(),
            Kind = kind,
            Amount = amount,
            Currency = currency.Trim().ToUpperInvariant(),
            Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim(),
            Description = description,
            OccurredAtUtc = occurredAtUtc ?? now,
            CreatedAtUtc = now,
        };

        _transactions.Add(record);
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<decimal> GetBalanceAsync(string currency, CancellationToken cancellationToken = default)
    {
        string normCurrency = currency.Trim().ToUpperInvariant();
        decimal income = _transactions
            .Where(t => t.Currency == normCurrency && t.Kind == FinanceTransactionKind.Income)
            .Sum(t => t.Amount);

        decimal expense = _transactions
            .Where(t => t.Currency == normCurrency && t.Kind == FinanceTransactionKind.Expense)
            .Sum(t => t.Amount);

        return Task.FromResult(income - expense);
    }

    /// <inheritdoc />
    public Task<FinanceReportDto> GetReportAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, string currency, CancellationToken cancellationToken = default)
    {
        string normCurrency = currency.Trim().ToUpperInvariant();
        var inRange = _transactions
            .Where(t => t.Currency == normCurrency && t.OccurredAtUtc >= fromUtc && t.OccurredAtUtc <= toUtc)
            .ToList();

        decimal totalIncome = inRange.Where(t => t.Kind == FinanceTransactionKind.Income).Sum(t => t.Amount);
        decimal totalExpense = inRange.Where(t => t.Kind == FinanceTransactionKind.Expense).Sum(t => t.Amount);

        var categories = inRange
            .GroupBy(t => t.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CategorySummaryDto
            {
                Category = g.Key,
                TotalIncome = g.Where(t => t.Kind == FinanceTransactionKind.Income).Sum(t => t.Amount),
                TotalExpense = g.Where(t => t.Kind == FinanceTransactionKind.Expense).Sum(t => t.Amount),
                TransactionCount = g.Count(),
            })
            .OrderByDescending(c => c.TotalExpense + c.TotalIncome)
            .ToList();

        var report = new FinanceReportDto
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Currency = normCurrency,
            TotalIncome = totalIncome,
            TotalExpense = totalExpense,
            Categories = categories,
        };

        return Task.FromResult(report);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FinanceTransactionRecord>> ListTransactionsAsync(
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? currency = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        string? normCurrency = currency?.Trim().ToUpperInvariant();
        IReadOnlyList<FinanceTransactionRecord> list = _transactions
            .Where(t => fromUtc is null || t.OccurredAtUtc >= fromUtc.Value)
            .Where(t => toUtc is null || t.OccurredAtUtc <= toUtc.Value)
            .Where(t => normCurrency is null || t.Currency == normCurrency)
            .OrderByDescending(t => t.OccurredAtUtc)
            .Take(limit)
            .ToList();

        return Task.FromResult(list);
    }
}

/// <summary>
/// Репозиторий напоминаний в памяти (TASK-051).
/// </summary>
public sealed class InMemoryReminderRepository : IReminderRepository
{
    private readonly ConcurrentDictionary<Guid, ReminderItemRecord> _reminders = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryReminderRepository(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<ReminderItemRecord> CreateAsync(
        string title,
        DateTimeOffset? dueTimeUtc,
        string? cronExpression = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title must not be empty.", nameof(title));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        var record = new ReminderItemRecord
        {
            ReminderId = Guid.NewGuid(),
            Title = title,
            DueTimeUtc = dueTimeUtc,
            CronExpression = cronExpression,
            Status = ReminderStatus.Pending,
            CreatedAtUtc = now,
            CompletedAtUtc = null,
        };

        _reminders[record.ReminderId] = record;
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<ReminderItemRecord?> GetAsync(Guid reminderId, CancellationToken cancellationToken = default)
    {
        if (_reminders.TryGetValue(reminderId, out ReminderItemRecord? record))
        {
            return Task.FromResult<ReminderItemRecord?>(record);
        }

        return Task.FromResult<ReminderItemRecord?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReminderItemRecord>> ListAsync(ReminderStatus? status = null, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ReminderItemRecord> list = _reminders.Values
            .Where(r => status is null || r.Status == status.Value)
            .OrderBy(r => r.DueTimeUtc ?? DateTimeOffset.MaxValue)
            .ThenByDescending(r => r.CreatedAtUtc)
            .ToList();

        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public Task<bool> CompleteAsync(Guid reminderId, CancellationToken cancellationToken = default)
    {
        if (_reminders.TryGetValue(reminderId, out ReminderItemRecord? existing) && existing.Status == ReminderStatus.Pending)
        {
            _reminders[reminderId] = existing with
            {
                Status = ReminderStatus.Completed,
                CompletedAtUtc = _timeProvider.GetUtcNow(),
            };
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<bool> CancelAsync(Guid reminderId, CancellationToken cancellationToken = default)
    {
        if (_reminders.TryGetValue(reminderId, out ReminderItemRecord? existing) && existing.Status == ReminderStatus.Pending)
        {
            _reminders[reminderId] = existing with
            {
                Status = ReminderStatus.Cancelled,
                CompletedAtUtc = _timeProvider.GetUtcNow(),
            };
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }
}

/// <summary>
/// Репозиторий контактов и истории CRM в памяти (TASK-051).
/// </summary>
public sealed class InMemoryContactRepository : IContactRepository
{
    private readonly ConcurrentDictionary<Guid, ContactRecord> _contacts = new();
    private readonly ConcurrentBag<InteractionRecord> _interactions = [];
    private readonly TimeProvider _timeProvider;

    public InMemoryContactRepository(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<ContactRecord> UpsertContactAsync(
        string name,
        IReadOnlyDictionary<string, string>? channels = null,
        string? notes = null,
        Guid? contactId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Contact name must not be empty.", nameof(name));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        // Поиск по существующему contactId или по имени
        ContactRecord? existing = null;
        if (contactId.HasValue && _contacts.TryGetValue(contactId.Value, out ContactRecord? byId))
        {
            existing = byId;
        }
        else
        {
            existing = _contacts.Values.FirstOrDefault(c => !c.IsDeleted && c.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (existing is not null)
        {
            var updatedChannels = new Dictionary<string, string>(existing.Channels, StringComparer.OrdinalIgnoreCase);
            if (channels is not null)
            {
                foreach (var (k, v) in channels)
                {
                    updatedChannels[k] = v;
                }
            }

            var updated = existing with
            {
                Name = name.Trim(),
                Channels = updatedChannels,
                Notes = notes ?? existing.Notes,
                UpdatedAtUtc = now,
                IsDeleted = false,
            };

            _contacts[existing.ContactId] = updated;
            return Task.FromResult(updated);
        }

        var created = new ContactRecord
        {
            ContactId = contactId ?? Guid.NewGuid(),
            Name = name.Trim(),
            Channels = channels ?? new Dictionary<string, string>(),
            Notes = notes,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsDeleted = false,
        };

        _contacts[created.ContactId] = created;
        return Task.FromResult(created);
    }

    /// <inheritdoc />
    public Task<ContactRecord?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult<ContactRecord?>(null);
        }

        ContactRecord? found = _contacts.Values
            .FirstOrDefault(c => !c.IsDeleted && c.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(found);
    }

    /// <inheritdoc />
    public Task<ContactRecord?> GetAsync(Guid contactId, CancellationToken cancellationToken = default)
    {
        if (_contacts.TryGetValue(contactId, out ContactRecord? contact) && !contact.IsDeleted)
        {
            return Task.FromResult<ContactRecord?>(contact);
        }

        return Task.FromResult<ContactRecord?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ContactRecord>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        string q = query?.Trim() ?? string.Empty;
        IReadOnlyList<ContactRecord> list = _contacts.Values
            .Where(c => !c.IsDeleted)
            .Where(c => string.IsNullOrWhiteSpace(q) ||
                        c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        (c.Notes?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        c.Channels.Values.Any(val => val.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(c => c.Name)
            .ToList();

        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public Task<InteractionRecord> AddInteractionAsync(
        Guid contactId,
        string channel,
        string note,
        DateTimeOffset? occurredAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        var record = new InteractionRecord
        {
            InteractionId = Guid.NewGuid(),
            ContactId = contactId,
            Channel = string.IsNullOrWhiteSpace(channel) ? "general" : channel.Trim(),
            Note = note,
            OccurredAtUtc = occurredAtUtc ?? now,
        };

        _interactions.Add(record);
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InteractionRecord>> GetHistoryAsync(Guid contactId, int limit = 50, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<InteractionRecord> list = _interactions
            .Where(i => i.ContactId == contactId)
            .OrderByDescending(i => i.OccurredAtUtc)
            .Take(limit)
            .ToList();

        return Task.FromResult(list);
    }
}

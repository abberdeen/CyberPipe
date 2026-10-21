using System.Text.Json;
using CyberPipe.Contracts;
using CyberPipe.Data;

namespace CyberPipe.Skills.Personal.Skills;

#region Notes Skills

/// <summary>Навык создания заметки (TASK-051).</summary>
public sealed class NoteCreateSkill(INoteStoreService repository) : ISkill
{
    public string SkillId => "notes.create";
    public string Name => "Create Note";
    public string Description => "Creates a new personal note with title, content, and optional tags.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        string title = string.Empty;
        string content = string.Empty;
        List<string>? tags = null;

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("title", out JsonElement tProp) && tProp.ValueKind == JsonValueKind.String)
                    {
                        title = tProp.GetString()!.Trim();
                    }

                    if (root.TryGetProperty("content", out JsonElement cProp) && cProp.ValueKind == JsonValueKind.String)
                    {
                        content = cProp.GetString()!.Trim();
                    }
                    else if (root.TryGetProperty("text", out JsonElement txtProp) && txtProp.ValueKind == JsonValueKind.String)
                    {
                        content = txtProp.GetString()!.Trim();
                    }

                    if (root.TryGetProperty("tags", out JsonElement tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
                    {
                        tags = [];
                        foreach (JsonElement tagElem in tagsProp.EnumerateArray())
                        {
                            if (tagElem.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(tagElem.GetString()))
                            {
                                tags.Add(tagElem.GetString()!.Trim());
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                return HostSkillResult.ValidationError("Invalid JSON payload for notes.create.");
            }
        }

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(content))
        {
            return HostSkillResult.ValidationError("Note title or content must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            title = content.Length > 30 ? content[..30] + "..." : content;
        }

        try
        {
            NoteRecord record = await repository.CreateAsync(title, content, tags, cancellationToken);
            string json = JsonSerializer.Serialize(record);
            return HostSkillResult.Ok(json, $"Note '{record.Title}' created.");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to create note: {ex.Message}");
        }
    }
}

/// <summary>Навык текстового поиска по заметкам (TASK-051).</summary>
public sealed class NoteSearchSkill(INoteStoreService repository) : ISkill
{
    public string SkillId => "notes.search";
    public string Name => "Search Notes";
    public string Description => "Performs text search across notes by title, content, and tags.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        string query = string.Empty;
        List<string>? tags = null;
        int limit = 20;

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("query", out JsonElement qProp) && qProp.ValueKind == JsonValueKind.String)
                    {
                        query = qProp.GetString()!;
                    }

                    if (root.TryGetProperty("limit", out JsonElement limProp) && limProp.TryGetInt32(out int limVal))
                    {
                        limit = Math.Clamp(limVal, 1, 100);
                    }

                    if (root.TryGetProperty("tags", out JsonElement tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
                    {
                        tags = [];
                        foreach (JsonElement tagElem in tagsProp.EnumerateArray())
                        {
                            if (tagElem.ValueKind == JsonValueKind.String)
                            {
                                tags.Add(tagElem.GetString()!);
                            }
                        }
                    }
                }
                else if (root.ValueKind == JsonValueKind.String)
                {
                    query = root.GetString()!;
                }
            }
            catch (JsonException)
            {
                return HostSkillResult.ValidationError("Invalid JSON payload for notes.search.");
            }
        }

        try
        {
            IReadOnlyList<NoteRecord> results = await repository.SearchAsync(query, tags, limit, cancellationToken);
            string json = JsonSerializer.Serialize(results);
            return HostSkillResult.Ok(json, $"Found {results.Count} note(s).");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to search notes: {ex.Message}");
        }
    }
}

/// <summary>Навык получения заметки по ID (TASK-051).</summary>
public sealed class NoteGetSkill(INoteStoreService repository) : ISkill
{
    public string SkillId => "notes.get";
    public string Name => "Get Note";
    public string Description => "Gets a specific note by ID.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        Guid? noteId = null;

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("noteId", out JsonElement idProp) &&
                    Guid.TryParse(idProp.GetString(), out Guid parsedId))
                {
                    noteId = parsedId;
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.String &&
                         Guid.TryParse(doc.RootElement.GetString(), out Guid directId))
                {
                    noteId = directId;
                }
            }
            catch (JsonException)
            {
                return HostSkillResult.ValidationError("Invalid JSON payload for notes.get.");
            }
        }

        if (noteId is null)
        {
            return HostSkillResult.ValidationError("Invalid or missing noteId in payload.");
        }

        try
        {
            NoteRecord? note = await repository.GetAsync(noteId.Value, cancellationToken);
            if (note is null)
            {
                return HostSkillResult.Failed("Note not found or deleted.", "Note.NotFound");
            }

            string json = JsonSerializer.Serialize(note);
            return HostSkillResult.Ok(json);
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to get note: {ex.Message}");
        }
    }
}

/// <summary>Навык обновления существующей заметки (TASK-051).</summary>
public sealed class NoteUpdateSkill(INoteStoreService repository) : ISkill
{
    public string SkillId => "notes.update";
    public string Name => "Update Note";
    public string Description => "Updates note title, content, or tags.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        Guid? noteId = null;
        string? title = null;
        string? content = null;
        List<string>? tags = null;

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("noteId", out JsonElement idProp) && Guid.TryParse(idProp.GetString(), out Guid parsedId))
                    {
                        noteId = parsedId;
                    }

                    if (root.TryGetProperty("title", out JsonElement tProp) && tProp.ValueKind == JsonValueKind.String)
                    {
                        title = tProp.GetString()!;
                    }

                    if (root.TryGetProperty("content", out JsonElement cProp) && cProp.ValueKind == JsonValueKind.String)
                    {
                        content = cProp.GetString()!;
                    }

                    if (root.TryGetProperty("tags", out JsonElement tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
                    {
                        tags = [];
                        foreach (JsonElement tagElem in tagsProp.EnumerateArray())
                        {
                            if (tagElem.ValueKind == JsonValueKind.String)
                            {
                                tags.Add(tagElem.GetString()!);
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                return HostSkillResult.ValidationError("Invalid JSON payload for notes.update.");
            }
        }

        if (noteId is null)
        {
            return HostSkillResult.ValidationError("Invalid or missing noteId in payload.");
        }

        try
        {
            NoteRecord updated = await repository.UpdateAsync(noteId.Value, title, content, tags, cancellationToken);
            string json = JsonSerializer.Serialize(updated);
            return HostSkillResult.Ok(json, $"Note '{updated.Title}' updated.");
        }
        catch (KeyNotFoundException)
        {
            return HostSkillResult.Failed("Note not found.", "Note.NotFound");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to update note: {ex.Message}");
        }
    }
}

/// <summary>Навык удаления заметки по ID (TASK-051).</summary>
public sealed class NoteDeleteSkill(INoteStoreService repository) : ISkill
{
    public string SkillId => "notes.delete";
    public string Name => "Delete Note";
    public string Description => "Deletes a note by ID.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        Guid? noteId = null;

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("noteId", out JsonElement idProp) &&
                    Guid.TryParse(idProp.GetString(), out Guid parsedId))
                {
                    noteId = parsedId;
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.String &&
                         Guid.TryParse(doc.RootElement.GetString(), out Guid directId))
                {
                    noteId = directId;
                }
            }
            catch (JsonException)
            {
                return HostSkillResult.ValidationError("Invalid JSON payload for notes.delete.");
            }
        }

        if (noteId is null)
        {
            return HostSkillResult.ValidationError("Invalid or missing noteId in payload.");
        }

        try
        {
            bool deleted = await repository.DeleteAsync(noteId.Value, cancellationToken);
            string json = $"{{\"noteId\":\"{noteId.Value}\",\"deleted\":{(deleted ? "true" : "false")}}}";
            return HostSkillResult.Ok(json, deleted ? "Note deleted." : "Note was not found or already deleted.");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to delete note: {ex.Message}");
        }
    }
}

#endregion

#region Finance Skills

/// <summary>Навык добавления расхода (TASK-051).</summary>
public sealed class FinanceAddExpenseSkill(IFinanceLedgerService repository) : ISkill
{
    private static readonly HashSet<string> AllowedCurrencies = new(StringComparer.OrdinalIgnoreCase) { "RUB", "USD", "EUR" };

    public string SkillId => "finance.add_expense";
    public string Name => "Add Expense";
    public string Description => "Records an expense with amount, currency, category, and optional description.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        decimal? amount = null;
        string currency = "RUB";
        string category = "General";
        string? description = null;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return HostSkillResult.ValidationError("Payload is required for finance.add_expense.");
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("amount", out JsonElement aProp))
                {
                    if (aProp.ValueKind == JsonValueKind.Number && aProp.TryGetDecimal(out decimal aVal))
                    {
                        amount = aVal;
                    }
                    else if (aProp.ValueKind == JsonValueKind.String && decimal.TryParse(aProp.GetString(), out decimal sVal))
                    {
                        amount = sVal;
                    }
                }

                if (root.TryGetProperty("currency", out JsonElement cProp) && !string.IsNullOrWhiteSpace(cProp.GetString()))
                {
                    currency = cProp.GetString()!.Trim().ToUpperInvariant();
                }

                if (root.TryGetProperty("category", out JsonElement catProp) && !string.IsNullOrWhiteSpace(catProp.GetString()))
                {
                    category = catProp.GetString()!.Trim();
                }

                if (root.TryGetProperty("description", out JsonElement dProp))
                {
                    description = dProp.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return HostSkillResult.ValidationError("Invalid JSON payload for finance.add_expense.");
        }

        if (amount is null || amount.Value <= 0)
        {
            return HostSkillResult.ValidationError("Expense amount must be strictly greater than 0.");
        }

        if (!AllowedCurrencies.Contains(currency))
        {
            return HostSkillResult.ValidationError($"Unsupported currency '{currency}'. Supported: {string.Join(", ", AllowedCurrencies)}.");
        }

        try
        {
            FinanceTransactionRecord record = await repository.AddTransactionAsync(
                FinanceTransactionKind.Expense,
                amount.Value,
                currency,
                category,
                description,
                null,
                cancellationToken);

            string json = JsonSerializer.Serialize(record);
            return HostSkillResult.Ok(json, $"Expense of {amount.Value} {currency} added under '{category}'.");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to add expense: {ex.Message}");
        }
    }
}

/// <summary>Навык добавления дохода (TASK-051).</summary>
public sealed class FinanceAddIncomeSkill(IFinanceLedgerService repository) : ISkill
{
    private static readonly HashSet<string> AllowedCurrencies = new(StringComparer.OrdinalIgnoreCase) { "RUB", "USD", "EUR" };

    public string SkillId => "finance.add_income";
    public string Name => "Add Income";
    public string Description => "Records an income transaction with amount, currency, category, and optional description.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        decimal? amount = null;
        string currency = "RUB";
        string category = "Income";
        string? description = null;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return HostSkillResult.ValidationError("Payload is required for finance.add_income.");
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("amount", out JsonElement aProp))
                {
                    if (aProp.ValueKind == JsonValueKind.Number && aProp.TryGetDecimal(out decimal aVal))
                    {
                        amount = aVal;
                    }
                    else if (aProp.ValueKind == JsonValueKind.String && decimal.TryParse(aProp.GetString(), out decimal sVal))
                    {
                        amount = sVal;
                    }
                }

                if (root.TryGetProperty("currency", out JsonElement cProp) && !string.IsNullOrWhiteSpace(cProp.GetString()))
                {
                    currency = cProp.GetString()!.Trim().ToUpperInvariant();
                }

                if (root.TryGetProperty("category", out JsonElement catProp) && !string.IsNullOrWhiteSpace(catProp.GetString()))
                {
                    category = catProp.GetString()!.Trim();
                }

                if (root.TryGetProperty("description", out JsonElement dProp))
                {
                    description = dProp.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return HostSkillResult.ValidationError("Invalid JSON payload for finance.add_income.");
        }

        if (amount is null || amount.Value <= 0)
        {
            return HostSkillResult.ValidationError("Income amount must be strictly greater than 0.");
        }

        if (!AllowedCurrencies.Contains(currency))
        {
            return HostSkillResult.ValidationError($"Unsupported currency '{currency}'. Supported: {string.Join(", ", AllowedCurrencies)}.");
        }

        try
        {
            FinanceTransactionRecord record = await repository.AddTransactionAsync(
                FinanceTransactionKind.Income,
                amount.Value,
                currency,
                category,
                description,
                null,
                cancellationToken);

            string json = JsonSerializer.Serialize(record);
            return HostSkillResult.Ok(json, $"Income of {amount.Value} {currency} added under '{category}'.");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to add income: {ex.Message}");
        }
    }
}

/// <summary>Навык запроса баланса (TASK-051).</summary>
public sealed class FinanceBalanceSkill(IFinanceLedgerService repository) : ISkill
{
    public string SkillId => "finance.balance";
    public string Name => "Get Balance";
    public string Description => "Calculates current net balance for the specified currency.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        string currency = "RUB";

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("currency", out JsonElement cProp) &&
                    !string.IsNullOrWhiteSpace(cProp.GetString()))
                {
                    currency = cProp.GetString()!.Trim().ToUpperInvariant();
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.String)
                {
                    currency = doc.RootElement.GetString()!.Trim().ToUpperInvariant();
                }
            }
            catch (JsonException)
            {
                return HostSkillResult.ValidationError("Invalid JSON payload for finance.balance.");
            }
        }

        try
        {
            decimal balance = await repository.GetBalanceAsync(currency, cancellationToken);
            string json = $"{{\"currency\":\"{currency}\",\"balance\":{balance}}}";
            return HostSkillResult.Ok(json, $"Current balance: {balance} {currency}.");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to get balance: {ex.Message}");
        }
    }
}

/// <summary>Навык построения финансового отчёта за период (TASK-051).</summary>
public sealed class FinanceReportSkill(IFinanceLedgerService repository, TimeProvider? timeProvider = null) : ISkill
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public string SkillId => "finance.report";
    public string Name => "Finance Report";
    public string Description => "Generates financial summary report broken down by categories for a date range.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        string currency = "RUB";
        DateTimeOffset toUtc = _timeProvider.GetUtcNow();
        DateTimeOffset fromUtc = new(toUtc.Year, toUtc.Month, 1, 0, 0, 0, TimeSpan.Zero);

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("currency", out JsonElement cProp) && !string.IsNullOrWhiteSpace(cProp.GetString()))
                    {
                        currency = cProp.GetString()!.Trim().ToUpperInvariant();
                    }

                    if (root.TryGetProperty("from", out JsonElement fromProp) &&
                        DateTimeOffset.TryParse(fromProp.GetString(), out DateTimeOffset parsedFrom))
                    {
                        fromUtc = parsedFrom;
                    }

                    if (root.TryGetProperty("to", out JsonElement toProp) &&
                        DateTimeOffset.TryParse(toProp.GetString(), out DateTimeOffset parsedTo))
                    {
                        toUtc = parsedTo;
                    }
                }
            }
            catch (JsonException)
            {
                return HostSkillResult.ValidationError("Invalid JSON payload for finance.report.");
            }
        }

        if (fromUtc > toUtc)
        {
            return HostSkillResult.ValidationError("Date 'from' cannot be after date 'to'.");
        }

        try
        {
            FinanceReportDto report = await repository.GetReportAsync(fromUtc, toUtc, currency, cancellationToken);
            string json = JsonSerializer.Serialize(report);
            return HostSkillResult.Ok(json, $"Report from {fromUtc:yyyy-MM-dd} to {toUtc:yyyy-MM-dd}: Income={report.TotalIncome}, Expense={report.TotalExpense}, Net={report.NetBalance} {currency}.");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to build report: {ex.Message}");
        }
    }
}

#endregion

#region Reminders Skills

/// <summary>Навык создания персонального напоминания (TASK-051).</summary>
public sealed class ReminderCreateSkill(
    IReminderStoreService repository,
    TimeProvider? timeProvider = null) : ISkill
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public string SkillId => "reminders.create";
    public string Name => "Create Reminder";
    public string Description => "Creates a reminder for a future time or recurring Cron expression.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        string title = string.Empty;
        DateTimeOffset? dueTimeUtc = null;
        string? cron = null;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return HostSkillResult.ValidationError("Payload is required for reminders.create.");
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("title", out JsonElement tProp) && !string.IsNullOrWhiteSpace(tProp.GetString()))
                {
                    title = tProp.GetString()!.Trim();
                }

                if (root.TryGetProperty("dueTime", out JsonElement dtProp) &&
                    DateTimeOffset.TryParse(dtProp.GetString(), out DateTimeOffset parsedDt))
                {
                    dueTimeUtc = parsedDt;
                }
                else if (root.TryGetProperty("dueTimeUtc", out JsonElement dtuProp) &&
                         DateTimeOffset.TryParse(dtuProp.GetString(), out DateTimeOffset parsedDtu))
                {
                    dueTimeUtc = parsedDtu;
                }

                if (root.TryGetProperty("cron", out JsonElement cronProp) && !string.IsNullOrWhiteSpace(cronProp.GetString()))
                {
                    cron = cronProp.GetString()!.Trim();
                }
                else if (root.TryGetProperty("cronExpression", out JsonElement ceProp) && !string.IsNullOrWhiteSpace(ceProp.GetString()))
                {
                    cron = ceProp.GetString()!.Trim();
                }
            }
        }
        catch (JsonException)
        {
            return HostSkillResult.ValidationError("Invalid JSON payload for reminders.create.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return HostSkillResult.ValidationError("Reminder title must not be empty.");
        }

        if (dueTimeUtc is null && string.IsNullOrWhiteSpace(cron))
        {
            return HostSkillResult.ValidationError("Reminder must specify either dueTime or cron schedule.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (dueTimeUtc.HasValue && dueTimeUtc.Value < now)
        {
            return HostSkillResult.ValidationError("Reminder due time cannot be in the past.");
        }

        try
        {
            ReminderItemRecord record = await repository.CreateAsync(title, dueTimeUtc, cron, cancellationToken);
            string json = JsonSerializer.Serialize(record);
            return HostSkillResult.Ok(json, $"Reminder '{title}' created.");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to create reminder: {ex.Message}");
        }
    }
}

/// <summary>Навык получения списка напоминаний (TASK-051).</summary>
public sealed class ReminderListSkill(IReminderStoreService repository) : ISkill
{
    public string SkillId => "reminders.list";
    public string Name => "List Reminders";
    public string Description => "Lists personal reminders with optional status filter.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        ReminderStatus? status = null;

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("status", out JsonElement sProp) &&
                    Enum.TryParse<ReminderStatus>(sProp.GetString(), ignoreCase: true, out ReminderStatus parsed))
                {
                    status = parsed;
                }
            }
            catch (JsonException)
            {
                return HostSkillResult.ValidationError("Invalid JSON payload for reminders.list.");
            }
        }

        try
        {
            IReadOnlyList<ReminderItemRecord> list = await repository.ListAsync(status, cancellationToken);
            string json = JsonSerializer.Serialize(list);
            return HostSkillResult.Ok(json, $"Found {list.Count} reminder(s).");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to list reminders: {ex.Message}");
        }
    }
}

/// <summary>Навык отмены напоминания по ID (TASK-051).</summary>
public sealed class ReminderCancelSkill(IReminderStoreService repository) : ISkill
{
    public string SkillId => "reminders.cancel";
    public string Name => "Cancel Reminder";
    public string Description => "Cancels an active reminder by ID.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        Guid? reminderId = null;

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("reminderId", out JsonElement idProp) &&
                    Guid.TryParse(idProp.GetString(), out Guid parsedId))
                {
                    reminderId = parsedId;
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.String &&
                         Guid.TryParse(doc.RootElement.GetString(), out Guid directId))
                {
                    reminderId = directId;
                }
            }
            catch (JsonException)
            {
                return HostSkillResult.ValidationError("Invalid JSON payload for reminders.cancel.");
            }
        }

        if (reminderId is null)
        {
            return HostSkillResult.ValidationError("Invalid or missing reminderId in payload.");
        }

        try
        {
            bool cancelled = await repository.CancelAsync(reminderId.Value, cancellationToken);
            string json = $"{{\"reminderId\":\"{reminderId.Value}\",\"cancelled\":{(cancelled ? "true" : "false")}}}";
            return HostSkillResult.Ok(json, cancelled ? "Reminder cancelled." : "Reminder not found or already completed/cancelled.");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to cancel reminder: {ex.Message}");
        }
    }
}

/// <summary>Навык отметки напоминания как выполненного (TASK-051).</summary>
public sealed class ReminderCompleteSkill(IReminderStoreService repository) : ISkill
{
    public string SkillId => "reminders.complete";
    public string Name => "Complete Reminder";
    public string Description => "Marks a reminder as completed by ID.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        Guid? reminderId = null;

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("reminderId", out JsonElement idProp) &&
                    Guid.TryParse(idProp.GetString(), out Guid parsedId))
                {
                    reminderId = parsedId;
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.String &&
                         Guid.TryParse(doc.RootElement.GetString(), out Guid directId))
                {
                    reminderId = directId;
                }
            }
            catch (JsonException)
            {
                return HostSkillResult.ValidationError("Invalid JSON payload for reminders.complete.");
            }
        }

        if (reminderId is null)
        {
            return HostSkillResult.ValidationError("Invalid or missing reminderId in payload.");
        }

        try
        {
            bool completed = await repository.CompleteAsync(reminderId.Value, cancellationToken);
            string json = $"{{\"reminderId\":\"{reminderId.Value}\",\"completed\":{(completed ? "true" : "false")}}}";
            return HostSkillResult.Ok(json, completed ? "Reminder marked as completed." : "Reminder not found or not in pending state.");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to complete reminder: {ex.Message}");
        }
    }
}

#endregion

#region CRM Skills

/// <summary>Навык создания или обновления контакта CRM (TASK-051).</summary>
public sealed class CrmContactUpsertSkill(IContactStoreService repository) : ISkill
{
    public string SkillId => "crm.contact_upsert";
    public string Name => "Upsert Contact";
    public string Description => "Creates or updates contact details with communication channels and notes.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        string name = string.Empty;
        var channels = new Dictionary<string, string>();
        string? notes = null;
        Guid? contactId = null;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return HostSkillResult.ValidationError("Payload is required for crm.contact_upsert.");
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("name", out JsonElement nProp) && !string.IsNullOrWhiteSpace(nProp.GetString()))
                {
                    name = nProp.GetString()!.Trim();
                }

                if (root.TryGetProperty("contactId", out JsonElement idProp) && Guid.TryParse(idProp.GetString(), out Guid parsedId))
                {
                    contactId = parsedId;
                }

                if (root.TryGetProperty("notes", out JsonElement notesProp))
                {
                    notes = notesProp.GetString();
                }

                if (root.TryGetProperty("channels", out JsonElement chanProp) && chanProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty prop in chanProp.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            channels[prop.Name] = prop.Value.GetString()!;
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            return HostSkillResult.ValidationError("Invalid JSON payload for crm.contact_upsert.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return HostSkillResult.ValidationError("Contact name must not be empty.");
        }

        try
        {
            ContactRecord contact = await repository.UpsertContactAsync(name, channels, notes, contactId, cancellationToken);
            string json = JsonSerializer.Serialize(contact);
            return HostSkillResult.Ok(json, $"Contact '{contact.Name}' saved.");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to upsert contact: {ex.Message}");
        }
    }
}

/// <summary>Навык поиска контакта CRM (TASK-051).</summary>
public sealed class CrmContactFindSkill(IContactStoreService repository) : ISkill
{
    public string SkillId => "crm.find";
    public string Name => "Find Contact";
    public string Description => "Finds contacts by name or communication channel.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        string query = string.Empty;

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("query", out JsonElement qProp) && !string.IsNullOrWhiteSpace(qProp.GetString()))
                    {
                        query = qProp.GetString()!.Trim();
                    }
                    else if (doc.RootElement.TryGetProperty("name", out JsonElement nProp) && !string.IsNullOrWhiteSpace(nProp.GetString()))
                    {
                        query = nProp.GetString()!.Trim();
                    }
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.String)
                {
                    query = doc.RootElement.GetString()!.Trim();
                }
            }
            catch (JsonException)
            {
                return HostSkillResult.ValidationError("Invalid JSON payload for crm.find.");
            }
        }

        try
        {
            IReadOnlyList<ContactRecord> results = await repository.SearchAsync(query, cancellationToken);
            string json = JsonSerializer.Serialize(results);
            return HostSkillResult.Ok(json, $"Found {results.Count} contact(s).");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to find contacts: {ex.Message}");
        }
    }
}

/// <summary>Навык добавления записи взаимодействия с контактом (TASK-051).</summary>
public sealed class CrmInteractionAddSkill(IContactStoreService repository) : ISkill
{
    public string SkillId => "crm.interaction_add";
    public string Name => "Add CRM Interaction";
    public string Description => "Records an interaction note with a contact.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        Guid? contactId = null;
        string channel = "general";
        string note = string.Empty;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return HostSkillResult.ValidationError("Payload is required for crm.interaction_add.");
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("contactId", out JsonElement idProp) && Guid.TryParse(idProp.GetString(), out Guid parsedId))
                {
                    contactId = parsedId;
                }

                if (root.TryGetProperty("channel", out JsonElement chProp) && !string.IsNullOrWhiteSpace(chProp.GetString()))
                {
                    channel = chProp.GetString()!.Trim();
                }

                if (root.TryGetProperty("note", out JsonElement nProp) && !string.IsNullOrWhiteSpace(nProp.GetString()))
                {
                    note = nProp.GetString()!.Trim();
                }
            }
        }
        catch (JsonException)
        {
            return HostSkillResult.ValidationError("Invalid JSON payload for crm.interaction_add.");
        }

        if (contactId is null)
        {
            return HostSkillResult.ValidationError("Invalid or missing contactId.");
        }

        if (string.IsNullOrWhiteSpace(note))
        {
            return HostSkillResult.ValidationError("Interaction note must not be empty.");
        }

        try
        {
            ContactRecord? contact = await repository.GetAsync(contactId.Value, cancellationToken);
            if (contact is null)
            {
                return HostSkillResult.Failed("Contact not found.", "Contact.NotFound");
            }

            InteractionRecord record = await repository.AddInteractionAsync(contactId.Value, channel, note, null, cancellationToken);
            string json = JsonSerializer.Serialize(record);
            return HostSkillResult.Ok(json, $"Interaction with '{contact.Name}' recorded.");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to add interaction: {ex.Message}");
        }
    }
}

/// <summary>Навык получения истории взаимодействий с контактом (TASK-051).</summary>
public sealed class CrmInteractionHistorySkill(IContactStoreService repository) : ISkill
{
    public string SkillId => "crm.history";
    public string Name => "CRM History";
    public string Description => "Lists interaction history for a contact.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        Guid? contactId = null;
        int limit = 50;

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("contactId", out JsonElement idProp) && Guid.TryParse(idProp.GetString(), out Guid parsedId))
                    {
                        contactId = parsedId;
                    }

                    if (doc.RootElement.TryGetProperty("limit", out JsonElement limProp) && limProp.TryGetInt32(out int limVal))
                    {
                        limit = Math.Clamp(limVal, 1, 200);
                    }
                }
            }
            catch (JsonException)
            {
                return HostSkillResult.ValidationError("Invalid JSON payload for crm.history.");
            }
        }

        if (contactId is null)
        {
            return HostSkillResult.ValidationError("Invalid or missing contactId.");
        }

        try
        {
            IReadOnlyList<InteractionRecord> history = await repository.GetHistoryAsync(contactId.Value, limit, cancellationToken);
            string json = JsonSerializer.Serialize(history);
            return HostSkillResult.Ok(json, $"Found {history.Count} interaction(s).");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to get history: {ex.Message}");
        }
    }
}

#endregion

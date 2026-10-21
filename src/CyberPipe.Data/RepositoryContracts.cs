using System.Text.Json;
using System.Text.Json.Serialization;
using CyberPipe.Contracts;

namespace CyberPipe.Data;

/// <summary>Опции репозиториев TASK-013: порог медленных запросов и пределы пагинации.</summary>
public sealed record RepositoryOptions
{
    /// <summary>Порог логирования медленного запроса; запрос дольше порога пишется Warning без значений параметров.</summary>
    public TimeSpan SlowQueryThreshold { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Максимальный размер страницы; больший Limit отклоняется валидацией.</summary>
    public int MaxPageLimit { get; init; } = 200;

    /// <summary>Проверяет опции; ошибки содержат имена полей.</summary>
    /// <returns>Список ошибок; пуст при валидных опциях.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (SlowQueryThreshold < TimeSpan.Zero)
            errors.Add("Repositories:SlowQueryThreshold must not be negative.");
        if (MaxPageLimit <= 0)
            errors.Add("Repositories:MaxPageLimit must be positive.");
        return errors;
    }
}

/// <summary>
/// Счётчики операций сервисов данных. Один экземпляр регистрируется в DI и
/// разделяется сервисами; конструктор сервиса без метрик создаёт свой.
/// Коммит и откат считает явная транзакция метода (пакетная запись эмбеддингов).
/// Медленный метод считает <c>EfServiceHelper</c>, когда операция дольше порога.
/// </summary>
public interface IDataServiceMetrics
{
    /// <summary>Число зафиксированных явных транзакций методов сервиса.</summary>
    long CommittedTransactions { get; }

    /// <summary>Число откаченных явных транзакций методов сервиса (исключение или отмена).</summary>
    long RolledBackTransactions { get; }

    /// <summary>Число методов сервиса, превысивших порог медленной операции.</summary>
    long SlowQueryCount { get; }
}

/// <summary>Потокобезопасные счётчики <see cref="IDataServiceMetrics" />.</summary>
public sealed class DataServiceMetrics : IDataServiceMetrics
{
    private long _committed;
    private long _rolledBack;
    private long _slowQueries;

    /// <inheritdoc />
    public long CommittedTransactions => Interlocked.Read(ref _committed);

    /// <inheritdoc />
    public long RolledBackTransactions => Interlocked.Read(ref _rolledBack);

    /// <inheritdoc />
    public long SlowQueryCount => Interlocked.Read(ref _slowQueries);

    internal void IncrementCommitted() => Interlocked.Increment(ref _committed);
    internal void IncrementRolledBack() => Interlocked.Increment(ref _rolledBack);
    internal void IncrementSlowQuery() => Interlocked.Increment(ref _slowQueries);
}

/// <summary>
/// Запрос страницы. Сортировка обязательна по контракту: <see cref="SortBy" /> имеет значение
/// по умолчанию и не может быть пустым; допустимые значения — только из белого списка полей
/// конкретного репозитория, неизвестное значение отклоняется до SQL. Направление — флаг, а не
/// текст, поэтому сортировку нельзя выразить конкатенацией пользовательского ввода.
/// </summary>
public sealed record PagedQuery
{
    /// <summary>Смещение первой строки страницы; не может быть отрицательным.</summary>
    public int Offset { get; init; }

    /// <summary>Размер страницы; должен быть положительным и не выше RepositoryOptions.MaxPageLimit.</summary>
    public int Limit { get; init; } = 50;

    /// <summary>Поле сортировки из белого списка репозитория; по умолчанию created_at.</summary>
    public string SortBy { get; init; } = "created_at";

    /// <summary>Сортировка по убыванию; по умолчанию true (сначала новые).</summary>
    public bool Descending { get; init; } = true;

    /// <summary>Проверяет базовые инварианты страницы до обращения к репозиторию.</summary>
    /// <param name="maxLimit">Максимально допустимый размер страницы из опций.</param>
    /// <exception cref="ArgumentException">Пустая сортировка, отрицательный Offset или неположительный Limit.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Limit выше максимума опций.</exception>
    public void Validate(int maxLimit)
    {
        if (Offset < 0)
            throw new ArgumentException("Offset must not be negative.", nameof(Offset));
        if (Limit <= 0)
            throw new ArgumentException("Limit must be positive.", nameof(Limit));
        if (Limit > maxLimit)
            throw new ArgumentOutOfRangeException(nameof(Limit), Limit, $"Limit must not exceed {maxLimit}.");
        if (string.IsNullOrWhiteSpace(SortBy))
            throw new ArgumentException("SortBy must be specified: pagination without sorting is not allowed.", nameof(SortBy));
    }
}

/// <summary>Одна страница результатов с общим числом строк стабильного набора.</summary>
/// <typeparam name="T">Тип строки страницы.</typeparam>
public sealed record PagedResult<T>
{
    /// <summary>Строки страницы в порядке сортировки запроса.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Общее число строк, matching фильтру, без пагинации.</summary>
    public required long TotalCount { get; init; }

    /// <summary>Смещение этой страницы.</summary>
    public required int Offset { get; init; }

    /// <summary>Запрошенный размер страницы.</summary>
    public required int Limit { get; init; }
}

/// <summary>
/// Конкурентное обновление устаревшей версии: запись изменена после чтения
/// (row_version/updated_at не совпал). Молчаливой перезаписи не происходит.
/// </summary>
public sealed class OptimisticConcurrencyException : Exception
{
    /// <summary>Создаёт исключение конкурентного обновления.</summary>
    /// <param name="aggregate">Имя агрегата, например approvals.</param>
    /// <param name="id">Идентификатор записи агрегата.</param>
    public OptimisticConcurrencyException(string aggregate, string id)
        : base($"Optimistic concurrency conflict on {aggregate} '{id}': the record was changed after it was read. Re-read and retry.")
    {
        Aggregate = aggregate;
        Id = id;
    }

    /// <summary>Имя агрегата.</summary>
    public string Aggregate { get; }

    /// <summary>Идентификатор записи.</summary>
    public string Id { get; }
}

/// <summary>Явные опции JSON для jsonb-метаданных репозиториев (TASK-013).</summary>
public static class RepositoryJson
{
    /// <summary>Опции сериализации jsonb: имена как в DTO, перечисления строками, без отступов.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Сериализует значение в JSON для колонки jsonb.</summary>
    /// <typeparam name="T">Тип значения.</typeparam>
    /// <param name="value">Значение.</param>
    /// <returns>JSON-строка.</returns>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Десериализует значение из JSON колонки jsonb.</summary>
    /// <typeparam name="T">Тип значения.</typeparam>
    /// <param name="json">JSON-строка.</param>
    /// <returns>Значение.</returns>
    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidOperationException($"JSON does not contain a {typeof(T).Name} value.");

    /// <summary>Проверяет, что строка — валидный JSON; null заменяется значением по умолчанию.</summary>
    /// <param name="json">Проверяемая строка.</param>
    /// <param name="paramName">Имя параметра для исключения.</param>
    /// <param name="defaultJson">JSON по умолчанию для null.</param>
    /// <returns>Валидный JSON.</returns>
    /// <exception cref="ArgumentException">Строка не является валидным JSON.</exception>
    public static string EnsureJson(string? json, string paramName, string defaultJson = "{}")
    {
        if (json is null)
            return defaultJson;
        try
        {
            using var _ = JsonDocument.Parse(json);
            return json;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Value must be valid JSON.", paramName, ex);
        }
    }
}

/// <summary>Статус записи одобрения в app.approvals.</summary>
public enum ApprovalStatus
{
    /// <summary>Ожидает решения владельца.</summary>
    Pending = 0,

    /// <summary>Одобрено (в том числе с правкой нагрузки).</summary>
    Approved = 1,

    /// <summary>Отклонено владельцем.</summary>
    Rejected = 2,

    /// <summary>Срок решения истёк.</summary>
    Expired = 3,
}

/// <summary>Запись запуска цепочки (app.chain_runs). RowVersion — значение updated_at при чтении.</summary>
public sealed record ChainRunRecord
{
    /// <summary>Идентификатор запуска; Empty — сгенерировать при записи.</summary>
    public RunId Id { get; init; }

    /// <summary>Цепочка запуска.</summary>
    public required ChainId ChainId { get; init; }

    /// <summary>Идентификатор события триггера; может отсутствовать.</summary>
    public Guid? TriggerEventId { get; init; }

    /// <summary>Ключ идемпотентности внутри цепочки; уникальный вместе с ChainId.</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>Статус запуска.</summary>
    public required ChainRunStatus Status { get; init; }

    /// <summary>Источник события запуска.</summary>
    public required string Source { get; init; }

    /// <summary>Нагрузка события в JSON.</summary>
    public string PayloadJson { get; init; } = "{}";

    /// <summary>Момент старта (UTC).</summary>
    public DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>Момент завершения; отсутствует у незавершённых.</summary>
    public DateTimeOffset? FinishedAtUtc { get; init; }

    /// <summary>Ошибка запуска; отсутствует при успехе.</summary>
    public ErrorDto? Error { get; init; }

    /// <summary>Момент создания строки (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Версия строки (updated_at) для оптимистичной конкуренции.</summary>
    public DateTimeOffset RowVersion { get; init; }
}

/// <summary>Запись контрольной точки (app.chain_checkpoints) с её идентификатором и версией строки.</summary>
public sealed record CheckpointRecord
{
    /// <summary>Идентификатор строки чекпоинта; Empty — сгенерировать при записи.</summary>
    public Guid Id { get; init; }

    /// <summary>DTO чекпоинта из TASK-002.</summary>
    public required ChainCheckpointDto Checkpoint { get; init; }

    /// <summary>Момент создания строки (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Версия строки (updated_at) для оптимистичной конкуренции.</summary>
    public DateTimeOffset RowVersion { get; init; }
}

/// <summary>Запись одобрения (app.approvals). RowVersion — значение updated_at при чтении.</summary>
public sealed record ApprovalRecord
{
    /// <summary>Идентификатор одобрения; Empty — сгенерировать при записи.</summary>
    public Guid Id { get; init; }

    /// <summary>Запуск, ожидающий решения; может отсутствовать.</summary>
    public RunId? RunId { get; init; }

    /// <summary>Цепочка, ожидающая решения; может отсутствовать.</summary>
    public ChainId? ChainId { get; init; }

    /// <summary>Шаг цепочки, ожидающий решения.</summary>
    public required string Step { get; init; }

    /// <summary>Статус одобрения.</summary>
    public ApprovalStatus Status { get; init; } = ApprovalStatus.Pending;

    /// <summary>Нагрузка одобрения в JSON.</summary>
    public string PayloadJson { get; init; } = "{}";

    /// <summary>Решение владельца; отсутствует до решения.</summary>
    public HitlDecision? Decision { get; init; }

    /// <summary>Момент решения; отсутствует до решения.</summary>
    public DateTimeOffset? DecidedAtUtc { get; init; }

    /// <summary>Момент истечения срока; может отсутствовать.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; init; }

    /// <summary>Момент создания строки (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Версия строки (updated_at) для оптимистичной конкуренции.</summary>
    public DateTimeOffset RowVersion { get; init; }
}

/// <summary>Запись книги токен-бюджета (app.token_budget_ledger). RowVersion — updated_at при чтении.</summary>
public sealed record TokenBudgetEntryRecord
{
    /// <summary>Идентификатор записи; Empty — сгенерировать при записи.</summary>
    public Guid Id { get; init; }

    /// <summary>Дата бюджета.</summary>
    public required DateOnly BudgetDate { get; init; }

    /// <summary>Идентификатор модели.</summary>
    public required string ModelId { get; init; }

    /// <summary>Размещение модели.</summary>
    public required ModelHostingType HostingType { get; init; }

    /// <summary>Вид записи книги, например usage или reservation; бизнес-правила — TASK-033.</summary>
    public required string EntryType { get; init; }

    /// <summary>Идентификатор запроса; может отсутствовать.</summary>
    public string? RequestId { get; init; }

    /// <summary>Входные токены.</summary>
    public int InputTokens { get; init; }

    /// <summary>Выходные токены.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Стоимость в долларах США.</summary>
    public decimal CostUsd { get; init; }

    /// <summary>Момент создания строки (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Версия строки (updated_at) для оптимистичной конкуренции.</summary>
    public DateTimeOffset RowVersion { get; init; }

    /// <summary>Строит запись книги из фактического расхода TASK-002.</summary>
    /// <param name="usage">Фактический расход вызова модели.</param>
    /// <param name="entryType">Вид записи книги.</param>
    /// <returns>Запись книги на дату вызова (UTC).</returns>
    public static TokenBudgetEntryRecord FromUsage(TokenUsageDto usage, string entryType = "usage")
    {
        ArgumentNullException.ThrowIfNull(usage);
        return new TokenBudgetEntryRecord
        {
            BudgetDate = DateOnly.FromDateTime(usage.OccurredAtUtc.UtcDateTime),
            ModelId = usage.ModelId,
            HostingType = usage.HostingType,
            EntryType = entryType,
            RequestId = usage.RequestId,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            CostUsd = usage.CostUsd,
            CreatedAtUtc = usage.OccurredAtUtc,
        };
    }
}

/// <summary>Дневной итог книги токен-бюджета.</summary>
public sealed record TokenBudgetDailySummary
{
    /// <summary>Дата бюджета.</summary>
    public required DateOnly BudgetDate { get; init; }

    /// <summary>Суммарная стоимость дня в долларах США.</summary>
    public decimal TotalCostUsd { get; init; }

    /// <summary>Суммарные входные токены дня.</summary>
    public long TotalInputTokens { get; init; }

    /// <summary>Суммарные выходные токены дня.</summary>
    public long TotalOutputTokens { get; init; }

    /// <summary>Число записей дня.</summary>
    public long EntryCount { get; init; }
}

/// <summary>Запись настройки ключ-значение (app.kv_settings). RowVersion — updated_at при чтении.</summary>
public sealed record KeyValueSettingRecord
{
    /// <summary>Ключ настройки (первичный ключ).</summary>
    public required string Key { get; init; }

    /// <summary>Значение настройки в JSON (jsonb).</summary>
    public required string ValueJson { get; init; }

    /// <summary>Описание настройки; может отсутствовать.</summary>
    public string? Description { get; init; }

    /// <summary>Момент создания строки (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Версия строки (updated_at) для оптимистичной конкуренции.</summary>
    public DateTimeOffset RowVersion { get; init; }
}

/// <summary>Запись журнала аудита (app.audit_log, append-only по смыслу).</summary>
public sealed record AuditLogRecord
{
    /// <summary>Идентификатор записи; 0 — сгенерировать базой при записи.</summary>
    public long Id { get; init; }

    /// <summary>Кто выполнил действие.</summary>
    public required string Actor { get; init; }

    /// <summary>Что было выполнено.</summary>
    public required string Action { get; init; }

    /// <summary>Тип сущности; может отсутствовать.</summary>
    public string? EntityType { get; init; }

    /// <summary>Идентификатор сущности; может отсутствовать.</summary>
    public string? EntityId { get; init; }

    /// <summary>Детали события в JSON без секретов.</summary>
    public string DetailsJson { get; init; } = "{}";

    /// <summary>Момент записи (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }
}

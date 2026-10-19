namespace CyberPipe.Contracts;

/// <summary>
/// Базовый интерфейс исполняемого навыка системы (TASK-002, TASK-050, TASK-051).
/// </summary>
public interface ISkill
{
    /// <summary>Уникальный идентификатор навыка (например, "notes.create", "media.play").</summary>
    string SkillId { get; }

    /// <summary>Имя навыка для отображения.</summary>
    string Name { get; }

    /// <summary>Описание навыка для модели и документации.</summary>
    string Description { get; }

    /// <summary>Исполняет навык с переданными аргументами в формате JSON.</summary>
    Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default);
}

#region Notes

/// <summary>
/// Запись персональной заметки (TASK-051).
/// </summary>
public sealed record NoteRecord
{
    /// <summary>Уникальный идентификатор заметки.</summary>
    public required Guid NoteId { get; init; }

    /// <summary>Заголовок заметки.</summary>
    public required string Title { get; init; }

    /// <summary>Текстовое содержимое заметки.</summary>
    public required string Content { get; init; }

    /// <summary>Теги заметки.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Время создания заметки (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Время последнего обновления заметки (UTC).</summary>
    public required DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>Признак логического удаления.</summary>
    public bool IsDeleted { get; init; }
}

/// <summary>
/// Репозиторий персональных заметок (TASK-051).
/// </summary>
public interface INoteRepository
{
    /// <summary>Создаёт новую заметку.</summary>
    Task<NoteRecord> CreateAsync(string title, string content, IReadOnlyList<string>? tags = null, CancellationToken cancellationToken = default);

    /// <summary>Возвращает заметку по ID.</summary>
    Task<NoteRecord?> GetAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>Обновляет существующую заметку.</summary>
    Task<NoteRecord> UpdateAsync(Guid noteId, string? title, string? content, IReadOnlyList<string>? tags = null, CancellationToken cancellationToken = default);

    /// <summary>Удаляет заметку по ID.</summary>
    Task<bool> DeleteAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>Выполняет текстовый поиск по заголовкам, содержимому и тегам.</summary>
    Task<IReadOnlyList<NoteRecord>> SearchAsync(string query, IReadOnlyList<string>? tags = null, int limit = 20, CancellationToken cancellationToken = default);
}

#endregion

#region Finance

/// <summary>Вид финансовой транзакции (TASK-051).</summary>
public enum FinanceTransactionKind
{
    /// <summary>Расход.</summary>
    Expense = 0,

    /// <summary>Доход.</summary>
    Income = 1,
}

/// <summary>
/// Запись финансовой транзакции (TASK-051).
/// </summary>
public sealed record FinanceTransactionRecord
{
    /// <summary>Уникальный идентификатор транзакции.</summary>
    public required Guid TransactionId { get; init; }

    /// <summary>Вид транзакции (доход/расход).</summary>
    public required FinanceTransactionKind Kind { get; init; }

    /// <summary>Сумма операции (строго > 0).</summary>
    public required decimal Amount { get; init; }

    /// <summary>Трёхбуквенный ISO-код валюты (например, "RUB", "USD", "EUR").</summary>
    public required string Currency { get; init; }

    /// <summary>Категория транзакции (например, "Еда", "Транспорт", "Зарплата").</summary>
    public required string Category { get; init; }

    /// <summary>Дополнительное описание транзакции.</summary>
    public string? Description { get; init; }

    /// <summary>Фактическое время совершения транзакции (UTC).</summary>
    public required DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>Время регистрации транзакции в системе (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

/// <summary>
/// Агрегированные итоги по категории расходов/доходов (TASK-051).
/// </summary>
public sealed record CategorySummaryDto
{
    /// <summary>Название категории.</summary>
    public required string Category { get; init; }

    /// <summary>Суммарные расходы по категории.</summary>
    public decimal TotalExpense { get; init; }

    /// <summary>Суммарные доходы по категории.</summary>
    public decimal TotalIncome { get; init; }

    /// <summary>Количество операций в категории.</summary>
    public int TransactionCount { get; init; }
}

/// <summary>
/// Финансовый отчёт за период (TASK-051).
/// </summary>
public sealed record FinanceReportDto
{
    /// <summary>Начало периода (UTC).</summary>
    public required DateTimeOffset FromUtc { get; init; }

    /// <summary>Конец периода (UTC).</summary>
    public required DateTimeOffset ToUtc { get; init; }

    /// <summary>Валюта отчёта.</summary>
    public required string Currency { get; init; }

    /// <summary>Общий доход за период.</summary>
    public decimal TotalIncome { get; init; }

    /// <summary>Общий расход за период.</summary>
    public decimal TotalExpense { get; init; }

    /// <summary>Чистый баланс за период (доход минус расход).</summary>
    public decimal NetBalance => TotalIncome - TotalExpense;

    /// <summary>Разбивка по категориям.</summary>
    public IReadOnlyList<CategorySummaryDto> Categories { get; init; } = [];
}

/// <summary>
/// Репозиторий учёта финансов (TASK-051).
/// </summary>
public interface IFinanceRepository
{
    /// <summary>Добавляет финансовую транзакцию.</summary>
    Task<FinanceTransactionRecord> AddTransactionAsync(
        FinanceTransactionKind kind,
        decimal amount,
        string currency,
        string category,
        string? description = null,
        DateTimeOffset? occurredAtUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>Возвращает текущий сальдо/баланс по указанной валюте.</summary>
    Task<decimal> GetBalanceAsync(string currency, CancellationToken cancellationToken = default);

    /// <summary>Строит финансовый отчёт за период.</summary>
    Task<FinanceReportDto> GetReportAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, string currency, CancellationToken cancellationToken = default);

    /// <summary>Возвращает список транзакций за период.</summary>
    Task<IReadOnlyList<FinanceTransactionRecord>> ListTransactionsAsync(
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? currency = null,
        int limit = 50,
        CancellationToken cancellationToken = default);
}

#endregion

#region Reminders

/// <summary>Статус напоминания (TASK-051).</summary>
public enum ReminderStatus
{
    /// <summary>Ожидает наступления срока.</summary>
    Pending = 0,

    /// <summary>Выполнено.</summary>
    Completed = 1,

    /// <summary>Отменено.</summary>
    Cancelled = 2,
}

/// <summary>
/// Запись персонального напоминания (TASK-051).
/// </summary>
public sealed record ReminderItemRecord
{
    /// <summary>Уникальный идентификатор напоминания.</summary>
    public required Guid ReminderId { get; init; }

    /// <summary>Текст напоминания.</summary>
    public required string Title { get; init; }

    /// <summary>Планируемое время срабатывания (UTC) для однократного напоминания.</summary>
    public DateTimeOffset? DueTimeUtc { get; init; }

    /// <summary>Cron-выражение для повторяющегося напоминания.</summary>
    public string? CronExpression { get; init; }

    /// <summary>Статус напоминания.</summary>
    public required ReminderStatus Status { get; init; }

    /// <summary>Время создания (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Время выполнения или отмены (UTC).</summary>
    public DateTimeOffset? CompletedAtUtc { get; init; }
}

/// <summary>
/// Репозиторий персональных напоминаний (TASK-051).
/// </summary>
public interface IReminderRepository
{
    /// <summary>Создаёт напоминание.</summary>
    Task<ReminderItemRecord> CreateAsync(
        string title,
        DateTimeOffset? dueTimeUtc,
        string? cronExpression = null,
        CancellationToken cancellationToken = default);

    /// <summary>Возвращает напоминание по ID.</summary>
    Task<ReminderItemRecord?> GetAsync(Guid reminderId, CancellationToken cancellationToken = default);

    /// <summary>Возвращает список напоминаний с опциональной фильтрацией по статусу.</summary>
    Task<IReadOnlyList<ReminderItemRecord>> ListAsync(ReminderStatus? status = null, CancellationToken cancellationToken = default);

    /// <summary>Помечает напоминание как выполненное.</summary>
    Task<bool> CompleteAsync(Guid reminderId, CancellationToken cancellationToken = default);

    /// <summary>Отменяет напоминание.</summary>
    Task<bool> CancelAsync(Guid reminderId, CancellationToken cancellationToken = default);
}

#endregion

#region CRM

/// <summary>
/// Запись контакта CRM (TASK-051).
/// </summary>
public sealed record ContactRecord
{
    /// <summary>Уникальный идентификатор контакта.</summary>
    public required Guid ContactId { get; init; }

    /// <summary>Имя контакта.</summary>
    public required string Name { get; init; }

    /// <summary>Каналы связи (например, "telegram" -> "@handle", "email" -> "...").</summary>
    public IReadOnlyDictionary<string, string> Channels { get; init; } = new Dictionary<string, string>();

    /// <summary>Заметки и особенности контакта.</summary>
    public string? Notes { get; init; }

    /// <summary>Время создания контакта (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Время последнего обновления контакта (UTC).</summary>
    public required DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>Признак логического удаления.</summary>
    public bool IsDeleted { get; init; }
}

/// <summary>
/// Запись взаимодействия с контактом (TASK-051).
/// </summary>
public sealed record InteractionRecord
{
    /// <summary>Уникальный идентификатор взаимодействия.</summary>
    public required Guid InteractionId { get; init; }

    /// <summary>Идентификатор контакта.</summary>
    public required Guid ContactId { get; init; }

    /// <summary>Канал взаимодействия (например, "telegram", "call", "in_person").</summary>
    public required string Channel { get; init; }

    /// <summary>Заметка о сути взаимодействия.</summary>
    public required string Note { get; init; }

    /// <summary>Время взаимодействия (UTC).</summary>
    public required DateTimeOffset OccurredAtUtc { get; init; }
}

/// <summary>
/// Репозиторий контактов и взаимодействий CRM (TASK-051).
/// </summary>
public interface IContactRepository
{
    /// <summary>Создаёт или обновляет контакт по имени или ID.</summary>
    Task<ContactRecord> UpsertContactAsync(
        string name,
        IReadOnlyDictionary<string, string>? channels = null,
        string? notes = null,
        Guid? contactId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Ищет контакт по точному имени.</summary>
    Task<ContactRecord?> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Возвращает контакт по ID.</summary>
    Task<ContactRecord?> GetAsync(Guid contactId, CancellationToken cancellationToken = default);

    /// <summary>Поиск контактов по строке запроса.</summary>
    Task<IReadOnlyList<ContactRecord>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>Добавляет запись взаимодействия с контактом.</summary>
    Task<InteractionRecord> AddInteractionAsync(
        Guid contactId,
        string channel,
        string note,
        DateTimeOffset? occurredAtUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>Возвращает историю взаимодействий с контактом.</summary>
    Task<IReadOnlyList<InteractionRecord>> GetHistoryAsync(Guid contactId, int limit = 50, CancellationToken cancellationToken = default);
}

#endregion

/// <summary>
/// Реестр персональных навыков владельца (TASK-051).
/// Реализует ISkillInvoker для прозрачной интеграции с роутером и цепочками.
/// </summary>
public interface IPersonalSkillRegistry : ISkillInvoker
{
    /// <summary>Регистрирует новый персональный навык.</summary>
    void RegisterSkill(ISkill skill);

    /// <summary>Ищет зарегистрированный навык по идентификатору.</summary>
    ISkill? FindSkill(string skillId);

    /// <summary>Возвращает все зарегистрированные персональные навыки.</summary>
    IReadOnlyList<ISkill> GetAllSkills();
}

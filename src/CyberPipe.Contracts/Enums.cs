namespace CyberPipe.Contracts;

/// <summary>
/// Ярус маршрутизации запроса: Tier0 — регулярные выражения, Tier1 — эмбеддинги и семантический кэш,
/// Tier2 — лёгкая модель, Tier3 — тяжёлая модель. Потребители: TASK-030..TASK-034.
/// </summary>
public enum IntentTier
{
    /// <summary>Ярус 0: детерминированные регулярные выражения.</summary>
    Tier0 = 0,

    /// <summary>Ярус 1: эмбеддинги и семантический кэш.</summary>
    Tier1 = 1,

    /// <summary>Ярус 2: лёгкая модель.</summary>
    Tier2 = 2,

    /// <summary>Ярус 3: тяжёлая модель.</summary>
    Tier3 = 3,
}

/// <summary>
/// Уровень риска действия. Потребители: TASK-046 (HITL-гейт), TASK-070..TASK-073 (безопасность).
/// </summary>
public enum RiskLevel
{
    /// <summary>Низкий риск.</summary>
    Low = 0,

    /// <summary>Средний риск.</summary>
    Medium = 1,

    /// <summary>Высокий риск.</summary>
    High = 2,

    /// <summary>Критический риск.</summary>
    Critical = 3,
}

/// <summary>
/// Состояние жизненного цикла задачи или запуска. Потребители: TASK-044 (исполнитель цепочек), каналы TASK-060..TASK-062.
/// </summary>
public enum TaskLifecycleState
{
    /// <summary>Создано, ещё не запущено.</summary>
    Created = 0,

    /// <summary>Выполняется.</summary>
    Running = 1,

    /// <summary>Ожидает подтверждения владельца.</summary>
    WaitingApproval = 2,

    /// <summary>Успешно завершено.</summary>
    Completed = 3,

    /// <summary>Завершено с ошибкой.</summary>
    Failed = 4,

    /// <summary>Отменено.</summary>
    Cancelled = 5,
}

/// <summary>
/// Вид триггера цепочки. Известны только четыре значения. Потребители: TASK-040, TASK-041, TASK-044, TASK-045.
/// </summary>
public enum ChainTriggerKind
{
    /// <summary>Входящий вебхук.</summary>
    Webhook = 0,

    /// <summary>Расписание (cron-выражение).</summary>
    Cron = 1,

    /// <summary>Вызов функции из кода или CLI.</summary>
    FunctionCall = 2,

    /// <summary>Сообщение из чата.</summary>
    Chat = 3,
}

/// <summary>
/// Канал связи с владельцем. Потребители: TASK-060 (Telegram), TASK-061 (Desktop), TASK-062 (CLI).
/// </summary>
public enum ChannelKind
{
    /// <summary>Telegram-канал.</summary>
    Telegram = 0,

    /// <summary>Десктопное приложение.</summary>
    Desktop = 1,

    /// <summary>Командная строка.</summary>
    Cli = 2,
}

/// <summary>
/// Решение владельца по HITL-запросу. Потребители: TASK-044, TASK-046, TASK-060, TASK-061.
/// </summary>
public enum HitlDecision
{
    /// <summary>Одобрено без изменений.</summary>
    Approved = 0,

    /// <summary>Одобрено с отредактированной полезной нагрузкой.</summary>
    Edited = 1,

    /// <summary>Отклонено.</summary>
    Rejected = 2,

    /// <summary>Срок ожидания решения истёк.</summary>
    Expired = 3,
}

/// <summary>
/// Размещение модели: локально или в облаке. Потребители: TASK-033 (бюджет), TASK-080..TASK-082 (модели).
/// </summary>
public enum ModelHostingType
{
    /// <summary>Локальная модель.</summary>
    Local = 0,

    /// <summary>Облачная модель удалённого провайдера.</summary>
    RemoteCloud = 1,
}

/// <summary>
/// Категория задачи, для которой рекомендуется модель. Потребители: TASK-080..TASK-082, TASK-031..TASK-033.
/// </summary>
public enum ModelTaskCategory
{
    /// <summary>Лёгкие и быстрые задачи яруса Tier2.</summary>
    LightFastTier2 = 0,

    /// <summary>Тяжёлое рассуждение яруса Tier3.</summary>
    HeavyReasoningTier3 = 1,

    /// <summary>Эмбеддинг яруса Tier1.</summary>
    EmbeddingTier1 = 2,
}

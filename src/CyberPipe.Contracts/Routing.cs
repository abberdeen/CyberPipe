namespace CyberPipe.Contracts;

/// <summary>
/// Запрос маршрутизации: текст и нейтральные метаданные источника.
/// Потребители: TASK-030..TASK-034.
/// </summary>
public sealed record RouteRequest
{
    /// <summary>Текст запроса владельца.</summary>
    public required string Text { get; init; }

    /// <summary>Канал, из которого пришёл запрос.</summary>
    public required ChannelKind Channel { get; init; }

    /// <summary>Локаль запроса, например <c>ru-RU</c>; может отсутствовать.</summary>
    public string? Locale { get; init; }

    /// <summary>Идентификатор отправителя в терминах канала; может отсутствовать.</summary>
    public string? SenderId { get; init; }

    /// <summary>Дополнительные метаданные триггера или канала.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// Статус результата маршрутизации. Потребители: TASK-030..TASK-034.
/// </summary>
public enum RouteStatus
{
    /// <summary>Намерение распознано на этом ярусе.</summary>
    Matched = 0,

    /// <summary>Намерение не распознано: запрос уходит на следующий ярус.</summary>
    Escalate = 1,

    /// <summary>Маршрутизация остановлена отказом бюджета.</summary>
    BudgetDenied = 2,

    /// <summary>Маршрутизация завершена ошибкой.</summary>
    Failed = 3,
}

/// <summary>
/// Результат маршрутизации одного яруса или роутера целиком.
/// Потребители: TASK-030..TASK-034, исполнение цепочек TASK-044.
/// </summary>
public sealed record RouteResult
{
    /// <summary>Статус результата.</summary>
    public required RouteStatus Status { get; init; }

    /// <summary>Ярус, давший результат.</summary>
    public required IntentTier Tier { get; init; }

    /// <summary>Распознанное намерение; отсутствует при эскалации и ошибке.</summary>
    public string? Intent { get; init; }

    /// <summary>Параметры намерения, извлечённые из запроса.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Уверенность результата от 0 до 1.</summary>
    public double Confidence { get; init; }

    /// <summary>Имя сработавшего правила или модели; может отсутствовать.</summary>
    public string? MatchedRuleName { get; init; }

    /// <summary>Диагностическая причина эскалации или ошибки; может отсутствовать.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Роутер намерений: запрос превращается в ярус и результат маршрутизации.
/// Реализация по ярусам — TASK-030..TASK-034; потребители — каналы TASK-060..TASK-062.
/// </summary>
public interface IIntentRouter
{
    /// <summary>Маршрутизирует запрос по ярусам Tier0..Tier3.</summary>
    /// <param name="request">Запрос маршрутизации.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Итоговый результат маршрутизации.</returns>
    Task<RouteResult> RouteAsync(RouteRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Один ярус маршрутизации. Реализации — TASK-030 (Tier0), TASK-031 (Tier1), TASK-032 (Tier2/Tier3).
/// </summary>
public interface IRouterTier
{
    /// <summary>Ярус, который реализует этот компонент.</summary>
    IntentTier Tier { get; }

    /// <summary>Пытается распознать намерение на своём ярусе; при неудаче возвращает эскалацию.</summary>
    /// <param name="request">Запрос маршрутизации.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат яруса.</returns>
    Task<RouteResult> TryRouteAsync(RouteRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Определение правила сопоставления по регулярному выражению для Tier 0 (TASK-030).
/// </summary>
public sealed record RegexRuleDefinition
{
    /// <summary>Имя правила для идентификации и диагностики.</summary>
    public required string Name { get; init; }

    /// <summary>Шаблон регулярного выражения.</summary>
    public required string Pattern { get; init; }

    /// <summary>Намерение (intent), присваиваемое при совпадении.</summary>
    public required string Intent { get; init; }

    /// <summary>Приоритет правила (чем выше, тем раньше сопоставляется).</summary>
    public int Priority { get; init; } = 100;

    /// <summary>Флаг игнорирования регистра символов.</summary>
    public bool IgnoreCase { get; init; } = true;

    /// <summary>Тайм-аут сопоставления в миллисекундах для защиты от катастрофического бэктрекинга.</summary>
    public int TimeoutMs { get; init; } = 200;
}

/// <summary>
/// Поставщик правил для Tier 0 regex-роутера (TASK-030).
/// </summary>
public interface IRegexRuleProvider
{
    /// <summary>Возвращает список правил сопоставления.</summary>
    IReadOnlyList<RegexRuleDefinition> GetRules();
}

/// <summary>
/// Класс модели для вызова через провайдера моделей. Потребитель: TASK-032.
/// </summary>
public enum ChatModelClass
{
    /// <summary>Лёгкая модель яруса Tier2.</summary>
    Light = 0,

    /// <summary>Тяжёлая модель яруса Tier3.</summary>
    Heavy = 1,
}

/// <summary>
/// Запрос к чат-модели через провайдера. Потребители: TASK-032, TASK-081.
/// </summary>
public sealed record ChatModelRequest
{
    /// <summary>Класс запрашиваемой модели.</summary>
    public required ChatModelClass ModelClass { get; init; }

    /// <summary>Системная инструкция; может отсутствовать.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Текст запроса к модели.</summary>
    public required string Prompt { get; init; }
}

/// <summary>
/// Ответ чат-модели с фактическим расходом токенов. Потребители: TASK-032, TASK-033.
/// </summary>
public sealed record ChatModelResponse
{
    /// <summary>Текст ответа модели.</summary>
    public required string Text { get; init; }

    /// <summary>Фактически потраченные входные токены.</summary>
    public int InputTokens { get; init; }

    /// <summary>Фактически потраченные выходные токены.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Идентификатор фактически использованной модели; может отсутствовать.</summary>
    public string? ModelId { get; init; }
}

/// <summary>
/// Провайдер чат-моделей по логическому классу, без привязки к конкретному поставщику.
/// Реализации — TASK-081/TASK-032; потребитель — TASK-032.
/// </summary>
public interface IChatModelProvider
{
    /// <summary>Вызывает модель указанного класса и возвращает текст с расходом токенов.</summary>
    /// <param name="request">Запрос к модели.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Ответ модели.</returns>
    Task<ChatModelResponse> CompleteAsync(
        ChatModelRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Схема структурированного ответа классификации LLM-роутера (TASK-032).
/// </summary>
public sealed record LlmClassificationJson
{
    /// <summary>Распознанное намерение.</summary>
    public string? Intent { get; init; }

    /// <summary>Оценка уверенности классификации от 0.0 до 1.0.</summary>
    public double Confidence { get; init; }

    /// <summary>Извлечённые параметры намерения.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Объяснение решения или причина неуверенности.</summary>
    public string? Reason { get; init; }
}

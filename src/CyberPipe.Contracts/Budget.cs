namespace CyberPipe.Contracts;

/// <summary>
/// Настраиваемые лимиты токен-бюджета. Значения по умолчанию — Q23: дневной кап $0.50,
/// soft 4000 и hard 12000 токенов, 3 облачных и 5 локальных ходов; все значения переопределяет TASK-003.
/// Определение типа — здесь; применение — TASK-033.
/// </summary>
public sealed record BudgetLimitsDto
{
    /// <summary>Лимиты по умолчанию из Q23.</summary>
    public static BudgetLimitsDto Default => new();

    /// <summary>Дневной денежный кап в долларах США.</summary>
    public decimal DailyCapUsd { get; init; } = 0.50m;

    /// <summary>Мягкий лимит токенов на задачу.</summary>
    public int SoftTokenLimit { get; init; } = 4000;

    /// <summary>Жёсткий лимит токенов на задачу.</summary>
    public int HardTokenLimit { get; init; } = 12000;

    /// <summary>Максимум облачных ходов на один запрос.</summary>
    public int MaxCloudTurns { get; init; } = 3;

    /// <summary>Максимум локальных ходов на один запрос.</summary>
    public int MaxLocalTurns { get; init; } = 5;
}

/// <summary>
/// Запрос оценки и резерва бюджета перед вызовом модели. Потребитель: TASK-033.
/// </summary>
public sealed record BudgetRequest
{
    /// <summary>Идентификатор модели из каталога TASK-080.</summary>
    public required string ModelId { get; init; }

    /// <summary>Размещение модели: локально или в облаке.</summary>
    public required ModelHostingType HostingType { get; init; }

    /// <summary>Оценка входных токенов вызова.</summary>
    public int EstimatedInputTokens { get; init; }

    /// <summary>Оценка выходных токенов вызова.</summary>
    public int EstimatedOutputTokens { get; init; }

    /// <summary>Идентификатор запроса или запуска для раздельного учёта ходов; может отсутствовать.</summary>
    public string? RequestId { get; init; }
}

/// <summary>
/// Причина отказа бюджета. Потребитель: TASK-033.
/// </summary>
public enum BudgetDenialReason
{
    /// <summary>Превышен дневной денежный кап.</summary>
    DailyBudgetExceeded = 0,

    /// <summary>Превышен лимит токенов.</summary>
    TokenLimitExceeded = 1,

    /// <summary>Превышен лимит ходов.</summary>
    TurnLimitExceeded = 2,

    /// <summary>Цена облачной модели неизвестна.</summary>
    UnknownModelPrice = 3,
}

/// <summary>
/// Решение губернатора бюджета по запросу оценки. Потребитель: TASK-033, TASK-032.
/// </summary>
public sealed record BudgetDecision
{
    /// <summary>Признак разрешения вызова.</summary>
    public required bool IsAllowed { get; init; }

    /// <summary>Причина отказа; отсутствует при разрешении.</summary>
    public BudgetDenialReason? DenialReason { get; init; }

    /// <summary>Человекочитаемое пояснение решения; может отсутствовать.</summary>
    public string? Message { get; init; }

    /// <summary>Снимок бюджета на момент решения; может отсутствовать.</summary>
    public BudgetStatusDto? Status { get; init; }
}

/// <summary>
/// Фактический расход токенов и стоимости одного вызова модели. Потребитель: TASK-033.
/// </summary>
public sealed record TokenUsageDto
{
    /// <summary>Идентификатор модели из каталога TASK-080.</summary>
    public required string ModelId { get; init; }

    /// <summary>Размещение модели: локально или в облаке.</summary>
    public required ModelHostingType HostingType { get; init; }

    /// <summary>Фактические входные токены.</summary>
    public int InputTokens { get; init; }

    /// <summary>Фактические выходные токены.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Фактическая стоимость вызова в долларах США; локальные модели — 0.</summary>
    public decimal CostUsd { get; init; }

    /// <summary>Момент вызова (UTC).</summary>
    public required DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>Идентификатор запроса или запуска; может отсутствовать.</summary>
    public string? RequestId { get; init; }
}

/// <summary>
/// Снимок состояния бюджета: лимиты, расход и остатки на текущий день.
/// Потребители: TASK-033, отображение TASK-061/TASK-062.
/// </summary>
public sealed record BudgetStatusDto
{
    /// <summary>Действующие лимиты.</summary>
    public required BudgetLimitsDto Limits { get; init; }

    /// <summary>Потрачено сегодня в долларах США.</summary>
    public decimal SpentTodayUsd { get; init; }

    /// <summary>Остаток дневного капа в долларах США.</summary>
    public decimal RemainingTodayUsd { get; init; }

    /// <summary>Использовано токенов сегодня.</summary>
    public int TokensUsedToday { get; init; }

    /// <summary>Использовано облачных ходов в текущем запросе.</summary>
    public int CloudTurnsUsed { get; init; }

    /// <summary>Использовано локальных ходов в текущем запросе.</summary>
    public int LocalTurnsUsed { get; init; }

    /// <summary>Дата снимка в часовом поясе владельца.</summary>
    public required DateOnly BudgetDate { get; init; }
}

/// <summary>
/// Губернатор токен-бюджета: оценка и резерв перед вызовом, учёт факта, снимок остатков.
/// Реализация — TASK-033; потребители — TASK-032, TASK-061, TASK-062.
/// </summary>
public interface ITokenBudgetGovernor
{
    /// <summary>Оценивает вызов и атомарно резервирует бюджет при разрешении.</summary>
    /// <param name="request">Запрос оценки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Решение губернатора.</returns>
    Task<BudgetDecision> TryAuthorizeAsync(
        BudgetRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Учитывает фактический расход и корректирует резерв.</summary>
    /// <param name="usage">Фактический расход вызова.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача учёта.</returns>
    Task RecordUsageAsync(TokenUsageDto usage, CancellationToken cancellationToken = default);

    /// <summary>Возвращает текущий снимок бюджета.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Снимок бюджета.</returns>
    Task<BudgetStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Хранилище записей и агрегатов токен-бюджета (TASK-033).
/// </summary>
public interface IBudgetStore
{
    /// <summary>Возвращает сумму расходов в USD за указанную дату.</summary>
    Task<decimal> GetSpentTodayUsdAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Возвращает количество использованных токенов за указанную дату.</summary>
    Task<int> GetTokensUsedTodayAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Сохраняет запись о фактическом использовании модели.</summary>
    Task RecordUsageAsync(TokenUsageDto usage, CancellationToken cancellationToken = default);
}

/// <summary>
/// Поставщик цен моделей для оценки стоимости перед вызовом (TASK-033).
/// </summary>
public interface IModelPriceProvider
{
    /// <summary>Возвращает цены за 1M токенов (входные, выходные) для модели; null если модель неизвестна.</summary>
    (decimal InputPricePer1mUsd, decimal OutputPricePer1mUsd)? GetModelPricing(string modelId);
}


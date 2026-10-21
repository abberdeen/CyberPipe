namespace CyberPipe.Contracts;

/// <summary>
/// Нормализованное событие триггера, запускающее цепочку. Потребители: TASK-041 (создание), TASK-044 (исполнение).
/// </summary>
public sealed record TriggerEvent
{
    /// <summary>Идентификатор события.</summary>
    public required Guid EventId { get; init; }

    /// <summary>Цепочка, которую запускает событие.</summary>
    public required ChainId ChainId { get; init; }

    /// <summary>Вид триггера события.</summary>
    public required ChainTriggerKind Kind { get; init; }

    /// <summary>Момент события (UTC).</summary>
    public required DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>Полезная нагрузка события в JSON.</summary>
    public required string PayloadJson { get; init; }

    /// <summary>Ссылки на файлы события.</summary>
    public IReadOnlyList<string> FileRefs { get; init; } = [];

    /// <summary>Ключ идемпотентности: повтор с тем же ключом не создаёт второй запуск.</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>Источник события: путь вебхука, идентификатор задания, имя функции или канал и чат.</summary>
    public required string Source { get; init; }
}

/// <summary>
/// Шаг линейной цепочки. Потребитель: TASK-044.
/// </summary>
public enum ChainStepKind
{
    /// <summary>Шаг триггера.</summary>
    Trigger = 0,

    /// <summary>Шаг навыка.</summary>
    Skill = 1,

    /// <summary>Шаг скрипта.</summary>
    Script = 2,

    /// <summary>Шаг HITL-подтверждения.</summary>
    Hitl = 3,

    /// <summary>Шаг доставки.</summary>
    Delivery = 4,
}

/// <summary>
/// Состояние шага цепочки. Потребитель: TASK-044.
/// </summary>
public enum ChainStepState
{
    /// <summary>Шаг ещё не начат.</summary>
    Pending = 0,

    /// <summary>Шаг выполняется.</summary>
    Running = 1,

    /// <summary>Шаг завершён.</summary>
    Completed = 2,

    /// <summary>Шаг завершён ошибкой.</summary>
    Failed = 3,

    /// <summary>Шаг пропущен.</summary>
    Skipped = 4,

    /// <summary>Шаг ожидает подтверждения владельца.</summary>
    WaitingApproval = 5,
}

/// <summary>
/// Состояние запуска цепочки. Потребитель: TASK-044.
/// </summary>
public enum ChainRunStatus
{
    /// <summary>Запуск выполняется.</summary>
    Running = 0,

    /// <summary>Запуск ожидает HITL-решения.</summary>
    WaitingForHitl = 1,

    /// <summary>Запуск ожидает отложенного повтора.</summary>
    WaitingForRetry = 2,

    /// <summary>Запуск успешно завершён.</summary>
    Completed = 3,

    /// <summary>Запуск завершён ошибкой.</summary>
    Failed = 4,

    /// <summary>Запуск отменён.</summary>
    Cancelled = 5,

    /// <summary>Запуск отменён из-за аварийной остановки процесса.</summary>
    CancelledByCrash = 6,
}

/// <summary>
/// Контекст одного запуска цепочки, передаваемый между шагами. Потребитель: TASK-044.
/// </summary>
public sealed record ChainRunContext
{
    /// <summary>Идентификатор запуска.</summary>
    public required RunId RunId { get; init; }

    /// <summary>Определение исполняемой цепочки.</summary>
    public required ChainDefinitionDto Definition { get; init; }

    /// <summary>Событие триггера, создавшее запуск.</summary>
    public required TriggerEvent Trigger { get; init; }

    /// <summary>Момент старта запуска (UTC).</summary>
    public required DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>Переменные запуска, общие для шагов.</summary>
    public IReadOnlyDictionary<string, string> Variables { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// Чекпоинт шага цепочки для хранения в SQLite. Хранение — TASK-011/TASK-044; тип определён здесь.
/// </summary>
public sealed record ChainCheckpointDto
{
    /// <summary>Запуск, которому принадлежит чекпоинт.</summary>
    public required RunId RunId { get; init; }

    /// <summary>Цепочка, которой принадлежит чекпоинт.</summary>
    public required ChainId ChainId { get; init; }

    /// <summary>Шаг, зафиксированный чекпоинтом.</summary>
    public required ChainStepKind Step { get; init; }

    /// <summary>Состояние шага на момент чекпоинта.</summary>
    public required ChainStepState State { get; init; }

    /// <summary>Число попыток шага.</summary>
    public int AttemptCount { get; init; }

    /// <summary>Вход шага в JSON; может отсутствовать.</summary>
    public string? InputJson { get; init; }

    /// <summary>Выход шага в JSON; может отсутствовать.</summary>
    public string? OutputJson { get; init; }

    /// <summary>Ошибка шага; может отсутствовать.</summary>
    public ErrorDto? Error { get; init; }

    /// <summary>Момент записи чекпоинта (UTC).</summary>
    public required DateTimeOffset CheckpointAtUtc { get; init; }
}

/// <summary>
/// Запрос доставки результата цепочки через логический канал. Потребители: TASK-044, TASK-060..TASK-062.
/// </summary>
public sealed record DeliveryRequest
{
    /// <summary>Логический канал доставки.</summary>
    public required ChannelKind Channel { get; init; }

    /// <summary>Адресат доставки в терминах канала.</summary>
    public required string Target { get; init; }

    /// <summary>Текст сообщения доставки; может отсутствовать при доставке только файлов.</summary>
    public string? Text { get; init; }

    /// <summary>Полезная нагрузка результата в JSON; может отсутствовать.</summary>
    public string? PayloadJson { get; init; }

    /// <summary>Файлы результата для отправки.</summary>
    public IReadOnlyList<MessageAttachmentDto> AttachFiles { get; init; } = [];

    /// <summary>Запуск цепочки-источника; может отсутствовать для ручной доставки.</summary>
    public RunId? RunId { get; init; }
}

/// <summary>
/// Результат доставки. Потребители: TASK-044, TASK-060..TASK-062.
/// </summary>
public sealed record DeliveryResult
{
    /// <summary>Признак успешной доставки.</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>Идентификатор доставленного сообщения в канале; может отсутствовать.</summary>
    public MessageId? DeliveredMessageId { get; init; }

    /// <summary>Ошибка доставки; отсутствует при успехе.</summary>
    public ErrorDto? Error { get; init; }

    /// <summary>Момент доставки (UTC); отсутствует при отказе.</summary>
    public DateTimeOffset? DeliveredAtUtc { get; init; }
}

/// <summary>
/// Порт доставки результата цепочки. Реализации каналов — TASK-060..TASK-062; потребитель — TASK-044.
/// </summary>
public interface IDeliveryPort
{
    /// <summary>Доставляет результат через логический канал запроса.</summary>
    /// <param name="request">Запрос доставки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат доставки.</returns>
    Task<DeliveryResult> SendAsync(DeliveryRequest request, CancellationToken cancellationToken = default);
}

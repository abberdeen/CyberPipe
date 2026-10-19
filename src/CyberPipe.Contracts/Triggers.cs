namespace CyberPipe.Contracts;

/// <summary>
/// Порт исполнителя цепочки для триггеров (TASK-041, реализация — TASK-044).
/// </summary>
public interface IChainExecutor
{
    /// <summary>Запускает выполнение цепочки по входящему событию триггера.</summary>
    /// <param name="triggerEvent">Событие триггера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор созданного запуска.</returns>
    Task<RunId> StartAsync(TriggerEvent triggerEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Хранилище ключей идемпотентности для дедупликации запусков цепочек (TASK-041).
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Пытается зарегистрировать ключ идемпотентности в пределах временного окна.
    /// </summary>
    /// <param name="key">Ключ идемпотентности.</param>
    /// <param name="window">Окно хранения ключа.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>True, если ключ новый и успешно зарегистрирован; false, если ключ уже существует (дубликат).</returns>
    Task<bool> TryRegisterAsync(string key, TimeSpan window, CancellationToken cancellationToken = default);
}

/// <summary>
/// Регистратор cron-триггеров в планировщике (TASK-041, реализация — TASK-045).
/// </summary>
public interface ICronTriggerRegistrar
{
    /// <summary>Регистрирует cron-расписание для цепочки.</summary>
    Task RegisterCronAsync(ChainId chainId, string cronExpression, CancellationToken cancellationToken = default);

    /// <summary>Отменяет cron-расписание для цепочки.</summary>
    Task UnregisterCronAsync(ChainId chainId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Интерфейс вызова триггеров цепочек (FunctionCall) (TASK-041).
/// </summary>
public interface ITriggerInvoker
{
    /// <summary>
    /// Вызывает цепочку вида FunctionCall.
    /// </summary>
    /// <param name="chainId">Идентификатор цепочки.</param>
    /// <param name="payloadJson">Полезная нагрузка в JSON.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор запуска.</returns>
    Task<RunId> InvokeFunctionCallAsync(ChainId chainId, string payloadJson, CancellationToken cancellationToken = default);
}

/// <summary>
/// Запрос к обработчику вебхуков (TASK-041).
/// </summary>
public sealed record WebhookRequest
{
    /// <summary>Идентификатор цепочки из пути URL.</summary>
    public required ChainId ChainId { get; init; }

    /// <summary>Относительный путь вебхука.</summary>
    public required string Path { get; init; }

    /// <summary>Тело запроса.</summary>
    public required string Body { get; init; }

    /// <summary>Заголовки запроса.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Ответ обработчика вебхуков (TASK-041).
/// </summary>
public sealed record WebhookResponse
{
    /// <summary>HTTP-код ответа.</summary>
    public required int StatusCode { get; init; }

    /// <summary>Идентификатор запуска цепочки, если запуск был создан.</summary>
    public RunId? RunId { get; init; }

    /// <summary>Сообщение об ошибке или статусе.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Реестр триггеров цепочек (TASK-041).
/// </summary>
public interface ITriggerRegistry
{
    /// <summary>Регистрирует активные триггеры цепочки.</summary>
    Task RegisterAsync(ChainDefinitionDto chain, CancellationToken cancellationToken = default);

    /// <summary>Снимает регистрацию всех триггеров цепочки.</summary>
    Task UnregisterAsync(ChainId chainId, CancellationToken cancellationToken = default);

    /// <summary>Проверяет, зарегистрирована ли цепочка.</summary>
    bool IsRegistered(ChainId chainId);
}

/// <summary>
/// Исключение при отсутствии запрошенной цепочки в триггерах (TASK-041).
/// </summary>
public sealed class ChainNotFoundException : Exception
{
    /// <summary>Идентификатор цепочки.</summary>
    public ChainId ChainId { get; }

    /// <summary>Создаёт исключение отсутствия цепочки.</summary>
    public ChainNotFoundException(ChainId chainId)
        : base($"Chain '{chainId}' was not found.")
    {
        ChainId = chainId;
    }
}

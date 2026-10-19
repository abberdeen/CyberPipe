namespace CyberPipe.Contracts;

/// <summary>
/// Обработчик типизированного сообщения шины. Реализации регистрируют потребители сообщений;
/// поставщик абстракции — TASK-090, потребители — TASK-041, TASK-060..TASK-062.
/// </summary>
/// <typeparam name="TMessage">Тип обрабатываемого сообщения.</typeparam>
public interface IMessageHandler<TMessage>
{
    /// <summary>Обрабатывает одно сообщение.</summary>
    /// <param name="message">Сообщение.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача обработки.</returns>
    Task HandleAsync(TMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Собственная шина сообщений проекта: публикация и подписка на типизированные сообщения
/// без зависимости от реализации транспорта. Реализация — TASK-090; потребители — TASK-041, TASK-044, TASK-060.
/// </summary>
public interface IMessageBus
{
    /// <summary>Публикует сообщение всем подписчикам его типа.</summary>
    /// <typeparam name="TMessage">Тип сообщения.</typeparam>
    /// <param name="message">Сообщение.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача публикации.</returns>
    Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default);

    /// <summary>Подписывает обработчик на сообщения указанного типа.</summary>
    /// <typeparam name="TMessage">Тип сообщения.</typeparam>
    /// <param name="handler">Обработчик сообщений.</param>
    /// <returns>Объект, отменяющий подписку при уничтожении.</returns>
    IDisposable Subscribe<TMessage>(IMessageHandler<TMessage> handler);
}

/// <summary>
/// Конверт сообщения для транспорта: только нейтральные поля проекта, без типов внешних систем.
/// Потребители: реализации транспорта в TASK-090.
/// </summary>
public sealed record MessageEnvelope
{
    /// <summary>Идентификатор сообщения.</summary>
    public required MessageId Id { get; init; }

    /// <summary>Полное имя типа сообщения.</summary>
    public required string MessageType { get; init; }

    /// <summary>Полезная нагрузка в JSON.</summary>
    public required string PayloadJson { get; init; }

    /// <summary>Момент создания сообщения (UTC).</summary>
    public required DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>Идентификатор корреляции связанной цепочки вызовов; может отсутствовать.</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// Точка замены реализации транспорта сообщений. Конкретная реализация выбирается в TASK-090;
/// бизнес-логика зависит только от этого интерфейса и <see cref="IMessageBus" />.
/// </summary>
public interface IMessageTransport
{
    /// <summary>Отправляет конверт сообщения через выбранную реализацию транспорта.</summary>
    /// <param name="envelope">Конверт сообщения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача отправки.</returns>
    Task SendAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>Подписывает обработчик конвертов на входящие сообщения транспорта.</summary>
    /// <param name="handler">Обработчик конвертов.</param>
    /// <returns>Объект, отменяющий подписку при уничтожении.</returns>
    IDisposable Subscribe(IMessageHandler<MessageEnvelope> handler);
}

/// <summary>
/// Источник нормализованных входящих сообщений каналов. Потребители: TASK-041 (чат-триггеры), TASK-060..TASK-062.
/// </summary>
public interface IInboundMessageSource
{
    /// <summary>Подписывает обработчик на входящие сообщения каналов.</summary>
    /// <param name="handler">Обработчик входящих сообщений.</param>
    /// <returns>Объект, отменяющий подписку при уничтожении.</returns>
    IDisposable Subscribe(IMessageHandler<IncomingMessage> handler);
}

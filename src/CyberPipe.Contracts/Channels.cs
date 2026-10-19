namespace CyberPipe.Contracts;

/// <summary>
/// Вложение сообщения канала: только метаданные, без привязки к API конкретного канала.
/// Потребители: TASK-060 (Telegram), TASK-061 (Desktop), TASK-062 (CLI).
/// </summary>
public sealed record MessageAttachmentDto
{
    /// <summary>Имя файла вложения.</summary>
    public required string FileName { get; init; }

    /// <summary>Медиатип вложения, например <c>image/jpeg</c>.</summary>
    public required string ContentType { get; init; }

    /// <summary>Размер вложения в байтах; может отсутствовать, если канал его не сообщил.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>Идентификатор или ссылка на файл в канале; может отсутствовать.</summary>
    public string? FileRef { get; init; }
}

/// <summary>
/// Кнопка действия в исходящем сообщении. Потребители: TASK-046 (HITL), TASK-060, TASK-061.
/// </summary>
public sealed record MessageActionButtonDto
{
    /// <summary>Стабильный идентификатор действия кнопки.</summary>
    public required string ActionId { get; init; }

    /// <summary>Видимый текст кнопки.</summary>
    public required string Text { get; init; }

    /// <summary>Полезная нагрузка действия; может отсутствовать.</summary>
    public string? Payload { get; init; }
}

/// <summary>
/// Нормализованное входящее сообщение канала. Потребители: роутер TASK-030..TASK-034,
/// чат-триггеры TASK-041, каналы TASK-060..TASK-062; проверка владельца — TASK-073.
/// </summary>
public sealed record IncomingMessage
{
    /// <summary>Идентификатор сообщения.</summary>
    public required MessageId Id { get; init; }

    /// <summary>Канал, из которого пришло сообщение.</summary>
    public required ChannelKind Channel { get; init; }

    /// <summary>Идентификатор отправителя в терминах канала (как есть, без интерпретации).</summary>
    public required string SenderId { get; init; }

    /// <summary>Идентификатор чата или диалога в терминах канала; может отсутствовать.</summary>
    public string? ChatId { get; init; }

    /// <summary>Текст сообщения; может отсутствовать при сообщении только с вложением.</summary>
    public string? Text { get; init; }

    /// <summary>Вложения сообщения.</summary>
    public IReadOnlyList<MessageAttachmentDto> Attachments { get; init; } = [];

    /// <summary>Момент отправки сообщения (UTC).</summary>
    public required DateTimeOffset SentAtUtc { get; init; }
}

/// <summary>
/// Исходящее сообщение канала. Потребители: доставка TASK-044, каналы TASK-060..TASK-062.
/// </summary>
public sealed record OutgoingMessage
{
    /// <summary>Канал доставки.</summary>
    public required ChannelKind Channel { get; init; }

    /// <summary>Адресат в терминах канала: чат, пользователь или локальный вывод.</summary>
    public required string Target { get; init; }

    /// <summary>Текст сообщения; может отсутствовать при сообщении только с вложением.</summary>
    public string? Text { get; init; }

    /// <summary>Вложения сообщения.</summary>
    public IReadOnlyList<MessageAttachmentDto> Attachments { get; init; } = [];

    /// <summary>Кнопки действий под сообщением.</summary>
    public IReadOnlyList<MessageActionButtonDto> ActionButtons { get; init; } = [];

    /// <summary>Идентификатор сообщения, на которое это является ответом; может отсутствовать.</summary>
    public MessageId? ReplyToMessageId { get; init; }
}

/// <summary>
/// Запрос HITL-подтверждения владельцу. Создаётся TASK-044, обрабатывается TASK-046,
/// отображается каналами TASK-060 и TASK-061.
/// </summary>
public sealed record HitlApprovalRequest
{
    /// <summary>Идентификатор запроса подтверждения.</summary>
    public required Guid ApprovalId { get; init; }

    /// <summary>Запуск цепочки, ожидающий решения.</summary>
    public required RunId RunId { get; init; }

    /// <summary>Цепочка, ожидающая решения.</summary>
    public required ChainId ChainId { get; init; }

    /// <summary>Заголовок карточки подтверждения.</summary>
    public required string Title { get; init; }

    /// <summary>Текст предпросмотра результата, который уйдёт в доставку.</summary>
    public required string PreviewText { get; init; }

    /// <summary>Полезная нагрузка доставки в JSON.</summary>
    public required string PayloadJson { get; init; }

    /// <summary>Файлы, прилагаемые к карточке подтверждения.</summary>
    public IReadOnlyList<MessageAttachmentDto> AttachFiles { get; init; } = [];

    /// <summary>Момент создания запроса (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Момент истечения срока решения (UTC).</summary>
    public required DateTimeOffset ExpiresAtUtc { get; init; }

    /// <summary>Действие, требующее подтверждения, в машинном виде; может отсутствовать.</summary>
    public string? Action { get; init; }

    /// <summary>Дополнительный контекст действия для владельца; может отсутствовать.</summary>
    public string? Context { get; init; }

    /// <summary>Уровень риска действия.</summary>
    public RiskLevel Risk { get; init; } = RiskLevel.Medium;
}

/// <summary>
/// Результат HITL-подтверждения: решение владельца и его контекст.
/// Потребители: TASK-044 (продолжение цепочки), TASK-046.
/// </summary>
public sealed record HitlApprovalResult
{
    /// <summary>Идентификатор запроса подтверждения.</summary>
    public required Guid ApprovalId { get; init; }

    /// <summary>Решение владельца.</summary>
    public required HitlDecision Decision { get; init; }

    /// <summary>Отредактированная нагрузка при решении <see cref="HitlDecision.Edited" />; иначе отсутствует.</summary>
    public string? EditedPayloadJson { get; init; }

    /// <summary>Идентификатор владельца, принявшего решение.</summary>
    public required string OwnerId { get; init; }

    /// <summary>Канал, через который принято решение.</summary>
    public required ChannelKind Channel { get; init; }

    /// <summary>Момент принятия решения (UTC).</summary>
    public required DateTimeOffset DecidedAtUtc { get; init; }

    /// <summary>Причина отклонения; может отсутствовать.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Порт HITL-гейта между шагами Script и Delivery. Реализация — TASK-046; потребитель — TASK-044.
/// </summary>
public interface IHitlApprovalGate
{
    /// <summary>Запрашивает подтверждение владельца и возвращает его решение.</summary>
    /// <param name="request">Запрос подтверждения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат подтверждения.</returns>
    Task<HitlApprovalResult> RequestApprovalAsync(
        HitlApprovalRequest request,
        CancellationToken cancellationToken = default);
}

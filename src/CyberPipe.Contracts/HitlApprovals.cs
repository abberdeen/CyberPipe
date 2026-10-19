namespace CyberPipe.Contracts;

/// <summary>
/// Статус жизненного цикла HITL-запроса (TASK-046).
/// </summary>
public enum HitlStatus
{
    /// <summary>Ожидает решения владельца.</summary>
    Pending = 0,

    /// <summary>Решение владельца принято (Approved, Edited, Rejected).</summary>
    Resolved = 1,

    /// <summary>Срок ожидания решения истёк.</summary>
    Expired = 2,
}

/// <summary>
/// Запись HITL-подтверждения для хранения в репозитории (TASK-046).
/// </summary>
public sealed record HitlApprovalRecord
{
    /// <summary>Идентификатор запроса подтверждения.</summary>
    public required Guid ApprovalId { get; init; }

    /// <summary>Запуск цепочки, ожидающий решения.</summary>
    public required RunId RunId { get; init; }

    /// <summary>Цепочка, ожидающая решения.</summary>
    public required ChainId ChainId { get; init; }

    /// <summary>Заголовок карточки подтверждения.</summary>
    public required string Title { get; init; }

    /// <summary>Текст предпросмотра результата.</summary>
    public required string PreviewText { get; init; }

    /// <summary>Полезная нагрузка в JSON.</summary>
    public required string PayloadJson { get; init; }

    /// <summary>Прикреплённые файлы.</summary>
    public IReadOnlyList<MessageAttachmentDto> AttachFiles { get; init; } = [];

    /// <summary>Момент создания запроса (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Момент истечения срока (UTC).</summary>
    public required DateTimeOffset ExpiresAtUtc { get; init; }

    /// <summary>Текущий статус запроса.</summary>
    public HitlStatus Status { get; init; } = HitlStatus.Pending;

    /// <summary>Принятое решение; null для статуса Pending.</summary>
    public HitlApprovalResult? Resolution { get; init; }
}

/// <summary>
/// Репозиторий хранения и атомарного разрешения HITL-подтверждений (TASK-046).
/// </summary>
public interface IHitlApprovalRepository
{
    /// <summary>Создаёт новую запись подтверждения.</summary>
    Task<HitlApprovalRecord> CreateAsync(HitlApprovalRecord record, CancellationToken cancellationToken = default);

    /// <summary>Возвращает запись подтверждения по идентификатору или null, если не найдена.</summary>
    Task<HitlApprovalRecord?> GetAsync(Guid approvalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Атомарно переводит статус запроса из Pending в Resolved/Expired и сохраняет решение.
    /// Если запрос уже разрешён или истёк, возвращает false без изменения состояния.
    /// </summary>
    Task<bool> TryResolveAsync(Guid approvalId, HitlApprovalResult result, CancellationToken cancellationToken = default);

    /// <summary>Возвращает список просроченных запросов в статусе Pending.</summary>
    Task<IReadOnlyList<HitlApprovalRecord>> ListPendingExpiredAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// Порт отправки карточки подтверждения в конкретный канал связи (TASK-046).
/// </summary>
public interface IHitlCardSender
{
    /// <summary>Канал связи, за который отвечает отправитель.</summary>
    ChannelKind Channel { get; }

    /// <summary>Отправляет карточку подтверждения владельцу.</summary>
    Task SendCardAsync(HitlApprovalRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Порт проверки принадлежности пользователя к владельцу системы (TASK-046, TASK-073).
/// </summary>
public interface IOwnerGuard
{
    /// <summary>Проверяет, является ли пользователь владельцем системы.</summary>
    Task<bool> IsOwnerAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Базовое исключение для ошибок модуля HITL (TASK-046).
/// </summary>
public abstract class HitlException : Exception
{
    /// <summary>Машиночитаемый код ошибки.</summary>
    public string Code { get; }

    protected HitlException(string code, string message) : base(message)
    {
        Code = code;
    }
}

/// <summary>Ошибка отказа в доступе (пользователь не является Owner).</summary>
public sealed class HitlAccessDeniedException : HitlException
{
    public HitlAccessDeniedException(string message) : base("Hitl.AccessDenied", message) { }
}

/// <summary>Ошибка повторного решения по уже разрешённому запросу.</summary>
public sealed class HitlAlreadyResolvedException : HitlException
{
    public HitlAlreadyResolvedException(string message) : base("Hitl.AlreadyResolved", message) { }
}

/// <summary>Ошибка действия по просроченному запросу.</summary>
public sealed class HitlExpiredException : HitlException
{
    public HitlExpiredException(string message) : base("Hitl.Expired", message) { }
}

/// <summary>Ошибка невалидной отредактированной нагрузки по схеме пакета.</summary>
public sealed class HitlInvalidEditedPayloadException : HitlException
{
    public HitlInvalidEditedPayloadException(string message) : base("Hitl.InvalidEditedPayload", message) { }
}

/// <summary>Ошибка: запрос подтверждения не найден.</summary>
public sealed class HitlNotFoundException : HitlException
{
    public HitlNotFoundException(string message) : base("Hitl.NotFound", message) { }
}

/// <summary>
/// Сервис управления жизненным циклом и решениями HITL-подтверждений (TASK-046).
/// </summary>
public interface IHitlApprovalService
{
    /// <summary>Создаёт запрос подтверждения и рассылает карточки по доступным каналам.</summary>
    Task<HitlApprovalRecord> CreateAsync(HitlApprovalRequest request, CancellationToken cancellationToken = default);

    /// <summary>Принимает решение Approve владельца и возобновляет выполнение цепочки.</summary>
    Task<HitlApprovalResult> ApproveAsync(Guid approvalId, string ownerId, ChannelKind channel, CancellationToken cancellationToken = default);

    /// <summary>Принимает решение Edit владельца с валидацией схемы и возобновляет выполнение цепочки.</summary>
    Task<HitlApprovalResult> EditAsync(Guid approvalId, string ownerId, string editedPayloadJson, ChannelKind channel, CancellationToken cancellationToken = default);

    /// <summary>Принимает решение Reject владельца и отменяет запуск цепочки.</summary>
    Task<HitlApprovalResult> RejectAsync(Guid approvalId, string ownerId, ChannelKind channel, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>Переводит просроченные запросы в статус Expired и отменяет их запуски по таймауту.</summary>
    Task<int> ExpireOverdueAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}

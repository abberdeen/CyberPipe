namespace CyberPipe.Data;

/// <summary>Итоговый статус bootstrap PostgreSQL.</summary>
public enum BootstrapStatus
{
    /// <summary>Сервер доступен, база и расширения готовы.</summary>
    Ready = 0,

    /// <summary>Нужен пароль пользователя; Service не падает, а ждёт ввода (TASK-063).</summary>
    NeedsPassword = 1,

    /// <summary>Подключение невозможно по причине, не связанной с вводом пароля.</summary>
    Unavailable = 2,
}

/// <summary>Машинный код причины результата bootstrap. Тексты с секретами сюда не попадают.</summary>
public enum BootstrapReasonCode
{
    /// <summary>Причины нет: подключение готово.</summary>
    None = 0,

    /// <summary>Сервер не запущен или недоступен по сети.</summary>
    ServerUnavailable = 1,

    /// <summary>Пароль отсутствует или неверен.</summary>
    AuthenticationFailed = 2,

    /// <summary>Пароль не задан, а запросить его не у кого (нет промпта).</summary>
    PasswordRequired = 3,

    /// <summary>У пользователя нет прав на создание базы или расширения.</summary>
    InsufficientPrivilege = 4,

    /// <summary>Расширение vector (pgvector) не установлено на сервере.</summary>
    PgVectorMissing = 5,

    /// <summary>Подключение или команда превысили тайм-аут.</summary>
    Timeout = 6,

    /// <summary>Неизвестная причина; детали намеренно не раскрываются.</summary>
    Unknown = 7,
}

/// <summary>
/// Результат bootstrap без секретов: ни пароля, ни строки подключения в полях нет.
/// </summary>
public sealed record BootstrapResult
{
    /// <summary>Статус bootstrap.</summary>
    public required BootstrapStatus Status { get; init; }

    /// <summary>Код причины для Unavailable/NeedsPassword; None при Ready.</summary>
    public BootstrapReasonCode ReasonCode { get; init; } = BootstrapReasonCode.None;

    /// <summary>Версия сервера PostgreSQL при Ready; иначе null.</summary>
    public string? ServerVersion { get; init; }

    /// <summary>Версия расширения vector при Ready; иначе null.</summary>
    public string? PgVectorVersion { get; init; }

    /// <summary>Человекочитаемая причина без секретов и строки подключения.</summary>
    public string? Detail { get; init; }

    /// <summary>Создаёт результат Ready.</summary>
    /// <param name="serverVersion">Версия сервера.</param>
    /// <param name="pgVectorVersion">Версия расширения vector.</param>
    /// <returns>Результат Ready.</returns>
    public static BootstrapResult Ready(string serverVersion, string? pgVectorVersion) => new()
    {
        Status = BootstrapStatus.Ready,
        ReasonCode = BootstrapReasonCode.None,
        ServerVersion = serverVersion,
        PgVectorVersion = pgVectorVersion,
    };

    /// <summary>Создаёт результат NeedsPassword.</summary>
    /// <param name="reasonCode">Код причины.</param>
    /// <returns>Результат NeedsPassword.</returns>
    public static BootstrapResult NeedsPassword(BootstrapReasonCode reasonCode) => new()
    {
        Status = BootstrapStatus.NeedsPassword,
        ReasonCode = reasonCode,
        Detail = "PostgreSQL password is required.",
    };

    /// <summary>Создаёт результат Unavailable.</summary>
    /// <param name="reasonCode">Код причины.</param>
    /// <returns>Результат Unavailable.</returns>
    public static BootstrapResult Unavailable(BootstrapReasonCode reasonCode) => new()
    {
        Status = BootstrapStatus.Unavailable,
        ReasonCode = reasonCode,
        Detail = $"PostgreSQL is unavailable: {reasonCode}.",
    };
}

/// <summary>
/// Недоступность PostgreSQL с машинным кодом причины. Message специально не содержит
/// строку подключения, пароль и исходный текст ошибки драйвера.
/// </summary>
public sealed class PostgresUnavailableException : Exception
{
    /// <summary>Создаёт исключение по коду причины.</summary>
    /// <param name="reasonCode">Машинный код причины.</param>
    public PostgresUnavailableException(BootstrapReasonCode reasonCode)
        : base($"PostgreSQL is unavailable: {reasonCode}.") => ReasonCode = reasonCode;

    /// <summary>Создаёт исключение по коду причины с внутренним исключением драйвера.</summary>
    /// <param name="reasonCode">Машинный код причины.</param>
    /// <param name="innerException">Исходное исключение; наружу через Message не раскрывается.</param>
    public PostgresUnavailableException(BootstrapReasonCode reasonCode, Exception innerException)
        : base($"PostgreSQL is unavailable: {reasonCode}.", innerException) => ReasonCode = reasonCode;

    /// <summary>Машинный код причины недоступности.</summary>
    public BootstrapReasonCode ReasonCode { get; }
}

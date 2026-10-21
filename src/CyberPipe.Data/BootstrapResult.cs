namespace CyberPipe.Data;

/// <summary>Итоговый статус bootstrap SQLite.</summary>
public enum BootstrapStatus
{
    /// <summary>Файл базы доступен, миграции применимы, режим WAL включён.</summary>
    Ready = 0,

    /// <summary>Нужен ввод пользователя; Service не падает, а ждёт ввода.</summary>
    NeedsPassword = 1,

    /// <summary>Файл недоступен по причине, не связанной с вводом.</summary>
    Unavailable = 2,
}

/// <summary>Машинный код причины результата bootstrap. Тексты с путями за пределами имени файла сюда не попадают.</summary>
public enum BootstrapReasonCode
{
    /// <summary>Причины нет: база готова.</summary>
    None = 0,

    /// <summary>Файл базы недоступен (каталог не создаётся, диск недоступен, блокировка).</summary>
    ServerUnavailable = 1,

    /// <summary>Пароль отсутствует или неверен. Зарезервировано; SQLite аутентификации не требует.</summary>
    AuthenticationFailed = 2,

    /// <summary>Пароль не задан, а запросить его не у кого (нет промпта).</summary>
    PasswordRequired = 3,

    /// <summary>Недостаточно прав на файл или каталог базы.</summary>
    InsufficientPrivilege = 4,

    /// <summary>Файл базы повреждён или не является базой SQLite.</summary>
    DatabaseCorrupt = 5,

    /// <summary>Операция превысила тайм-аут (блокировка записи).</summary>
    Timeout = 6,

    /// <summary>Неизвестная причина; детали намеренно не раскрываются.</summary>
    Unknown = 7,
}

/// <summary>
/// Результат bootstrap без секретов: пароля и строки подключения в полях нет.
/// </summary>
public sealed record BootstrapResult
{
    /// <summary>Статус bootstrap.</summary>
    public required BootstrapStatus Status { get; init; }

    /// <summary>Код причины для Unavailable/NeedsPassword; None при Ready.</summary>
    public BootstrapReasonCode ReasonCode { get; init; } = BootstrapReasonCode.None;

    /// <summary>Версия SQLite при Ready; иначе null.</summary>
    public string? ServerVersion { get; init; }

    /// <summary>Человекочитаемая причина без секретов и строки подключения.</summary>
    public string? Detail { get; init; }

    /// <summary>Создаёт результат Ready.</summary>
    /// <param name="serverVersion">Версия SQLite.</param>
    /// <returns>Результат Ready.</returns>
    public static BootstrapResult Ready(string serverVersion) => new()
    {
        Status = BootstrapStatus.Ready,
        ReasonCode = BootstrapReasonCode.None,
        ServerVersion = serverVersion,
    };

    /// <summary>Создаёт результат NeedsPassword.</summary>
    /// <param name="reasonCode">Код причины.</param>
    /// <returns>Результат NeedsPassword.</returns>
    public static BootstrapResult NeedsPassword(BootstrapReasonCode reasonCode) => new()
    {
        Status = BootstrapStatus.NeedsPassword,
        ReasonCode = reasonCode,
        Detail = "Database input is required.",
    };

    /// <summary>Создаёт результат Unavailable.</summary>
    /// <param name="reasonCode">Код причины.</param>
    /// <returns>Результат Unavailable.</returns>
    public static BootstrapResult Unavailable(BootstrapReasonCode reasonCode) => new()
    {
        Status = BootstrapStatus.Unavailable,
        ReasonCode = reasonCode,
        Detail = $"SQLite database is unavailable: {reasonCode}.",
    };
}

/// <summary>
/// Недоступность SQLite с машинным кодом причины. Message специально не содержит
/// строку подключения, абсолютный путь и исходный текст ошибки драйвера.
/// </summary>
public sealed class SqliteUnavailableException : Exception
{
    /// <summary>Создаёт исключение по коду причины.</summary>
    /// <param name="reasonCode">Машинный код причины.</param>
    public SqliteUnavailableException(BootstrapReasonCode reasonCode)
        : base($"SQLite database is unavailable: {reasonCode}.") => ReasonCode = reasonCode;

    /// <summary>Создаёт исключение по коду причины с внутренним исключением драйвера.</summary>
    /// <param name="reasonCode">Машинный код причины.</param>
    /// <param name="innerException">Исходное исключение; наружу через Message не раскрывается.</param>
    public SqliteUnavailableException(BootstrapReasonCode reasonCode, Exception innerException)
        : base($"SQLite database is unavailable: {reasonCode}.", innerException) => ReasonCode = reasonCode;

    /// <summary>Машинный код причины недоступности.</summary>
    public BootstrapReasonCode ReasonCode { get; }
}

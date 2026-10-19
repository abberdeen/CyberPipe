namespace CyberPipe.Contracts;

/// <summary>
/// Канал принципала для проверки владельца (TASK-073).
/// Три известных канала сообщений повторяют <see cref="ChannelKind" />; отдельные значения
/// покрывают источник триггеров Webhook (TASK-041) и нераспознанные каналы — оба всегда
/// закрыты для Owner-авторизации (fail-closed).
/// </summary>
public enum OwnerChannel
{
    /// <summary>Telegram-канал (TASK-060).</summary>
    Telegram = 0,

    /// <summary>Десктопное приложение (TASK-061).</summary>
    Desktop = 1,

    /// <summary>Командная строка (TASK-062).</summary>
    Cli = 2,

    /// <summary>
    /// Источник триггеров Webhook (TASK-041): личность Owner здесь не устанавливается,
    /// проверка подписи остаётся за триггером; как Owner никогда не авторизуется.
    /// </summary>
    Webhook = 3,

    /// <summary>Нераспознанный или будущий канал; всегда отклоняется (fail-closed).</summary>
    Unknown = 4,
}

/// <summary>
/// Принципал канала: личность отправителя в терминах его канала (TASK-073).
/// Установление личности внутри канала — дело каналов TASK-060/TASK-061/TASK-062;
/// здесь только готовый принципал для единого решения «это Owner или нет».
/// </summary>
public sealed record ChannelPrincipal
{
    /// <summary>Канал, из которого пришёл принципал.</summary>
    public required OwnerChannel Channel { get; init; }

    /// <summary>
    /// Идентификатор отправителя в терминах канала (Telegram user id, локальный
    /// SID/имя пользователя, идентификатор сессии). Единственное поле, участвующее в решении.
    /// </summary>
    public required string SenderId { get; init; }

    /// <summary>
    /// Отображаемое имя (username и т.п.): только для журнала, в решении не участвует,
    /// потому что отображаемые имена меняются и не идентифицируют владельца.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>Создаёт принципал из известного канала сообщений.</summary>
    public static ChannelPrincipal FromChannel(ChannelKind channel, string senderId, string? displayName = null) =>
        new()
        {
            Channel = channel switch
            {
                ChannelKind.Telegram => OwnerChannel.Telegram,
                ChannelKind.Desktop => OwnerChannel.Desktop,
                ChannelKind.Cli => OwnerChannel.Cli,
                _ => OwnerChannel.Unknown,
            },
            SenderId = senderId,
            DisplayName = displayName,
        };
}

/// <summary>
/// Исход проверки владельца (TASK-073). Существует только Owner; режимов с
/// пониженными правами и анонимного доступа нет ни в коде, ни в конфигурации.
/// </summary>
public enum OwnerAuthOutcome
{
    /// <summary>Идентификатор точно совпал с белым списком своего канала.</summary>
    Authorized = 0,

    /// <summary>Доступ отклонён по любой причине (fail-closed).</summary>
    Denied = 1,
}

/// <summary>
/// Код причины решения проверки владельца (TASK-073).
/// </summary>
public enum OwnerAuthReason
{
    /// <summary>Идентификатор найден в белом списке своего канала.</summary>
    WhitelistMatch = 0,

    /// <summary>Идентификатора нет в белом списке канала.</summary>
    NotInWhitelist = 1,

    /// <summary>Белый список канала пуст: канал закрыт, а не открыт всем.</summary>
    EmptyWhitelist = 2,

    /// <summary>Пустой идентификатор отправителя.</summary>
    MissingIdentity = 3,

    /// <summary>Неизвестный канал (включая Webhook как Owner-принципал).</summary>
    UnknownChannel = 4,

    /// <summary>
    /// Белый список недоступен или повреждён: отклоняются все, включая ранее
    /// авторизованных; последнее закэшированное разрешение не переиспользуется.
    /// </summary>
    WhitelistUnavailable = 5,
}

/// <summary>
/// Решение проверки владельца (TASK-073).
/// </summary>
public sealed record OwnerAuthDecision
{
    /// <summary>Исход проверки.</summary>
    public required OwnerAuthOutcome Outcome { get; init; }

    /// <summary>Код причины решения.</summary>
    public required OwnerAuthReason Reason { get; init; }

    /// <summary>Короткая форма исхода для HITL (TASK-046) и управления Service (TASK-021).</summary>
    public bool IsAuthorized => Outcome == OwnerAuthOutcome.Authorized;

    /// <summary>Решение о доступе владельца.</summary>
    public static OwnerAuthDecision Authorized() =>
        new() { Outcome = OwnerAuthOutcome.Authorized, Reason = OwnerAuthReason.WhitelistMatch };

    /// <summary>Решение об отклонении с причиной.</summary>
    public static OwnerAuthDecision Denied(OwnerAuthReason reason) =>
        new() { Outcome = OwnerAuthOutcome.Denied, Reason = reason };
}

/// <summary>
/// Актуальный белый список идентификаторов владельца по каналам (TASK-073).
/// Полные идентификаторы: только для внутреннего сопоставления в авторизаторе,
/// наружу (UI, журналы) не раскрываются — для этого есть <see cref="OwnerWhitelistSummary" />.
/// Пустой список канала означает «канал закрыт», а не «открыт всем».
/// </summary>
public sealed record OwnerWhitelistSnapshot
{
    /// <summary>Идентификаторы владельца в Telegram.</summary>
    public IReadOnlyList<string> TelegramOwnerIds { get; init; } = [];

    /// <summary>Идентификатор владельца в Desktop; пусто — не задан.</summary>
    public string? DesktopOwnerId { get; init; }

    /// <summary>Идентификатор владельца в CLI; пусто — не задан.</summary>
    public string? CliOwnerId { get; init; }

    /// <summary>Признак, что список не пуст хотя бы для одного канала (для мастера TASK-063 и UI TASK-061).</summary>
    public bool HasAnyOwner =>
        TelegramOwnerIds.Count != 0 ||
        !string.IsNullOrWhiteSpace(DesktopOwnerId) ||
        !string.IsNullOrWhiteSpace(CliOwnerId);

    /// <summary>Пустой снимок: все каналы закрыты.</summary>
    public static OwnerWhitelistSnapshot Empty => new();

    /// <summary>Строит обезличенную сводку без полных идентификаторов.</summary>
    public OwnerWhitelistSummary ToSummary() =>
        new()
        {
            TelegramCount = TelegramOwnerIds.Count,
            HasDesktopOwner = !string.IsNullOrWhiteSpace(DesktopOwnerId),
            HasCliOwner = !string.IsNullOrWhiteSpace(CliOwnerId),
            HasAnyOwner = HasAnyOwner,
        };
}

/// <summary>
/// Обезличенная сводка белого списка для мастера первого запуска (TASK-063) и UI (TASK-061):
/// каналы и число идентификаторов, без раскрытия полных идентификаторов.
/// </summary>
public sealed record OwnerWhitelistSummary
{
    /// <summary>Число идентификаторов в белом списке Telegram.</summary>
    public int TelegramCount { get; init; }

    /// <summary>Признак, что задан идентификатор владельца в Desktop.</summary>
    public bool HasDesktopOwner { get; init; }

    /// <summary>Признак, что задан идентификатор владельца в CLI.</summary>
    public bool HasCliOwner { get; init; }

    /// <summary>Признак, что список не пуст хотя бы для одного канала.</summary>
    public bool HasAnyOwner { get; init; }
}

/// <summary>
/// Поставщик актуального белого списка поверх конфигурации TASK-003 (TASK-073).
/// Авторизатор не читает файлы конфигурации напрямую; изменение списка применяется
/// к следующим проверкам без перезапуска Service, отзыв действует немедленно.
/// </summary>
public interface IOwnerWhitelistProvider
{
    /// <summary>Возвращает актуальный снимок белого списка.</summary>
    Task<OwnerWhitelistSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>Сигнал об изменении белого списка.</summary>
    event EventHandler? WhitelistChanged;
}

/// <summary>
/// Авторизатор владельца по белому списку (TASK-073). Единое решение «это Owner
/// или нет» для Telegram, Desktop и CLI; переиспользуется HITL (TASK-046) и
/// управлением Service (TASK-021) через короткую форму <see cref="IsOwnerAsync" />.
/// </summary>
public interface IOwnerAuthorizer
{
    /// <summary>Проверяет входящее сообщение или команду по готовому принципалу.</summary>
    Task<OwnerAuthDecision> AuthorizeAsync(ChannelPrincipal principal, CancellationToken cancellationToken = default);

    /// <summary>Короткая форма для HITL и управления Service.</summary>
    Task<bool> IsOwnerAsync(ChannelPrincipal principal, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает обезличенную сводку белого списка (каналы и числа, без полных
    /// идентификаторов) для мастера первого запуска и UI.
    /// </summary>
    Task<OwnerWhitelistSummary> GetWhitelistSummaryAsync(CancellationToken cancellationToken = default);
}

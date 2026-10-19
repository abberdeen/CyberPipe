namespace CyberPipe.Contracts;

/// <summary>
/// Класс адресата исходящих данных (TASK-072).
/// </summary>
public enum EgressDestination
{
    /// <summary>Облачная большая языковая модель (Tier 2/3).</summary>
    CloudLlm,

    /// <summary>Исходящий вебхук доставки.</summary>
    Webhook,

    /// <summary>Внешний API стороннего сервиса.</summary>
    ExternalApi,

    /// <summary>Канал Telegram.</summary>
    Telegram,

    /// <summary>Иной внешний получатель.</summary>
    Other
}

/// <summary>
/// Вид содержимого исходящего пакета (TASK-072).
/// </summary>
public enum EgressContentKind
{
    /// <summary>Свободный текст.</summary>
    Text,

    /// <summary>Пары ключ-значение (заголовки, параметры формы, словарь).</summary>
    KeyValue,

    /// <summary>Структурированный JSON-документ.</summary>
    Json
}

/// <summary>
/// Класс чувствительности обнаруженных данных (TASK-072).
/// </summary>
public enum SensitivityClass
{
    /// <summary>API-ключ стороннего сервиса.</summary>
    ApiKey,

    /// <summary>Токен авторизации (Bearer, JWT и др.).</summary>
    Token,

    /// <summary>Пароль пользователя или сервиса.</summary>
    Password,

    /// <summary>Строка подключения к базе данных или ресурсу.</summary>
    ConnectionString,

    /// <summary>Приватный криптографический ключ.</summary>
    PrivateKey,

    /// <summary>Адрес электронной почты.</summary>
    Email,

    /// <summary>Номер телефона.</summary>
    Phone,

    /// <summary>Номер банковской карты (прошедший алгоритм Луна).</summary>
    CardNumber,

    /// <summary>Известный секрет из платформенного хранилища приложения.</summary>
    KnownSecret,

    /// <summary>Строка высокой энтропии с секретоподобными характеристиками.</summary>
    HighEntropyValue
}

/// <summary>
/// Действие при обнаружении чувствительных данных (TASK-072).
/// </summary>
public enum SanitizeAction
{
    /// <summary>Полная замена значения плейсхолдером.</summary>
    Mask,

    /// <summary>Частичное маскирование с сохранением видимого минимума для контекста.</summary>
    PartialMask,

    /// <summary>Полная блокировка отправки всего пакета (fail-closed).</summary>
    Block
}

/// <summary>
/// Исход операции санитизации исходящего пакета (TASK-072).
/// </summary>
public enum EgressSanitizeOutcome
{
    /// <summary>Пакет разрешён к отправке (очищенная копия сформирована).</summary>
    Allowed,

    /// <summary>Пакет заблокирован из-за критических находок или ошибки (fail-closed).</summary>
    Blocked
}

/// <summary>
/// Исходящий пакет данных перед отправкой во внешние системы (TASK-072).
/// </summary>
public sealed record EgressPayload
{
    /// <summary>Класс адресата.</summary>
    public EgressDestination Destination { get; init; } = EgressDestination.ExternalApi;

    /// <summary>Вид содержимого.</summary>
    public EgressContentKind ContentKind { get; init; } = EgressContentKind.Text;

    /// <summary>Текстовое содержимое (при ContentKind == Text).</summary>
    public string? TextContent { get; init; }

    /// <summary>Пары ключ-значение (при ContentKind == KeyValue).</summary>
    public IReadOnlyDictionary<string, string?>? KeyValueContent { get; init; }

    /// <summary>JSON-содержимое (при ContentKind == Json).</summary>
    public string? JsonContent { get; init; }

    /// <summary>Метаданные источника (цепочка, навык, канал).</summary>
    public string? Source { get; init; }

    /// <summary>Создаёт пакет со свободным текстом.</summary>
    public static EgressPayload Text(string text, EgressDestination destination = EgressDestination.ExternalApi, string? source = null) =>
        new()
        {
            Destination = destination,
            ContentKind = EgressContentKind.Text,
            TextContent = text,
            Source = source
        };

    /// <summary>Создаёт пакет со словарем ключ-значение.</summary>
    public static EgressPayload KeyValue(IReadOnlyDictionary<string, string?> keyValue, EgressDestination destination = EgressDestination.ExternalApi, string? source = null) =>
        new()
        {
            Destination = destination,
            ContentKind = EgressContentKind.KeyValue,
            KeyValueContent = keyValue,
            Source = source
        };

    /// <summary>Создаёт пакет с JSON-документом.</summary>
    public static EgressPayload Json(string json, EgressDestination destination = EgressDestination.ExternalApi, string? source = null) =>
        new()
        {
            Destination = destination,
            ContentKind = EgressContentKind.Json,
            JsonContent = json,
            Source = source
        };
}

/// <summary>
/// Находка чувствительных данных в исходящем пакете (TASK-072).
/// Никогда не содержит само значение секрета!
/// </summary>
public sealed record SanitizeFinding
{
    /// <summary>Класс чувствительности.</summary>
    public SensitivityClass Class { get; init; }

    /// <summary>Путь поля или позиция находки (например, 'user.password', 'headers.Authorization', 'offset 42').</summary>
    public string PathOrPosition { get; init; } = string.Empty;

    /// <summary>Применённое действие.</summary>
    public SanitizeAction Action { get; init; }

    /// <summary>Безопасное описание находки без раскрытия значения секрета.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Результат санитизации исходящего пакета (TASK-072).
/// </summary>
public sealed record EgressSanitizeResult
{
    /// <summary>Исход операции.</summary>
    public EgressSanitizeOutcome Outcome { get; init; }

    /// <summary>Очищенная копия пакета (доступна только при Outcome == Allowed).</summary>
    public EgressPayload? SanitizedPayload { get; init; }

    /// <summary>Список находок чувствительных данных без раскрытия их значений.</summary>
    public IReadOnlyList<SanitizeFinding> Findings { get; init; } = [];

    /// <summary>Причина блокировки при Outcome == Blocked.</summary>
    public string? BlockReason { get; init; }

    /// <summary>Успешный результат без изменений.</summary>
    public static EgressSanitizeResult AllowedUnchanged(EgressPayload original) =>
        new()
        {
            Outcome = EgressSanitizeOutcome.Allowed,
            SanitizedPayload = original,
            Findings = []
        };

    /// <summary>Успешный результат с очищенным пакетом и находками.</summary>
    public static EgressSanitizeResult AllowedCleaned(EgressPayload sanitized, IReadOnlyList<SanitizeFinding> findings) =>
        new()
        {
            Outcome = EgressSanitizeOutcome.Allowed,
            SanitizedPayload = sanitized,
            Findings = findings
        };

    /// <summary>Результат блокировки пакета.</summary>
    public static EgressSanitizeResult Blocked(string reason, IReadOnlyList<SanitizeFinding>? findings = null) =>
        new()
        {
            Outcome = EgressSanitizeOutcome.Blocked,
            SanitizedPayload = null,
            Findings = findings ?? [],
            BlockReason = reason
        };
}

/// <summary>
/// Поставщик значений известных секретов для сопоставления внутри санитизатора (TASK-072).
/// </summary>
public interface IKnownSecretsProvider
{
    /// <summary>Возвращает актуальный набор открытых значений секретов только для внутреннего сравнения.</summary>
    Task<IReadOnlyCollection<string>> GetKnownSecretValuesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Параметры конфигурации санитизатора исходящих данных (TASK-072).
/// </summary>
public sealed record EgressSanitizerOptions
{
    /// <summary>Максимальный размер пакета в байтах. Превышение ведёт к блокировке (fail-closed).</summary>
    public int MaxPayloadSizeBytes { get; init; } = 10 * 1024 * 1024;

    /// <summary>Порог энтропии Шеннона для обнаружения случайных токенов.</summary>
    public double HighEntropyThreshold { get; init; } = 4.2;

    /// <summary>Минимальная длина строки для проверки энтропии.</summary>
    public int HighEntropyMinLength { get; init; } = 20;

    /// <summary>Маска по умолчанию для полного скрытия секрета.</summary>
    public string MaskPlaceholder { get; init; } = "[REDACTED]";

    /// <summary>Маппинг действий по классам чувствительности.</summary>
    public IReadOnlyDictionary<SensitivityClass, SanitizeAction> ClassActions { get; init; } = new Dictionary<SensitivityClass, SanitizeAction>
    {
        [SensitivityClass.PrivateKey] = SanitizeAction.Block,
        [SensitivityClass.ConnectionString] = SanitizeAction.Block,
        [SensitivityClass.Password] = SanitizeAction.Mask,
        [SensitivityClass.ApiKey] = SanitizeAction.Mask,
        [SensitivityClass.Token] = SanitizeAction.Mask,
        [SensitivityClass.KnownSecret] = SanitizeAction.Mask,
        [SensitivityClass.HighEntropyValue] = SanitizeAction.Mask,
        [SensitivityClass.Email] = SanitizeAction.PartialMask,
        [SensitivityClass.Phone] = SanitizeAction.PartialMask,
        [SensitivityClass.CardNumber] = SanitizeAction.PartialMask
    };
}

/// <summary>
/// Санитизатор исходящих данных перед отправкой за пределы локальной машины (TASK-072).
/// </summary>
public interface IEgressSanitizer
{
    /// <summary>
    /// Очищает исходящий пакет от секретов и персональных данных.
    /// Исходный пакет не мутируется; возвращается очищенная копия либо решение о блокировке.
    /// </summary>
    Task<EgressSanitizeResult> SanitizeAsync(EgressPayload payload, CancellationToken cancellationToken = default);
}

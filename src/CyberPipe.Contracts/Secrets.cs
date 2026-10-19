namespace CyberPipe.Contracts;

/// <summary>Единый реестр имён ключей секретов. Новые секреты добавляются только сюда.</summary>
public static class SecretKeys
{
    /// <summary>Пароль PostgreSQL.</summary>
    public const string DatabasePassword = "database.password";

    /// <summary>Токен Telegram-бота.</summary>
    public const string TelegramBotToken = "telegram.botToken";

    /// <summary>Все зарегистрированные ключи секретов.</summary>
    public static IReadOnlyList<string> All { get; } = [DatabasePassword, TelegramBotToken];
}

/// <summary>
/// Значение секрета. <see cref="ToString" /> всегда возвращает [REDACTED], чтобы секрет не попадал
/// в логи и диагностику; явное чтение — только через <see cref="Reveal" /> в точке использования.
/// Интеграция с логированием — TASK-004, исходящая очистка — TASK-072.
/// </summary>
public sealed class SecretValue : IEquatable<SecretValue>
{
    private readonly string _value;

    private SecretValue(string value) => _value = value;

    /// <summary>Создаёт секрет из открытого значения в точке ввода.</summary>
    /// <param name="plainText">Открытое значение секрета.</param>
    /// <returns>Обёртка секрета.</returns>
    public static SecretValue FromPlainText(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        return new SecretValue(plainText);
    }

    /// <summary>Явно возвращает открытое значение для непосредственного использования.</summary>
    /// <returns>Открытое значение секрета.</returns>
    public string Reveal() => _value;

    /// <inheritdoc />
    public override string ToString() => "[REDACTED]";

    /// <inheritdoc />
    public bool Equals(SecretValue? other) =>
        other is not null && string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SecretValue);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value);
}

/// <summary>
/// Хранилище секретов по строковому ключу из <see cref="SecretKeys" />.
/// Реализация по умолчанию — платформенное хранилище ОС (CyberPipe.Security);
/// файловая реализация существует только для тестов и помечена небезопасной.
/// </summary>
public interface ISecretStore
{
    /// <summary>Признак, что реализация является платформенным защищённым хранилищем ОС.</summary>
    bool IsPlatformStore { get; }

    /// <summary>Записывает или заменяет секрет по ключу.</summary>
    /// <param name="key">Ключ из <see cref="SecretKeys" />.</param>
    /// <param name="value">Значение секрета.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Асинхронная операция записи.</returns>
    Task SetAsync(string key, SecretValue value, CancellationToken cancellationToken = default);

    /// <summary>Читает секрет по ключу; null, если ключа нет.</summary>
    /// <param name="key">Ключ из <see cref="SecretKeys" />.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Секрет или null.</returns>
    Task<SecretValue?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Удаляет секрет по ключу.</summary>
    /// <param name="key">Ключ из <see cref="SecretKeys" />.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>True, если ключ существовал и удалён.</returns>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}

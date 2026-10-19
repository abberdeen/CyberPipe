using CyberPipe.Contracts;

namespace CyberPipe.Data;

/// <summary>
/// Параметры подключения к единственной PostgreSQL (TASK-010). Все значения конфигурируемы;
/// пароль значением здесь не хранится — только <see cref="PasswordSecretKey" />, ссылка на
/// секрет TASK-003 в <see cref="ISecretStore" />. Строка подключения собирается только из этих
/// опций и секрета в <see cref="PostgresConnectionFactory" />.
/// </summary>
public sealed record PostgresOptions
{
    /// <summary>Хост PostgreSQL.</summary>
    public string Host { get; init; } = "localhost";

    /// <summary>Порт PostgreSQL.</summary>
    public int Port { get; init; } = 5432;

    /// <summary>Имя базы данных приложения.</summary>
    public string Database { get; init; } = "cyberpipe";

    /// <summary>Пользователь базы данных.</summary>
    public string Username { get; init; } = "cyberpipe";

    /// <summary>Ключ секрета с паролем в ISecretStore, не сам пароль.</summary>
    public string PasswordSecretKey { get; init; } = SecretKeys.DatabasePassword;

    /// <summary>Максимальный размер пула соединений.</summary>
    public int MaxPoolSize { get; init; } = 100;

    /// <summary>Тайм-аут команды в секундах.</summary>
    public int CommandTimeout { get; init; } = 30;

    /// <summary>Тайм-аут подключения в секундах.</summary>
    public int ConnectTimeout { get; init; } = 15;

    /// <summary>Сколько раз максимум запрашивать пароль через ISecretPrompt при неверном пароле.</summary>
    public int MaxPasswordPromptAttempts { get; init; } = 3;

    /// <summary>Создаёт опции из секции Database корневой конфигурации TASK-003.</summary>
    /// <param name="database">Секция Database из CyberPipeOptions.</param>
    /// <returns>Опции подключения PostgreSQL.</returns>
    public static PostgresOptions From(DatabaseOptions database)
    {
        ArgumentNullException.ThrowIfNull(database);
        return new PostgresOptions
        {
            Host = database.Host,
            Port = database.Port,
            Database = database.Database,
            Username = database.Username,
            PasswordSecretKey = database.PasswordSecretKey,
            MaxPoolSize = database.MaxPoolSize,
            CommandTimeout = database.CommandTimeout,
        };
    }

    /// <summary>Проверяет опции; ошибки содержат имена полей, не значения секретов.</summary>
    /// <returns>Список ошибок; пуст при валидных опциях.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Host))
            errors.Add("Postgres:Host must not be empty.");
        if (Port is < 1 or > 65535)
            errors.Add("Postgres:Port must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(Database))
            errors.Add("Postgres:Database must not be empty.");
        if (string.IsNullOrWhiteSpace(Username))
            errors.Add("Postgres:Username must not be empty.");
        if (string.IsNullOrWhiteSpace(PasswordSecretKey))
            errors.Add("Postgres:PasswordSecretKey must name a secret key.");
        if (MaxPoolSize <= 0)
            errors.Add("Postgres:MaxPoolSize must be positive.");
        if (CommandTimeout <= 0)
            errors.Add("Postgres:CommandTimeout must be positive.");
        if (ConnectTimeout <= 0)
            errors.Add("Postgres:ConnectTimeout must be positive.");
        if (MaxPasswordPromptAttempts < 0)
            errors.Add("Postgres:MaxPasswordPromptAttempts must not be negative.");
        return errors;
    }
}

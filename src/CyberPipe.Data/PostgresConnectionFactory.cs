using CyberPipe.Contracts;
using Npgsql;

namespace CyberPipe.Data;

/// <summary>
/// Единственный способ получать соединения PostgreSQL для TASK-011–TASK-013.
/// Соединение уже открыто; вызывающий владеет им и обязан освободить (await using).
/// </summary>
public interface IPostgresConnectionFactory
{
    /// <summary>Создаёт открытое соединение с базой приложения.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Открытое соединение с базой приложения.</returns>
    Task<NpgsqlConnection> CreateOpenedConnectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Фабрика соединений Npgsql. Строка подключения собирается только из <see cref="PostgresOptions" />
/// и секрета из <see cref="ISecretStore" /> (TASK-003) либо явного переопределения пароля из
/// <see cref="ISecretPrompt" /> на время одной попытки bootstrap. Строка подключения и пароль
/// нигде не логируются и не попадают в исключения: ошибки открытия классифицируются в
/// <see cref="PostgresUnavailableException" /> без исходного текста драйвера.
/// </summary>
public sealed class PostgresConnectionFactory : IPostgresConnectionFactory
{
    /// <summary>Имя служебной базы, через которую проверяется сервер и создаётся база приложения.</summary>
    public const string MaintenanceDatabase = "postgres";

    private readonly PostgresOptions _options;
    private readonly Func<CancellationToken, Task<SecretValue?>> _passwordProvider;

    /// <summary>Создаёт фабрику, читающую пароль из хранилища секретов по ключу из опций.</summary>
    /// <param name="options">Опции подключения; пароль значением не содержат.</param>
    /// <param name="secretStore">Хранилище секретов TASK-003.</param>
    public PostgresConnectionFactory(PostgresOptions options, ISecretStore secretStore)
        : this(
            options,
            cancellationToken => (secretStore ?? throw new ArgumentNullException(nameof(secretStore)))
                .GetAsync(options.PasswordSecretKey, cancellationToken))
    {
    }

    /// <summary>Создаёт фабрику с явным поставщиком пароля (точка расширения для тестов и bootstrap).</summary>
    /// <param name="options">Опции подключения; пароль значением не содержат.</param>
    /// <param name="passwordProvider">Поставщик секрета пароля; null означает отсутствие пароля.</param>
    public PostgresConnectionFactory(
        PostgresOptions options,
        Func<CancellationToken, Task<SecretValue?>> passwordProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _passwordProvider = passwordProvider ?? throw new ArgumentNullException(nameof(passwordProvider));
    }

    /// <inheritdoc />
    public async Task<NpgsqlConnection> CreateOpenedConnectionAsync(CancellationToken cancellationToken = default)
    {
        SecretValue? password = await _passwordProvider(cancellationToken).ConfigureAwait(false);
        return await CreateOpenedConnectionAsync(_options.Database, password, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Создаёт открытое соединение с произвольной базой и явным паролем. Используется bootstrap
    /// для служебной базы и для повторных попыток с паролем из промпта до его сохранения.
    /// </summary>
    /// <param name="database">Имя базы данных.</param>
    /// <param name="password">Пароль переопределением; null — читать из поставщика пароля.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Открытое соединение.</returns>
    /// <exception cref="PostgresUnavailableException">Подключение невозможно; причина классифицирована, секретов в сообщении нет.</exception>
    public async Task<NpgsqlConnection> CreateOpenedConnectionAsync(
        string database,
        SecretValue? password,
        CancellationToken cancellationToken = default)
    {
        SecretValue? effectivePassword = password
            ?? await _passwordProvider(cancellationToken).ConfigureAwait(false);
        string connectionString = BuildConnectionString(_options, database, effectivePassword);
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw new PostgresUnavailableException(PostgresErrorClassifier.Classify(ex), ex);
        }
    }

    /// <summary>
    /// Собирает строку подключения только из опций и секрета. Метод не логирует результат;
    /// вызывающие обязаны не выводить возвращаемую строку наружу.
    /// </summary>
    /// <param name="options">Опции подключения.</param>
    /// <param name="database">Имя базы данных.</param>
    /// <param name="password">Секрет пароля; null — без пароля (сервер отклонит при scram/md5).</param>
    /// <returns>Строка подключения Npgsql.</returns>
    public static string BuildConnectionString(PostgresOptions options, string database, SecretValue? password)
    {
        ArgumentNullException.ThrowIfNull(options);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = options.Host,
            Port = options.Port,
            Database = database,
            Username = options.Username,
            Password = password?.Reveal(),
            MaxPoolSize = options.MaxPoolSize,
            Timeout = options.ConnectTimeout,
            CommandTimeout = options.CommandTimeout,
            Pooling = true,
            ApplicationName = "CyberPipe",
        };
        return builder.ConnectionString;
    }
}

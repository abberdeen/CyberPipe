using CyberPipe.Contracts;
using Npgsql;

namespace CyberPipe.Data;

/// <summary>Сведения о готовом сервере без секретов.</summary>
/// <param name="ServerVersion">Версия PostgreSQL (server_version).</param>
/// <param name="PgVectorVersion">Версия расширения vector; null, если расширение не установлено.</param>
public sealed record PostgresServerInfo(string ServerVersion, string? PgVectorVersion);

/// <summary>
/// Низкоуровневые операции bootstrap над реальным сервером. Отдельный интерфейс — шов для unit-тестов:
/// <see cref="PostgresBootstrapper" /> тестируется на фейке без реальной БД, а классификация ошибок
/// реальной реализации проверяется интеграционно. Все методы бросают только
/// <see cref="PostgresUnavailableException" /> с классифицированной причиной без секретов.
/// </summary>
public interface IPostgresBootstrapGateway
{
    /// <summary>
    /// Полный проход: подключение, создание базы приложения при отсутствии, включение расширений
    /// vector и pgcrypto. Идемпотентно: существующую базу и данные не трогает.
    /// </summary>
    /// <param name="password">Пароль этой попытки; null — пароль из хранилища секретов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Версии сервера и расширения vector.</returns>
    Task<PostgresServerInfo> EnsureReadyAsync(SecretValue? password, CancellationToken cancellationToken = default);

    /// <summary>Лёгкая проверка SELECT 1 по базе приложения для health-check.</summary>
    /// <param name="password">Пароль этой попытки; null — пароль из хранилища секретов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Асинхронная операция проверки.</returns>
    Task ProbeAsync(SecretValue? password, CancellationToken cancellationToken = default);
}

/// <summary>Реализация <see cref="IPostgresBootstrapGateway" /> поверх Npgsql.</summary>
public sealed class PostgresBootstrapGateway : IPostgresBootstrapGateway
{
    private readonly PostgresOptions _options;
    private readonly PostgresConnectionFactory _connectionFactory;

    /// <summary>Создаёт шлюз.</summary>
    /// <param name="options">Опции подключения.</param>
    /// <param name="connectionFactory">Фабрика соединений с тем же набором опций.</param>
    public PostgresBootstrapGateway(PostgresOptions options, PostgresConnectionFactory connectionFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <inheritdoc />
    public async Task<PostgresServerInfo> EnsureReadyAsync(
        SecretValue? password,
        CancellationToken cancellationToken = default)
    {
        string serverVersion;
        await using (NpgsqlConnection maintenance = await _connectionFactory
            .CreateOpenedConnectionAsync(PostgresConnectionFactory.MaintenanceDatabase, password, cancellationToken)
            .ConfigureAwait(false))
        {
            serverVersion = await SelectServerVersionAsync(maintenance, cancellationToken).ConfigureAwait(false);
            await EnsureDatabaseExistsAsync(maintenance, cancellationToken).ConfigureAwait(false);
        }

        await using NpgsqlConnection app = await _connectionFactory
            .CreateOpenedConnectionAsync(_options.Database, password, cancellationToken)
            .ConfigureAwait(false);
        string pgVectorVersion = await EnsureExtensionsAsync(app, cancellationToken).ConfigureAwait(false);
        return new PostgresServerInfo(serverVersion, pgVectorVersion);
    }

    /// <inheritdoc />
    public async Task ProbeAsync(SecretValue? password, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connectionFactory
            .CreateOpenedConnectionAsync(_options.Database, password, cancellationToken)
            .ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        _ = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> SelectServerVersionAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = new NpgsqlCommand("SELECT current_setting('server_version'), 1", connection);
            await using NpgsqlDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            _ = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            return reader.GetString(0);
        }
        catch (PostgresUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PostgresUnavailableException(PostgresErrorClassifier.Classify(ex), ex);
        }
    }

    private async Task EnsureDatabaseExistsAsync(
        NpgsqlConnection maintenance,
        CancellationToken cancellationToken)
    {
        try
        {
            bool exists;
            await using (var check = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = @name", maintenance))
            {
                check.Parameters.AddWithValue("name", _options.Database);
                exists = await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
            }

            if (exists)
                return;

            // CREATE DATABASE не параметризуется: имя берётся из опций и экранируется как идентификатор.
            string createSql = $"CREATE DATABASE {QuoteIdentifier(_options.Database)}";
            await using var create = new NpgsqlCommand(createSql, maintenance);
            await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PostgresUnavailableException(PostgresErrorClassifier.Classify(ex), ex);
        }
    }

    private static async Task<string> EnsureExtensionsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            bool vectorAvailable;
            await using (var available = new NpgsqlCommand(
                "SELECT 1 FROM pg_available_extensions WHERE name = 'vector'", connection))
            {
                vectorAvailable = await available.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
            }

            if (!vectorAvailable)
                throw new PostgresUnavailableException(BootstrapReasonCode.PgVectorMissing);

            await using (var createVector = new NpgsqlCommand(
                "CREATE EXTENSION IF NOT EXISTS vector", connection))
            {
                await createVector.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var createCrypto = new NpgsqlCommand(
                "CREATE EXTENSION IF NOT EXISTS pgcrypto", connection))
            {
                await createCrypto.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var version = new NpgsqlCommand(
                "SELECT extversion FROM pg_extension WHERE extname = 'vector'", connection);
            object? result = await version.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result as string
                ?? throw new PostgresUnavailableException(BootstrapReasonCode.PgVectorMissing);
        }
        catch (PostgresUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PostgresUnavailableException(PostgresErrorClassifier.Classify(ex), ex);
        }
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

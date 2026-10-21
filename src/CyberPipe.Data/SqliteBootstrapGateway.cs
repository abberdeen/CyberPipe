using Microsoft.Data.Sqlite;

namespace CyberPipe.Data;

/// <summary>Сведения о готовой базе без секретов.</summary>
/// <param name="ServerVersion">Версия SQLite (sqlite_version()).</param>
public sealed record SqliteServerInfo(string ServerVersion);

/// <summary>
/// Низкоуровневые операции bootstrap над файлом SQLite. Отдельный интерфейс — шов для unit-тестов.
/// Все методы бросают только <see cref="SqliteUnavailableException" /> с классифицированной причиной.
/// </summary>
public interface IDatabaseBootstrapGateway
{
    /// <summary>Открывает файл базы, включает WAL и возвращает версию SQLite.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Версия SQLite.</returns>
    Task<SqliteServerInfo> EnsureReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>Лёгкая проверка SELECT 1 по базе приложения для health-check.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Асинхронная операция проверки.</returns>
    Task ProbeAsync(CancellationToken cancellationToken = default);
}

/// <summary>Реализация <see cref="IDatabaseBootstrapGateway" /> поверх Microsoft.Data.Sqlite.</summary>
public sealed class SqliteBootstrapGateway : IDatabaseBootstrapGateway
{
    private readonly SqliteConnectionFactory _connectionFactory;

    /// <summary>Создаёт шлюз.</summary>
    /// <param name="connectionFactory">Фабрика соединений с тем же файлом базы.</param>
    public SqliteBootstrapGateway(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <inheritdoc />
    public async Task<SqliteServerInfo> EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using SqliteConnection connection = await _connectionFactory
                .CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT sqlite_version()";
            object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return new SqliteServerInfo(result?.ToString() ?? "unknown");
        }
        catch (SqliteUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SqliteUnavailableException(SqliteErrorClassifier.Classify(ex), ex);
        }
    }

    /// <inheritdoc />
    public async Task ProbeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using SqliteConnection connection = await _connectionFactory
                .CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            _ = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SqliteUnavailableException(SqliteErrorClassifier.Classify(ex), ex);
        }
    }
}

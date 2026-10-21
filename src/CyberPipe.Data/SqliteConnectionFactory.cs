using Microsoft.Data.Sqlite;

namespace CyberPipe.Data;

/// <summary>
/// Единственный способ получать соединения SQLite WAL.
/// Соединение уже открыто и настроено (journal_mode=WAL, foreign_keys=ON, busy_timeout);
/// вызывающий владеет им и обязан освободить (await using).
/// </summary>
public interface IDatabaseConnectionFactory
{
    /// <summary>Создаёт открытое настроенное соединение с файлом базы.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Открытое соединение с базой приложения.</returns>
    Task<SqliteConnection> CreateOpenedConnectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Фабрика соединений SQLite. Каталог файла создаётся при первом открытии;
/// на каждом соединении включаются WAL, внешние ключи и busy timeout.
/// Ошибки открытия классифицируются в <see cref="SqliteUnavailableException" />.
/// </summary>
public sealed class SqliteConnectionFactory : IDatabaseConnectionFactory
{
    private readonly SqliteOptions _options;

    /// <summary>Создаёт фабрику поверх опций SQLite.</summary>
    /// <param name="options">Опции; путь значением содержат, секретов нет.</param>
    public SqliteConnectionFactory(SqliteOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<SqliteConnection> CreateOpenedConnectionAsync(CancellationToken cancellationToken = default)
    {
        string fullPath = _options.FullPath();
        string? directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        };
        var connection = new SqliteConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ApplyPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
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
            throw new SqliteUnavailableException(SqliteErrorClassifier.Classify(ex), ex);
        }
    }

    private async Task ApplyPragmasAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA foreign_keys=ON;
                PRAGMA synchronous=NORMAL;
                """;
            command.CommandTimeout = _options.CommandTimeout;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await using var busy = connection.CreateCommand();
            busy.CommandText = $"PRAGMA busy_timeout={_options.BusyTimeoutMs};";
            busy.CommandTimeout = _options.CommandTimeout;
            await busy.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

using CyberPipe.Contracts;

namespace CyberPipe.Data;

/// <summary>
/// Адаптер bootstrap SQLite под порт мастера первого запуска (TASK-090).
/// Пароль игнорируется: у встроенной базы аутентификации нет.
/// </summary>
public sealed class SetupDatabaseBootstrapAdapter : IDatabaseBootstrapService
{
    /// <summary>Создаёт адаптер.</summary>
    public SetupDatabaseBootstrapAdapter()
    {
    }

    /// <inheritdoc />
    public async Task<Result<bool>> CheckConnectionAsync(
        DatabaseOptions options,
        SecretValue? password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            var sqlite = SqliteOptions.From(options);
            var factory = new SqliteConnectionFactory(sqlite);
            await using var connection = await factory.CreateOpenedConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            return Result<bool>.Success(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure("Database.Unreachable", $"SQLite database is unreachable: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<Result<int>> ApplyMigrationsAsync(
        DatabaseOptions options,
        SecretValue? password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            var sqlite = SqliteOptions.From(options);
            var factory = new SqliteConnectionFactory(sqlite);
            var migrator = new SchemaMigrator(factory);
            MigrationReport report = await migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);
            return Result<int>.Success(report.AppliedVersions.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<int>.Failure("Database.MigrationFailed", $"Migrations failed: {ex.Message}");
        }
    }
}

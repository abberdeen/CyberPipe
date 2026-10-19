using CyberPipe.Contracts;

namespace CyberPipe.Data;

/// <summary>
/// Адаптер шлюза bootstrap PostgreSQL под порт мастера первого запуска (TASK-090).
/// Пароль используется только внутри вызова и никогда не попадает в сообщения об ошибках.
/// </summary>
public sealed class SetupDatabaseBootstrapAdapter : IDatabaseBootstrapService
{
    private readonly ISecretStore _secretStore;

    /// <summary>Создаёт адаптер поверх хранилища секретов.</summary>
    public SetupDatabaseBootstrapAdapter(ISecretStore secretStore)
    {
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
    }

    /// <inheritdoc />
    public async Task<Result<bool>> CheckConnectionAsync(
        DatabaseOptions options,
        SecretValue password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(password);

        try
        {
            var postgres = PostgresOptions.From(options);
            var factory = new PostgresConnectionFactory(
                postgres,
                _ => Task.FromResult<SecretValue?>(password));
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
            return Result<bool>.Failure("Database.Unreachable", $"PostgreSQL is unreachable: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<Result<int>> ApplyMigrationsAsync(
        DatabaseOptions options,
        SecretValue password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(password);

        try
        {
            var postgres = PostgresOptions.From(options);
            var factory = new PostgresConnectionFactory(
                postgres,
                _ => Task.FromResult<SecretValue?>(password));
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

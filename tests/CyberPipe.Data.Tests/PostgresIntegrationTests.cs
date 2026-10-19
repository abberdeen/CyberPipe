using CyberPipe.Contracts;
using CyberPipe.Data;
using Npgsql;
using Xunit;

namespace CyberPipe.Data.Tests;

/// <summary>
/// Интеграция с реальным PostgreSQL + pgvector. Запуск: задать CYBERPIPE_TEST_POSTGRES строкой
/// подключения администратора к служебной базе (например после
/// `docker run -e POSTGRES_PASSWORD=... -p 55432:5432 pgvector/pgvector:pg16`).
/// Без переменной тесты пропускаются с явным сообщением; категория Integration исключается
/// из обычного unit-прогона фильтром Category!=Integration.
/// </summary>
/// <summary>Fact, пропускаемый на этапе обнаружения без CYBERPIPE_TEST_POSTGRES с явной причиной.</summary>
public sealed class PostgresIntegrationFactAttribute : FactAttribute
{
    /// <summary>Создаёт атрибут; без переменной окружения задаёт Skip с сообщением.</summary>
    public PostgresIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresIntegrationTests.EnvVariable)))
        {
            Skip = $"{PostgresIntegrationTests.EnvVariable} is not set: integration PostgreSQL+pgvector " +
                "is unavailable. Set it to an admin connection string (see src/CyberPipe.Data/README.md).";
        }
    }
}

[Trait("Category", "Integration")]
public sealed class PostgresIntegrationTests
{
    internal const string EnvVariable = "CYBERPIPE_TEST_POSTGRES";

    [PostgresIntegrationFact]
    public async Task Bootstrap_CleanServer_CreatesDatabaseEnablesVectorAndIsIdempotent()
    {
        string adminConnection = GetAdminConnection();
        var admin = new NpgsqlConnectionStringBuilder(adminConnection);
        string databaseName = "cyberpipe_it_" + Guid.NewGuid().ToString("N")[..12];
        string password = admin.Password ?? string.Empty;
        var options = new PostgresOptions
        {
            Host = admin.Host ?? "localhost",
            Port = admin.Port,
            Database = databaseName,
            Username = admin.Username ?? "postgres",
            PasswordSecretKey = SecretKeys.DatabasePassword,
        };
        var store = new FakeSecretStore();
        store.Seed(SecretKeys.DatabasePassword, password);
        var prompt = new FakeSecretPrompt();
        var bootstrapper = new PostgresBootstrapper(options, store, prompt);

        try
        {
            BootstrapResult first = await bootstrapper.EnsureReadyAsync();
            Assert.Equal(BootstrapStatus.Ready, first.Status);
            Assert.False(string.IsNullOrWhiteSpace(first.ServerVersion));
            Assert.False(string.IsNullOrWhiteSpace(first.PgVectorVersion));
            Assert.Equal(0, prompt.CallCount);

            // Данные между запусками bootstrap не сбрасывает.
            await using (NpgsqlConnection connection = await OpenAppAsync(options, password))
            {
                await ExecuteAsync(connection, "CREATE TABLE IF NOT EXISTS it_marker (v text)");
                await ExecuteAsync(connection, "INSERT INTO it_marker VALUES ('keep-me')");
            }

            BootstrapResult second = await bootstrapper.EnsureReadyAsync();
            Assert.Equal(BootstrapStatus.Ready, second.Status);
            Assert.Equal(0, prompt.CallCount);

            await using NpgsqlConnection verify = await OpenAppAsync(options, password);
            object? marker = await ExecuteScalarAsync(verify, "SELECT v FROM it_marker");
            Assert.Equal("keep-me", marker);
            object? vectorVersion = await ExecuteScalarAsync(
                verify, "SELECT extversion FROM pg_extension WHERE extname = 'vector'");
            Assert.Equal(first.PgVectorVersion, vectorVersion);
        }
        finally
        {
            await DropDatabaseAsync(adminConnection, databaseName);
        }
    }

    [PostgresIntegrationFact]
    public async Task Bootstrap_WrongStoredPassword_PromptSuppliesCorrectPassword()
    {
        string adminConnection = GetAdminConnection();
        var admin = new NpgsqlConnectionStringBuilder(adminConnection);
        string databaseName = "cyberpipe_it_" + Guid.NewGuid().ToString("N")[..12];
        string password = admin.Password ?? string.Empty;
        var options = new PostgresOptions
        {
            Host = admin.Host ?? "localhost",
            Port = admin.Port,
            Database = databaseName,
            Username = admin.Username ?? "postgres",
            PasswordSecretKey = SecretKeys.DatabasePassword,
        };
        var store = new FakeSecretStore();
        store.Seed(SecretKeys.DatabasePassword, "definitely-wrong-password");
        var prompt = new FakeSecretPrompt(password);
        var bootstrapper = new PostgresBootstrapper(options, store, prompt);

        try
        {
            BootstrapResult result = await bootstrapper.EnsureReadyAsync();

            Assert.Equal(BootstrapStatus.Ready, result.Status);
            Assert.Equal(1, prompt.CallCount);
            Assert.Equal(password, store.StoredPlainText(SecretKeys.DatabasePassword));
        }
        finally
        {
            await DropDatabaseAsync(adminConnection, databaseName);
        }
    }

    private static string GetAdminConnection() =>
        Environment.GetEnvironmentVariable(EnvVariable)
        ?? throw new InvalidOperationException($"{EnvVariable} is not set.");

    private static async Task<NpgsqlConnection> OpenAppAsync(PostgresOptions options, string password)
    {
        string connectionString = PostgresConnectionFactory.BuildConnectionString(
            options, options.Database, SecretValue.FromPlainText(password));
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ExecuteScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static async Task DropDatabaseAsync(string adminConnection, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(adminConnection)
        {
            Database = PostgresConnectionFactory.MaintenanceDatabase,
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var terminate = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name AND pid <> pg_backend_pid()",
            connection);
        terminate.Parameters.AddWithValue("name", databaseName);
        await terminate.ExecuteNonQueryAsync();
        await using var drop = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\"", connection);
        await drop.ExecuteNonQueryAsync();
    }
}

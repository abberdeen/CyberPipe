using CyberPipe.Contracts;
using CyberPipe.Data;
using Npgsql;
using Xunit;

namespace CyberPipe.Data.Tests;

[Trait("Category", "Integration")]
public sealed class SchemaMigratorIntegrationTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public required PostgresOptions Options { get; init; }
        public required FakeSecretStore Store { get; init; }
        public required string AdminConnection { get; init; }
        public required string DatabaseName { get; init; }
        public required string Password { get; init; }

        public PostgresConnectionFactory Factory => new(Options, Store);

        public SchemaMigrator Migrator(IMigrationSource? source = null) =>
            source is null ? new SchemaMigrator(Factory) : new SchemaMigrator(Factory, source);

        public async ValueTask DisposeAsync()
        {
            var builder = new NpgsqlConnectionStringBuilder(AdminConnection) { Database = PostgresConnectionFactory.MaintenanceDatabase };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using (var terminate = new NpgsqlCommand(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name AND pid <> pg_backend_pid()", connection))
            {
                terminate.Parameters.AddWithValue("name", DatabaseName);
                await terminate.ExecuteNonQueryAsync();
            }

            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{DatabaseName}\"", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<Fixture> CreateAsync()
    {
        string adminConnection = Environment.GetEnvironmentVariable(PostgresIntegrationTests.EnvVariable)
            ?? throw new InvalidOperationException("env not set");
        var admin = new NpgsqlConnectionStringBuilder(adminConnection);
        string databaseName = "cyberpipe_mig_" + Guid.NewGuid().ToString("N")[..12];
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
        var fixture = new Fixture { Options = options, Store = store, AdminConnection = adminConnection, DatabaseName = databaseName, Password = password };
        BootstrapResult bootstrap = await new PostgresBootstrapper(options, store, new FakeSecretPrompt()).EnsureReadyAsync();
        Assert.Equal(BootstrapStatus.Ready, bootstrap.Status);
        return fixture;
    }

    private static async Task<object?> ScalarAsync(Fixture fx, string sql)
    {
        await using NpgsqlConnection c = await fx.Factory.CreateOpenedConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        return await cmd.ExecuteScalarAsync();
    }

    [PostgresIntegrationFact]
    public async Task Migrate_EmptyDatabase_CreatesSchemaAndSecondRunIsNoOp()
    {
        await using Fixture fx = await CreateAsync();
        SchemaMigrator migrator = fx.Migrator();

        MigrationReport first = await migrator.MigrateAsync();
        Assert.NotEmpty(first.AppliedVersions);
        Assert.Equal(first.AppliedVersions.Max(), first.CurrentVersion);
        Assert.Empty(await migrator.ValidateAsync());

        foreach (string table in new[] { "embeddings", "semantic_cache", "chains", "chain_runs", "chain_checkpoints", "approvals", "token_budget_ledger", "kv_settings", "audit_log", "schema_migrations" })
            Assert.Equal(true, await ScalarAsync(fx, $"SELECT to_regclass('app.{table}') IS NOT NULL"));
        Assert.Equal(0L, Convert.ToInt64(await ScalarAsync(fx, "SELECT count(*) FROM pg_tables WHERE schemaname='quartz'")));
        Assert.NotNull(await ScalarAsync(fx, "SELECT value_json FROM app.kv_settings WHERE key='schema.version'"));

        MigrationReport second = await migrator.MigrateAsync();
        Assert.Empty(second.AppliedVersions);
        Assert.Equal(first.CurrentVersion, second.CurrentVersion);
    }

    [PostgresIntegrationFact]
    public async Task Migrate_ChangedAppliedMigration_ThrowsChecksumMismatchAndValidateReports()
    {
        await using Fixture fx = await CreateAsync();
        await fx.Migrator().MigrateAsync();
        IReadOnlyList<MigrationDefinition> original = new EmbeddedMigrationSource().GetMigrations();
        var tampered = original.Select(m => m.Version == 1
            ? m with { UpSql = m.UpSql + "\n-- tampered" }
            : m).ToArray();
        var migrator = fx.Migrator(new StaticMigrationSource(tampered));

        SchemaChecksumMismatchException ex = await Assert.ThrowsAsync<SchemaChecksumMismatchException>(() => migrator.MigrateAsync());
        Assert.Equal(1, ex.Version);
        Assert.Contains(await migrator.ValidateAsync(), d => d.Contains("checksum mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [PostgresIntegrationFact]
    public async Task Migrate_ParallelRuns_DoNotDuplicateOrPartiallyApply()
    {
        await using Fixture fx = await CreateAsync();
        MigrationReport[] reports = await Task.WhenAll(fx.Migrator().MigrateAsync(), fx.Migrator().MigrateAsync());
        Assert.Equal(1, reports.Count(r => r.AppliedVersions.Count > 0));
        Assert.Equal(reports[0].CurrentVersion, reports[1].CurrentVersion);
        Assert.Empty(await fx.Migrator().ValidateAsync());
    }

    [PostgresIntegrationFact]
    public async Task RollbackLast_AndReapply_PreservesRowsInEarlierTables()
    {
        await using Fixture fx = await CreateAsync();
        SchemaMigrator migrator = fx.Migrator();
        MigrationReport applied = await migrator.MigrateAsync();
        Guid chainId = Guid.NewGuid();
        await using (NpgsqlConnection c = await fx.Factory.CreateOpenedConnectionAsync())
        {
            await using var cmd = new NpgsqlCommand("INSERT INTO app.chains (id, name, definition_json) VALUES (@id,'keep','{}'::jsonb)", c);
            cmd.Parameters.AddWithValue("id", chainId);
            await cmd.ExecuteNonQueryAsync();
        }

        MigrationReport rolled = await migrator.RollbackLastAsync();
        Assert.Equal(applied.CurrentVersion - 1, rolled.CurrentVersion);
        Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(fx, "SELECT count(*) FROM app.chains WHERE name='keep'")));

        MigrationReport reapplied = await migrator.MigrateAsync();
        Assert.Equal([applied.CurrentVersion], reapplied.AppliedVersions);
        Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(fx, "SELECT count(*) FROM app.chains WHERE name='keep'")));
    }
}

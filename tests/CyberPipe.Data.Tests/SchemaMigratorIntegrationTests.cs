using CyberPipe.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CyberPipe.Data.Tests;

[Trait("Category", "Integration")]
public sealed class SchemaMigratorIntegrationTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public SqliteTestDatabase Database { get; private set; } = null!;

        public SqliteConnectionFactory Factory => Database.Factory;

        public SchemaMigrator Migrator(IMigrationSource? source = null) =>
            source is null ? new SchemaMigrator(Factory) : new SchemaMigrator(Factory, source);

        public Task<object?> ScalarAsync(string sql) => Database.ScalarAsync(sql);

        public static async Task<Fixture> CreateAsync()
        {
            var database = await SqliteTestDatabase.CreateBootstrappedAsync();
            return new Fixture { Database = database };
        }

        public async ValueTask DisposeAsync() => await Database.DisposeAsync();
    }

    [SqliteIntegrationFact]
    public async Task Migrate_EmptyDatabase_CreatesSchemaAndSecondRunIsNoOp()
    {
        await using Fixture fx = await Fixture.CreateAsync();
        SchemaMigrator migrator = fx.Migrator();

        MigrationReport first = await migrator.MigrateAsync();
        Assert.NotEmpty(first.AppliedVersions);
        Assert.Equal(first.AppliedVersions.Max(), first.CurrentVersion);
        Assert.Empty(await migrator.ValidateAsync());

        foreach (string table in new[] { "embeddings", "semantic_cache", "chains", "chain_runs", "chain_checkpoints", "approvals", "token_budget_ledger", "kv_settings", "audit_log", "schema_migrations", "chain_hitl_approvals", "netrunner_packages", "personal_notes", "personal_finance", "personal_reminders", "personal_contacts", "personal_interactions" })
            Assert.Equal(1L, Convert.ToInt64(await fx.ScalarAsync($"SELECT count(*) FROM sqlite_master WHERE type='table' AND name='{table}'")));
        Assert.Equal(0L, Convert.ToInt64(await fx.ScalarAsync("SELECT count(*) FROM sqlite_master WHERE type='table' AND name LIKE 'quartz%'")));
        Assert.NotNull(await fx.ScalarAsync("SELECT value_json FROM kv_settings WHERE key='schema.version'"));

        MigrationReport second = await migrator.MigrateAsync();
        Assert.Empty(second.AppliedVersions);
        Assert.Equal(first.CurrentVersion, second.CurrentVersion);
    }

    [SqliteIntegrationFact]
    public async Task Migrate_ChangedAppliedMigration_ThrowsChecksumMismatchAndValidateReports()
    {
        await using Fixture fx = await Fixture.CreateAsync();
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

    [SqliteIntegrationFact]
    public async Task Migrate_ParallelRuns_DoNotDuplicateOrPartiallyApply()
    {
        await using Fixture fx = await Fixture.CreateAsync();
        MigrationReport[] reports = await Task.WhenAll(fx.Migrator().MigrateAsync(), fx.Migrator().MigrateAsync());
        Assert.Equal(1, reports.Count(r => r.AppliedVersions.Count > 0));
        Assert.Equal(reports[0].CurrentVersion, reports[1].CurrentVersion);
        Assert.Empty(await fx.Migrator().ValidateAsync());
    }

    [SqliteIntegrationFact]
    public async Task RollbackLast_AndReapply_PreservesRowsInEarlierTables()
    {
        await using Fixture fx = await Fixture.CreateAsync();
        SchemaMigrator migrator = fx.Migrator();
        MigrationReport applied = await migrator.MigrateAsync();
        Guid chainId = Guid.NewGuid();
        await using (SqliteConnection c = await fx.Factory.CreateOpenedConnectionAsync())
        {
            await using var cmd = new SqliteCommand("INSERT INTO chains (id, name, definition_json) VALUES (@id,'keep','{}')", c);
            cmd.Parameters.AddWithValue("@id", chainId.ToString("D", System.Globalization.CultureInfo.InvariantCulture));
            await cmd.ExecuteNonQueryAsync();
        }

        MigrationReport rolled = await migrator.RollbackLastAsync();
        Assert.Equal(applied.CurrentVersion - 1, rolled.CurrentVersion);
        Assert.Equal(1L, Convert.ToInt64(await fx.ScalarAsync("SELECT count(*) FROM chains WHERE name='keep'")));

        MigrationReport reapplied = await migrator.MigrateAsync();
        Assert.Equal([applied.CurrentVersion], reapplied.AppliedVersions);
        Assert.Equal(1L, Convert.ToInt64(await fx.ScalarAsync("SELECT count(*) FROM chains WHERE name='keep'")));
    }
}

using CyberPipe.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CyberPipe.Data.Tests;

/// <summary>
/// Интеграция с реальной SQLite (WAL): временный файл на тест, сервер не нужен.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SqliteIntegrationTests
{
    [SqliteIntegrationFact]
    public async Task Bootstrap_CleanFile_EnablesWalAndIsIdempotent()
    {
        await using SqliteTestDatabase db = await SqliteTestDatabase.CreateAsync();

        object? journalMode = await db.ScalarAsync("PRAGMA journal_mode");
        Assert.Equal("wal", journalMode?.ToString());
        object? foreignKeys = await db.ScalarAsync("PRAGMA foreign_keys");
        Assert.Equal(1L, Convert.ToInt64(foreignKeys));

        // Данные между запусками bootstrap не сбрасывает.
        await using (SqliteConnection connection = await db.Factory.CreateOpenedConnectionAsync())
        {
            await using var create = new SqliteCommand("CREATE TABLE IF NOT EXISTS it_marker (v text)", connection);
            await create.ExecuteNonQueryAsync();
            await using var insert = new SqliteCommand("INSERT INTO it_marker VALUES ('keep-me')", connection);
            await insert.ExecuteNonQueryAsync();
        }

        BootstrapResult second = await new SqliteBootstrapper(db.Options).EnsureReadyAsync();
        Assert.Equal(BootstrapStatus.Ready, second.Status);

        Assert.Equal("keep-me", await db.ScalarAsync("SELECT v FROM it_marker"));
    }

    [SqliteIntegrationFact]
    public async Task Bootstrap_MissingDirectory_CreatesItAndSucceeds()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "cyberpipe-missing-" + Guid.NewGuid().ToString("N"), "sub", "test.db");
        try
        {
            var bootstrapper = new SqliteBootstrapper(new SqliteOptions { Path = path });
            BootstrapResult result = await bootstrapper.EnsureReadyAsync();
            Assert.Equal(BootstrapStatus.Ready, result.Status);
            Assert.True(File.Exists(path));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(path))!, recursive: true); }
            catch (DirectoryNotFoundException) { }
        }
    }

    [SqliteIntegrationFact]
    public async Task SetupAdapter_CheckAndMigrate_OnTempFile()
    {
        await using SqliteTestDatabase db = await SqliteTestDatabase.CreateAsync();
        var adapter = new SetupDatabaseBootstrapAdapter();

        Assert.True((await adapter.CheckConnectionAsync(db.Options.ToDatabaseOptions(), null)).Value);
        Assert.True((await adapter.ApplyMigrationsAsync(db.Options.ToDatabaseOptions(), null)).Value >= 0);
    }
}

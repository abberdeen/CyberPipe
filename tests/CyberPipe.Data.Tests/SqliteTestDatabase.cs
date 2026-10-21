using CyberPipe.Data;
using CyberPipe.Data.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CyberPipe.Data.Tests;

/// <summary>Фабрика контекстов поверх временного файла тестовой базы (WAL через общие опции).</summary>
internal sealed class TestDbContextFactory : IDbContextFactory<CyberPipeDbContext>
{
    private readonly SqliteOptions _options;

    public TestDbContextFactory(SqliteOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public CyberPipeDbContext CreateDbContext() => new(CyberPipeDbOptions.Create(_options));

    public Task<CyberPipeDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());
}

/// <summary>Fact категории Integration для SQLite: всегда выполняется (временный файл, сервер не нужен).</summary>
public sealed class SqliteIntegrationFactAttribute : FactAttribute
{
}

/// <summary>Временная файловая база SQLite с применёнными миграциями; файл удаляется при dispose.</summary>
internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    public string Path { get; }
    public SqliteOptions Options { get; }
    public SqliteConnectionFactory Factory { get; }
    public IDbContextFactory<CyberPipeDbContext> DbFactory { get; }

    private SqliteTestDatabase(string path)
    {
        Path = path;
        Options = new SqliteOptions { Path = path };
        Factory = new SqliteConnectionFactory(Options);
        DbFactory = new TestDbContextFactory(Options);
    }

    public static async Task<SqliteTestDatabase> CreateAsync(CancellationToken cancellationToken = default)
    {
        SqliteTestDatabase database = await CreateBootstrappedAsync(cancellationToken).ConfigureAwait(false);
        MigrationReport report = await new SchemaMigrator(database.Factory).MigrateAsync(cancellationToken)
            .ConfigureAwait(false);
        Assert.True(report.CurrentVersion >= 7, $"Expected migrations through 0007, got {report.CurrentVersion}");
        return database;
    }

    /// <summary>Создаёт файл базы без применения миграций (тесты вызывают MigrateAsync сами).</summary>
    public static async Task<SqliteTestDatabase> CreateBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "cyberpipe-test-" + Guid.NewGuid().ToString("N") + ".db");
        var database = new SqliteTestDatabase(path);
        BootstrapResult bootstrap = await new SqliteBootstrapper(database.Options).EnsureReadyAsync(cancellationToken)
            .ConfigureAwait(false);
        Assert.Equal(BootstrapStatus.Ready, bootstrap.Status);
        return database;
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (File.Exists(Path)) File.Delete(Path);
                string wal = Path + "-wal";
                if (File.Exists(wal)) File.Delete(wal);
                string shm = Path + "-shm";
                if (File.Exists(shm)) File.Delete(shm);
                string journal = Path + "-journal";
                if (File.Exists(journal)) File.Delete(journal);
                return;
            }
            catch (IOException)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
        }
    }

    public async Task<object?> ScalarAsync(string sql, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection c = await Factory.CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqliteCommand(sql, c);
        return await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }
}

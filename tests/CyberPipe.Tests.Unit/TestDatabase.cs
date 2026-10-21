using CyberPipe.Data;
using CyberPipe.Data.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CyberPipe.Tests.Unit;

/// <summary>Фабрика контекстов поверх временного файла тестовой базы.</summary>
internal sealed class TestDbContextFactory(SqliteOptions options) : IDbContextFactory<CyberPipeDbContext>
{
    public CyberPipeDbContext CreateDbContext() => new(CyberPipeDbOptions.Create(options));

    public Task<CyberPipeDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());
}

/// <summary>
/// Временная файловая база SQLite с применёнными миграциями и готовыми сервисами
/// данных (ChainCatalogService, ChainExecutionStoreService, HitlApprovalStoreService,
/// NetrunnerPackageStoreService). Файл удаляется при dispose.
/// </summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    public IDbContextFactory<CyberPipeDbContext> DbFactory { get; }

    public ChainCatalogService Catalog { get; }

    public ChainRunRecordService Runs { get; }

    public CheckpointStoreService Checkpoints { get; }

    public ChainExecutionStoreService Executions { get; }

    public HitlApprovalStoreService Hitl { get; }

    public NetrunnerPackageStoreService Packages { get; }

    public EmbeddingStoreService Embeddings { get; }

    private readonly string _path;

    private TestDatabase(string path, SqliteOptions options)
    {
        _path = path;
        DbFactory = new TestDbContextFactory(options);
        Catalog = new ChainCatalogService(DbFactory);
        Runs = new ChainRunRecordService(DbFactory);
        Checkpoints = new CheckpointStoreService(DbFactory);
        Executions = new ChainExecutionStoreService(Runs, Checkpoints);
        Hitl = new HitlApprovalStoreService(DbFactory);
        Packages = new NetrunnerPackageStoreService(DbFactory);
        Embeddings = new EmbeddingStoreService(DbFactory);
    }

    public static async Task<TestDatabase> CreateAsync(CancellationToken cancellationToken = default)
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "cyberpipe-test-" + Guid.NewGuid().ToString("N") + ".db");
        var options = new SqliteOptions { Path = path };
        BootstrapResult bootstrap = await new SqliteBootstrapper(options)
            .EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        Assert.Equal(BootstrapStatus.Ready, bootstrap.Status);
        MigrationReport report = await new SchemaMigrator(new SqliteConnectionFactory(options))
            .MigrateAsync(cancellationToken).ConfigureAwait(false);
        Assert.True(report.CurrentVersion >= 7, $"Expected migrations through 0007, got {report.CurrentVersion}");
        return new TestDatabase(path, options);
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (File.Exists(_path)) File.Delete(_path);
                string wal = _path + "-wal";
                if (File.Exists(wal)) File.Delete(wal);
                string shm = _path + "-shm";
                if (File.Exists(shm)) File.Delete(shm);
                string journal = _path + "-journal";
                if (File.Exists(journal)) File.Delete(journal);
                return;
            }
            catch (IOException)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
        }
    }
}

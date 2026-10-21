using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CyberPipe.Data.Persistence;

/// <summary>
/// Перехватчик WAL для EF-соединений: режим журнала, внешние ключи, busy timeout и
/// синхронный режим применяются на каждом открытии соединения (pool переиспользует
/// соединения, а PRAGMA кроме journal_mode действуют только в рамках соединения).
/// </summary>
internal sealed class SqliteWalInterceptor : DbConnectionInterceptor
{
    private readonly int _busyTimeoutMs;
    private readonly int _commandTimeout;

    public SqliteWalInterceptor(int busyTimeoutMs, int commandTimeout)
    {
        _busyTimeoutMs = busyTimeoutMs;
        _commandTimeout = commandTimeout;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        ApplyPragmas(connection);

    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ApplyPragmas(connection);
        return Task.CompletedTask;
    }

    private void ApplyPragmas(DbConnection connection)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA synchronous=NORMAL;";
        command.CommandTimeout = _commandTimeout;
        command.ExecuteNonQuery();
        using DbCommand busy = connection.CreateCommand();
        busy.CommandText = $"PRAGMA busy_timeout={_busyTimeoutMs};";
        busy.CommandTimeout = _commandTimeout;
        busy.ExecuteNonQuery();
    }
}

/// <summary>
/// Единая сборка опций контекста: connection string из <see cref="SqliteOptions" />
/// плюс WAL-перехватчик. Используется DI-регистрацией, тестами и design-time фабрикой.
/// </summary>
public static class CyberPipeDbOptions
{
    public static DbContextOptions<CyberPipeDbContext> Create(SqliteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = options.FullPath(),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        };
        var optionsBuilder = new DbContextOptionsBuilder<CyberPipeDbContext>();
        optionsBuilder.UseSqlite(builder.ConnectionString);
        optionsBuilder.AddInterceptors(new SqliteWalInterceptor(options.BusyTimeoutMs, options.CommandTimeout));
        return optionsBuilder.Options;
    }
}

/// <summary>
/// Design-time фабрика контекста для <c>dotnet ef</c>: указывает временный файл,
/// на схему не влияет (миграции генерируются из модели, а не из файла).
/// </summary>
public sealed class CyberPipeDesignTimeFactory : IDesignTimeDbContextFactory<CyberPipeDbContext>
{
    /// <inheritdoc />
    public CyberPipeDbContext CreateDbContext(string[] args) =>
        new(CyberPipeDbOptions.Create(new SqliteOptions
        {
            Path = Path.Combine(Path.GetTempPath(), "cyberpipe-design-time.db"),
        }));
}

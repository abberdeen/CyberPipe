using CyberPipe.Contracts;
using CyberPipe.Data.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CyberPipe.Data;

/// <summary>
/// Модуль регистрации доступа к данным: единственная SQLite (WAL) через EF Core.
/// Регистрирует конкретные сервисы данных (без интерфейсов репозиториев и unit-of-work):
/// каждый метод сервиса сам владеет контекстом; миграции и bootstrap выполняются
/// шагом запуска, а не здесь. Только регистрация — подключений при регистрации нет
/// (фабрики ленивые).
/// </summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>Регистрирует фабрику контекстов, пробу, миграции и сервисы данных SQLite.</summary>
    public static IServiceCollection AddCyberPipeData(this IServiceCollection services, CyberPipeOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var sqlite = SqliteOptions.From(options.Database);
        services.AddSingleton(sqlite);
        services.AddPooledDbContextFactory<CyberPipeDbContext>((sp, builder) =>
        {
            SqliteOptions o = sp.GetRequiredService<SqliteOptions>();
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = o.FullPath(),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
            }.ConnectionString;
            builder.UseSqlite(connectionString);
            builder.AddInterceptors(new SqliteWalInterceptor(o.BusyTimeoutMs, o.CommandTimeout));
        });
        services.TryAddSingleton<IDatabaseConnectionFactory>(sp =>
            new SqliteConnectionFactory(sp.GetRequiredService<SqliteOptions>()));
        services.TryAddSingleton<IDatabaseHealthProbe>(sp =>
            new SqliteHealthProbe(sp.GetRequiredService<SqliteOptions>()));
        services.TryAddSingleton<ISchemaMigrator>(sp =>
            new SchemaMigrator(sp.GetRequiredService<IDatabaseConnectionFactory>()));
        services.TryAddSingleton<IDatabaseBootstrapper>(sp =>
            new SqliteBootstrapper(sp.GetRequiredService<SqliteOptions>()));

        // Диагностика: счётчики транзакций методов и медленных запросов.
        services.AddSingleton<UnitOfWorkMetrics>();
        services.AddSingleton<IUnitOfWorkMetrics>(sp => sp.GetRequiredService<UnitOfWorkMetrics>());

        // Конкретные сервисы данных — единственная регистрация; интерфейсов нет,
        // потребители берут конкретный класс из DI.
        services.AddSingleton<ChainCatalogService>();
        services.AddSingleton<ChainRunRecordService>();
        services.AddSingleton<CheckpointStoreService>();
        services.AddSingleton<ApprovalLedgerService>();
        services.AddSingleton<TokenBudgetLedgerService>();
        services.AddSingleton<KvSettingsService>();
        services.AddSingleton<AuditLogService>();
        services.AddSingleton<ChainExecutionStoreService>();
        services.AddSingleton<HitlApprovalStoreService>();
        services.AddSingleton<NetrunnerPackageStoreService>();
        services.AddSingleton<NoteStoreService>();
        services.AddSingleton<FinanceLedgerService>();
        services.AddSingleton<ReminderStoreService>();
        services.AddSingleton<ContactStoreService>();
        services.AddSingleton<EmbeddingStoreService>();
        services.AddSingleton<BudgetLedgerService>();
        services.TryAddSingleton<IDatabaseBootstrapService, SetupDatabaseBootstrapAdapter>();

        return services;
    }
}

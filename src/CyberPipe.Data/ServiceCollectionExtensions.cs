using CyberPipe.Contracts;
using CyberPipe.Data.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CyberPipe.Data;

/// <summary>
/// Модуль регистрации доступа к данным: единственная SQLite (WAL) через EF Core.
/// Регистрирует интерфейсы сервисов данных. Потребители берут интерфейс из DI;
/// каждый метод сервиса сам владеет контекстом. Миграции и bootstrap выполняются
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

        // Диагностика сервисов: явные транзакции методов и медленные операции.
        services.AddSingleton<DataServiceMetrics>();
        services.AddSingleton<IDataServiceMetrics>(sp => sp.GetRequiredService<DataServiceMetrics>());

        // Интерфейс сервиса — то, что берут потребители. Конкретный класс остаётся
        // в контейнере, чтобы фабрика интерфейса и логирование ILogger<TImpl> совпадали.
        AddDataService<IChainCatalogService, ChainCatalogService>(services);
        AddDataService<IChainRunRecordService, ChainRunRecordService>(services);
        AddDataService<ICheckpointStoreService, CheckpointStoreService>(services);
        AddDataService<IApprovalLedgerService, ApprovalLedgerService>(services);
        AddDataService<ITokenBudgetLedgerService, TokenBudgetLedgerService>(services);
        AddDataService<IKvSettingsService, KvSettingsService>(services);
        AddDataService<IAuditLogService, AuditLogService>(services);
        AddDataService<IChainExecutionStoreService, ChainExecutionStoreService>(services);
        AddDataService<IHitlApprovalStoreService, HitlApprovalStoreService>(services);
        AddDataService<INetrunnerPackageStoreService, NetrunnerPackageStoreService>(services);
        AddDataService<INoteStoreService, NoteStoreService>(services);
        AddDataService<IFinanceLedgerService, FinanceLedgerService>(services);
        AddDataService<IReminderStoreService, ReminderStoreService>(services);
        AddDataService<IContactStoreService, ContactStoreService>(services);
        AddDataService<IEmbeddingStoreService, EmbeddingStoreService>(services);
        AddDataService<IBudgetLedgerService, BudgetLedgerService>(services);

        // Те же экземпляры закрывают прежние контрактные порты: отдельного репозитория нет.
        services.AddSingleton<IChainDefinitionRepository>(sp => sp.GetRequiredService<IChainCatalogService>());
        services.AddSingleton<IChainExecutionRepository>(sp => sp.GetRequiredService<IChainExecutionStoreService>());
        services.AddSingleton<IHitlApprovalRepository>(sp => sp.GetRequiredService<IHitlApprovalStoreService>());
        services.AddSingleton<IPackageRegistryRepository>(sp => sp.GetRequiredService<INetrunnerPackageStoreService>());
        services.AddSingleton<INoteRepository>(sp => sp.GetRequiredService<INoteStoreService>());
        services.AddSingleton<IFinanceRepository>(sp => sp.GetRequiredService<IFinanceLedgerService>());
        services.AddSingleton<IReminderRepository>(sp => sp.GetRequiredService<IReminderStoreService>());
        services.AddSingleton<IContactRepository>(sp => sp.GetRequiredService<IContactStoreService>());
        services.AddSingleton<IEmbeddingStore>(sp => sp.GetRequiredService<IEmbeddingStoreService>());
        services.AddSingleton<IBudgetStore>(sp => sp.GetRequiredService<IBudgetLedgerService>());
        services.TryAddSingleton<IDatabaseBootstrapService, SetupDatabaseBootstrapAdapter>();

        return services;
    }

    private static void AddDataService<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.AddSingleton<TImplementation>();
        services.AddSingleton<TService>(sp => sp.GetRequiredService<TImplementation>());
    }
}

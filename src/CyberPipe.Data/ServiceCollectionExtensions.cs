using CyberPipe.Contracts;
using CyberPipe.Data.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CyberPipe.Data;

/// <summary>
/// Модуль регистрации доступа к данным: единственная SQLite (WAL) через EF Core.
/// Только регистрация — подключений при регистрации нет (фабрики ленивые);
/// миграции и bootstrap выполняются шагом запуска, а не здесь.
/// </summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>Регистрирует фабрику контекстов, пробу, миграции и репозитории SQLite.</summary>
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
        services.TryAddSingleton<IUnitOfWorkFactory>(sp =>
            new UnitOfWorkFactory(
                sp.GetRequiredService<IDbContextFactory<CyberPipeDbContext>>(),
                sp.GetService<IEmbeddingStore>()));

        // SQLite-репозитории — авторитетные; вызываются после in-memory TryAdd
        // модуля цепочек, поэтому здесь Add (побеждает последняя регистрация).
        services.AddSingleton<ChainRepository>();
        services.AddSingleton<IChainRepository>(sp => sp.GetRequiredService<ChainRepository>());
        services.AddSingleton<IChainDefinitionRepository>(sp => sp.GetRequiredService<ChainRepository>());
        services.AddSingleton<IChainRunRepository, ChainRunRepository>();
        services.AddSingleton<ICheckpointRepository, CheckpointRepository>();
        services.AddSingleton<IApprovalRepository, ApprovalRepository>();
        services.AddSingleton<ITokenBudgetRepository, TokenBudgetRepository>();
        services.AddSingleton<IKeyValueSettingsRepository, KeyValueSettingsRepository>();
        services.AddSingleton<IAuditLogRepository, AuditLogRepository>();
        services.AddSingleton<IHitlApprovalRepository, SqliteHitlApprovalRepository>();
        // Бюджет губернатора — в SQLite (Data регистрируется раньше Router, его TryAdd InMemory не сработает).
        services.AddSingleton<IBudgetStore, SqliteBudgetStore>();
        // FIX-03: запуски цепочек, реестр пакетов и личные данные — в SQLite,
        // in-memory реализации Chains/Skills остаются fallback-ом standalone-композиций.
        services.AddSingleton<IChainExecutionRepository, SqliteChainExecutionRepository>();
        services.AddSingleton<IPackageRegistryRepository, SqlitePackageRegistryRepository>();
        services.AddSingleton<INoteRepository, SqliteNoteRepository>();
        services.AddSingleton<IFinanceRepository, SqliteFinanceRepository>();
        services.AddSingleton<IReminderRepository, SqliteReminderRepository>();
        services.AddSingleton<IContactRepository, SqliteContactRepository>();
        services.TryAddSingleton<IEmbeddingStore, SqliteEmbeddingStore>();
        services.TryAddSingleton<IDatabaseBootstrapService, SetupDatabaseBootstrapAdapter>();

        return services;
    }
}

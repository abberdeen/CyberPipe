using CyberPipe.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CyberPipe.Data;

/// <summary>
/// Модуль регистрации доступа к данным (TASK-090): единственная PostgreSQL + pgvector.
/// Только регистрация — подключений при регистрации нет (фабрики ленивые);
/// миграции и bootstrap выполняются шагом запуска, а не здесь.
/// </summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>Регистрирует фабрику соединений, пробу, миграции и репозитории PostgreSQL.</summary>
    public static IServiceCollection AddCyberPipeData(this IServiceCollection services, CyberPipeOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var postgres = PostgresOptions.From(options.Database);
        services.AddSingleton(postgres);
        services.TryAddSingleton<IPostgresConnectionFactory>(sp =>
            new PostgresConnectionFactory(postgres, sp.GetRequiredService<ISecretStore>()));
        services.TryAddSingleton<IDatabaseHealthProbe>(sp =>
            new PostgresHealthProbe(postgres, sp.GetRequiredService<ISecretStore>()));
        services.TryAddSingleton<ISchemaMigrator>(sp =>
            new SchemaMigrator(sp.GetRequiredService<IPostgresConnectionFactory>()));
        services.TryAddSingleton<IPostgresBootstrapper>(sp =>
            new PostgresBootstrapper(postgres, sp.GetRequiredService<ISecretStore>()));
        services.TryAddSingleton<IUnitOfWorkFactory>(sp =>
            new UnitOfWorkFactory(
                sp.GetRequiredService<IPostgresConnectionFactory>(),
                sp.GetService<IEmbeddingStore>()));

        // PostgreSQL-репозитории — авторитетные; вызываются после in-memory TryAdd
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
        services.AddSingleton<IHitlApprovalRepository, PostgresHitlApprovalRepository>();
        // Бюджет губернатора — в PostgreSQL (Data регистрируется раньше Router, его TryAdd InMemory не сработает).
        services.AddSingleton<IBudgetStore, PostgresBudgetStore>();
        services.TryAddSingleton<IEmbeddingStore, PgVectorEmbeddingStore>();
        services.TryAddSingleton<IDatabaseBootstrapService, SetupDatabaseBootstrapAdapter>();

        return services;
    }
}

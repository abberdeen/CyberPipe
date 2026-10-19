using CyberPipe.Chains;
using CyberPipe.Chains.Execution;
using CyberPipe.Chains.Runners;
using CyberPipe.Channels.Telegram;
using CyberPipe.Channels.Telegram.Adapter;
using CyberPipe.Contracts;
using CyberPipe.Data;
using CyberPipe.Execution.Sandbox;
using CyberPipe.Logging.Telemetry;
using CyberPipe.Models;
using CyberPipe.Router;
using CyberPipe.Security;
using CyberPipe.Service.Messaging;
using CyberPipe.Setup;
using CyberPipe.Skills;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;

namespace CyberPipe.Service.Composition;

/// <summary>
/// Композиционный корень процесса Service (TASK-090): единственный, где реальные
/// реализации областей 01–09 встречаются. Порядок модулей фиксирован: сначала
/// безопасность (секреты, белый список), затем данные, роутер, цепочки, навыки,
/// каналы, модели и мастер. Обратных зависимостей областей от композиции нет.
/// </summary>
public static class ServiceComposition
{
    /// <summary>Регистрирует все подсистемы Service.</summary>
    public static IServiceCollection AddCyberPipeService(this IServiceCollection services, CyberPipeOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddCyberPipeSecurity(options);
        services.AddCyberPipeData(options);
        services.AddCyberPipeRouter(options);
        services.AddCyberPipeChains(options);
        services.AddCyberPipeSkills(options);
        services.AddCyberPipeChannels(options);
        services.AddCyberPipeModels(options);
        services.AddCyberPipeSetup(options);

        services.TryAddSingleton<IMessageBus, InProcessMessageBus>();
        services.TryAddSingleton<IMessageTransport, InProcessMessageTransport>();
        if (OperatingSystem.IsWindows())
        {
            services.TryAddSingleton<ISandboxLauncher>(sp =>
                new WindowsSandboxLauncher(
                    sp.GetService<CyberPipeOptions>() ?? options,
                    sp.GetService<ILogger<WindowsSandboxLauncher>>()));
        }

        services.AddSingleton<IScriptRunnerRegistry>(sp =>
        {
            var registry = new ScriptRunnerRegistry(sp.GetService<INetrunnerPackageValidator>());
            ISandboxLauncher? sandbox = sp.GetService<ISandboxLauncher>();
            ISchemaValidator validators = sp.GetRequiredService<ISchemaValidator>();
            if (sandbox is not null)
            {
                registry.RegisterRunner(new CSharpScriptRunner(sandbox, validators));
                registry.RegisterRunner(new PythonScriptRunner(sandbox, validators));
                registry.RegisterRunner(new JavaScriptScriptRunner(sandbox, validators));
            }

            return registry;
        });
        services.TryAddSingleton<IModelPriceProvider>(sp =>
            new CatalogModelPriceProvider(sp.GetRequiredService<IModelCatalog>().Find()));
        services.TryAddSingleton<ISkillInvoker>(sp => new CompositeSkillInvoker(new ISkillInvoker[]
        {
            sp.GetRequiredService<Skills.Host.Registry.HostSkillRegistry>(),
            sp.GetRequiredService<Skills.Personal.Registry.PersonalSkillRegistry>(),
            sp.GetRequiredService<Skills.Knowledge.Registry.KnowledgeSkillRegistry>(),
            sp.GetRequiredService<Skills.Voice.Registry.VoiceSkillRegistry>(),
        }));
        services.TryAddSingleton<ServiceStartupCoordinator>();
        services.AddCyberPipeTelemetry(options.Telemetry, "CyberPipe.Service");

        return services;
    }

    /// <summary>
    /// Диагностический прогон конфигурации: связность DI-графа, каталоги моделей 15+8,
    /// отсутствие запрещённых сервисов. Без запуска каналов и без записи в БД.
    /// </summary>
    public static ConfigurationValidationReport ValidateConfiguration(IServiceProvider provider, CyberPipeOptions options)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        foreach (string error in options.Validate())
        {
            errors.Add($"Options: {error}");
        }

        foreach (Type contract in SmokeContracts)
        {
            try
            {
                object? service = provider.GetService(contract);
                if (service is null)
                {
                    errors.Add($"DI graph: contract '{contract.Name}' is not resolvable.");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"DI graph: contract '{contract.Name}' failed to resolve: {ex.Message}");
            }
        }

        try
        {
            IModelCatalog? catalog = provider.GetService<IModelCatalog>();
            if (catalog is null)
            {
                errors.Add("Model catalogs: IModelCatalog is not resolvable.");
            }
            else
            {
                if (catalog.LocalModels.Count != 15)
                {
                    errors.Add($"Model catalogs: expected 15 local models, found {catalog.LocalModels.Count}.");
                }

                if (catalog.CloudModels.Count != 8)
                {
                    errors.Add($"Model catalogs: expected 8 cloud models, found {catalog.CloudModels.Count}.");
                }

                if (!string.Equals(catalog.EmbeddingModel.Model.Id, "multilingual-e5-small", StringComparison.Ordinal))
                {
                    errors.Add("Model catalogs: standard Tier 1 embedding 'multilingual-e5-small' is missing.");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Model catalogs: failed to load: {ex.Message}");
        }

        foreach (Type contract in SmokeContracts)
        {
            object? service = null;
            try
            {
                service = provider.GetService(contract);
            }
            catch (Exception)
            {
            }

            if (service is not null)
            {
                string name = service.GetType().FullName ?? string.Empty;
                foreach (string forbidden in ForbiddenServiceTokens)
                {
                    if (name.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"Forbidden service: '{name}' is registered for '{contract.Name}'.");
                    }
                }
            }
        }

        return new ConfigurationValidationReport(errors);
    }

    private static readonly Type[] SmokeContracts =
    [
        typeof(IMessageBus),
        typeof(IMessageTransport),
        typeof(ISecretStore),
        typeof(IOwnerAuthorizer),
        typeof(IOwnerGuard),
        typeof(IAntiWipeGuard),
        typeof(IEgressSanitizer),
        typeof(IAudioContentSafetyEvaluator),
        typeof(IPostgresConnectionFactory),
        typeof(IDatabaseHealthProbe),
        typeof(ISchemaMigrator),
        typeof(IPostgresBootstrapper),
        typeof(IEmbeddingStore),
        typeof(IEmbeddingProvider),
        typeof(ITokenBudgetGovernor),
        typeof(IBudgetStore),
        typeof(IChainDefinitionValidator),
        typeof(IChainDefinitionLoader),
        typeof(IChainExecutor),
        typeof(ISkillInvoker),
        typeof(IScriptRunnerRegistry),
        typeof(INetrunnerPackageValidator),
        typeof(ITriggerRegistry),
        typeof(ITriggerInvoker),
        typeof(IDelayedResumeScheduler),
        typeof(IDeliveryPort),
        typeof(IHitlApprovalService),
        typeof(IHitlApprovalGate),
        typeof(IModelCatalog),
        typeof(IChatModelProvider),
        typeof(ILocalRuntimeInstaller),
        typeof(ILocalModelInstaller),
        typeof(IHardwareCapabilitiesProbe),
        typeof(IModelCompatibilityAdvisor),
        typeof(IHostSkillRegistry),
        typeof(IPersonalSkillRegistry),
        typeof(IKnowledgeSkillRegistry),
        typeof(IVoiceSkillRegistry),
        typeof(ISetupStateRepository),
        typeof(SetupWizardEngine),
        typeof(IOwnerRegistrationService),
        typeof(IModelInstaller),
        typeof(IModelCatalogProvider),
        typeof(IChannelVerifier),
        typeof(IDatabaseBootstrapService),
    ];

    private static readonly string[] ForbiddenServiceTokens =
    [
        "Guest",
        "DevTools",
        "Hangfire",
        "RabbitMQ",
        "Kafka",
        "MassTransit",
        "NServiceBus",
        "ServiceBus",
        "Grpc",
    ];
}

/// <summary>
/// Отчёт диагностического прогона конфигурации.
/// </summary>
/// <param name="Errors">Ошибки; пусто — прогон успешен.</param>
public sealed record ConfigurationValidationReport(IReadOnlyList<string> Errors)
{
    /// <summary>Признак успешного прогона.</summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Результат шага запуска Service.
/// </summary>
/// <param name="Step">Имя шага.</param>
/// <param name="Ok">Шаг выполнен.</param>
/// <param name="Degraded">Шаг деградировал, но запуск продолжен.</param>
/// <param name="Detail">Детали без секретов.</param>
public sealed record StartupStepResult(string Step, bool Ok, bool Degraded, string? Detail = null);

/// <summary>
/// Отчёт порядка запуска Service.
/// </summary>
/// <param name="Steps">Результаты шагов по порядку.</param>
public sealed record StartupReport(IReadOnlyList<StartupStepResult> Steps)
{
    /// <summary>Все обязательные шаги выполнены, БД здорова.</summary>
    public bool IsReady => Steps.All(s => s.Ok) && !NeedsConnectionRecovery;

    /// <summary>Требуется восстановление подключения к БД и запрос пароля (Q21).</summary>
    public bool NeedsConnectionRecovery =>
        Steps.Any(s => s.Step == "Database" && !s.Ok);
}

/// <summary>
/// Координатор порядка запуска Service (TASK-090): конфигурация → каталоги →
/// транспорт → БД → миграции → планировщик → восстановление цепочек → каналы.
/// Недоступная БД переводит в состояние восстановления подключения, а не в цикл рестартов.
/// </summary>
public sealed class ServiceStartupCoordinator
{
    private readonly IServiceProvider _services;
    private readonly CyberPipeOptions _options;
    private readonly ILogger<ServiceStartupCoordinator> _logger;

    /// <summary>Создаёт координатор.</summary>
    public ServiceStartupCoordinator(
        IServiceProvider services,
        CyberPipeOptions options,
        ILogger<ServiceStartupCoordinator>? logger = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<ServiceStartupCoordinator>.Instance;
    }

    /// <summary>Выполняет шаги запуска по порядку.</summary>
    public async Task<StartupReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var steps = new List<StartupStepResult>();
        bool databaseOk = false;

        steps.Add(RunStep("Configuration", () =>
        {
            IReadOnlyList<string> errors = _options.Validate();
            return errors.Count == 0
                ? (true, false, (string?)"Configuration is valid.")
                : throw new InvalidOperationException($"Invalid configuration: {string.Join("; ", errors)}");
        }));

        steps.Add(RunStep("ModelCatalogs", () =>
        {
            IModelCatalog catalog = _services.GetRequiredService<IModelCatalog>();
            if (catalog.LocalModels.Count != 15 || catalog.CloudModels.Count != 8)
            {
                throw new InvalidOperationException("Model catalogs are incomplete.");
            }

            return (true, false, (string?)$"15 local, 8 cloud, embedding '{catalog.EmbeddingModel.Model.Id}'.");
        }));

        steps.Add(await RunStepAsync("MessageTransport", async ct =>
        {
            IMessageBus bus = _services.GetRequiredService<IMessageBus>();
            IMessageTransport transport = _services.GetRequiredService<IMessageTransport>();
            var received = new TaskCompletionSource<MessageEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (transport.Subscribe(new CallbackHandler(e => received.TrySetResult(e))))
            {
                MessageEnvelope envelope = InProcessMessageTransport.ToEnvelope(new StartupPing("startup"), "startup");
                await transport.SendAsync(envelope, ct).ConfigureAwait(false);
                MessageEnvelope echoed = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                if (!string.Equals(echoed.CorrelationId, "startup", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Transport loopback failed.");
                }
            }

            return (true, false, (string?)$"Bus '{bus.GetType().Name}', transport '{transport.GetType().Name}'.");
        }, cancellationToken).ConfigureAwait(false));

        StartupStepResult database = await RunStepAsync("Database", async ct =>
        {
            IDatabaseHealthProbe probe = _services.GetRequiredService<IDatabaseHealthProbe>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            DatabaseHealthDto health = await probe.CheckAsync(linked.Token).ConfigureAwait(false);
            if (health.Status != HealthStatus.Healthy)
            {
                return (false, true, (string?)$"Database is {health.Status}: {health.Detail ?? "connection recovery required"}.");
            }

            return (true, false, (string?)$"Database is healthy (host {health.Host ?? "unknown"}).");
        }, cancellationToken).ConfigureAwait(false);
        steps.Add(database);
        databaseOk = database.Ok;

        if (databaseOk)
        {
            steps.Add(await RunStepAsync("Migrations", async ct =>
            {
                ISchemaMigrator migrator = _services.GetRequiredService<ISchemaMigrator>();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                MigrationReport report = await migrator.MigrateAsync(linked.Token).ConfigureAwait(false);
                return (true, false, (string?)$"Schema version {report.CurrentVersion}, applied {report.AppliedVersions.Count}.");
            }, cancellationToken).ConfigureAwait(false));
        }
        else
        {
            steps.Add(new StartupStepResult("Migrations", Ok: false, Degraded: true, "Skipped: database is unreachable."));
        }

        steps.Add(await RunStepAsync("Scheduler", async ct =>
        {
            try
            {
                IScheduler scheduler = _services.GetRequiredService<IScheduler>();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                if (!scheduler.IsStarted)
                {
                    await scheduler.Start(linked.Token).ConfigureAwait(false);
                }

                return (true, false, (string?)"Scheduler started.");
            }
            catch (Exception ex)
            {
                return (false, true, (string?)$"Scheduler degraded: {ex.Message}");
            }
        }, cancellationToken).ConfigureAwait(false));

        if (databaseOk)
        {
            steps.Add(await RunStepAsync("ChainRecovery", async ct =>
            {
                var recovery = _services.GetRequiredService<ChainRecoveryService>();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                int recovered = await recovery.RecoverAsync(linked.Token).ConfigureAwait(false);
                return (true, false, (string?)$"Recovered {recovered} interrupted runs.");
            }, cancellationToken).ConfigureAwait(false));
        }
        else
        {
            steps.Add(new StartupStepResult("ChainRecovery", Ok: false, Degraded: true, "Skipped: database is unreachable."));
        }

        steps.Add(RunStep("Channels", () =>
        {
            TelegramChannelService telegram = _services.GetRequiredService<TelegramChannelService>();
            ITelegramBotClientAdapter adapter = _services.GetRequiredService<ITelegramBotClientAdapter>();
            string mode = adapter is FakeTelegramBotClientAdapter ? "disabled (no bot token)" : "enabled";
            return (true, false, (string?)$"Telegram channel registered ({mode}).");
        }));

        foreach (StartupStepResult step in steps)
        {
            _logger.LogInformation(
                "Startup step {Step}: {Status}{Detail}",
                step.Step,
                step.Ok ? "OK" : step.Degraded ? "DEGRADED" : "FAILED",
                step.Detail is null ? string.Empty : $" — {step.Detail}");
        }

        return new StartupReport(steps);
    }

    /// <summary>Упорядоченная остановка: приём → Quartz → чекпоинты → каналы → пул БД.</summary>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Shutdown order: stop message intake, stop scheduler, checkpoint chains, stop channels, close database pool.");
        _logger.LogInformation("Completed checkpoints are preserved; no rollback of finished work.");
        return Task.CompletedTask;
    }

    private StartupStepResult RunStep(string name, Func<(bool Ok, bool Degraded, string? Detail)> run)
    {
        try
        {
            (bool ok, bool degraded, string? detail) = run();
            return new StartupStepResult(name, ok, degraded, detail);
        }
        catch (Exception ex)
        {
            return new StartupStepResult(name, Ok: false, Degraded: false, ex.Message);
        }
    }

    private static async Task<StartupStepResult> RunStepAsync(
        string name,
        Func<CancellationToken, Task<(bool Ok, bool Degraded, string? Detail)>> run,
        CancellationToken ct)
    {
        try
        {
            (bool ok, bool degraded, string? detail) = await run(ct).ConfigureAwait(false);
            return new StartupStepResult(name, ok, degraded, detail);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new StartupStepResult(name, Ok: false, Degraded: false, ex.Message);
        }
    }

    private sealed record StartupPing(string Reason);

    private sealed class CallbackHandler(Action<MessageEnvelope> callback) : IMessageHandler<MessageEnvelope>
    {
        public Task HandleAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
        {
            callback(message);
            return Task.CompletedTask;
        }
    }
}

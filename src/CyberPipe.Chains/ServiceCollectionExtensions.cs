using CyberPipe.Chains.Execution;
using CyberPipe.Chains.Hitl;
using CyberPipe.Chains.Packages;
using CyberPipe.Chains.Runners;
using CyberPipe.Data;
using CyberPipe.Chains.Scheduling;
using CyberPipe.Chains.Triggers;
using CyberPipe.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;
using Quartz.Impl;

namespace CyberPipe.Chains;

/// <summary>
/// Модуль регистрации цепочек Trigger → Skill → Script → Delivery, триггеров,
/// пакетов, раннеров, Quartz-планировщика и HITL (TASK-090).
/// Хранилища — конкретные сервисы CyberPipe.Data (ChainCatalogService,
/// ChainExecutionStoreService, HitlApprovalStoreService,
/// NetrunnerPackageStoreService), регистрируемые модулем данных.
/// </summary>
public static class ChainsServiceCollectionExtensions
{
    /// <summary>Регистрирует сервисы цепочек.</summary>
    public static IServiceCollection AddCyberPipeChains(this IServiceCollection services, CyberPipeOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.TryAddSingleton<IChainDefinitionValidator, ChainDefinitionValidator>();
        services.TryAddSingleton<IChainDefinitionLoader, ChainDefinitionLoader>();
        services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.TryAddSingleton<ITriggerRegistry, TriggerRegistry>();
        services.TryAddSingleton<ITriggerInvoker, FunctionCallTriggerInvoker>();
        services.TryAddSingleton<WebhookTriggerHandler>();
        services.TryAddSingleton<ChatTriggerListener>();
        services.TryAddSingleton<CronTriggerManager>();
        services.TryAddSingleton<INetrunnerPackageValidator, NetrunnerPackageValidator>();
        services.TryAddSingleton<ISchemaValidator, JsonSchemaDraft202012Validator>();
        services.TryAddSingleton<IChainExecutor, ChainExecutor>();
        services.TryAddSingleton<ChainRecoveryService>();
        services.TryAddSingleton<IHitlApprovalService>(sp => sp.GetRequiredService<HitlApprovalService>());
        services.TryAddSingleton<HitlApprovalService>(sp => new HitlApprovalService(
            sp.GetRequiredService<HitlApprovalStoreService>(),
            sp.GetRequiredService<IEnumerable<IHitlCardSender>>(),
            sp.GetRequiredService<IOwnerGuard>(),
            new DeferredChainExecutor(sp),
            sp.GetService<ISchemaValidator>(),
            sp.GetService<ChainCatalogService>(),
            sp.GetService<NetrunnerPackageStoreService>(),
            sp.GetService<ChainExecutionStoreService>()));
        services.TryAddSingleton<IHitlApprovalGate, HitlApprovalGate>();
        services.TryAddSingleton<IDelayedResumeScheduler>(sp =>
            new TimerDelayedResumeScheduler((runId, ct) =>
                sp.GetRequiredService<IChainExecutor>().ResumeAsync(runId, ct)));

        services.TryAddSingleton<ChainCronJob>();
        services.TryAddSingleton<ChainResumeJob>();
        services.TryAddSingleton<IScheduler>(sp =>
        {
            var factory = new StdSchedulerFactory(new System.Collections.Specialized.NameValueCollection
            {
                ["quartz.scheduler.instanceName"] = "CyberPipe",
                ["quartz.threadPool.threadCount"] = "3",
            });
            return factory.GetScheduler().GetAwaiter().GetResult();
        });
        services.TryAddSingleton<QuartzChainSchedulerService>(sp =>
            new QuartzChainSchedulerService(
                sp.GetRequiredService<IScheduler>(),
                new QuartzSchedulerOptions(),
                (chainId, fireTime) => sp.GetRequiredService<ITriggerInvoker>() is FunctionCallTriggerInvoker invoker
                    ? invoker.InvokeFunctionCallAsync(chainId, "{}", CancellationToken.None)
                    : Task.FromResult(RunId.New()),
                sp.GetService<IChainExecutor>(),
                sp.GetService<IDeliveryPort>()));
        services.TryAddSingleton<IChainSchedulerService>(sp => sp.GetRequiredService<QuartzChainSchedulerService>());
        services.TryAddSingleton<ICronTriggerRegistrar>(sp => sp.GetRequiredService<QuartzChainSchedulerService>());

        return services;
    }
}

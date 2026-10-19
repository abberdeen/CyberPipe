using System.Collections.Specialized;
using CyberPipe.Contracts;
using Quartz;
using Quartz.Impl;
using Quartz.Impl.Matchers;

namespace CyberPipe.Chains.Scheduling;

/// <summary>
/// Сервис управления расписанием Quartz.NET для цепочек и отложенных возобновлений (TASK-045).
/// Реализует <see cref="IChainSchedulerService"/>, <see cref="ICronTriggerRegistrar"/> и <see cref="IDelayedResumeScheduler"/>.
/// </summary>
public sealed class QuartzChainSchedulerService : IChainSchedulerService, ICronTriggerRegistrar, IDelayedResumeScheduler, IAsyncDisposable
{
    private readonly IScheduler _scheduler;
    private readonly QuartzSchedulerOptions _options;
    private readonly Func<ChainId, DateTimeOffset, Task> _fireCronHandler;
    private readonly IChainExecutor? _executor;
    private readonly IDeliveryPort? _deliveryPort;
    private readonly bool _ownsScheduler;

    /// <summary>
    /// Инициализирует сервис с готовым планировщиком Quartz.
    /// </summary>
    public QuartzChainSchedulerService(
        IScheduler scheduler,
        QuartzSchedulerOptions options,
        Func<ChainId, DateTimeOffset, Task> fireCronHandler,
        IChainExecutor? executor = null,
        IDeliveryPort? deliveryPort = null,
        bool ownsScheduler = true)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _fireCronHandler = fireCronHandler ?? throw new ArgumentNullException(nameof(fireCronHandler));
        _executor = executor;
        _deliveryPort = deliveryPort;
        _ownsScheduler = ownsScheduler;

        ConfigureSchedulerContext();
    }

    /// <summary>
    /// Создаёт и запускает экземпляр QuartzChainSchedulerService по переданным настройкам.
    /// </summary>
    public static async Task<QuartzChainSchedulerService> CreateAsync(
        QuartzSchedulerOptions options,
        Func<ChainId, DateTimeOffset, Task> fireCronHandler,
        IChainExecutor? executor = null,
        IDeliveryPort? deliveryPort = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fireCronHandler);

        var props = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = options.InstanceName,
            ["quartz.scheduler.instanceId"] = options.InstanceId,
            ["quartz.threadPool.maxConcurrency"] = "10",
        };

        if (options.UsePostgres && !string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            await QuartzPostgresInitializer.EnsureQuartzTablesAsync(options.ConnectionString, cancellationToken).ConfigureAwait(false);

            props["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
            props["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.PostgreSQLDelegate, Quartz";
            props["quartz.jobStore.tablePrefix"] = options.TablePrefix;
            props["quartz.jobStore.dataSource"] = "default";
            props["quartz.dataSource.default.connectionString"] = options.ConnectionString;
            props["quartz.dataSource.default.provider"] = "Npgsql";
            props["quartz.serializer.type"] = "json";
        }
        else
        {
            props["quartz.jobStore.type"] = "Quartz.Simpl.RAMJobStore, Quartz";
        }

        var factory = new StdSchedulerFactory(props);
        IScheduler scheduler = await factory.GetScheduler(cancellationToken).ConfigureAwait(false);

        scheduler.JobFactory = new QuartzJobFactory(jobType =>
        {
            if (jobType == typeof(ChainCronJob))
            {
                return new ChainCronJob(fireCronHandler, deliveryPort);
            }

            if (jobType == typeof(ChainResumeJob))
            {
                return new ChainResumeJob(executor);
            }

            return (IJob)Activator.CreateInstance(jobType)!;
        });

        var service = new QuartzChainSchedulerService(scheduler, options, fireCronHandler, executor, deliveryPort, ownsScheduler: true);
        await scheduler.Start(cancellationToken).ConfigureAwait(false);

        return service;
    }

    private void ConfigureSchedulerContext()
    {
        _scheduler.Context.Put("FireCronHandler", _fireCronHandler);
        if (_executor is not null)
        {
            _scheduler.Context.Put("ChainExecutor", _executor);
        }

        if (_deliveryPort is not null)
        {
            _scheduler.Context.Put("DeliveryPort", _deliveryPort);
        }
    }

    /// <inheritdoc />
    public async Task ScheduleChainAsync(
        ChainId chainId,
        string cronExpression,
        ChainMisfirePolicy misfirePolicy = ChainMisfirePolicy.FireOnceNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cronExpression);

        if (!CronExpression.IsValidExpression(cronExpression))
        {
            throw new InvalidCronExpressionException(
                cronExpression,
                $"Cron expression '{cronExpression}' is invalid for Quartz scheduler (6-7 fields format with seconds required).");
        }

        var jobKey = new JobKey("chain/" + chainId.Value, "chains");
        var triggerKey = new TriggerKey("chain/" + chainId.Value + "/cron", "chains");

        CronScheduleBuilder scheduleBuilder = CronScheduleBuilder.CronSchedule(cronExpression);
        switch (misfirePolicy)
        {
            case ChainMisfirePolicy.FireOnceNow:
            case ChainMisfirePolicy.SkipIfLate:
            case ChainMisfirePolicy.AskUser:
                scheduleBuilder = scheduleBuilder.WithMisfireHandlingInstructionFireAndProceed();
                break;
            case ChainMisfirePolicy.DoNothing:
                scheduleBuilder = scheduleBuilder.WithMisfireHandlingInstructionDoNothing();
                break;
            case ChainMisfirePolicy.IgnoreMisfires:
                scheduleBuilder = scheduleBuilder.WithMisfireHandlingInstructionIgnoreMisfires();
                break;
        }

        var jobData = new JobDataMap
        {
            ["ChainId"] = chainId.Value.ToString(),
            ["MisfirePolicy"] = misfirePolicy.ToString(),
            ["LateMinutesThreshold"] = _options.DefaultMisfireLateMinutes,
            ["CronExpression"] = cronExpression,
        };

        IJobDetail job = JobBuilder.Create<ChainCronJob>()
            .WithIdentity(jobKey)
            .UsingJobData(jobData)
            .StoreDurably()
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .WithSchedule(scheduleBuilder)
            .UsingJobData(jobData)
            .Build();

        bool jobExists = await _scheduler.CheckExists(jobKey, cancellationToken).ConfigureAwait(false);
        bool triggerExists = await _scheduler.CheckExists(triggerKey, cancellationToken).ConfigureAwait(false);

        if (triggerExists)
        {
            await _scheduler.RescheduleJob(triggerKey, trigger, cancellationToken).ConfigureAwait(false);
            await _scheduler.AddJob(job, replace: true, cancellationToken).ConfigureAwait(false);
        }
        else if (jobExists)
        {
            await _scheduler.AddJob(job, replace: true, cancellationToken).ConfigureAwait(false);
            await _scheduler.ScheduleJob(trigger, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _scheduler.ScheduleJob(job, trigger, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task PauseChainAsync(ChainId chainId, CancellationToken cancellationToken = default)
    {
        var jobKey = new JobKey("chain/" + chainId.Value, "chains");
        await _scheduler.PauseJob(jobKey, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResumeChainAsync(ChainId chainId, CancellationToken cancellationToken = default)
    {
        var jobKey = new JobKey("chain/" + chainId.Value, "chains");
        await _scheduler.ResumeJob(jobKey, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UnscheduleChainAsync(ChainId chainId, CancellationToken cancellationToken = default)
    {
        var jobKey = new JobKey("chain/" + chainId.Value, "chains");
        await _scheduler.DeleteJob(jobKey, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduledChainInfo>> ListScheduledAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<ScheduledChainInfo>();
        IReadOnlyCollection<JobKey> jobKeys = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("chains"), cancellationToken).ConfigureAwait(false);

        foreach (JobKey jobKey in jobKeys)
        {
            string idPart = jobKey.Name.StartsWith("chain/", StringComparison.Ordinal)
                ? jobKey.Name["chain/".Length..]
                : jobKey.Name;

            if (!Guid.TryParse(idPart, out Guid parsedId))
            {
                continue;
            }

            var chainId = new ChainId(parsedId);
            var triggerKey = new TriggerKey("chain/" + parsedId + "/cron", "chains");
            ITrigger? trigger = await _scheduler.GetTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
            if (trigger is null)
            {
                continue;
            }

            TriggerState state = await _scheduler.GetTriggerState(triggerKey, cancellationToken).ConfigureAwait(false);
            bool isPaused = state == TriggerState.Paused;

            string cron = (trigger as ICronTrigger)?.CronExpressionString
                ?? trigger.JobDataMap.GetString("CronExpression")
                ?? string.Empty;

            string? policyString = trigger.JobDataMap.GetString("MisfirePolicy");
            ChainMisfirePolicy policy = Enum.TryParse<ChainMisfirePolicy>(policyString, out var parsedPolicy)
                ? parsedPolicy
                : ChainMisfirePolicy.FireOnceNow;

            DateTimeOffset? nextFire = trigger.GetNextFireTimeUtc();

            list.Add(new ScheduledChainInfo
            {
                ChainId = chainId,
                CronExpression = cron,
                NextFireTimeUtc = nextFire,
                IsPaused = isPaused,
                MisfirePolicy = policy,
            });
        }

        return list;
    }

    /// <inheritdoc />
    public async Task TriggerChainNowAsync(ChainId chainId, CancellationToken cancellationToken = default)
    {
        await _fireCronHandler(chainId, DateTimeOffset.UtcNow).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SyncWithRepositoryAsync(
        IChainDefinitionRepository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        IReadOnlyList<ChainDefinitionDto> enabled = await repository.ListEnabledAsync(cancellationToken).ConfigureAwait(false);
        var expectedCronChains = new Dictionary<ChainId, ChainDefinitionDto>();

        foreach (ChainDefinitionDto chain in enabled)
        {
            if (chain.Trigger.Kind == ChainTriggerKind.Cron && !string.IsNullOrWhiteSpace(chain.Trigger.CronExpression))
            {
                expectedCronChains[chain.Id] = chain;
                await ScheduleChainAsync(chain.Id, chain.Trigger.CronExpression, chain.MisfirePolicy, cancellationToken).ConfigureAwait(false);
            }
        }

        // Удаление сиротских заданий из Quartz, которых нет среди включённых cron-цепочек в репозитории
        IReadOnlyCollection<JobKey> jobKeys = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("chains"), cancellationToken).ConfigureAwait(false);
        foreach (JobKey jobKey in jobKeys)
        {
            string idPart = jobKey.Name.StartsWith("chain/", StringComparison.Ordinal)
                ? jobKey.Name["chain/".Length..]
                : jobKey.Name;

            if (Guid.TryParse(idPart, out Guid parsedId))
            {
                var chainId = new ChainId(parsedId);
                if (!expectedCronChains.ContainsKey(chainId))
                {
                    await _scheduler.DeleteJob(jobKey, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <inheritdoc />
    public Task RegisterCronAsync(ChainId chainId, string cronExpression, CancellationToken cancellationToken = default) =>
        ScheduleChainAsync(chainId, cronExpression, ChainMisfirePolicy.FireOnceNow, cancellationToken);

    /// <inheritdoc />
    public Task UnregisterCronAsync(ChainId chainId, CancellationToken cancellationToken = default) =>
        UnscheduleChainAsync(chainId, cancellationToken);

    /// <inheritdoc />
    public Task ScheduleResumeAsync(RunId runId, TimeSpan delay, CancellationToken cancellationToken = default) =>
        ScheduleResumeAsync(runId, delay, "script", cancellationToken);

    /// <inheritdoc />
    public async Task ScheduleResumeAsync(RunId runId, TimeSpan delay, string stepId, CancellationToken cancellationToken = default)
    {
        var jobKey = new JobKey("resume/" + runId.Value, "resumes");
        var triggerKey = new TriggerKey("resume/" + runId.Value + "/" + stepId, "resumes");

        var jobData = new JobDataMap
        {
            ["RunId"] = runId.Value.ToString(),
            ["StepId"] = stepId,
        };

        IJobDetail job = JobBuilder.Create<ChainResumeJob>()
            .WithIdentity(jobKey)
            .UsingJobData(jobData)
            .StoreDurably()
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartAt(DateTimeOffset.UtcNow.Add(delay))
            .WithSimpleSchedule()
            .UsingJobData(jobData)
            .Build();

        bool jobExists = await _scheduler.CheckExists(jobKey, cancellationToken).ConfigureAwait(false);
        bool triggerExists = await _scheduler.CheckExists(triggerKey, cancellationToken).ConfigureAwait(false);

        if (triggerExists)
        {
            await _scheduler.RescheduleJob(triggerKey, trigger, cancellationToken).ConfigureAwait(false);
        }
        else if (jobExists)
        {
            await _scheduler.ScheduleJob(trigger, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _scheduler.ScheduleJob(job, trigger, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_ownsScheduler && !_scheduler.IsShutdown)
        {
            await _scheduler.Shutdown(waitForJobsToComplete: false).ConfigureAwait(false);
        }
    }
}

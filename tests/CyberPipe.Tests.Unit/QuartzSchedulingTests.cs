using CyberPipe.Chains;
using CyberPipe.Chains.Scheduling;
using CyberPipe.Contracts;
using Quartz;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class QuartzSchedulingTests
{
    private sealed class FakeDeliveryPort : IDeliveryPort
    {
        public List<DeliveryRequest> SentRequests { get; } = [];

        public Task<DeliveryResult> SendAsync(DeliveryRequest request, CancellationToken cancellationToken = default)
        {
            SentRequests.Add(request);
            return Task.FromResult(new DeliveryResult
            {
                IsSuccess = true,
                DeliveredMessageId = MessageId.New(),
                DeliveredAtUtc = DateTimeOffset.UtcNow,
            });
        }
    }

    private sealed class FakeChainExecutor : IChainExecutor
    {
        public List<RunId> ResumedRuns { get; } = [];
        public List<RunId> CancelledRuns { get; } = [];
        public List<TriggerEvent> StartedRuns { get; } = [];

        public Task<RunId> StartAsync(TriggerEvent trigger, CancellationToken cancellationToken = default)
        {
            StartedRuns.Add(trigger);
            return Task.FromResult(RunId.New());
        }

        public Task ResumeAsync(RunId runId, CancellationToken cancellationToken = default)
        {
            ResumedRuns.Add(runId);
            return Task.CompletedTask;
        }

        public Task CancelAsync(RunId runId, CancellationToken cancellationToken = default)
        {
            CancelledRuns.Add(runId);
            return Task.CompletedTask;
        }

        public Task<ChainRunState?> GetRunAsync(RunId runId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ChainRunState?>(null);
    }

    [Fact]
    public async Task CronValidation_RejectsInvalidExpressions_AndDoesNotCreateJob()
    {
        var fireCronCalls = new List<(ChainId ChainId, DateTimeOffset Time)>();
        var options = new QuartzSchedulerOptions
        {
            InstanceName = "TestScheduler_Validation_" + Guid.NewGuid().ToString("N"),
            UseSqlite = false,
        };

        await using var scheduler = await QuartzChainSchedulerService.CreateAsync(
            options,
            (id, time) =>
            {
                fireCronCalls.Add((id, time));
                return Task.CompletedTask;
            });

        string[] invalidCrons = ["ежедневно", "* * * *", "", "   ", "invalid_cron_expression", "60 * * * * ?"];

        foreach (string invalid in invalidCrons)
        {
            var ex = await Assert.ThrowsAsync<InvalidCronExpressionException>(() =>
                scheduler.ScheduleChainAsync(ChainId.New(), invalid));

            Assert.Equal("Scheduler.InvalidCronExpression", ex.Code);
            Assert.Equal(invalid, ex.Expression);
        }

        IReadOnlyList<ScheduledChainInfo> scheduled = await scheduler.ListScheduledAsync();
        Assert.Empty(scheduled);
        Assert.Empty(fireCronCalls);
    }

    [Fact]
    public async Task ScheduleChain_ValidCron_RegistersAndCanTriggerNow()
    {
        var fired = new TaskCompletionSource<(ChainId ChainId, DateTimeOffset Time)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new QuartzSchedulerOptions
        {
            InstanceName = "TestScheduler_TriggerNow_" + Guid.NewGuid().ToString("N"),
            UseSqlite = false,
        };

        var chainId = ChainId.New();
        await using var scheduler = await QuartzChainSchedulerService.CreateAsync(
            options,
            (id, time) =>
            {
                fired.TrySetResult((id, time));
                return Task.CompletedTask;
            });

        DateTimeOffset beforeSchedule = DateTimeOffset.UtcNow;
        await scheduler.ScheduleChainAsync(chainId, "0 * * * * ?");

        IReadOnlyList<ScheduledChainInfo> list = await scheduler.ListScheduledAsync();
        ScheduledChainInfo info = Assert.Single(list);
        Assert.Equal(chainId, info.ChainId);
        Assert.Equal("0 * * * * ?", info.CronExpression);
        Assert.False(info.IsPaused);
        Assert.NotNull(info.NextFireTimeUtc);
        Assert.True(info.NextFireTimeUtc >= beforeSchedule.AddSeconds(-1));

        // Ручной запуск TriggerChainNowAsync
        await scheduler.TriggerChainNowAsync(chainId);
        (ChainId firedId, DateTimeOffset firedTime) = await fired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(chainId, firedId);
        Assert.True(firedTime <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ScheduleChain_Reschedule_ReplacesSchedule_AndPauseResumeUnscheduleWork()
    {
        var options = new QuartzSchedulerOptions
        {
            InstanceName = "TestScheduler_Lifecycle_" + Guid.NewGuid().ToString("N"),
            UseSqlite = false,
        };

        var chainId = ChainId.New();
        await using var scheduler = await QuartzChainSchedulerService.CreateAsync(
            options,
            (_, _) => Task.CompletedTask);

        // Первичная регистрация
        await scheduler.ScheduleChainAsync(chainId, "0 0 12 * * ?");
        IReadOnlyList<ScheduledChainInfo> list1 = await scheduler.ListScheduledAsync();
        ScheduledChainInfo info1 = Assert.Single(list1);
        Assert.Equal("0 0 12 * * ?", info1.CronExpression);
        DateTimeOffset? next1 = info1.NextFireTimeUtc;

        // Повторная регистрация с другим cron (reschedule)
        await scheduler.ScheduleChainAsync(chainId, "0 0 18 * * ?");
        IReadOnlyList<ScheduledChainInfo> list2 = await scheduler.ListScheduledAsync();
        ScheduledChainInfo info2 = Assert.Single(list2); // Должен быть ровно один job!
        Assert.Equal("0 0 18 * * ?", info2.CronExpression);
        DateTimeOffset? next2 = info2.NextFireTimeUtc;
        Assert.NotEqual(next1, next2);

        // Pause
        await scheduler.PauseChainAsync(chainId);
        IReadOnlyList<ScheduledChainInfo> listPaused = await scheduler.ListScheduledAsync();
        Assert.True(listPaused[0].IsPaused);

        // Resume
        await scheduler.ResumeChainAsync(chainId);
        IReadOnlyList<ScheduledChainInfo> listResumed = await scheduler.ListScheduledAsync();
        Assert.False(listResumed[0].IsPaused);

        // Unschedule
        await scheduler.UnscheduleChainAsync(chainId);
        IReadOnlyList<ScheduledChainInfo> listEmpty = await scheduler.ListScheduledAsync();
        Assert.Empty(listEmpty);
    }

    [Fact]
    public async Task ScheduleChain_FiresAutomatically_OnSchedule()
    {
        var firedTcs = new TaskCompletionSource<ChainId>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new QuartzSchedulerOptions
        {
            InstanceName = "TestScheduler_AutoFire_" + Guid.NewGuid().ToString("N"),
            UseSqlite = false,
        };

        var chainId = ChainId.New();
        await using var scheduler = await QuartzChainSchedulerService.CreateAsync(
            options,
            (id, _) =>
            {
                if (id == chainId)
                {
                    firedTcs.TrySetResult(id);
                }

                return Task.CompletedTask;
            });

        // Срабатывать каждую секунду: * * * * * ?
        await scheduler.ScheduleChainAsync(chainId, "* * * * * ?");

        ChainId resultId = await firedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(chainId, resultId);
    }

    [Fact]
    public async Task Misfire_FireOnceNow_FiresEvenIfLate()
    {
        var fireCalls = new List<(ChainId Id, DateTimeOffset Time)>();
        var delivery = new FakeDeliveryPort();

        var job = new ChainCronJob(
            (id, time) =>
            {
                fireCalls.Add((id, time));
                return Task.CompletedTask;
            },
            delivery);

        var chainId = ChainId.New();
        var scheduledTime = DateTimeOffset.UtcNow.AddMinutes(-10); // 10 минут назад
        var actualTime = DateTimeOffset.UtcNow;

        var jobData = new JobDataMap
        {
            ["ChainId"] = chainId.Value.ToString(),
            ["MisfirePolicy"] = ChainMisfirePolicy.FireOnceNow.ToString(),
            ["LateMinutesThreshold"] = 5,
        };

        IJobExecutionContext ctx = TestJobExecutionContext.Create(jobData, scheduledTime, actualTime);
        await job.Execute(ctx);

        Assert.Single(fireCalls);
        Assert.Equal(chainId, fireCalls[0].Id);
        Assert.Equal(scheduledTime.UtcDateTime, fireCalls[0].Time.UtcDateTime);
        Assert.Empty(delivery.SentRequests);
    }

    [Fact]
    public async Task Misfire_SkipIfLate_SkipsExecutionWhenLate()
    {
        var fireCalls = new List<(ChainId Id, DateTimeOffset Time)>();
        var delivery = new FakeDeliveryPort();

        var job = new ChainCronJob(
            (id, time) =>
            {
                fireCalls.Add((id, time));
                return Task.CompletedTask;
            },
            delivery);

        var chainId = ChainId.New();
        var scheduledTime = DateTimeOffset.UtcNow.AddMinutes(-10); // Опоздание 10 минут при пороге 5
        var actualTime = DateTimeOffset.UtcNow;

        var jobData = new JobDataMap
        {
            ["ChainId"] = chainId.Value.ToString(),
            ["MisfirePolicy"] = ChainMisfirePolicy.SkipIfLate.ToString(),
            ["LateMinutesThreshold"] = 5,
        };

        IJobExecutionContext ctx = TestJobExecutionContext.Create(jobData, scheduledTime, actualTime);
        await job.Execute(ctx);

        // Должно быть пропущено!
        Assert.Empty(fireCalls);
        Assert.Empty(delivery.SentRequests);
    }

    [Fact]
    public async Task Misfire_AskUser_SendsDeliveryRequest_AndDoesNotFire()
    {
        var fireCalls = new List<(ChainId Id, DateTimeOffset Time)>();
        var delivery = new FakeDeliveryPort();

        var job = new ChainCronJob(
            (id, time) =>
            {
                fireCalls.Add((id, time));
                return Task.CompletedTask;
            },
            delivery);

        var chainId = ChainId.New();
        var scheduledTime = DateTimeOffset.UtcNow.AddMinutes(-10); // Опоздание 10 минут при пороге 5
        var actualTime = DateTimeOffset.UtcNow;

        var jobData = new JobDataMap
        {
            ["ChainId"] = chainId.Value.ToString(),
            ["MisfirePolicy"] = ChainMisfirePolicy.AskUser.ToString(),
            ["LateMinutesThreshold"] = 5,
        };

        IJobExecutionContext ctx = TestJobExecutionContext.Create(jobData, scheduledTime, actualTime);
        await job.Execute(ctx);

        // FireCron НЕ должен быть вызван
        Assert.Empty(fireCalls);

        // Должен быть отправлен ровно один запрос через deliveryPort
        DeliveryRequest req = Assert.Single(delivery.SentRequests);
        Assert.Equal("owner", req.Target);
        Assert.Contains(chainId.ToString(), req.Text!);
        Assert.Contains("MisfireAskUser", req.PayloadJson!);
    }

    [Fact]
    public async Task SyncWithRepository_SynchronizesDrift_InFavorOfRepository()
    {
        var repo = new InMemoryChainDefinitionRepository();
        var options = new QuartzSchedulerOptions
        {
            InstanceName = "TestScheduler_Sync_" + Guid.NewGuid().ToString("N"),
            UseSqlite = false,
        };

        await using var scheduler = await QuartzChainSchedulerService.CreateAsync(
            options,
            (_, _) => Task.CompletedTask);

        var activeChain1 = CreateChain("Active1", isEnabled: true, cron: "0 0 10 * * ?");
        var disabledChain2 = CreateChain("Disabled2", isEnabled: false, cron: "0 0 11 * * ?");
        var activeChain3 = CreateChain("Active3", isEnabled: true, cron: "0 0 12 * * ?");

        await repo.UpsertAsync(activeChain1);
        await repo.UpsertAsync(disabledChain2);
        await repo.UpsertAsync(activeChain3);

        // Синхронизация: должны зарегистрироваться только Active1 и Active3
        await scheduler.SyncWithRepositoryAsync(repo);

        IReadOnlyList<ScheduledChainInfo> list1 = await scheduler.ListScheduledAsync();
        Assert.Equal(2, list1.Count);
        Assert.Contains(list1, s => s.ChainId == activeChain1.Id);
        Assert.Contains(list1, s => s.ChainId == activeChain3.Id);
        Assert.DoesNotContain(list1, s => s.ChainId == disabledChain2.Id);

        // Теперь отключаем Active1 и удаляем Active3 из репозитория
        await repo.SetEnabledAsync(activeChain1.Id, false);
        // Репозиторий in-memory: перезаписываем репозиторий новым, где есть только новая цепочка 4
        var repo2 = new InMemoryChainDefinitionRepository();
        var activeChain4 = CreateChain("Active4", isEnabled: true, cron: "0 0 15 * * ?");
        await repo2.UpsertAsync(activeChain4);

        // Синхронизация с новым состоянием
        await scheduler.SyncWithRepositoryAsync(repo2);

        IReadOnlyList<ScheduledChainInfo> list2 = await scheduler.ListScheduledAsync();
        ScheduledChainInfo only4 = Assert.Single(list2);
        Assert.Equal(activeChain4.Id, only4.ChainId);
        Assert.Equal("0 0 15 * * ?", only4.CronExpression);
    }

    [Fact]
    public async Task ScheduleResume_FiresExecutorResume_AfterDelay()
    {
        var executor = new FakeChainExecutor();
        var options = new QuartzSchedulerOptions
        {
            InstanceName = "TestScheduler_Resume_" + Guid.NewGuid().ToString("N"),
            UseSqlite = false,
        };

        await using var scheduler = await QuartzChainSchedulerService.CreateAsync(
            options,
            (_, _) => Task.CompletedTask,
            executor: executor);

        var runId = RunId.New();

        // Планируем возобновление через 150 миллисекунд
        await scheduler.ScheduleResumeAsync(runId, TimeSpan.FromMilliseconds(150), "script");

        Assert.Empty(executor.ResumedRuns);

        // Ожидаем срабатывания (с запасом до 3 секунд)
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (executor.ResumedRuns.Count == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.Single(executor.ResumedRuns);
        Assert.Equal(runId, executor.ResumedRuns[0]);
    }

    [Fact]
    public async Task CronTriggerRegistrar_InterfaceMethods_WorkAsExpected()
    {
        var options = new QuartzSchedulerOptions
        {
            InstanceName = "TestScheduler_Registrar_" + Guid.NewGuid().ToString("N"),
            UseSqlite = false,
        };

        await using var scheduler = await QuartzChainSchedulerService.CreateAsync(
            options,
            (_, _) => Task.CompletedTask);

        ICronTriggerRegistrar registrar = scheduler;
        var chainId = ChainId.New();

        await registrar.RegisterCronAsync(chainId, "0 0 8 * * ?");
        IReadOnlyList<ScheduledChainInfo> list = await scheduler.ListScheduledAsync();
        Assert.Single(list);
        Assert.Equal(chainId, list[0].ChainId);

        await registrar.UnregisterCronAsync(chainId);
        IReadOnlyList<ScheduledChainInfo> empty = await scheduler.ListScheduledAsync();
        Assert.Empty(empty);
    }

    private static ChainDefinitionDto CreateChain(string name, bool isEnabled, string cron) => new()
    {
        Id = ChainId.New(),
        Name = name,
        IsEnabled = isEnabled,
        Trigger = new ChainTriggerDto
        {
            Kind = ChainTriggerKind.Cron,
            CronExpression = cron,
        },
        Skill = new ChainSkillStepDto { SkillId = "test-skill" },
        Script = new ChainScriptStepDto { PackageSlug = "test-pkg", PackageVersion = "1.0.0" },
        Delivery = new ChainDeliveryStepDto { Channel = ChannelKind.Telegram, Target = "chat-1", MessageTemplate = "done" },
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
    };

    private sealed class TestJobExecutionContext : IJobExecutionContext
    {
        public JobDataMap MergedJobDataMap { get; init; } = new();
        public DateTimeOffset? ScheduledFireTimeUtc { get; init; }
        public DateTimeOffset FireTimeUtc { get; init; }
        public IScheduler Scheduler => null!;
        public ITrigger Trigger => null!;
        public ICalendar? Calendar => null;
        public bool Recovering => false;
        public TriggerKey RecoveringTriggerKey => TriggerKey;
        public TriggerKey TriggerKey => new("test", "test");
        public string FireInstanceId => "test";
        public IJobDetail JobDetail => null!;
        public IJob JobInstance => null!;
        public DateTimeOffset? NextFireTimeUtc => null;
        public DateTimeOffset? PreviousFireTimeUtc => null;
        public int RefireCount => 0;
        public TimeSpan JobRunTime => TimeSpan.Zero;
        public object? Result { get; set; }
        public CancellationToken CancellationToken => CancellationToken.None;

        public static TestJobExecutionContext Create(JobDataMap data, DateTimeOffset scheduled, DateTimeOffset actual) => new()
        {
            MergedJobDataMap = data,
            ScheduledFireTimeUtc = scheduled,
            FireTimeUtc = actual,
        };

        public void Put(object key, object objectValue) { }
        public object? Get(object key) => null;
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task SqliteQuartz_PersistsAcrossRestart_AndCleansUp()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "cyberpipe-quartz-" + Guid.NewGuid().ToString("N") + ".db");
        string connectionString = "Data Source=" + dbPath;
        try
        {
            var options = new QuartzSchedulerOptions
            {
                InstanceName = "SqliteScheduler_" + Guid.NewGuid().ToString("N")[..8],
                InstanceId = "instance-1",
                UseSqlite = true,
                ConnectionString = connectionString,
                TablePrefix = "qrtz_",
            };

        var chainId = ChainId.New();
        DateTimeOffset? fireTime1;

        // Первый экземпляр: регистрируем задание
        await using (var scheduler1 = await QuartzChainSchedulerService.CreateAsync(
            options,
            (_, _) => Task.CompletedTask))
        {
            await scheduler1.ScheduleChainAsync(chainId, "0 0 12 * * ?");
            IReadOnlyList<ScheduledChainInfo> list1 = await scheduler1.ListScheduledAsync();
            ScheduledChainInfo info1 = Assert.Single(list1);
            Assert.Equal("0 0 12 * * ?", info1.CronExpression);
            fireTime1 = info1.NextFireTimeUtc;
            Assert.NotNull(fireTime1);
        }

            // Второй экземпляр («рестарт» хоста на той же БД)
            var options2 = new QuartzSchedulerOptions
            {
                InstanceName = options.InstanceName,
                InstanceId = "instance-2",
                UseSqlite = true,
                ConnectionString = connectionString,
                TablePrefix = "qrtz_",
            };

            await using (var scheduler2 = await QuartzChainSchedulerService.CreateAsync(
                options2,
                (_, _) => Task.CompletedTask))
            {
                IReadOnlyList<ScheduledChainInfo> list2 = await scheduler2.ListScheduledAsync();
                ScheduledChainInfo info2 = Assert.Single(list2);
                Assert.Equal(chainId, info2.ChainId);
                Assert.Equal("0 0 12 * * ?", info2.CronExpression);
                Assert.Equal(fireTime1, info2.NextFireTimeUtc);

                // Очищаем
                await scheduler2.UnscheduleChainAsync(chainId);
                IReadOnlyList<ScheduledChainInfo> listEmpty = await scheduler2.ListScheduledAsync();
                Assert.Empty(listEmpty);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                try
                {
                    string file = dbPath + suffix;
                    if (File.Exists(file)) File.Delete(file);
                }
                catch (IOException) { }
            }
        }
    }
}

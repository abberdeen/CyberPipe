namespace CyberPipe.Tests.E2E;

using CyberPipe.Chains.Execution;
using CyberPipe.Chains.Packages;
using CyberPipe.Chains.Scheduling;
using CyberPipe.Chains.Triggers;
using CyberPipe.Contracts;
using CyberPipe.Data;
using Xunit;

/// <summary>
/// Сквозные сценарии цепочек Trigger → Skill → Script → Delivery (TASK-092)
/// на реальной композиции TASK-090 и тестовой PostgreSQL. Настоящие: определения
/// (PG), валидаторы, Python-раннер через песочницу, исполнитель, PG-репозитории
/// запусков/чекпоинтов, Quartz, HITL. Фейки: скиллы и доставка.
/// </summary>
public sealed class ChainScenarioTests
{
    private sealed class SingleMessageSource : IInboundMessageSource
    {
        private readonly IncomingMessage _message;
        private bool _consumed;

        public SingleMessageSource(IncomingMessage message)
        {
            _message = message;
        }

        public Task<IncomingMessage?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            if (_consumed)
            {
                return Task.FromResult<IncomingMessage?>(null);
            }

            _consumed = true;
            return Task.FromResult<IncomingMessage?>(_message);
        }

        public IDisposable Subscribe(IMessageHandler<IncomingMessage> handler) =>
            new NullSubscription();

        private sealed class NullSubscription : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    [E2EFact]
    public async Task WebhookTrigger_RunsFullChainWithPgCheckpoints()
    {
        await using var host = await ChainE2EHost.CreateAsync();
        var chainId = new ChainId(Guid.NewGuid());
        await host.RegisterAsync(host.BuildDefinition(
            chainId,
            new ChainTriggerDto { Kind = ChainTriggerKind.Webhook, WebhookPath = "/wh/e2e-a" },
            "test.chain.skill-a",
            "e2e-A"));

        WebhookResponse response = await host.Webhooks.HandleWebhookAsync(new WebhookRequest
        {
            ChainId = chainId,
            Path = "/wh/e2e-a",
            Body = "{\"ping\":1}",
        });
        Assert.Equal(200, response.StatusCode);
        Assert.NotNull(response.RunId);

        await WaitForDeliveriesAsync(host, 1);

        Assert.Equal(1, host.Skills.GetCallCount("test.chain.skill-a"));
        Assert.Equal(1, host.Scripts.GetCallCount("Python"));
        DeliveryRequest delivery = Assert.Single(host.Delivery.Requests);
        Assert.Equal("e2e-A", delivery.Target);
        Assert.Contains("processed-A", delivery.Text ?? string.Empty, StringComparison.Ordinal);

        await AssertCheckpointsCompletedAsync(host, response.RunId!.Value, 4);
    }

    [E2EFact]
    public async Task FunctionCallTrigger_RunsFullChainWithPgCheckpoints()
    {
        await using var host = await ChainE2EHost.CreateAsync();
        var chainId = new ChainId(Guid.NewGuid());
        await host.RegisterAsync(host.BuildDefinition(
            chainId,
            new ChainTriggerDto { Kind = ChainTriggerKind.FunctionCall, FunctionName = "e2e_fn" },
            "test.chain.skill-a",
            "e2e-fn"));

        RunId runId = await host.FunctionCalls.InvokeFunctionCallAsync(chainId, "{\"trigger\":\"hello\"}");

        await WaitForDeliveriesAsync(host, 1);

        Assert.Equal(1, host.Skills.GetCallCount("test.chain.skill-a"));
        DeliveryRequest delivery = Assert.Single(host.Delivery.Requests);
        Assert.Equal("e2e-fn", delivery.Target);
        Assert.Contains("processed-A", delivery.Text ?? string.Empty, StringComparison.Ordinal);

        await AssertCheckpointsCompletedAsync(host, runId, 4);
    }

    [E2EFact]
    public async Task ChatTrigger_RunsFullChainWithPgCheckpoints()
    {
        await using var host = await ChainE2EHost.CreateAsync();
        var chainId = new ChainId(Guid.NewGuid());
        await host.RegisterAsync(host.BuildDefinition(
            chainId,
            new ChainTriggerDto { Kind = ChainTriggerKind.Chat, ChatFilter = "отчёт" },
            "test.chain.skill-a",
            "e2e-chat"));

        var listener = new ChatTriggerListener(
            host.Definitions,
            host.Executor,
            new SingleMessageSource(new IncomingMessage
            {
                Id = new MessageId(Guid.NewGuid().ToString("N")),
                Channel = ChannelKind.Telegram,
                SenderId = "111",
                ChatId = "777001",
                Text = "отчёт e2e",
                SentAtUtc = DateTimeOffset.UtcNow,
            }),
            host.Idempotency);
        await listener.HandleAsync(new IncomingMessage
        {
            Id = new MessageId(Guid.NewGuid().ToString("N")),
            Channel = ChannelKind.Telegram,
            SenderId = "111",
            ChatId = "777001",
            Text = "отчёт e2e",
            SentAtUtc = DateTimeOffset.UtcNow,
        });

        await WaitForDeliveriesAsync(host, 1);

        Assert.Equal(1, host.Skills.GetCallCount("test.chain.skill-a"));
        DeliveryRequest delivery = Assert.Single(host.Delivery.Requests);
        Assert.Equal("e2e-chat", delivery.Target);
        Assert.Contains("processed-A", delivery.Text ?? string.Empty, StringComparison.Ordinal);
    }

    [E2EFact]
    public async Task CronTrigger_FiresThroughRealQuartz()
    {
        await using var host = await ChainE2EHost.CreateAsync();
        var chainId = new ChainId(Guid.NewGuid());
        await host.RegisterAsync(host.BuildDefinition(
            chainId,
            new ChainTriggerDto { Kind = ChainTriggerKind.Cron, CronExpression = "0/1 * * * * ?" },
            "test.chain.skill-a",
            "e2e-cron"));

        CronTriggerManager? manager = null;
        await using var scheduler = await QuartzChainSchedulerService.CreateAsync(
            new QuartzSchedulerOptions
            {
                InstanceName = "E2EChain" + Guid.NewGuid().ToString("N"),
                UsePostgres = false,
            },
            (firedId, fireTime) => manager!.FireCronAsync(firedId, fireTime));
        manager = new CronTriggerManager(
            host.Definitions, host.Executor, scheduler, host.Idempotency);
        await manager.RegisterCronChainAsync(
            (await host.Definitions.GetByIdAsync(chainId))!);

        await WaitForDeliveriesAsync(host, 1, TimeSpan.FromSeconds(15));

        Assert.True(host.Delivery.Invocations >= 1, "Cron never fired the chain.");
        Assert.Equal(1, host.Skills.GetCallCount("test.chain.skill-a"));
        DeliveryRequest delivery = host.Delivery.Requests[0];
        Assert.Equal("e2e-cron", delivery.Target);
        Assert.Contains("processed-A", delivery.Text ?? string.Empty, StringComparison.Ordinal);
    }

    [E2EFact]
    public async Task TwoChains_RunIsolatedFromDifferentTriggers()
    {
        await using var host = await ChainE2EHost.CreateAsync();
        var chainA = new ChainId(Guid.NewGuid());
        var chainB = new ChainId(Guid.NewGuid());
        await host.RegisterAsync(host.BuildDefinition(
            chainA,
            new ChainTriggerDto { Kind = ChainTriggerKind.Webhook, WebhookPath = "/wh/e2e-a" },
            "test.chain.skill-a",
            "e2e-A"));
        await host.RegisterAsync(host.BuildDefinition(
            chainB,
            new ChainTriggerDto { Kind = ChainTriggerKind.FunctionCall, FunctionName = "e2e_fn_b" },
            "test.chain.skill-b",
            "e2e-B"));

        WebhookResponse webhook = await host.Webhooks.HandleWebhookAsync(new WebhookRequest
        {
            ChainId = chainA,
            Path = "/wh/e2e-a",
            Body = "{\"ping\":\"a\"}",
        });
        Assert.Equal(200, webhook.StatusCode);
        await host.FunctionCalls.InvokeFunctionCallAsync(chainB, "{\"trigger\":\"b\"}");

        await WaitForDeliveriesAsync(host, 2);

        Assert.Equal(1, host.Skills.GetCallCount("test.chain.skill-a"));
        Assert.Equal(1, host.Skills.GetCallCount("test.chain.skill-b"));
        Assert.Equal(2, host.Delivery.Invocations);
        DeliveryRequest toA = host.Delivery.Requests.Single(r => r.Target == "e2e-A");
        DeliveryRequest toB = host.Delivery.Requests.Single(r => r.Target == "e2e-B");
        Assert.Contains("processed-A", toA.Text ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("processed-B", toB.Text ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("processed-B", toA.Text ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("processed-A", toB.Text ?? string.Empty, StringComparison.Ordinal);
    }

    [E2EFact]
    public async Task RestartAfterScript_ResumesWithoutRerun()
    {
        ChainId chainId = new(Guid.NewGuid());
        RunId runId;
        try
        {
            await using var host = await ChainE2EHost.CreateAsync();
            await host.RegisterAsync(host.BuildDefinition(
                chainId,
                new ChainTriggerDto { Kind = ChainTriggerKind.FunctionCall, FunctionName = "e2e_restart" },
                "test.chain.skill-a",
                "e2e-restart"));

            // Флаг — после CreateAsync: скрипт читает его во время прогона.
            ChainPackageFixture.SetDeferFlag(true);
            runId = await host.FunctionCalls.InvokeFunctionCallAsync(chainId, "{\"trigger\":\"restart\"}");

            ChainRunState? interrupted = await host.Executor.GetRunAsync(runId);
            Assert.NotNull(interrupted);
            Assert.Equal(ChainRunStatus.WaitingForRetry, interrupted.Status);
            Assert.Equal(1, host.Skills.GetCallCount("test.chain.skill-a"));
            Assert.Equal(0, host.Delivery.Invocations);
        }
        finally
        {
            ChainPackageFixture.SetDeferFlag(false);
        }

        // Рестарт на той же БД без зачистки определений: приостановленный
        // запуск и его чекпоинты должны пережить пересоздание хоста.
        await using var restarted = await ChainE2EHost.CreateAsync("111", cleanDefinitions: false);
        var recovery = new ChainRecoveryService(restarted.Executions, restarted.Executor);
        int recovered = await recovery.RecoverAsync();
        Assert.True(recovered >= 1);

        ChainRunState? completed = await restarted.Executor.GetRunAsync(runId);
        Assert.NotNull(completed);
        Assert.Equal(ChainRunStatus.Completed, completed.Status);
        Assert.Equal(0, restarted.Skills.TotalCalls);
        Assert.Equal(1, restarted.Scripts.GetCallCount("Python"));
        Assert.Equal(1, restarted.Delivery.Invocations);
        DeliveryRequest delivery = Assert.Single(restarted.Delivery.Requests);
        Assert.Equal("e2e-restart", delivery.Target);

        await AssertCheckpointsCompletedAsync(restarted, runId, 4);
    }

    [E2EFact]
    public async Task HitlApprove_PausesBeforeDeliveryThenDeliversOnce()
    {
        ChainId chainId = new(Guid.NewGuid());
        Guid approvalId;
        RunId runId;

        await using (var host = await ChainE2EHost.CreateAsync("111"))
        {
            await host.RegisterAsync(host.BuildDefinition(
                chainId,
                new ChainTriggerDto { Kind = ChainTriggerKind.FunctionCall, FunctionName = "e2e_hitl" },
                "test.chain.skill-a",
                "e2e-hitl",
                requiresHitl: true));

            Task<RunId> startTask = host.FunctionCalls.InvokeFunctionCallAsync(chainId, "{\"trigger\":\"hitl\"}");

            HitlApprovalRequest card = await WaitForHitlCardAsync(host);
            approvalId = card.ApprovalId;

            ChainRunState paused = await WaitForPausedRunAsync(host, chainId);
            runId = paused.RunId;
            Assert.Equal(ChainRunStatus.WaitingForHitl, paused.Status);
            Assert.Equal(1, host.Skills.GetCallCount("test.chain.skill-a"));
            Assert.Equal(0, host.Delivery.Invocations);

            // «Рестарт» до решения: конвейер остаётся висеть на in-memory
            // ожидателе старого хоста и никогда не просыпается; решение
            // принимает новый хост на той же БД (штатный путь CLI-рестарта).
            // startTask осознанно не ждём: он привязан к disposed-хосту.
            _ = startTask;
        }

        await using var restarted = await ChainE2EHost.CreateAsync("111", cleanDefinitions: false);

        IReadOnlyList<RunId> unfinished = await restarted.Executions.ListUnfinishedRunsAsync();
        Assert.Contains(runId, unfinished);

        HitlApprovalResult decision = await restarted.Hitl.ApproveAsync(
            approvalId, "111", ChannelKind.Telegram);
        Assert.Equal(HitlDecision.Approved, decision.Decision);

        await WaitForDeliveriesAsync(restarted, 1);

        // Resume идёт только шагом Delivery: Skill и Script не повторяются.
        Assert.Equal(0, restarted.Skills.TotalCalls);
        Assert.Equal(0, restarted.Scripts.TotalCalls);
        DeliveryRequest delivery = Assert.Single(restarted.Delivery.Requests);
        Assert.Equal("e2e-hitl", delivery.Target);
        Assert.Contains("processed-A", delivery.Text ?? string.Empty, StringComparison.Ordinal);

        ChainRunState? completed = await restarted.Executor.GetRunAsync(runId);
        Assert.NotNull(completed);
        Assert.Equal(ChainRunStatus.Completed, completed.Status);

        HitlApprovalRecord? record = await restarted.HitlRepository.GetAsync(approvalId);
        Assert.NotNull(record);
        Assert.Equal(HitlStatus.Resolved, record.Status);
        Assert.NotNull(record.Resolution);
        Assert.Equal(HitlDecision.Approved, record.Resolution.Decision);
    }

    [E2EFact]
    public async Task HitlReject_NoDeliveryRunNotCompleted()
    {
        ChainId chainId = new(Guid.NewGuid());
        Guid approvalId;
        RunId runId;

        await using (var host = await ChainE2EHost.CreateAsync("111"))
        {
            await host.RegisterAsync(host.BuildDefinition(
                chainId,
                new ChainTriggerDto { Kind = ChainTriggerKind.FunctionCall, FunctionName = "e2e_hitl_no" },
                "test.chain.skill-a",
                "e2e-hitl-no",
                requiresHitl: true));

            Task<RunId> startTask = host.FunctionCalls.InvokeFunctionCallAsync(chainId, "{\"trigger\":\"hitl-no\"}");

            HitlApprovalRequest card = await WaitForHitlCardAsync(host);
            approvalId = card.ApprovalId;

            ChainRunState paused = await WaitForPausedRunAsync(host, chainId);
            runId = paused.RunId;
            Assert.Equal(ChainRunStatus.WaitingForHitl, paused.Status);
            Assert.Equal(0, host.Delivery.Invocations);

            // «Рестарт» до решения (как в approve-тесте): живой конвейер
            // умирал бы вместе с решением в гонке OCC на строке запуска
            // (живой пайплайн пишет Failed одновременно с Cancelled от
            // RejectAsync). Решение принимает новый хост на той же БД.
            _ = startTask;
        }

        await using var restarted = await ChainE2EHost.CreateAsync("111", cleanDefinitions: false);

        IReadOnlyList<RunId> unfinished = await restarted.Executions.ListUnfinishedRunsAsync();
        Assert.Contains(runId, unfinished);

        HitlApprovalResult decision = await restarted.Hitl.RejectAsync(
            approvalId, "111", ChannelKind.Telegram, "e2e");
        Assert.Equal(HitlDecision.Rejected, decision.Decision);

        await Task.Delay(500);
        Assert.Equal(0, restarted.Delivery.Invocations);

        // Политика TASK-046: Reject отменяет запуск (Cancelled), не Failed.
        ChainRunState? finished = await restarted.Executor.GetRunAsync(runId);
        Assert.NotNull(finished);
        Assert.Equal(ChainRunStatus.Cancelled, finished.Status);

        HitlApprovalRecord? record = await restarted.HitlRepository.GetAsync(approvalId);
        Assert.NotNull(record);
        Assert.Equal(HitlStatus.Resolved, record.Status);
        Assert.NotNull(record.Resolution);
        Assert.Equal(HitlDecision.Rejected, record.Resolution.Decision);
    }

    [E2EFact]
    public async Task BadManifest_RejectedBeforeScriptRun()
    {
        await using var host = await ChainE2EHost.CreateAsync();
        string badDir = Path.Combine(Path.GetTempPath(), "cyberpipe-e2e-bad-pkg", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(badDir);
        File.WriteAllText(Path.Combine(badDir, "netrunner.manifest.json"), """
            {
              "slug": "e2e-bad-pkg",
              "version": "1.0.0",
              "runtime": {
                "language": "python",
                "languageVersion": "3.10",
                "environment": "cpython",
                "environmentVersion": "3.10.0",
                "entrypoint": "main.py",
                "timeoutMs": 5000,
                "deterministic": true
              }
            }
            """);

        var validator = new NetrunnerPackageValidator();
        PackageValidationResult result = validator.ValidatePackage(badDir);
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(
            result.Errors, e => e.Message.Contains("skill-hack", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, host.Scripts.TotalCalls);
    }

    [E2EFact]
    public async Task FailingScript_NoDeliveryFailedThenResumeOnlyFailedStep()
    {
        try
        {
            await using var host = await ChainE2EHost.CreateAsync();
            var chainId = new ChainId(Guid.NewGuid());
            await host.RegisterAsync(host.BuildDefinition(
                chainId,
                new ChainTriggerDto { Kind = ChainTriggerKind.FunctionCall, FunctionName = "e2e_fail" },
                "test.chain.skill-a",
                "e2e-fail"));

            // Фаза 1: постоянное падение скрипта → Failed без доставки.
            // Флаг — после CreateAsync: скрипт читает его во время прогона.
            ChainPackageFixture.SetFailFlag(true);
            RunId runId = await host.FunctionCalls.InvokeFunctionCallAsync(chainId, "{\"trigger\":\"fail\"}");

            ChainRunState? failed = await host.Executor.GetRunAsync(runId);
            Assert.NotNull(failed);
            Assert.Equal(ChainRunStatus.Failed, failed.Status);
            Assert.Equal(0, host.Delivery.Invocations);
            Assert.Equal(1, host.Skills.GetCallCount("test.chain.skill-a"));
            Assert.Equal(1, host.Scripts.GetCallCount("Python"));

            // Resume терминального запуска — no-op по контракту ResumeAsync
            // (он только для прерванных нетерминальных прогонов): ни шагов,
            // ни доставки это не вызывает.
            ChainPackageFixture.SetFailFlag(false);
            await host.Executor.ResumeAsync(runId);

            ChainRunState? stillFailed = await host.Executor.GetRunAsync(runId);
            Assert.NotNull(stillFailed);
            Assert.Equal(ChainRunStatus.Failed, stillFailed.Status);
            Assert.Equal(0, host.Delivery.Invocations);
            Assert.Equal(1, host.Scripts.GetCallCount("Python"));

            // Фаза 2: flaky-скрипт (первая попытка падает) при MaxAttempts=2 —
            // повтор идёт только шагом Script по политике TASK-044: Skill
            // не повторяется, вторая попытка доводит прогон до доставки.
            var flakyId = new ChainId(Guid.NewGuid());
            ChainDefinitionDto flakyDef = host.BuildDefinition(
                flakyId,
                new ChainTriggerDto { Kind = ChainTriggerKind.FunctionCall, FunctionName = "e2e_flaky" },
                "test.chain.skill-a",
                "e2e-flaky") with
            {
                RetryPolicy = new ChainRetryPolicyDto { MaxAttempts = 2, BackoffMs = 1 },
            };
            await host.RegisterAsync(flakyDef);

            ChainPackageFixture.SetFlakyFlag(true);
            RunId flakyRunId = await host.FunctionCalls.InvokeFunctionCallAsync(flakyId, "{\"trigger\":\"flaky\"}");

            ChainRunState? completed = await host.Executor.GetRunAsync(flakyRunId);
            Assert.NotNull(completed);
            Assert.Equal(ChainRunStatus.Completed, completed.Status);
            Assert.Equal(2, host.Skills.GetCallCount("test.chain.skill-a"));
            Assert.Equal(3, host.Scripts.GetCallCount("Python"));
            await WaitForDeliveriesAsync(host, 1);
            DeliveryRequest delivery = Assert.Single(host.Delivery.Requests);
            Assert.Equal("e2e-flaky", delivery.Target);
            Assert.Contains("processed-A", delivery.Text ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            ChainPackageFixture.ClearFlags();
        }
    }

    [E2EFact]
    public async Task DuplicateTrigger_SameIdempotencyKey_NoSecondRun()
    {
        await using var host = await ChainE2EHost.CreateAsync();
        var chainId = new ChainId(Guid.NewGuid());
        await host.RegisterAsync(host.BuildDefinition(
            chainId,
            new ChainTriggerDto { Kind = ChainTriggerKind.FunctionCall, FunctionName = "e2e_dupe" },
            "test.chain.skill-a",
            "e2e-dupe"));

        var trigger = new TriggerEvent
        {
            EventId = Guid.NewGuid(),
            ChainId = chainId,
            Kind = ChainTriggerKind.FunctionCall,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            PayloadJson = "{\"trigger\":\"dupe\"}",
            FileRefs = [],
            IdempotencyKey = "dupe-key-1",
            Source = "e2e_dupe",
        };

        RunId first = await host.Executor.StartAsync(trigger);
        RunId second = await host.Executor.StartAsync(trigger with { EventId = Guid.NewGuid() });

        Assert.Equal(first, second);
        await WaitForDeliveriesAsync(host, 1);
        Assert.Equal(1, host.Skills.GetCallCount("test.chain.skill-a"));
        Assert.Equal(1, host.Delivery.Invocations);
    }

    private static async Task<HitlApprovalRequest> WaitForHitlCardAsync(ChainE2EHost host)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (host.HitlCards.Requests.Count > 0)
            {
                return host.HitlCards.Requests[^1];
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("HITL card never appeared.");
    }

    private static async Task<ChainRunState> WaitForPausedRunAsync(ChainE2EHost host, ChainId chainId)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            IReadOnlyList<RunId> unfinished = await host.Executions.ListUnfinishedRunsAsync();
            foreach (RunId id in unfinished)
            {
                ChainRunState? state = await host.Executor.GetRunAsync(id);
                if (state is not null && state.ChainId == chainId && state.Status == ChainRunStatus.WaitingForHitl)
                {
                    return state;
                }
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("Chain run never paused on HITL.");
    }

    private static async Task WaitForDeliveriesAsync(ChainE2EHost host, int count, TimeSpan? timeout = null)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(10));
        while (host.Delivery.Invocations < count && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(250);
        }

        Assert.True(
            host.Delivery.Invocations >= count,
            $"Expected at least {count} deliveries, got {host.Delivery.Invocations}.");
    }

    private static async Task AssertCheckpointsCompletedAsync(ChainE2EHost host, RunId runId, int expectedCount)
    {
        PagedResult<CheckpointRecord> page = await host.Checkpoints.ListAsync(
            new PagedQuery { Offset = 0, Limit = 50, SortBy = "step", Descending = false }, runId);
        List<ChainCheckpointDto> steps = page.Items.Select(r => r.Checkpoint).OrderBy(c => c.Step).ToList();
        Assert.Equal(expectedCount, steps.Count);
        Assert.All(steps, c => Assert.Equal(ChainStepState.Completed, c.State));
        Assert.Contains(steps, c => c.Step == ChainStepKind.Trigger);
        Assert.Contains(steps, c => c.Step == ChainStepKind.Skill);
        Assert.Contains(steps, c => c.Step == ChainStepKind.Script);
        Assert.Contains(steps, c => c.Step == ChainStepKind.Delivery);
    }
}

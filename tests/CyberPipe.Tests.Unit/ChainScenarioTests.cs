namespace CyberPipe.Tests.Unit;

using CyberPipe.Chains;
using CyberPipe.Chains.Execution;
using CyberPipe.Chains.Hitl;
using CyberPipe.Chains.Packages;
using CyberPipe.Chains.Runners;
using CyberPipe.Chains.Scheduling;
using CyberPipe.Chains.Triggers;
using CyberPipe.Contracts;
using CyberPipe.Data;
using Quartz;
using Quartz.Impl;
using Xunit;

public sealed class ChainScenarioTests
{
    private sealed class CountingSkillInvoker : ISkillInvoker
    {
        public int Invocations { get; private set; }

        public Task<SkillInvocationResult> InvokeSkillAsync(SkillInvocationRequest request, CancellationToken cancellationToken = default)
        {
            Invocations++;
            return Task.FromResult(new SkillInvocationResult
            {
                IsSuccess = true,
                OutputJson = "{\"normalized\":\"hello\"}",
            });
        }
    }

    private sealed class ScriptRunnerStub : IScriptRunner
    {
        public string Language => "Python";

        public string Environment => "CPython";

        public int Invocations { get; private set; }

        public bool ShouldDefer { get; set; }

        public bool CanRun(NetrunnerManifestDto manifest) => true;

        public Task<ScriptRunResult> RunAsync(ScriptRunRequest request, CancellationToken cancellationToken = default)
        {
            Invocations++;
            return Task.FromResult(ShouldDefer
                ? ScriptRunResult.Failed(ScriptRunStatus.DeferRetry, "Stub deferred", retryAfterSeconds: 1)
                : ScriptRunResult.Success("{\"script\":\"processed\"}", [], 0, "ok", string.Empty, 5));
        }
    }

    private sealed class CapturingDeliveryPort : IDeliveryPort
    {
        public int Invocations { get; private set; }

        public List<DeliveryRequest> Requests { get; } = [];

        public Task<DeliveryResult> SendAsync(DeliveryRequest request, CancellationToken cancellationToken = default)
        {
            Invocations++;
            Requests.Add(request);
            return Task.FromResult(new DeliveryResult { IsSuccess = true });
        }
    }

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

    private sealed class Fixture : IAsyncDisposable
    {
        public ChainId ChainId { get; } = new(Guid.NewGuid());

        public ChainCatalogService Definitions { get; }

        public ChainExecutionStoreService Executions { get; }

        public NetrunnerPackageStoreService Packages { get; }

        public InMemoryIdempotencyStore Idempotency { get; } = new();

        public CountingSkillInvoker Skills { get; } = new();

        public ScriptRunnerStub Scripts { get; } = new();

        public CapturingDeliveryPort Delivery { get; } = new();

        public ScriptRunnerRegistry Runners { get; } = new(null);

        public ChainExecutor Executor { get; }

        private readonly TestDatabase _db;

        private Fixture(TestDatabase db)
        {
            _db = db;
            Definitions = db.Catalog;
            Executions = db.Executions;
            Packages = db.Packages;
            Runners.RegisterRunner(Scripts);
            Executor = new ChainExecutor(Executions, Definitions, Skills, Runners, Packages, Delivery);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var db = await TestDatabase.CreateAsync();
            try
            {
                return new Fixture(db);
            }
            catch
            {
                await db.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync() => await _db.DisposeAsync();

        public ChainDefinitionDto BuildDefinition(ChainTriggerDto trigger, bool requiresHitl = false)
        {
            const string slug = "e2e-pkg";
            var definition = new ChainDefinitionDto
            {
                Id = ChainId,
                Name = "E2E chain",
                IsEnabled = true,
                RequiresHitl = requiresHitl,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                RetryPolicy = new ChainRetryPolicyDto { MaxAttempts = 1, BackoffMs = 1 },
                Trigger = trigger,
                Skill = new ChainSkillStepDto
                {
                    SkillId = "test.skill",
                    InputMapping = new Dictionary<string, string> { ["text"] = "{{trigger}}" },
                },
                Script = new ChainScriptStepDto { PackageSlug = slug, PackageVersion = "1.0.0" },
                Delivery = new ChainDeliveryStepDto
                {
                    Channel = ChannelKind.Telegram,
                    Target = "777001",
                    MessageTemplate = "Done: {{script}}",
                },
            };
            return definition;
        }

        public async Task RegisterAsync(ChainDefinitionDto definition)
        {
            var validator = new ChainDefinitionValidator();
            ChainValidationResult validation = validator.Validate(definition);
            Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Message)));
            await Definitions.UpsertAsync(definition);
            await Packages.RegisterPackageAsync(
                new NetrunnerManifestDto
                {
                    Slug = definition.Script.PackageSlug,
                    Name = "stub",
                    Version = definition.Script.PackageVersion,
                    Description = "stub",
                    Runtime = new NetrunnerRuntimeDto
                    {
                        Language = "Python",
                        LanguageVersion = "3.11",
                        Environment = "CPython",
                        EnvironmentVersion = "3.11",
                        Entrypoint = "main.py",
                        TimeoutMs = 5000,
                        Deterministic = true,
                    },
                    Io = new NetrunnerIoDto
                    {
                        InputSchemaFile = "schema.input.json",
                        OutputSchemaFile = "schema.output.json",
                    },
                    Permissions = new NetrunnerPermissionsDto { FilesystemScope = "IsolatedRunWorkspaceOnly" },
                    Integrity = new NetrunnerIntegrityDto(),
                },
                Path.GetTempPath(),
                "stub");
        }
    }

    [Fact]
    public async Task WebhookTrigger_RunsAllStepsOnceAndDelivers()
    {
        await using var fixture = await Fixture.CreateAsync();
        ChainDefinitionDto definition = fixture.BuildDefinition(new ChainTriggerDto
        {
            Kind = ChainTriggerKind.Webhook,
            WebhookPath = "/webhook/e2e",
        });
        await fixture.RegisterAsync(definition);
        var handler = new WebhookTriggerHandler(fixture.Definitions, fixture.Executor, fixture.Idempotency);

        WebhookResponse response = await handler.HandleWebhookAsync(new WebhookRequest
        {
            ChainId = fixture.ChainId,
            Path = "/webhook/e2e",
            Body = "{\"trigger\":\"hello\"}",
        });

        Assert.NotNull(response);
        Assert.Equal(1, fixture.Skills.Invocations);
        Assert.Equal(1, fixture.Scripts.Invocations);
        Assert.Equal(1, fixture.Delivery.Invocations);
        Assert.Contains("processed", fixture.Delivery.Requests[0].Text, StringComparison.Ordinal);
        Assert.Equal("777001", fixture.Delivery.Requests[0].Target);

        Assert.True(response.RunId.HasValue);
        IReadOnlyList<ChainCheckpointDto> checkpoints =
            await fixture.Executions.GetCheckpointsAsync(response.RunId!.Value);
        Assert.Equal(4, checkpoints.Count);
        Assert.All(checkpoints, cp => Assert.Equal(ChainStepState.Completed, cp.State));
    }

    [Fact]
    public async Task FunctionCallTrigger_RunsAllStepsOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        ChainDefinitionDto definition = fixture.BuildDefinition(new ChainTriggerDto
        {
            Kind = ChainTriggerKind.FunctionCall,
            FunctionName = "e2e.function",
        });
        await fixture.RegisterAsync(definition);
        var invoker = new FunctionCallTriggerInvoker(fixture.Definitions, fixture.Executor);

        RunId runId = await invoker.InvokeFunctionCallAsync(fixture.ChainId, "{\"trigger\":\"hello\"}");

        ChainRunState? state = await fixture.Executor.GetRunAsync(runId);
        Assert.NotNull(state);
        Assert.Equal(ChainRunStatus.Completed, state.Status);
        Assert.Equal(1, fixture.Skills.Invocations);
        Assert.Equal(1, fixture.Delivery.Invocations);
    }

    [Fact]
    public async Task ChatTrigger_RunsChainFromMessage()
    {
        await using var fixture = await Fixture.CreateAsync();
        ChainDefinitionDto definition = fixture.BuildDefinition(new ChainTriggerDto
        {
            Kind = ChainTriggerKind.Chat,
            ChatFilter = "отчёт",
        });
        await fixture.RegisterAsync(definition);
        var listener = new ChatTriggerListener(
            fixture.Definitions,
            fixture.Executor,
            new SingleMessageSource(new IncomingMessage
            {
                Id = MessageId.New(),
                Channel = ChannelKind.Telegram,
                SenderId = "111",
                ChatId = "777001",
                Text = "отчёт за неделю",
                SentAtUtc = DateTimeOffset.UtcNow,
            }),
            fixture.Idempotency);

        await listener.HandleAsync(new IncomingMessage
        {
            Id = MessageId.New(),
            Channel = ChannelKind.Telegram,
            SenderId = "111",
            ChatId = "777001",
            Text = "отчёт за неделю",
            SentAtUtc = DateTimeOffset.UtcNow,
        });

        Assert.Equal(1, fixture.Skills.Invocations);
        Assert.Equal(1, fixture.Delivery.Invocations);
    }

    [Fact]
    public async Task CronTrigger_FiresThroughRealScheduler()
    {
        await using var fixture = await Fixture.CreateAsync();
        ChainDefinitionDto definition = fixture.BuildDefinition(new ChainTriggerDto
        {
            Kind = ChainTriggerKind.Cron,
            CronExpression = "0/1 * * * * ?",
        });
        await fixture.RegisterAsync(definition);

        CronTriggerManager? manager = null;
        await using var service = await QuartzChainSchedulerService.CreateAsync(
            new QuartzSchedulerOptions
            {
                InstanceName = "E2E" + Guid.NewGuid().ToString("N"),
                UseSqlite = false,
            },
            (chainId, fireTime) => manager!.FireCronAsync(chainId, fireTime));
        manager = new CronTriggerManager(fixture.Definitions, fixture.Executor, service, fixture.Idempotency);

        await manager.RegisterCronChainAsync(definition);

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (fixture.Delivery.Invocations == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(250);
        }

        Assert.True(fixture.Delivery.Invocations >= 1, "Cron never fired the chain.");
        Assert.Equal(ChainTriggerKind.Cron, definition.Trigger.Kind);
    }

    [Fact]
    public async Task RestartAfterInterruption_ResumesWithoutRerunningCompletedSteps()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Scripts.ShouldDefer = true;
        ChainDefinitionDto definition = fixture.BuildDefinition(new ChainTriggerDto
        {
            Kind = ChainTriggerKind.Webhook,
            WebhookPath = "/webhook/e2e",
        });
        await fixture.RegisterAsync(definition);

        RunId runId = await fixture.Executor.StartAsync(new TriggerEvent
        {
            EventId = Guid.NewGuid(),
            ChainId = fixture.ChainId,
            Kind = ChainTriggerKind.Webhook,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            PayloadJson = "{\"trigger\":\"hello\"}",
            IdempotencyKey = "restart-" + Guid.NewGuid().ToString("N"),
            Source = "/webhook/e2e",
        });

        ChainRunState? interrupted = await fixture.Executor.GetRunAsync(runId);
        Assert.NotNull(interrupted);
        Assert.Equal(ChainRunStatus.WaitingForRetry, interrupted.Status);
        Assert.Equal(1, fixture.Skills.Invocations);
        Assert.Equal(0, fixture.Delivery.Invocations);

        // "Restart": new executor + recovery over the same repositories.
        fixture.Scripts.ShouldDefer = false;
        var restarted = new ChainExecutor(
            fixture.Executions, fixture.Definitions, fixture.Skills, fixture.Runners,
            fixture.Packages, fixture.Delivery);
        var recovery = new ChainRecoveryService(fixture.Executions, restarted);

        int recovered = await recovery.RecoverAsync();

        Assert.Equal(1, recovered);
        ChainRunState? completed = await restarted.GetRunAsync(runId);
        Assert.NotNull(completed);
        Assert.Equal(ChainRunStatus.Completed, completed.Status);
        Assert.Equal(1, fixture.Skills.Invocations);
        Assert.Equal(2, fixture.Scripts.Invocations);
        Assert.Equal(1, fixture.Delivery.Invocations);
    }
}

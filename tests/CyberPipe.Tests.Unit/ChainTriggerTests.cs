using System.Collections.Concurrent;
using System.Text.Json;
using CyberPipe.Chains;
using CyberPipe.Chains.Triggers;
using CyberPipe.Contracts;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class ChainTriggerTests
{
    private sealed class FakeChainExecutor : IChainExecutor
    {
        public ConcurrentBag<TriggerEvent> Events { get; } = new();

        public Task<RunId> StartAsync(TriggerEvent triggerEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(triggerEvent);
            return Task.FromResult(RunId.New());
        }
    }

    private sealed class FakeCronTriggerRegistrar : ICronTriggerRegistrar
    {
        public ConcurrentDictionary<ChainId, string> RegisteredCrons { get; } = new();

        public Task RegisterCronAsync(ChainId chainId, string cronExpression, CancellationToken cancellationToken = default)
        {
            RegisteredCrons[chainId] = cronExpression;
            return Task.CompletedTask;
        }

        public Task UnregisterCronAsync(ChainId chainId, CancellationToken cancellationToken = default)
        {
            RegisteredCrons.TryRemove(chainId, out _);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInboundMessageSource : IInboundMessageSource
    {
        private readonly List<IMessageHandler<IncomingMessage>> _handlers = new();

        public IDisposable Subscribe(IMessageHandler<IncomingMessage> handler)
        {
            _handlers.Add(handler);
            return new Subscription(() => _handlers.Remove(handler));
        }

        public async Task EmitAsync(IncomingMessage message, CancellationToken cancellationToken = default)
        {
            foreach (IMessageHandler<IncomingMessage> handler in _handlers.ToArray())
            {
                await handler.HandleAsync(message, cancellationToken);
            }
        }

        private sealed class Subscription(Action onDispose) : IDisposable
        {
            public void Dispose() => onDispose();
        }
    }

    private sealed class FakeSecretStore : ISecretStore
    {
        public bool IsPlatformStore => false;
        private readonly Dictionary<string, SecretValue> _secrets = new(StringComparer.Ordinal);

        public void Set(string key, string plainText) =>
            _secrets[key] = SecretValue.FromPlainText(plainText);

        public Task<SecretValue?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_secrets.TryGetValue(key, out SecretValue? val) ? val : null);

        public Task SetAsync(string key, SecretValue value, CancellationToken cancellationToken = default)
        {
            _secrets[key] = value;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_secrets.Remove(key));
    }

    private static ChainDefinitionDto CreateChain(ChainTriggerKind kind, bool isEnabled = true, string? secretKey = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var parameters = new Dictionary<string, string>();
        if (secretKey is not null)
        {
            parameters["secretRef"] = secretKey;
            parameters["secretHeader"] = "X-Test-Secret";
        }

        ChainTriggerDto trigger = kind switch
        {
            ChainTriggerKind.Webhook => new ChainTriggerDto
            {
                Kind = ChainTriggerKind.Webhook,
                WebhookPath = "/api/v1/alert",
                Parameters = parameters,
            },
            ChainTriggerKind.Cron => new ChainTriggerDto
            {
                Kind = ChainTriggerKind.Cron,
                CronExpression = "0 0 12 * * ?",
                Parameters = parameters,
            },
            ChainTriggerKind.FunctionCall => new ChainTriggerDto
            {
                Kind = ChainTriggerKind.FunctionCall,
                FunctionName = "sync_inventory",
                Parameters = parameters,
            },
            ChainTriggerKind.Chat => new ChainTriggerDto
            {
                Kind = ChainTriggerKind.Chat,
                ChatFilter = "cmd:diagnose",
                Parameters = parameters,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        return new ChainDefinitionDto
        {
            Id = ChainId.New(),
            Name = $"{kind} Chain",
            IsEnabled = isEnabled,
            Trigger = trigger,
            Skill = new ChainSkillStepDto { SkillId = "skill-1" },
            Script = new ChainScriptStepDto { PackageSlug = "pkg-1", PackageVersion = "1.0.0" },
            Delivery = new ChainDeliveryStepDto { Channel = ChannelKind.Cli, Target = "user", MessageTemplate = "done" },
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    [Fact]
    public async Task AllFourTriggers_EmitTriggerEvent_WithCorrectProperties_AndCallExecutorOnce()
    {
        var executor = new FakeChainExecutor();
        var repo = new InMemoryChainDefinitionRepository();
        var idempotencyStore = new InMemoryIdempotencyStore();
        var cronRegistrar = new FakeCronTriggerRegistrar();
        var messageSource = new FakeInboundMessageSource();

        var webhookHandler = new WebhookTriggerHandler(repo, executor, idempotencyStore);
        var cronManager = new CronTriggerManager(repo, executor, cronRegistrar, idempotencyStore);
        var functionInvoker = new FunctionCallTriggerInvoker(repo, executor);
        using var chatListener = new ChatTriggerListener(repo, executor, messageSource, idempotencyStore);

        // 1. Webhook trigger
        ChainDefinitionDto webhookChain = CreateChain(ChainTriggerKind.Webhook);
        await repo.UpsertAsync(webhookChain);
        var webhookRequest = new WebhookRequest
        {
            ChainId = webhookChain.Id,
            Path = "/api/v1/alert",
            Body = "{\"alertId\": 101}",
        };
        WebhookResponse webhookResponse = await webhookHandler.HandleWebhookAsync(webhookRequest);
        Assert.Equal(200, webhookResponse.StatusCode);
        Assert.NotNull(webhookResponse.RunId);

        // 2. Cron trigger
        ChainDefinitionDto cronChain = CreateChain(ChainTriggerKind.Cron);
        await repo.UpsertAsync(cronChain);
        await cronManager.RegisterCronChainAsync(cronChain);
        DateTimeOffset scheduledAt = DateTimeOffset.UtcNow;
        RunId? cronRunId = await cronManager.FireCronAsync(cronChain.Id, scheduledAt);
        Assert.NotNull(cronRunId);

        // 3. FunctionCall trigger
        ChainDefinitionDto functionChain = CreateChain(ChainTriggerKind.FunctionCall);
        await repo.UpsertAsync(functionChain);
        RunId fnRunId = await functionInvoker.InvokeFunctionCallAsync(functionChain.Id, "{\"itemId\": 55}");
        Assert.NotEqual(Guid.Empty, fnRunId.Value);

        // 4. Chat trigger
        ChainDefinitionDto chatChain = CreateChain(ChainTriggerKind.Chat);
        await repo.UpsertAsync(chatChain);
        var incomingMsg = new IncomingMessage
        {
            Id = MessageId.New(),
            Channel = ChannelKind.Telegram,
            SenderId = "12345",
            ChatId = "chat-99",
            Text = "/diagnose system",
            SentAtUtc = DateTimeOffset.UtcNow,
        };
        await messageSource.EmitAsync(incomingMsg);

        // Assert 4 events received by executor
        Assert.Equal(4, executor.Events.Count);

        TriggerEvent webhookEvent = Assert.Single(executor.Events, e => e.Kind == ChainTriggerKind.Webhook);
        Assert.Equal(webhookChain.Id, webhookEvent.ChainId);
        Assert.Equal("{\"alertId\": 101}", webhookEvent.PayloadJson);

        TriggerEvent cronEvent = Assert.Single(executor.Events, e => e.Kind == ChainTriggerKind.Cron);
        Assert.Equal(cronChain.Id, cronEvent.ChainId);
        Assert.Contains("scheduledAtUtc", cronEvent.PayloadJson);

        TriggerEvent fnEvent = Assert.Single(executor.Events, e => e.Kind == ChainTriggerKind.FunctionCall);
        Assert.Equal(functionChain.Id, fnEvent.ChainId);
        Assert.Equal("{\"itemId\": 55}", fnEvent.PayloadJson);

        TriggerEvent chatEvent = Assert.Single(executor.Events, e => e.Kind == ChainTriggerKind.Chat);
        Assert.Equal(chatChain.Id, chatEvent.ChainId);
        Assert.Contains("/diagnose system", chatEvent.PayloadJson);
    }

    [Fact]
    public async Task Webhook_DuplicateWithSameIdempotencyKey_DoesNotCallExecutorSecondTime()
    {
        var executor = new FakeChainExecutor();
        var repo = new InMemoryChainDefinitionRepository();
        var idempotencyStore = new InMemoryIdempotencyStore();
        var webhookHandler = new WebhookTriggerHandler(repo, executor, idempotencyStore);

        ChainDefinitionDto chain = CreateChain(ChainTriggerKind.Webhook);
        await repo.UpsertAsync(chain);

        var headers = new Dictionary<string, string> { ["Idempotency-Key"] = "idem-key-1" };
        var req1 = new WebhookRequest
        {
            ChainId = chain.Id,
            Path = "/api/v1/alert",
            Body = "{\"val\": 1}",
            Headers = headers,
        };

        WebhookResponse res1 = await webhookHandler.HandleWebhookAsync(req1);
        Assert.Equal(200, res1.StatusCode);
        Assert.NotNull(res1.RunId);
        Assert.Single(executor.Events);

        // Same idempotency key -> duplicate ignored
        var req2 = new WebhookRequest
        {
            ChainId = chain.Id,
            Path = "/api/v1/alert",
            Body = "{\"val\": 1}",
            Headers = headers,
        };

        WebhookResponse res2 = await webhookHandler.HandleWebhookAsync(req2);
        Assert.Equal(200, res2.StatusCode);
        Assert.Null(res2.RunId);
        Assert.Equal("Duplicate webhook ignored.", res2.Message);
        Assert.Single(executor.Events);

        // Different body without explicit key -> different computed hash -> creates second run
        var req3 = new WebhookRequest
        {
            ChainId = chain.Id,
            Path = "/api/v1/alert",
            Body = "{\"val\": 2}",
        };

        WebhookResponse res3 = await webhookHandler.HandleWebhookAsync(req3);
        Assert.Equal(200, res3.StatusCode);
        Assert.NotNull(res3.RunId);
        Assert.Equal(2, executor.Events.Count);
    }

    [Fact]
    public async Task Webhook_SecretMismatch_Returns401_AndExecutorNotCalled()
    {
        var executor = new FakeChainExecutor();
        var repo = new InMemoryChainDefinitionRepository();
        var idempotencyStore = new InMemoryIdempotencyStore();
        var secretStore = new FakeSecretStore();
        secretStore.Set("webhook:secret_1", "correct-horse-battery-staple");

        var webhookHandler = new WebhookTriggerHandler(repo, executor, idempotencyStore, secretStore);

        ChainDefinitionDto chain = CreateChain(ChainTriggerKind.Webhook, secretKey: "webhook:secret_1");
        await repo.UpsertAsync(chain);

        // Invalid secret
        var reqWrongSecret = new WebhookRequest
        {
            ChainId = chain.Id,
            Path = "/api/v1/alert",
            Body = "{\"ok\": true}",
            Headers = new Dictionary<string, string> { ["X-Test-Secret"] = "wrong-secret" },
        };

        WebhookResponse resWrong = await webhookHandler.HandleWebhookAsync(reqWrongSecret);
        Assert.Equal(401, resWrong.StatusCode);
        Assert.Empty(executor.Events);

        // Correct secret -> 200
        var reqCorrectSecret = new WebhookRequest
        {
            ChainId = chain.Id,
            Path = "/api/v1/alert",
            Body = "{\"ok\": true}",
            Headers = new Dictionary<string, string> { ["X-Test-Secret"] = "correct-horse-battery-staple" },
        };

        WebhookResponse resCorrect = await webhookHandler.HandleWebhookAsync(reqCorrectSecret);
        Assert.Equal(200, resCorrect.StatusCode);
        Assert.Single(executor.Events);
    }

    [Fact]
    public async Task Webhook_DisabledChain_Returns409()
    {
        var executor = new FakeChainExecutor();
        var repo = new InMemoryChainDefinitionRepository();
        var idempotencyStore = new InMemoryIdempotencyStore();
        var webhookHandler = new WebhookTriggerHandler(repo, executor, idempotencyStore);

        ChainDefinitionDto disabledChain = CreateChain(ChainTriggerKind.Webhook, isEnabled: false);
        await repo.UpsertAsync(disabledChain);

        var req = new WebhookRequest
        {
            ChainId = disabledChain.Id,
            Path = "/api/v1/alert",
            Body = "{\"ok\": true}",
        };

        WebhookResponse res = await webhookHandler.HandleWebhookAsync(req);
        Assert.Equal(409, res.StatusCode);
        Assert.Empty(executor.Events);
    }

    [Fact]
    public async Task Webhook_BodyOverLimit_Returns413()
    {
        var executor = new FakeChainExecutor();
        var repo = new InMemoryChainDefinitionRepository();
        var idempotencyStore = new InMemoryIdempotencyStore();
        // Set maximum body limit to 10 bytes
        var webhookHandler = new WebhookTriggerHandler(repo, executor, idempotencyStore, maxBodySizeBytes: 10);

        ChainDefinitionDto chain = CreateChain(ChainTriggerKind.Webhook);
        await repo.UpsertAsync(chain);

        var req = new WebhookRequest
        {
            ChainId = chain.Id,
            Path = "/api/v1/alert",
            Body = "{\"largePayload\": \"this is much larger than 10 bytes\"}",
        };

        WebhookResponse res = await webhookHandler.HandleWebhookAsync(req);
        Assert.Equal(413, res.StatusCode);
        Assert.Empty(executor.Events);
    }

    [Fact]
    public async Task Webhook_NonJsonBody_Returns400()
    {
        var executor = new FakeChainExecutor();
        var repo = new InMemoryChainDefinitionRepository();
        var idempotencyStore = new InMemoryIdempotencyStore();
        var webhookHandler = new WebhookTriggerHandler(repo, executor, idempotencyStore);

        ChainDefinitionDto chain = CreateChain(ChainTriggerKind.Webhook);
        await repo.UpsertAsync(chain);

        var req = new WebhookRequest
        {
            ChainId = chain.Id,
            Path = "/api/v1/alert",
            Body = "THIS IS NOT JSON",
        };

        WebhookResponse res = await webhookHandler.HandleWebhookAsync(req);
        Assert.Equal(400, res.StatusCode);
        Assert.Empty(executor.Events);
    }

    [Fact]
    public async Task ChatMessage_NoFilterMatch_DoesNotStartExecution()
    {
        var executor = new FakeChainExecutor();
        var repo = new InMemoryChainDefinitionRepository();
        var idempotencyStore = new InMemoryIdempotencyStore();
        var messageSource = new FakeInboundMessageSource();

        using var chatListener = new ChatTriggerListener(repo, executor, messageSource, idempotencyStore);

        ChainDefinitionDto chain = CreateChain(ChainTriggerKind.Chat);
        await repo.UpsertAsync(chain);

        // Emit message that does not match "cmd:diagnose"
        var unrelatedMessage = new IncomingMessage
        {
            Id = MessageId.New(),
            Channel = ChannelKind.Telegram,
            SenderId = "100",
            Text = "Hello, what's the weather today?",
            SentAtUtc = DateTimeOffset.UtcNow,
        };

        await messageSource.EmitAsync(unrelatedMessage);
        Assert.Empty(executor.Events);
    }

    [Fact]
    public async Task FunctionCall_UnknownChain_ThrowsChainNotFoundException()
    {
        var executor = new FakeChainExecutor();
        var repo = new InMemoryChainDefinitionRepository();
        var functionInvoker = new FunctionCallTriggerInvoker(repo, executor);

        ChainId nonExistentChainId = ChainId.New();

        var ex = await Assert.ThrowsAsync<ChainNotFoundException>(() =>
            functionInvoker.InvokeFunctionCallAsync(nonExistentChainId, "{}"));

        Assert.Equal(nonExistentChainId, ex.ChainId);
        Assert.Empty(executor.Events);
    }

    [Fact]
    public async Task Unregister_StopsSubsequentRuns_AcrossAllTriggers()
    {
        var executor = new FakeChainExecutor();
        var repo = new InMemoryChainDefinitionRepository();
        var idempotencyStore = new InMemoryIdempotencyStore();
        var cronRegistrar = new FakeCronTriggerRegistrar();
        var messageSource = new FakeInboundMessageSource();

        var webhookHandler = new WebhookTriggerHandler(repo, executor, idempotencyStore);
        var cronManager = new CronTriggerManager(repo, executor, cronRegistrar, idempotencyStore);
        var functionInvoker = new FunctionCallTriggerInvoker(repo, executor);
        using var chatListener = new ChatTriggerListener(repo, executor, messageSource, idempotencyStore);

        var registry = new TriggerRegistry(repo, cronManager);

        // 1. Register Webhook and unregister
        ChainDefinitionDto webhookChain = CreateChain(ChainTriggerKind.Webhook);
        await registry.RegisterAsync(webhookChain);
        Assert.True(registry.IsRegistered(webhookChain.Id));

        await registry.UnregisterAsync(webhookChain.Id);
        Assert.False(registry.IsRegistered(webhookChain.Id));

        // Calling unregistered webhook returns 409 (disabled)
        WebhookResponse whRes = await webhookHandler.HandleWebhookAsync(new WebhookRequest
        {
            ChainId = webhookChain.Id,
            Path = "/api/v1/alert",
            Body = "{}",
        });
        Assert.Equal(409, whRes.StatusCode);

        // 2. Register Cron and unregister
        ChainDefinitionDto cronChain = CreateChain(ChainTriggerKind.Cron);
        await registry.RegisterAsync(cronChain);
        Assert.True(cronRegistrar.RegisteredCrons.ContainsKey(cronChain.Id));

        await registry.UnregisterAsync(cronChain.Id);
        Assert.False(cronRegistrar.RegisteredCrons.ContainsKey(cronChain.Id));

        RunId? cronRun = await cronManager.FireCronAsync(cronChain.Id, DateTimeOffset.UtcNow);
        Assert.Null(cronRun);

        // 3. Register FunctionCall and unregister
        ChainDefinitionDto fnChain = CreateChain(ChainTriggerKind.FunctionCall);
        await registry.RegisterAsync(fnChain);
        await registry.UnregisterAsync(fnChain.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            functionInvoker.InvokeFunctionCallAsync(fnChain.Id, "{}"));

        // 4. Register Chat and unregister
        ChainDefinitionDto chatChain = CreateChain(ChainTriggerKind.Chat);
        await registry.RegisterAsync(chatChain);
        await registry.UnregisterAsync(chatChain.Id);

        await messageSource.EmitAsync(new IncomingMessage
        {
            Id = MessageId.New(),
            Channel = ChannelKind.Telegram,
            SenderId = "1",
            Text = "/diagnose now",
            SentAtUtc = DateTimeOffset.UtcNow,
        });

        Assert.Empty(executor.Events);
    }
}

using CyberPipe.Contracts;
using CyberPipe.Router;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class Tier23LlmRouterTests
{
    [Fact]
    public async Task Tier23_ConfidentValidTier2_ReturnsMatched_WithoutCallingTier3()
    {
        var modelProvider = new FakeChatModelProvider();
        modelProvider.SetResponse(ChatModelClass.Light, """
            {
                "intent": "system.help",
                "confidence": 0.88,
                "parameters": { "topic": "network" },
                "reason": "Clear user question about network help"
            }
            """);

        var governor = new FakeBudgetGovernor(allowAll: true);
        var router = new Tier23LlmRouter(modelProvider, governor);

        var request = new RouteRequest
        {
            Text = "Help me check the network",
            Channel = ChannelKind.Telegram
        };

        RouteResult result = await router.TryRouteAsync(request);

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal(IntentTier.Tier2, result.Tier);
        Assert.Equal("system.help", result.Intent);
        Assert.Equal(0.88, result.Confidence);
        Assert.Equal("network", result.Parameters["topic"]);

        Assert.Equal(1, modelProvider.GetCallCount(ChatModelClass.Light));
        Assert.Equal(0, modelProvider.GetCallCount(ChatModelClass.Heavy));
    }

    [Fact]
    public async Task Tier23_LowConfidenceTier2_EscalatesToTier3_AndMatches()
    {
        var modelProvider = new FakeChatModelProvider();
        // Tier 2 low confidence (0.45 < 0.70 default threshold)
        modelProvider.SetResponse(ChatModelClass.Light, """
            {
                "intent": "system.help",
                "confidence": 0.45,
                "reason": "Not sure"
            }
            """);

        // Tier 3 confident (0.92 >= 0.60 default threshold)
        modelProvider.SetResponse(ChatModelClass.Heavy, """
            {
                "intent": "diagnostics.run",
                "confidence": 0.92,
                "parameters": { "target": "storage" },
                "reason": "Deep reasoning identified diagnostics intent"
            }
            """);

        var governor = new FakeBudgetGovernor(allowAll: true);
        var router = new Tier23LlmRouter(modelProvider, governor);

        var request = new RouteRequest
        {
            Text = "Something strange with my disk",
            Channel = ChannelKind.Telegram
        };

        RouteResult result = await router.TryRouteAsync(request);

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal(IntentTier.Tier3, result.Tier);
        Assert.Equal("diagnostics.run", result.Intent);
        Assert.Equal(0.92, result.Confidence);

        Assert.Equal(1, modelProvider.GetCallCount(ChatModelClass.Light));
        Assert.Equal(1, modelProvider.GetCallCount(ChatModelClass.Heavy));
    }

    [Fact]
    public async Task Tier23_InvalidJsonTier2_EscalatesToTier3()
    {
        var modelProvider = new FakeChatModelProvider();
        // Tier 2 returns non-JSON text
        modelProvider.SetResponse(ChatModelClass.Light, "I think this is related to checking disk space.");

        // Tier 3 returns valid JSON
        modelProvider.SetResponse(ChatModelClass.Heavy, """
            {
                "intent": "diagnostics.run",
                "confidence": 0.85
            }
            """);

        var governor = new FakeBudgetGovernor(allowAll: true);
        var router = new Tier23LlmRouter(modelProvider, governor);

        var result = await router.TryRouteAsync(new RouteRequest
        {
            Text = "check space",
            Channel = ChannelKind.Telegram
        });

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal(IntentTier.Tier3, result.Tier);
        Assert.Equal("diagnostics.run", result.Intent);

        Assert.Equal(1, modelProvider.GetCallCount(ChatModelClass.Light));
        Assert.Equal(1, modelProvider.GetCallCount(ChatModelClass.Heavy));
    }

    [Fact]
    public async Task Tier23_BudgetDeniedOnTier2_ReturnsBudgetDenied_WithoutCallingModel()
    {
        var modelProvider = new FakeChatModelProvider();
        var governor = new FakeBudgetGovernor(allowAll: false, denialReason: BudgetDenialReason.DailyBudgetExceeded);

        var router = new Tier23LlmRouter(modelProvider, governor);

        var result = await router.TryRouteAsync(new RouteRequest
        {
            Text = "Any request",
            Channel = ChannelKind.Telegram
        });

        Assert.Equal(RouteStatus.BudgetDenied, result.Status);
        Assert.Equal(IntentTier.Tier2, result.Tier);
        Assert.Contains("DailyBudgetExceeded", result.Reason);

        // Model provider must never be called when budget is denied!
        Assert.Equal(0, modelProvider.GetCallCount(ChatModelClass.Light));
        Assert.Equal(0, modelProvider.GetCallCount(ChatModelClass.Heavy));
    }

    [Fact]
    public async Task Tier23_BudgetDeniedOnTier3_ReturnsBudgetDenied()
    {
        var modelProvider = new FakeChatModelProvider();
        modelProvider.SetResponse(ChatModelClass.Light, """
            {
                "intent": "unknown",
                "confidence": 0.30
            }
            """);

        // Allow Tier 2, but deny Tier 3
        var governor = new SelectiveBudgetGovernor(allowTier2: true, allowTier3: false);

        var router = new Tier23LlmRouter(modelProvider, governor);

        var result = await router.TryRouteAsync(new RouteRequest
        {
            Text = "Complex obscure task",
            Channel = ChannelKind.Telegram
        });

        Assert.Equal(RouteStatus.BudgetDenied, result.Status);
        Assert.Equal(IntentTier.Tier3, result.Tier);

        Assert.Equal(1, modelProvider.GetCallCount(ChatModelClass.Light));
        Assert.Equal(0, modelProvider.GetCallCount(ChatModelClass.Heavy));
    }

    [Fact]
    public async Task Tier23_RecordsUsage_ToGovernor_ForBothTiers()
    {
        var modelProvider = new FakeChatModelProvider();
        modelProvider.SetResponse(ChatModelClass.Light, """{ "intent": "low", "confidence": 0.20 }""", inputTokens: 50, outputTokens: 20);
        modelProvider.SetResponse(ChatModelClass.Heavy, """{ "intent": "system.status", "confidence": 0.90 }""", inputTokens: 120, outputTokens: 40);

        var governor = new FakeBudgetGovernor(allowAll: true);
        var router = new Tier23LlmRouter(modelProvider, governor);

        await router.TryRouteAsync(new RouteRequest { Text = "status check", Channel = ChannelKind.Telegram });

        Assert.Equal(2, governor.RecordedUsages.Count);
        Assert.Equal(50, governor.RecordedUsages[0].InputTokens);
        Assert.Equal(20, governor.RecordedUsages[0].OutputTokens);
        Assert.Equal(120, governor.RecordedUsages[1].InputTokens);
        Assert.Equal(40, governor.RecordedUsages[1].OutputTokens);
    }

    [Fact]
    public async Task Tier23_ModelProviderFailure_RetriesTransientErrors()
    {
        var modelProvider = new FlakyChatModelProvider(failTimes: 1, transientException: new TimeoutException("Temporary network timeout"));
        modelProvider.SetResponse(ChatModelClass.Light, """{ "intent": "system.help", "confidence": 0.90 }""");

        var governor = new FakeBudgetGovernor(allowAll: true);
        var options = new CyberPipeOptions
        {
            Router = new RouterOptions { LlmMaxRetries = 2 }
        };

        var router = new Tier23LlmRouter(modelProvider, governor, options);

        var result = await router.TryRouteAsync(new RouteRequest { Text = "Help please", Channel = ChannelKind.Telegram });

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal("system.help", result.Intent);
        Assert.Equal(2, modelProvider.TotalAttempts);
    }

    [Fact]
    public async Task Tier23_ModelProviderExhaustedFailure_ReturnsFailed()
    {
        var modelProvider = new FlakyChatModelProvider(failTimes: 5, transientException: new HttpRequestException("Service Unavailable"));
        var governor = new FakeBudgetGovernor(allowAll: true);
        var options = new CyberPipeOptions
        {
            Router = new RouterOptions { LlmMaxRetries = 1 }
        };

        var router = new Tier23LlmRouter(modelProvider, governor, options);

        var result = await router.TryRouteAsync(new RouteRequest { Text = "Test", Channel = ChannelKind.Telegram });

        Assert.Equal(RouteStatus.Failed, result.Status);
        Assert.Contains("Service Unavailable", result.Reason);
    }

    [Fact]
    public async Task Tier23_ParsesMarkdownFencedJson()
    {
        var modelProvider = new FakeChatModelProvider();
        modelProvider.SetResponse(ChatModelClass.Light, """
            ```json
            {
                "intent": "system.status",
                "confidence": 0.91,
                "reason": "User asks for system status"
            }
            ```
            """);

        var governor = new FakeBudgetGovernor(allowAll: true);
        var router = new Tier23LlmRouter(modelProvider, governor);

        var result = await router.TryRouteAsync(new RouteRequest { Text = "status", Channel = ChannelKind.Telegram });

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal("system.status", result.Intent);
    }

    // --- Test Doubles ---

    private sealed class FakeChatModelProvider : IChatModelProvider
    {
        private readonly Dictionary<ChatModelClass, string> _responses = new();
        private readonly Dictionary<ChatModelClass, (int Input, int Output)> _tokens = new();
        private readonly Dictionary<ChatModelClass, int> _callCounts = new();

        public void SetResponse(ChatModelClass modelClass, string text, int inputTokens = 100, int outputTokens = 50)
        {
            _responses[modelClass] = text;
            _tokens[modelClass] = (inputTokens, outputTokens);
        }

        public int GetCallCount(ChatModelClass modelClass) =>
            _callCounts.TryGetValue(modelClass, out int c) ? c : 0;

        public Task<ChatModelResponse> CompleteAsync(ChatModelRequest request, CancellationToken cancellationToken = default)
        {
            _callCounts[request.ModelClass] = GetCallCount(request.ModelClass) + 1;

            if (_responses.TryGetValue(request.ModelClass, out string? text))
            {
                var (inTok, outTok) = _tokens[request.ModelClass];
                return Task.FromResult(new ChatModelResponse
                {
                    Text = text,
                    InputTokens = inTok,
                    OutputTokens = outTok,
                    ModelId = request.ModelClass == ChatModelClass.Light ? "test-light" : "test-heavy"
                });
            }

            return Task.FromResult(new ChatModelResponse
            {
                Text = "{}",
                InputTokens = 10,
                OutputTokens = 10
            });
        }
    }

    private sealed class FlakyChatModelProvider(int failTimes, Exception transientException) : IChatModelProvider
    {
        private string _successResponse = "{}";
        public int TotalAttempts { get; private set; }

        public void SetResponse(ChatModelClass modelClass, string text) => _successResponse = text;

        public Task<ChatModelResponse> CompleteAsync(ChatModelRequest request, CancellationToken cancellationToken = default)
        {
            TotalAttempts++;
            if (TotalAttempts <= failTimes)
            {
                throw transientException;
            }

            return Task.FromResult(new ChatModelResponse
            {
                Text = _successResponse,
                InputTokens = 50,
                OutputTokens = 25
            });
        }
    }

    private sealed class FakeBudgetGovernor(bool allowAll, BudgetDenialReason denialReason = BudgetDenialReason.DailyBudgetExceeded) : ITokenBudgetGovernor
    {
        public List<TokenUsageDto> RecordedUsages { get; } = [];

        public Task<BudgetDecision> TryAuthorizeAsync(BudgetRequest request, CancellationToken cancellationToken = default)
        {
            if (allowAll)
            {
                return Task.FromResult(new BudgetDecision { IsAllowed = true });
            }

            return Task.FromResult(new BudgetDecision
            {
                IsAllowed = false,
                DenialReason = denialReason,
                Message = $"Denied due to {denialReason}"
            });
        }

        public Task RecordUsageAsync(TokenUsageDto usage, CancellationToken cancellationToken = default)
        {
            RecordedUsages.Add(usage);
            return Task.CompletedTask;
        }

        public Task<BudgetStatusDto> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BudgetStatusDto
            {
                BudgetDate = DateOnly.FromDateTime(DateTime.UtcNow),
                SpentTodayUsd = 0m,
                RemainingTodayUsd = 1.0m,
                TokensUsedToday = 0,
                CloudTurnsUsed = 0,
                LocalTurnsUsed = 0,
                Limits = new BudgetLimitsDto { DailyCapUsd = 1.0m, SoftTokenLimit = 4000, HardTokenLimit = 12000, MaxCloudTurns = 3, MaxLocalTurns = 5 }
            });
    }

    private sealed class SelectiveBudgetGovernor(bool allowTier2, bool allowTier3) : ITokenBudgetGovernor
    {
        public Task<BudgetDecision> TryAuthorizeAsync(BudgetRequest request, CancellationToken cancellationToken = default)
        {
            bool allowed = request.ModelId.Contains("tier2", StringComparison.OrdinalIgnoreCase) ? allowTier2 : allowTier3;
            return Task.FromResult(new BudgetDecision
            {
                IsAllowed = allowed,
                DenialReason = allowed ? null : BudgetDenialReason.TurnLimitExceeded,
                Message = allowed ? null : "Turn limit exceeded"
            });
        }

        public Task RecordUsageAsync(TokenUsageDto usage, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<BudgetStatusDto> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BudgetStatusDto
            {
                BudgetDate = DateOnly.FromDateTime(DateTime.UtcNow),
                SpentTodayUsd = 0m,
                RemainingTodayUsd = 1.0m,
                TokensUsedToday = 0,
                CloudTurnsUsed = 0,
                LocalTurnsUsed = 0,
                Limits = new BudgetLimitsDto { DailyCapUsd = 1.0m, SoftTokenLimit = 4000, HardTokenLimit = 12000, MaxCloudTurns = 3, MaxLocalTurns = 5 }
            });
    }
}

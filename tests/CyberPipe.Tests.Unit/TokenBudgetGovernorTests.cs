using CyberPipe.Contracts;
using CyberPipe.Router;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class TokenBudgetGovernorTests
{
    private readonly InMemoryBudgetStore _store;
    private readonly CatalogModelPriceProvider _priceProvider;
    private readonly TestTimeProvider _timeProvider;

    public TokenBudgetGovernorTests()
    {
        _store = new InMemoryBudgetStore();
        _priceProvider = new CatalogModelPriceProvider();
        _priceProvider.SetPrice("gpt-4o", inputPricePer1mUsd: 5.0m, outputPricePer1mUsd: 15.0m);
        _priceProvider.SetPrice("claude-3-5-sonnet", inputPricePer1mUsd: 3.0m, outputPricePer1mUsd: 15.0m);
        _timeProvider = new TestTimeProvider();
    }

    [Fact]
    public async Task TokenBudget_WithinBudget_IsAllowed()
    {
        var options = new CyberPipeOptions
        {
            Budget = new BudgetOptions { DailyCapUsd = 0.50m }
        };

        var governor = new TokenBudgetGovernor(_store, options, _priceProvider, _timeProvider);

        var request = new BudgetRequest
        {
            ModelId = "gpt-4o",
            HostingType = ModelHostingType.RemoteCloud,
            EstimatedInputTokens = 1000,
            EstimatedOutputTokens = 500,
            RequestId = "req-1"
        };

        BudgetDecision decision = await governor.TryAuthorizeAsync(request);

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.DenialReason);
        Assert.NotNull(decision.Status);
        Assert.True(decision.Status.RemainingTodayUsd < 0.50m);
    }

    [Fact]
    public async Task TokenBudget_ExceedingDailyCap_IsDeniedWithDailyBudgetExceeded()
    {
        var options = new CyberPipeOptions
        {
            Budget = new BudgetOptions { DailyCapUsd = 0.02m }
        };

        var governor = new TokenBudgetGovernor(_store, options, _priceProvider, _timeProvider);

        // Call cost: (2,000 / 1M * 5) + (1,000 / 1M * 15) = 0.010 + 0.015 = $0.025 > $0.02 (3,000 tokens <= 4,000 soft ceiling)
        var request = new BudgetRequest
        {
            ModelId = "gpt-4o",
            HostingType = ModelHostingType.RemoteCloud,
            EstimatedInputTokens = 2_000,
            EstimatedOutputTokens = 1_000,
            RequestId = "req-heavy"
        };

        BudgetDecision decision = await governor.TryAuthorizeAsync(request);

        Assert.False(decision.IsAllowed);
        Assert.Equal(BudgetDenialReason.DailyBudgetExceeded, decision.DenialReason);
        Assert.Contains("Daily budget cap", decision.Message);
    }

    [Fact]
    public async Task TokenBudget_TurnLimits_FourthCloudTurnIsDenied()
    {
        var options = new CyberPipeOptions
        {
            Budget = new BudgetOptions
            {
                DailyCapUsd = 10.0m,
                MaxCloudTurns = 3,
                MaxLocalTurns = 5
            }
        };

        var governor = new TokenBudgetGovernor(_store, options, _priceProvider, _timeProvider);
        string requestId = "multi-turn-req";

        var cloudRequest = new BudgetRequest
        {
            ModelId = "gpt-4o",
            HostingType = ModelHostingType.RemoteCloud,
            EstimatedInputTokens = 100,
            EstimatedOutputTokens = 100,
            RequestId = requestId
        };

        // Turn 1: OK
        var d1 = await governor.TryAuthorizeAsync(cloudRequest);
        Assert.True(d1.IsAllowed);

        // Turn 2: OK
        var d2 = await governor.TryAuthorizeAsync(cloudRequest);
        Assert.True(d2.IsAllowed);

        // Turn 3: OK
        var d3 = await governor.TryAuthorizeAsync(cloudRequest);
        Assert.True(d3.IsAllowed);

        // Turn 4: Denied by TurnLimitExceeded
        var d4 = await governor.TryAuthorizeAsync(cloudRequest);
        Assert.False(d4.IsAllowed);
        Assert.Equal(BudgetDenialReason.TurnLimitExceeded, d4.DenialReason);
        Assert.Contains("Cloud turn limit (3) exceeded", d4.Message);

        // Local turn on same request is independent and still allowed!
        var localRequest = new BudgetRequest
        {
            ModelId = "llama3:8b",
            HostingType = ModelHostingType.Local,
            EstimatedInputTokens = 500,
            EstimatedOutputTokens = 200,
            RequestId = requestId
        };

        var localDecision = await governor.TryAuthorizeAsync(localRequest);
        Assert.True(localDecision.IsAllowed);
    }

    [Fact]
    public async Task TokenBudget_TokenLimits_ExceededSoftOrHardTokens_IsDenied()
    {
        var options = new CyberPipeOptions
        {
            Budget = new BudgetOptions
            {
                DailyCapUsd = 10.0m,
                SoftTokenLimit = 4000,
                HardTokenLimit = 12000
            }
        };

        var governor = new TokenBudgetGovernor(_store, options, _priceProvider, _timeProvider);

        // Cloud request exceeding soft limit (4000)
        var cloudReq = new BudgetRequest
        {
            ModelId = "gpt-4o",
            HostingType = ModelHostingType.RemoteCloud,
            EstimatedInputTokens = 3500,
            EstimatedOutputTokens = 1000, // total 4500 > 4000
            RequestId = "too-many-cloud-tokens"
        };

        var dCloud = await governor.TryAuthorizeAsync(cloudReq);
        Assert.False(dCloud.IsAllowed);
        Assert.Equal(BudgetDenialReason.TokenLimitExceeded, dCloud.DenialReason);

        // Local request exceeding hard limit (12000)
        var localReq = new BudgetRequest
        {
            ModelId = "deepseek-coder",
            HostingType = ModelHostingType.Local,
            EstimatedInputTokens = 10000,
            EstimatedOutputTokens = 3000, // total 13000 > 12000
            RequestId = "too-many-local-tokens"
        };

        var dLocal = await governor.TryAuthorizeAsync(localReq);
        Assert.False(dLocal.IsAllowed);
        Assert.Equal(BudgetDenialReason.TokenLimitExceeded, dLocal.DenialReason);
    }

    [Fact]
    public async Task TokenBudget_UnknownModelPrice_IsDeniedWithUnknownModelPrice()
    {
        var options = new CyberPipeOptions
        {
            Budget = new BudgetOptions { DailyCapUsd = 10.0m }
        };

        var governor = new TokenBudgetGovernor(_store, options, _priceProvider, _timeProvider);

        var request = new BudgetRequest
        {
            ModelId = "unknown-cloud-llm",
            HostingType = ModelHostingType.RemoteCloud,
            EstimatedInputTokens = 100,
            EstimatedOutputTokens = 100,
            RequestId = "req-unknown"
        };

        BudgetDecision decision = await governor.TryAuthorizeAsync(request);

        Assert.False(decision.IsAllowed);
        Assert.Equal(BudgetDenialReason.UnknownModelPrice, decision.DenialReason);
    }

    [Fact]
    public async Task TokenBudget_LocalModel_ZeroFinancialCost_AllowedEvenIfDailyCapReached()
    {
        var options = new CyberPipeOptions
        {
            Budget = new BudgetOptions { DailyCapUsd = 0.0m } // $0.00 daily budget
        };

        var governor = new TokenBudgetGovernor(_store, options, _priceProvider, _timeProvider);

        var request = new BudgetRequest
        {
            ModelId = "llama3:8b",
            HostingType = ModelHostingType.Local,
            EstimatedInputTokens = 500,
            EstimatedOutputTokens = 500,
            RequestId = "local-free"
        };

        BudgetDecision decision = await governor.TryAuthorizeAsync(request);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public async Task TokenBudget_DayRollover_ResetsDailyCountersWithoutRestart()
    {
        var options = new CyberPipeOptions
        {
            Budget = new BudgetOptions { DailyCapUsd = 0.10m }
        };

        var governor = new TokenBudgetGovernor(_store, options, _priceProvider, _timeProvider);

        // Day 1: Spend $0.08
        await governor.RecordUsageAsync(new TokenUsageDto
        {
            ModelId = "gpt-4o",
            HostingType = ModelHostingType.RemoteCloud,
            InputTokens = 5000,
            OutputTokens = 2000,
            CostUsd = 0.08m,
            OccurredAtUtc = _timeProvider.GetUtcNow(),
            RequestId = "day1-req"
        });

        // Requesting another $0.025 on Day 1 will exceed $0.10 cap ($0.08 + $0.025 = $0.105 > $0.10)
        var heavyReq = new BudgetRequest
        {
            ModelId = "gpt-4o",
            HostingType = ModelHostingType.RemoteCloud,
            EstimatedInputTokens = 2000,
            EstimatedOutputTokens = 1000,
            RequestId = "day1-heavy"
        };

        var d1 = await governor.TryAuthorizeAsync(heavyReq);
        Assert.False(d1.IsAllowed);
        Assert.Equal(BudgetDenialReason.DailyBudgetExceeded, d1.DenialReason);

        // Roll over to Day 2 (+25 hours)
        _timeProvider.Advance(TimeSpan.FromHours(25));

        // On Day 2, counters reset to 0, same request is now allowed!
        var d2 = await governor.TryAuthorizeAsync(heavyReq);
        Assert.True(d2.IsAllowed);
        Assert.Equal(0.10m, d2.Status!.Limits.DailyCapUsd);
    }

    [Fact]
    public async Task TokenBudget_ConcurrentAuthorizations_NeverExceedDailyBudget()
    {
        decimal cap = 0.50m;
        var options = new CyberPipeOptions
        {
            Budget = new BudgetOptions { DailyCapUsd = cap }
        };

        var governor = new TokenBudgetGovernor(_store, options, _priceProvider, _timeProvider);

        // Model cost: (1000/1M * 5) + (500/1M * 15) = $0.0125 per call
        // 40 calls would equal $0.50. Run 100 concurrent tasks!
        int numTasks = 100;
        var tasks = new Task<BudgetDecision>[numTasks];

        for (int i = 0; i < numTasks; i++)
        {
            int index = i;
            tasks[i] = Task.Run(() => governor.TryAuthorizeAsync(new BudgetRequest
            {
                ModelId = "gpt-4o",
                HostingType = ModelHostingType.RemoteCloud,
                EstimatedInputTokens = 1000,
                EstimatedOutputTokens = 500,
                RequestId = $"concurrent-{index}"
            }));
        }

        BudgetDecision[] decisions = await Task.WhenAll(tasks);

        int allowedCount = decisions.Count(d => d.IsAllowed);
        int deniedCount = decisions.Count(d => !d.IsAllowed);

        // Exactly 40 calls should be allowed ($0.0125 * 40 = $0.50), remaining 60 denied
        Assert.Equal(40, allowedCount);
        Assert.Equal(60, deniedCount);

        BudgetStatusDto status = await governor.GetStatusAsync();
        Assert.Equal(0.0m, status.RemainingTodayUsd);
    }

    [Fact]
    public async Task TokenBudget_RecordUsage_AdjustsReservationAndReflectsInSnapshot()
    {
        var options = new CyberPipeOptions
        {
            Budget = new BudgetOptions
            {
                DailyCapUsd = 1.00m,
                SoftTokenLimit = 25_000
            }
        };

        var governor = new TokenBudgetGovernor(_store, options, _priceProvider, _timeProvider);
        string requestId = "reservation-test";

        // Authorize with estimated cost: (10,000 / 1M * 5) + (10,000 / 1M * 15) = $0.20
        var auth = await governor.TryAuthorizeAsync(new BudgetRequest
        {
            ModelId = "gpt-4o",
            HostingType = ModelHostingType.RemoteCloud,
            EstimatedInputTokens = 10_000,
            EstimatedOutputTokens = 10_000,
            RequestId = requestId
        });

        Assert.True(auth.IsAllowed);

        var statusAfterReserve = await governor.GetStatusAsync();
        Assert.Equal(0.80m, statusAfterReserve.RemainingTodayUsd);

        // Actual usage was less than estimated: only $0.05
        await governor.RecordUsageAsync(new TokenUsageDto
        {
            ModelId = "gpt-4o",
            HostingType = ModelHostingType.RemoteCloud,
            InputTokens = 4000,
            OutputTokens = 2000,
            CostUsd = 0.05m,
            OccurredAtUtc = _timeProvider.GetUtcNow(),
            RequestId = requestId
        });

        // Remaining should now be $1.00 - $0.05 = $0.95
        var statusAfterUsage = await governor.GetStatusAsync();
        Assert.Equal(0.05m, statusAfterUsage.SpentTodayUsd);
        Assert.Equal(0.95m, statusAfterUsage.RemainingTodayUsd);
        Assert.Equal(6000, statusAfterUsage.TokensUsedToday);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        private long _timestamp = 0;

        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan delta)
        {
            _utcNow = _utcNow.Add(delta);
            _timestamp += (long)(delta.TotalSeconds * TimestampFrequency);
        }
    }
}

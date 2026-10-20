using CyberPipe.Contracts;
using CyberPipe.Router;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class CompositeIntentRouterTests
{
    private sealed class EmptyRuleProvider : IRegexRuleProvider
    {
        public IReadOnlyList<RegexRuleDefinition> GetRules() => [];
    }

    private sealed class FixedRuleProvider : IRegexRuleProvider
    {
        public IReadOnlyList<RegexRuleDefinition> GetRules() =>
            [new RegexRuleDefinition { Name = "help", Pattern = "^help$", Intent = "system.help", Priority = 1 }];
    }

    private sealed class FixedEmbeddingProvider : IEmbeddingProvider
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(new float[] { 1f, 0f, 0f });
    }

    private sealed class EmptyStore : IEmbeddingStore
    {
        public Task UpsertAsync(EmbeddingRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpsertManyAsync(IReadOnlyList<EmbeddingRecord> records, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<EmbeddingMatch>> SearchAsync(EmbeddingQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmbeddingMatch>>([]);
        public Task<bool> DeleteAsync(string ns, string sourceRef, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<int> DeleteByOwnerScopeAsync(string ns, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<long> CountAsync(string ns, CancellationToken cancellationToken = default) => Task.FromResult(0L);
        public Task<int> DeleteOldestAsync(string ns, int keepCount, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeChat : IChatModelProvider
    {
        public int Calls { get; private set; }
        public Task<ChatModelResponse> CompleteAsync(ChatModelRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ChatModelResponse
            {
                Text = """{"intent":"llm.intent","confidence":0.95,"parameters":{},"reason":"fake"}""",
                ModelId = "fake",
                InputTokens = 1,
                OutputTokens = 1
            });
        }
    }

    private sealed class AllowGovernor : ITokenBudgetGovernor
    {
        public Task<BudgetDecision> TryAuthorizeAsync(BudgetRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BudgetDecision { IsAllowed = true });
        public Task RecordUsageAsync(TokenUsageDto usage, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<BudgetStatusDto> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BudgetStatusDto { Limits = new BudgetLimitsDto(), BudgetDate = DateOnly.FromDateTime(DateTime.UtcNow) });
    }

    private static CompositeIntentRouter Create(IRegexRuleProvider rules, FakeChat chat) =>
        new(
            new Tier0RegexRouter(rules),
            new Tier1SemanticRouter(new FixedEmbeddingProvider(), new EmptyStore()),
            new Tier23LlmRouter(chat, new AllowGovernor()));

    [Fact]
    public async Task Composite_Tier0Match_ShortCircuitsLlm()
    {
        var chat = new FakeChat();
        var router = Create(new FixedRuleProvider(), chat);

        RouteResult result = await router.RouteAsync(new RouteRequest { Text = "help", Channel = ChannelKind.Cli });

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal("system.help", result.Intent);
        Assert.Equal(0, chat.Calls);
    }

    [Fact]
    public async Task Composite_NoRuleMatch_FallsThroughToLlm()
    {
        var chat = new FakeChat();
        var router = Create(new EmptyRuleProvider(), chat);

        RouteResult result = await router.RouteAsync(new RouteRequest { Text = "something unscripted", Channel = ChannelKind.Cli });

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal("llm.intent", result.Intent);
        Assert.True(chat.Calls >= 1);
    }
}

using CyberPipe.Contracts;
using CyberPipe.Data;
using CyberPipe.Router;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class CompositeIntentRouterTests : IAsyncLifetime
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

    private sealed class NoExamplesProvider : ISemanticExampleProvider
    {
        public IReadOnlyList<SemanticExampleDefinition> GetExamples() => [];
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

    private sealed class FakeSkillInvoker : ISkillInvoker
    {
        public List<SkillInvocationRequest> Invocations { get; } = [];

        public Task<SkillInvocationResult> InvokeSkillAsync(SkillInvocationRequest request, CancellationToken cancellationToken = default)
        {
            Invocations.Add(request);
            if (request.SkillId == "volume.get")
            {
                return Task.FromResult(new SkillInvocationResult
                {
                    IsSuccess = true,
                    OutputJson = """{"volume":65,"isMuted":false}""",
                    Message = "Host volume is 65%, muted: False."
                });
            }
            if (request.SkillId == "volume.set")
            {
                return Task.FromResult(new SkillInvocationResult
                {
                    IsSuccess = true,
                    OutputJson = request.PayloadJson,
                    Message = "Volume set to target."
                });
            }

            return Task.FromResult(new SkillInvocationResult
            {
                IsSuccess = true,
                OutputJson = "{}",
                Message = "Executed " + request.SkillId
            });
        }
    }

    private TestDatabase? _db;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }

    private CompositeIntentRouter Create(IRegexRuleProvider rules, FakeChat chat, ISkillInvoker? invoker = null) =>
        new(
            new Tier0RegexRouter(rules),
            new Tier1SemanticRouter(
                new FixedEmbeddingProvider(),
                _db!.Embeddings,
                exampleProvider: new NoExamplesProvider()),
            new Tier23LlmRouter(chat, new AllowGovernor()),
            invoker);

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
    public async Task Composite_Tier0VolumeGet_WithSkillInvoker_ExecutesSkillAndPopulatesResponseText()
    {
        var chat = new FakeChat();
        var invoker = new FakeSkillInvoker();
        var router = Create(new ConfigurationRegexRuleProvider(), chat, invoker);

        RouteResult result = await router.RouteAsync(new RouteRequest { Text = "какая громкость", Channel = ChannelKind.Cli });

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal("volume.get", result.Intent);
        Assert.Equal("Host volume is 65%, muted: False.", result.ResponseText);
        Assert.Single(invoker.Invocations);
        Assert.Equal("volume.get", invoker.Invocations[0].SkillId);
    }

    [Fact]
    public async Task Composite_Tier0VolumeSet_TranslatesLevelAndExecutesSkill()
    {
        var chat = new FakeChat();
        var invoker = new FakeSkillInvoker();
        var router = Create(new ConfigurationRegexRuleProvider(), chat, invoker);

        RouteResult result = await router.RouteAsync(new RouteRequest { Text = "громкость 75", Channel = ChannelKind.Cli });

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal("media.volume", result.Intent);
        Assert.Equal("Volume set to target.", result.ResponseText);
        Assert.Single(invoker.Invocations);
        Assert.Equal("volume.set", invoker.Invocations[0].SkillId);
        Assert.Contains("75", invoker.Invocations[0].PayloadJson);
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

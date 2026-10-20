using CyberPipe.Contracts;
using CyberPipe.Router;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class Tier0RegexRouterTests
{
    private readonly Tier0RegexRouter _router;

    public Tier0RegexRouterTests()
    {
        var ruleProvider = new ConfigurationRegexRuleProvider();
        _router = new Tier0RegexRouter(ruleProvider);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task Tier0_EmptyOrWhitespaceInput_ReturnsEscalate(string input)
    {
        var request = CreateRequest(input);
        RouteResult result = await _router.TryRouteAsync(request);

        Assert.Equal(RouteStatus.Escalate, result.Status);
        Assert.Equal(IntentTier.Tier0, result.Tier);
        Assert.Equal(0.0, result.Confidence);
        Assert.Null(result.Intent);
        Assert.Contains("empty", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("привет", "system.greeting")]
    [InlineData("Привет!", "system.greeting")]
    [InlineData("здравствуйте", "system.greeting")]
    [InlineData("добрый день", "system.greeting")]
    [InlineData("Hello there", "system.greeting")]
    [InlineData("hi bot", "system.greeting")]
    public async Task Tier0_DefaultRules_MatchesGreeting(string input, string expectedIntent)
    {
        var request = CreateRequest(input);
        RouteResult result = await _router.TryRouteAsync(request);

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal(IntentTier.Tier0, result.Tier);
        Assert.Equal(expectedIntent, result.Intent);
        Assert.Equal(1.0, result.Confidence);
        Assert.Equal("Greeting", result.MatchedRuleName);
    }

    [Theory]
    [InlineData("статус", "system.status")]
    [InlineData("как дела?", "system.status")]
    [InlineData("status", "system.status")]
    [InlineData("health", "system.status")]
    public async Task Tier0_DefaultRules_MatchesStatus(string input, string expectedIntent)
    {
        var request = CreateRequest(input);
        RouteResult result = await _router.TryRouteAsync(request);

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal(IntentTier.Tier0, result.Tier);
        Assert.Equal(expectedIntent, result.Intent);
        Assert.Equal("Status", result.MatchedRuleName);
    }

    [Theory]
    [InlineData("поставь таймер на 15 минут", "15", "минут")]
    [InlineData("таймер на 30 секунд", "30", "секунд")]
    [InlineData("таймер на 2 часа", "2", "часа")]
    public async Task Tier0_DefaultRules_ExtractsNamedParameters_Timer(string input, string expectedDuration, string expectedUnit)
    {
        var request = CreateRequest(input);
        RouteResult result = await _router.TryRouteAsync(request);

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal("system.timer", result.Intent);
        Assert.Equal(expectedDuration, result.Parameters["duration"]);
        Assert.Equal(expectedUnit, result.Parameters["unit"]);
    }

    [Theory]
    [InlineData("сделай громкость на 80%", "80")]
    [InlineData("громкость 50", "50")]
    [InlineData("громкость на 100", "100")]
    public async Task Tier0_DefaultRules_ExtractsNamedParameters_Volume(string input, string expectedLevel)
    {
        var request = CreateRequest(input);
        RouteResult result = await _router.TryRouteAsync(request);

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal("media.volume", result.Intent);
        Assert.Equal(expectedLevel, result.Parameters["level"]);
    }

    [Fact]
    public async Task Tier0_DefaultRules_ExtractsNamedParameters_MediaPlay()
    {
        var request = CreateRequest("включи рок музыку");
        RouteResult result = await _router.TryRouteAsync(request);

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal("media.play", result.Intent);
        Assert.Equal("рок музыку", result.Parameters["query"]);
    }

    [Theory]
    [InlineData("стоп")]
    [InlineData("останови")]
    [InlineData("pause")]
    public async Task Tier0_DefaultRules_MatchesMediaStop(string input)
    {
        var request = CreateRequest(input);
        RouteResult result = await _router.TryRouteAsync(request);

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal("media.stop", result.Intent);
    }

    [Theory]
    [InlineData("какая громкость")]
    [InlineData("какая громкость?")]
    [InlineData("текущая громкость")]
    [InlineData("громкость")]
    [InlineData("уровень звука")]
    [InlineData("get volume")]
    [InlineData("volume")]
    public async Task Tier0_DefaultRules_MatchesVolumeGet(string input)
    {
        var request = CreateRequest(input);
        RouteResult result = await _router.TryRouteAsync(request);

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal("volume.get", result.Intent);
        Assert.Equal("VolumeGet", result.MatchedRuleName);
    }

    [Theory]
    [InlineData("сделай громче", "volume.up")]
    [InlineData("громче", "volume.up")]
    [InlineData("прибавь громкость", "volume.up")]
    [InlineData("увеличь громкость", "volume.up")]
    [InlineData("volume up", "volume.up")]
    [InlineData("сделай тише", "volume.down")]
    [InlineData("тише", "volume.down")]
    [InlineData("убавь громкость", "volume.down")]
    [InlineData("уменьши громкость", "volume.down")]
    [InlineData("volume down", "volume.down")]
    [InlineData("выключи звук", "volume.mute")]
    [InlineData("без звука", "volume.mute")]
    [InlineData("mute", "volume.mute")]
    [InlineData("включи звук", "volume.unmute")]
    [InlineData("со звуком", "volume.unmute")]
    [InlineData("unmute", "volume.unmute")]
    [InlineData("переключи звук", "volume.toggle_mute")]
    [InlineData("toggle mute", "volume.toggle_mute")]
    public async Task Tier0_DefaultRules_MatchesVolumeControls(string input, string expectedIntent)
    {
        var request = CreateRequest(input);
        RouteResult result = await _router.TryRouteAsync(request);

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal(expectedIntent, result.Intent);
    }

    [Fact]
    public async Task Tier0_UnmatchedInput_ReturnsEscalateWithoutException()
    {
        var request = CreateRequest("напиши сложную программу на Python для анализа логов");
        RouteResult result = await _router.TryRouteAsync(request);

        Assert.Equal(RouteStatus.Escalate, result.Status);
        Assert.Equal(IntentTier.Tier0, result.Tier);
        Assert.Equal(0.0, result.Confidence);
        Assert.Null(result.Intent);
        Assert.Null(result.MatchedRuleName);
    }

    [Fact]
    public async Task Tier0_MultipleMatchingRules_HighestPriorityWins()
    {
        var lowPriorityRule = new RegexRuleDefinition
        {
            Name = "LowRule",
            Pattern = ".*тест.*",
            Intent = "intent.low",
            Priority = 10
        };

        var highPriorityRule = new RegexRuleDefinition
        {
            Name = "HighRule",
            Pattern = ".*тест.*",
            Intent = "intent.high",
            Priority = 100
        };

        var provider = new TestRuleProvider([lowPriorityRule, highPriorityRule]);
        var router = new Tier0RegexRouter(provider);

        var request = CreateRequest("это обычный тест");
        RouteResult result = await router.TryRouteAsync(request);

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal("intent.high", result.Intent);
        Assert.Equal("HighRule", result.MatchedRuleName);
    }

    [Fact]
    public async Task Tier0_CatastrophicBacktracking_DoesNotHang_TimesOutAndSkipsRule()
    {
        // Pathological regex prone to catastrophic backtracking
        var pathologicalRule = new RegexRuleDefinition
        {
            Name = "PathologicalRule",
            Pattern = @"^(a+)+$",
            Intent = "intent.pathological",
            Priority = 100,
            TimeoutMs = 50 // Short 50ms timeout
        };

        var fallbackRule = new RegexRuleDefinition
        {
            Name = "FallbackRule",
            Pattern = @"^a+b$",
            Intent = "intent.fallback",
            Priority = 10,
            TimeoutMs = 200
        };

        var provider = new TestRuleProvider([pathologicalRule, fallbackRule]);
        var router = new Tier0RegexRouter(provider);

        // Input triggers exponential backtracking on (a+)+$
        string maliciousInput = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaab";

        var sw = System.Diagnostics.Stopwatch.StartNew();
        RouteResult result = await router.TryRouteAsync(CreateRequest(maliciousInput));
        sw.Stop();

        // Router must NOT hang indefinitely; timeout must occur within reasonable threshold
        Assert.True(sw.ElapsedMilliseconds < 2000, $"Router took {sw.ElapsedMilliseconds}ms, expected timeout < 2000ms");
        Assert.True(router.TimeoutCount >= 1, "TimeoutCount should be incremented");

        // The fallback rule matches after the pathological rule times out
        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal("intent.fallback", result.Intent);
        Assert.Equal("FallbackRule", result.MatchedRuleName);
    }

    [Fact]
    public async Task Tier0_Normalization_CollapsesWhitespaceAndTrims()
    {
        var request = CreateRequest("   привет     друг   ");
        RouteResult result = await _router.TryRouteAsync(request);

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal("system.greeting", result.Intent);
    }

    [Fact]
    public async Task Tier0_Metrics_TracksHitsAndMisses()
    {
        var provider = new ConfigurationRegexRuleProvider();
        var router = new Tier0RegexRouter(provider);

        Assert.Equal(0, router.HitCount);
        Assert.Equal(0, router.MissCount);

        await router.TryRouteAsync(CreateRequest("привет"));
        Assert.Equal(1, router.HitCount);
        Assert.Equal(0, router.MissCount);

        await router.TryRouteAsync(CreateRequest("непонятная фраза"));
        Assert.Equal(1, router.HitCount);
        Assert.Equal(1, router.MissCount);
        Assert.True(router.LastMatchDurationMs >= 0);
    }

    private static RouteRequest CreateRequest(string text) =>
        new()
        {
            Text = text,
            Channel = ChannelKind.Telegram,
            Locale = "ru-RU"
        };

    private sealed class TestRuleProvider : IRegexRuleProvider
    {
        private readonly IReadOnlyList<RegexRuleDefinition> _rules;

        public TestRuleProvider(IReadOnlyList<RegexRuleDefinition> rules)
        {
            _rules = rules;
        }

        public IReadOnlyList<RegexRuleDefinition> GetRules() => _rules;
    }
}

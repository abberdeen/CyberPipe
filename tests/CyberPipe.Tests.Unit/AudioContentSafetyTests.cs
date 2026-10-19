using CyberPipe.Contracts;
using CyberPipe.Security;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class AudioContentSafetyTests
{
    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        public TestTimeProvider(DateTimeOffset initialUtcNow) => _utcNow = initialUtcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
    }

    private sealed class FakeEmbeddingProvider : IEmbeddingProvider
    {
        public bool ShouldThrow { get; set; }
        private readonly Dictionary<string, float[]> _vectors = new(StringComparer.OrdinalIgnoreCase);

        public void SetVector(string text, float[] vector)
        {
            _vectors[text] = vector;
        }

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("ONNX embedding runtime offline.");
            }

            foreach (var (key, vec) in _vectors)
            {
                if (text.Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(vec);
                }
            }

            // Default neutral vector
            return Task.FromResult(new float[] { 0.1f, 0.9f });
        }
    }

    private static (AudioContentSafetyEvaluator Evaluator, FakeEmbeddingProvider Embeddings, TestTimeProvider Clock) CreateTestEvaluator(
        AudioContentSafetyOptions? options = null)
    {
        var embeddings = new FakeEmbeddingProvider();

        // Prototype risk vector: dimension 0 is high
        float[] riskVector = [0.95f, 0.05f];
        embeddings.SetVector("screaming", riskVector);
        embeddings.SetVector("крик", riskVector);
        embeddings.SetVector("siren", riskVector);
        embeddings.SetVector("alarm", riskVector);
        embeddings.SetVector("jump scare", riskVector);
        embeddings.SetVector("horror", riskVector);
        embeddings.SetVector("screech", riskVector);
        embeddings.SetVector("взрыв", riskVector);
        embeddings.SetVector("шум", riskVector);

        var clock = new TestTimeProvider(new DateTimeOffset(2026, 10, 19, 14, 0, 0, TimeSpan.Zero)); // 14:00 daytime

        var evaluator = new AudioContentSafetyEvaluator(embeddings, options, clock);
        return (evaluator, embeddings, clock);
    }

    [Fact]
    public async Task CalmDaytimeAudio_ByExplicitRequest_ReturnsAllow()
    {
        var (evaluator, _, _) = CreateTestEvaluator();

        var request = new AudioSafetyRequest
        {
            Title = "Morning Jazz Chillout",
            Description = "Soft acoustic music for focus and concentration",
            PeakDbfs = -18.0,
            LoudnessLufs = -20.0,
            StartTransientDb = 2.0,
            IsExplicitOwnerRequest = true,
            OwnerLocalTime = new TimeOnly(14, 30),
        };

        AudioSafetyDecision decision = await evaluator.EvaluateAsync(request);

        Assert.Equal(AudioSafetyAction.Allow, decision.Action);
        Assert.True(decision.RiskScore < 0.30);
        Assert.Null(decision.RecommendedVolumeLevel);
    }

    [Fact]
    public async Task SameContent_NightUnprompted_ReceivesMoreSevereActionThanDaytimeExplicit()
    {
        var (evaluator, _, _) = CreateTestEvaluator();

        // Daytime explicit request for loud action sound
        var dayRequest = new AudioSafetyRequest
        {
            Title = "Movie trailer with explosion and siren",
            PeakDbfs = -2.0,
            LoudnessLufs = -9.0,
            StartTransientDb = 12.0,
            IsExplicitOwnerRequest = true,
            OwnerLocalTime = new TimeOnly(14, 0),
        };

        AudioSafetyDecision dayDecision = await evaluator.EvaluateAsync(dayRequest);

        // Nighttime unprompted playback of the exact same content
        var nightRequest = new AudioSafetyRequest
        {
            Title = "Movie trailer with explosion and siren",
            PeakDbfs = -2.0,
            LoudnessLufs = -9.0,
            StartTransientDb = 12.0,
            IsExplicitOwnerRequest = false,
            OwnerLocalTime = new TimeOnly(2, 30), // 02:30 AM night
        };

        AudioSafetyDecision nightDecision = await evaluator.EvaluateAsync(nightRequest);

        // Monotonicity: night unprompted risk must be strictly higher than day explicit
        Assert.True(nightDecision.RiskScore > dayDecision.RiskScore);
        Assert.True(nightDecision.Action >= dayDecision.Action);
        Assert.Equal(AudioSafetyAction.DeferOrDeny, nightDecision.Action);
    }

    [Fact]
    public async Task ReduceVolume_TargetLevel_MonotonicallyDecreasesWithHigherRisk()
    {
        var (evaluator, _, _) = CreateTestEvaluator();

        // Moderate risk: peak -12 dBFS
        var req1 = new AudioSafetyRequest
        {
            Title = "Notice tone",
            PeakDbfs = -14.0,
            LoudnessLufs = -15.0,
            IsExplicitOwnerRequest = false,
            OwnerLocalTime = new TimeOnly(12, 0),
        };

        // Higher risk: peak -5 dBFS with transient
        var req2 = new AudioSafetyRequest
        {
            Title = "Urgent bell with loud transient",
            PeakDbfs = -6.0,
            LoudnessLufs = -10.0,
            StartTransientDb = 14.0,
            IsExplicitOwnerRequest = false,
            OwnerLocalTime = new TimeOnly(12, 0),
        };

        AudioSafetyDecision dec1 = await evaluator.EvaluateAsync(req1);
        AudioSafetyDecision dec2 = await evaluator.EvaluateAsync(req2);

        Assert.Equal(AudioSafetyAction.ReduceVolume, dec1.Action);
        Assert.Equal(AudioSafetyAction.ReduceVolume, dec2.Action);

        Assert.NotNull(dec1.RecommendedVolumeLevel);
        Assert.NotNull(dec2.RecommendedVolumeLevel);

        // Monotonic decrease: dec2 has higher risk, so its recommended volume must be lower
        Assert.True(dec2.RiskScore > dec1.RiskScore);
        Assert.True(dec2.RecommendedVolumeLevel < dec1.RecommendedVolumeLevel);

        // Never a fixed static clamp (such as 0.60 or 0.25)
        Assert.NotEqual(0.60, dec1.RecommendedVolumeLevel);
        Assert.NotEqual(0.25, dec1.RecommendedVolumeLevel);
    }

    [Fact]
    public async Task SemanticProximity_ClassifiesByEmbeddingSimilarity_NotKeywordMatch()
    {
        var (evaluator, embeddings, _) = CreateTestEvaluator();

        // Synonym description without matching prototype words literally
        // Vector is close to risk prototype [0.90f, 0.10f]
        embeddings.SetVector("abrupt horrifying acoustic shock", [0.90f, 0.10f]);

        var request = new AudioSafetyRequest
        {
            Title = "abrupt horrifying acoustic shock",
            IsExplicitOwnerRequest = false,
            OwnerLocalTime = new TimeOnly(15, 0),
        };

        AudioSafetyDecision decision = await evaluator.EvaluateAsync(request);

        SignalContribution? semanticContrib = decision.Contributions.FirstOrDefault(c => c.SignalKind == "Semantic");
        Assert.NotNull(semanticContrib);
        Assert.True(semanticContrib!.NormalizedRisk > 0.70);
    }

    [Fact]
    public async Task DegradedMode_WhenEmbeddingProviderFails_UsesAcousticAndContext()
    {
        var (evaluator, embeddings, _) = CreateTestEvaluator();
        embeddings.ShouldThrow = true; // Simulating provider error

        var request = new AudioSafetyRequest
        {
            Title = "Test sound",
            PeakDbfs = -3.0,
            LoudnessLufs = -8.0,
            StartTransientDb = 15.0,
            IsExplicitOwnerRequest = false,
            OwnerLocalTime = new TimeOnly(12, 0),
        };

        AudioSafetyDecision decision = await evaluator.EvaluateAsync(request);

        // Did not crash!
        Assert.NotNull(decision);
        SignalContribution? semantic = decision.Contributions.FirstOrDefault(c => c.SignalKind == "Semantic");
        Assert.NotNull(semantic);
        Assert.Equal("error", semantic!.RawValue);

        // Decision was made using acoustic and context
        SignalContribution? acoustic = decision.Contributions.FirstOrDefault(c => c.SignalKind == "Acoustic");
        Assert.NotNull(acoustic);
        Assert.True(acoustic!.NormalizedRisk > 0.50);
    }

    [Fact]
    public async Task MissingAllSignals_UnpromptedPlayback_ReturnsWarn()
    {
        // No embeddings, no acoustic metadata, unprompted
        var evaluator = new AudioContentSafetyEvaluator(embeddingProvider: null);

        var request = new AudioSafetyRequest
        {
            IsExplicitOwnerRequest = false, // unprompted sudden playback
            Source = "background-chain",
        };

        AudioSafetyDecision decision = await evaluator.EvaluateAsync(request);

        Assert.Equal(AudioSafetyAction.Warn, decision.Action);
    }

    [Fact]
    public async Task Explanation_ContainsContributions_AndOmitsCleartextSecrets()
    {
        var (evaluator, _, _) = CreateTestEvaluator();

        var request = new AudioSafetyRequest
        {
            Title = "SecretPassword123 Sound Effect",
            PeakDbfs = -10.0,
            LoudnessLufs = -12.0,
            IsExplicitOwnerRequest = true,
        };

        AudioSafetyDecision decision = await evaluator.EvaluateAsync(request);

        Assert.NotEmpty(decision.Contributions);
        Assert.Contains("Acoustic", decision.Explanation);
        Assert.Contains("Context", decision.Explanation);

        // Explanation does not leak raw Title or Transcript text
        Assert.DoesNotContain("SecretPassword123", decision.Explanation);
    }

    [Fact]
    public async Task CustomOptions_AltersEvaluationThresholds()
    {
        var customOptions = new AudioContentSafetyOptions
        {
            LowRiskThreshold = 0.60, // More lenient
            MediumRiskThreshold = 0.80,
            HighRiskThreshold = 0.95,
        };

        var (evaluator, _, _) = CreateTestEvaluator(customOptions);

        var request = new AudioSafetyRequest
        {
            PeakDbfs = -10.0,
            LoudnessLufs = -12.0,
            IsExplicitOwnerRequest = false,
            OwnerLocalTime = new TimeOnly(15, 0),
        };

        AudioSafetyDecision decision = await evaluator.EvaluateAsync(request);

        // With LowRiskThreshold=0.50, moderate risk (e.g. 0.40) is Allowed instead of ReduceVolume
        Assert.Equal(AudioSafetyAction.Allow, decision.Action);
    }
}

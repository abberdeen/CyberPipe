using CyberPipe.Contracts;
using CyberPipe.Data;
using CyberPipe.Router;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class Tier1SemanticRouterTests
{
    [Fact]
    public async Task Tier1_MatchesIntent_WhenSimilarityAboveThresholdAndMarginSufficient()
    {
        await using var db = await TestDatabase.CreateAsync();         EmbeddingStoreService store = db.Embeddings;
        var provider = new FakeEmbeddingProvider();

        // Seed target example
        provider.Register("помощь", [1.0f, 0.0f, 0.0f]);
        provider.Register("статус", [0.0f, 1.0f, 0.0f]);

        var examples = new FakeExampleProvider(
        [
            new() { Text = "помощь", Intent = "system.help" },
            new() { Text = "статус", Intent = "system.status" },
        ]);

        var options = new CyberPipeOptions
        {
            Router = new RouterOptions
            {
                Tier1SimilarityThreshold = 0.80,
                Tier1MarginThreshold = 0.05
            }
        };

        var router = new Tier1SemanticRouter(provider, store, options, exampleProvider: examples);

        // Query vector close to "помощь"
        provider.Register("помоги мне", [0.98f, 0.10f, 0.0f]); // Cosine ~ 0.99 with "помощь"

        var request = new RouteRequest
        {
            Text = "помоги мне",
            Channel = ChannelKind.Telegram
        };

        RouteResult result = await router.TryRouteAsync(request);

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal(IntentTier.Tier1, result.Tier);
        Assert.Equal("system.help", result.Intent);
        Assert.True(result.Confidence >= 0.80);
    }

    [Fact]
    public async Task Tier1_Escalates_WhenNoMatchesAboveSimilarityThreshold()
    {
        await using var db = await TestDatabase.CreateAsync();         EmbeddingStoreService store = db.Embeddings;
        var provider = new FakeEmbeddingProvider();

        provider.Register("помощь", [1.0f, 0.0f, 0.0f]);
        var examples = new FakeExampleProvider(
        [
            new() { Text = "помощь", Intent = "system.help" }
        ]);

        var options = new CyberPipeOptions
        {
            Router = new RouterOptions { Tier1SimilarityThreshold = 0.85 }
        };

        var router = new Tier1SemanticRouter(provider, store, options, exampleProvider: examples);

        // Orthogonal / distant query vector (similarity 0.0 < 0.85)
        provider.Register("купи слона", [0.0f, 1.0f, 0.0f]);

        var result = await router.TryRouteAsync(new RouteRequest
        {
            Text = "купи слона",
            Channel = ChannelKind.Telegram
        });

        Assert.Equal(RouteStatus.Escalate, result.Status);
        Assert.Equal(IntentTier.Tier1, result.Tier);
        Assert.Contains("similarity threshold", result.Reason);
    }

    [Fact]
    public async Task Tier1_Escalates_WhenMarginBelowThresholdBetweenConflictingIntents()
    {
        await using var db = await TestDatabase.CreateAsync();         EmbeddingStoreService store = db.Embeddings;
        var provider = new FakeEmbeddingProvider();

        // Two intents with slightly different vectors
        provider.Register("помощь", [1.0f, 0.0f, 0.0f]);
        provider.Register("диагностика", [0.96f, 0.28f, 0.0f]);

        var examples = new FakeExampleProvider(
        [
            new() { Text = "помощь", Intent = "system.help" },
            new() { Text = "диагностика", Intent = "diagnostics.run" }
        ]);

        var options = new CyberPipeOptions
        {
            Router = new RouterOptions
            {
                Tier1SimilarityThreshold = 0.70,
                Tier1MarginThreshold = 0.10 // Requires at least 0.10 margin
            }
        };

        var router = new Tier1SemanticRouter(provider, store, options, exampleProvider: examples);

        // Query sits right between them: similarity to "помощь" ~ 0.98, to "диагностика" ~ 0.95 (margin 0.03 < 0.10)
        provider.Register("неоднозначный запрос", [0.98f, 0.14f, 0.0f]);

        var result = await router.TryRouteAsync(new RouteRequest
        {
            Text = "неоднозначный запрос",
            Channel = ChannelKind.Telegram
        });

        Assert.Equal(RouteStatus.Escalate, result.Status);
        Assert.Equal(IntentTier.Tier1, result.Tier);
        Assert.Contains("margin", result.Reason);
    }

    [Fact]
    public async Task Tier1_DoesNotEscalate_WhenTwoTopCandidatesHaveSameIntent()
    {
        await using var db = await TestDatabase.CreateAsync();         EmbeddingStoreService store = db.Embeddings;
        var provider = new FakeEmbeddingProvider();

        // Two examples for the SAME intent
        provider.Register("помощь", [1.0f, 0.0f, 0.0f]);
        provider.Register("справка", [0.95f, 0.10f, 0.0f]);

        var examples = new FakeExampleProvider(
        [
            new() { Text = "помощь", Intent = "system.help" },
            new() { Text = "справка", Intent = "system.help" }
        ]);

        var options = new CyberPipeOptions
        {
            Router = new RouterOptions
            {
                Tier1SimilarityThreshold = 0.80,
                Tier1MarginThreshold = 0.10
            }
        };

        var router = new Tier1SemanticRouter(provider, store, options, exampleProvider: examples);

        provider.Register("дай справку", [0.97f, 0.05f, 0.0f]);

        var result = await router.TryRouteAsync(new RouteRequest
        {
            Text = "дай справку",
            Channel = ChannelKind.Telegram
        });

        // Should match system.help, not escalate for margin between same intent
        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal("system.help", result.Intent);
    }

    [Fact]
    public async Task Tier1_Escalates_WhenAnotherIntentIsCloseBehindRepeatedExamples()
    {
        await using var db = await TestDatabase.CreateAsync();         EmbeddingStoreService store = db.Embeddings;
        var provider = new FakeEmbeddingProvider();
        provider.Register("тише", [1.0f, 0.0f, 0.0f]);
        provider.Register("убавь", [0.99f, 0.05f, 0.0f]);
        provider.Register("mute", [0.96f, 0.28f, 0.0f]);

        var examples = new FakeExampleProvider(
        [
            new() { Text = "тише", Intent = "volume.down" },
            new() { Text = "убавь", Intent = "volume.down" },
            new() { Text = "mute", Intent = "volume.mute" },
        ]);

        var options = new CyberPipeOptions
        {
            Router = new RouterOptions
            {
                Tier1SimilarityThreshold = 0.70,
                Tier1MarginThreshold = 0.05
            }
        };

        var router = new Tier1SemanticRouter(provider, store, options, exampleProvider: examples);
        provider.Register("не про громкость", [0.995f, 0.02f, 0.0f]);

        RouteResult result = await router.TryRouteAsync(new RouteRequest
        {
            Text = "не про громкость",
            Channel = ChannelKind.Telegram
        });

        Assert.Equal(RouteStatus.Escalate, result.Status);
        Assert.Contains("margin", result.Reason);
    }

    [Fact]
    public async Task E5_Question_IsCloserToChatThanToVolume()
    {
        string model = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyberPipe", "embeddings", "multilingual-e5-small", "model.onnx");
        if (!File.Exists(model))
        {
            return;
        }

        using var provider = new OnnxEmbeddingProvider(model);
        float[] question = await provider.EmbedAsync("capital of russia");
        float[] volume = await provider.EmbedAsync("сделай тише");
        float[] chat = await provider.EmbedAsync("столица франции");

        double toVolume = Cosine(question, volume);
        double toChat = Cosine(question, chat);
        Assert.True(toChat > toVolume, $"chat {toChat:F3} should beat volume {toVolume:F3}");
        Assert.True(toVolume < 0.80, $"volume similarity {toVolume:F3} should stay under the router threshold");
    }

    [Fact]
    public async Task Tier1_DegradesGracefully_WhenEmbeddingProviderThrows()
    {
        await using var db = await TestDatabase.CreateAsync();         EmbeddingStoreService store = db.Embeddings;
        var throwingProvider = new ThrowingEmbeddingProvider(new FileNotFoundException("ONNX model not found."));

        var router = new Tier1SemanticRouter(throwingProvider, store);

        var result = await router.TryRouteAsync(new RouteRequest
        {
            Text = "тестовый запрос",
            Channel = ChannelKind.Telegram
        });

        Assert.Equal(RouteStatus.Escalate, result.Status);
        Assert.Equal(IntentTier.Tier1, result.Tier);
        Assert.Contains("Embedding model failure", result.Reason);
    }

    [Fact]
    public async Task Tier1_DegradesGracefully_WhenEmbeddingStoreThrows()
    {
        await using var brokenDb = await TestDatabase.CreateAsync();
        EmbeddingStoreService throwingStore = brokenDb.Embeddings;
        await brokenDb.DisposeAsync();
        var provider = new FakeEmbeddingProvider();
        provider.Register("test", [1.0f, 0.0f, 0.0f]);

        var router = new Tier1SemanticRouter(provider, throwingStore);

        var result = await router.TryRouteAsync(new RouteRequest
        {
            Text = "test",
            Channel = ChannelKind.Telegram
        });

        Assert.Equal(RouteStatus.Escalate, result.Status);
        Assert.Equal(IntentTier.Tier1, result.Tier);
        Assert.Contains("Embedding store", result.Reason);
    }

    [Fact]
    public async Task Tier1_ConsultsSemanticCacheFirst_AndReturnsCachedResultWithoutStoreSearch()
    {
        await using var db = await TestDatabase.CreateAsync();         EmbeddingStoreService store = db.Embeddings;
        var provider = new FakeEmbeddingProvider();
        provider.Register("кэшированный запрос", [1.0f, 0.0f, 0.0f]);

        var cache = new FakeSemanticCache();
        cache.SetResponse([1.0f, 0.0f, 0.0f], new RouteResult
        {
            Status = RouteStatus.Matched,
            Tier = IntentTier.Tier1,
            Intent = "cached.intent",
            Confidence = 0.95
        });

        var options = new CyberPipeOptions
        {
            Router = new RouterOptions
            {
                SemanticCacheEnabled = true,
                SemanticCacheThreshold = 0.90
            }
        };

        var router = new Tier1SemanticRouter(provider, store, options, semanticCache: cache);

        var result = await router.TryRouteAsync(new RouteRequest
        {
            Text = "кэшированный запрос",
            Channel = ChannelKind.Telegram
        });

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal("cached.intent", result.Intent);
        Assert.Equal("SemanticCache", result.MatchedRuleName);
    }

    [Fact]
    public async Task Tier1_ReindexesExamplesOnlyWhenSetChanges()
    {
        await using var db = await TestDatabase.CreateAsync();         EmbeddingStoreService store = db.Embeddings;
        var provider = new FakeEmbeddingProvider();
        provider.Register("ex1", [1.0f, 0.0f, 0.0f]);
        provider.Register("ex2", [0.0f, 1.0f, 0.0f]);
        provider.Register("query", [1.0f, 0.0f, 0.0f]);

        var mutableProvider = new MutableExampleProvider(
        [
            new() { Text = "ex1", Intent = "intent.one" },
            new() { Text = "ex2", Intent = "intent.two" }
        ]);

        var router = new Tier1SemanticRouter(provider, store, exampleProvider: mutableProvider);

        // Call 1 -> triggers initial indexing
        await router.TryRouteAsync(new RouteRequest { Text = "query", Channel = ChannelKind.Telegram });
        Assert.Equal(2, await store.CountAsync(EmbeddingNamespaces.Router));

        // Call 2 with identical examples -> skips indexing!
        await router.TryRouteAsync(new RouteRequest { Text = "query", Channel = ChannelKind.Telegram });
        Assert.Equal(2, await store.CountAsync(EmbeddingNamespaces.Router));

        // Modify examples set
        provider.Register("ex3", [0.0f, 0.0f, 1.0f]);
        mutableProvider.Add(new SemanticExampleDefinition { Text = "ex3", Intent = "intent.three" });

        // Call 3 with modified examples -> reindexes!
        await router.TryRouteAsync(new RouteRequest { Text = "query", Channel = ChannelKind.Telegram });
        Assert.Equal(3, await store.CountAsync(EmbeddingNamespaces.Router));
    }

    [Fact]
    public async Task Tier1_EscalatesImmediately_WhenDisabledInConfiguration()
    {
        await using var db = await TestDatabase.CreateAsync();         EmbeddingStoreService store = db.Embeddings;
        var provider = new FakeEmbeddingProvider();

        var options = new CyberPipeOptions
        {
            Router = new RouterOptions { Tier1Enabled = false }
        };

        var router = new Tier1SemanticRouter(provider, store, options);

        var result = await router.TryRouteAsync(new RouteRequest
        {
            Text = "помощь",
            Channel = ChannelKind.Telegram
        });

        Assert.Equal(RouteStatus.Escalate, result.Status);
        Assert.Contains("disabled", result.Reason);
    }

    [Fact]
    public async Task OnnxEmbeddingProvider_ThrowsMeaningfulException_WhenModelNotFound()
    {
        using var provider = new OnnxEmbeddingProvider("C:\\nonexistent\\model.onnx");
        Assert.False(provider.IsModelLoaded);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.EmbedAsync("test text"));
    }

    // --- Test doubles and helpers ---

    private sealed class FakeEmbeddingProvider : IEmbeddingProvider
    {
        private readonly Dictionary<string, float[]> _vectors = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string text, float[] vector)
        {
            // Normalize L2
            float norm = MathF.Sqrt(vector.Sum(v => v * v));
            float[] normalized = norm > 0 ? vector.Select(v => v / norm).ToArray() : vector;
            _vectors[text] = normalized;
        }

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            if (_vectors.TryGetValue(text, out float[]? vector))
            {
                return Task.FromResult(vector);
            }

            // Deterministic synthetic fallback vector based on string hash
            int dim = 3;
            float[] gen = new float[dim];
            int hash = text.GetHashCode();
            for (int i = 0; i < dim; i++)
            {
                gen[i] = ((hash >> (i * 8)) & 0xFF) / 255.0f;
            }
            float norm = MathF.Sqrt(gen.Sum(v => v * v));
            return Task.FromResult(norm > 0 ? gen.Select(v => v / norm).ToArray() : gen);
        }
    }

    private sealed class ThrowingEmbeddingProvider(Exception ex) : IEmbeddingProvider
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
            throw ex;
    }

    private sealed class FakeExampleProvider(IReadOnlyList<SemanticExampleDefinition> examples) : ISemanticExampleProvider
    {
        public IReadOnlyList<SemanticExampleDefinition> GetExamples() => examples;
    }

    private sealed class MutableExampleProvider(IEnumerable<SemanticExampleDefinition> initial) : ISemanticExampleProvider
    {
        private readonly List<SemanticExampleDefinition> _examples = [.. initial];

        public void Add(SemanticExampleDefinition example) => _examples.Add(example);

        public IReadOnlyList<SemanticExampleDefinition> GetExamples() => _examples;
    }

    private sealed class FakeSemanticCache : ISemanticCache
    {
        private readonly List<(float[] Vector, RouteResult Result)> _entries = [];
        private long _hits;
        private long _misses;

        public void SetResponse(float[] vector, RouteResult result) => _entries.Add((vector, result));

        public Task<RouteResult?> TryGetAsync(float[] embedding, CancellationToken cancellationToken = default)
        {
            foreach (var (v, res) in _entries)
            {
                double similarity = Cosine(embedding, v);
                if (similarity >= 0.90)
                {
                    _hits++;
                    return Task.FromResult<RouteResult?>(res);
                }
            }

            _misses++;
            return Task.FromResult<RouteResult?>(null);
        }

        public Task PutAsync(float[] embedding, RouteResult result, CancellationToken cancellationToken = default)
        {
            _entries.Add((embedding, result));
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            _entries.Clear();
            return Task.CompletedTask;
        }

        public Task<CacheStatsDto> GetStatsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CacheStatsDto { Hits = _hits, Misses = _misses, EntryCount = _entries.Count });
    }

    private static double Cosine(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        if (a.Count != b.Count) return 0.0;
        double dot = 0.0;
        double normA = 0.0;
        double normB = 0.0;
        for (int i = 0; i < a.Count; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        double denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom > 1e-12 ? Math.Clamp(dot / denom, -1.0, 1.0) : 0.0;
    }
}

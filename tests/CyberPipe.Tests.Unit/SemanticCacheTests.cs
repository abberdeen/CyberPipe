using CyberPipe.Contracts;
using CyberPipe.Data;
using CyberPipe.Router;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class SemanticCacheTests
{
    [Fact]
    public async Task SemanticCache_RepeatedCloseQuery_ReturnsCachedMatched()
    {
        await using var db = await TestDatabase.CreateAsync();         EmbeddingStoreService store = db.Embeddings;
        var cache = new SemanticCache(store);

        float[] seedVector = [1.0f, 0.0f, 0.0f];
        var originalResult = new RouteResult
        {
            Status = RouteStatus.Matched,
            Tier = IntentTier.Tier1,
            Intent = "system.help",
            Confidence = 0.95,
            Parameters = new Dictionary<string, string> { ["topic"] = "network" }
        };

        await cache.PutAsync(seedVector, originalResult);

        // Query with close vector (cosine similarity ~0.99 >= 0.90)
        float[] closeQuery = [0.99f, 0.10f, 0.0f];
        RouteResult? cached = await cache.TryGetAsync(closeQuery);

        Assert.NotNull(cached);
        Assert.Equal(RouteStatus.Matched, cached.Status);
        Assert.Equal(IntentTier.Tier1, cached.Tier);
        Assert.Equal("system.help", cached.Intent);
        Assert.Equal("SemanticCache", cached.MatchedRuleName);
        Assert.Equal("network", cached.Parameters["topic"]);

        CacheStatsDto stats = await cache.GetStatsAsync();
        Assert.Equal(1, stats.Hits);
        Assert.Equal(0, stats.Misses);
        Assert.Equal(1, stats.EntryCount);
    }

    [Fact]
    public async Task SemanticCache_ExceedingMaxEntries_EvictsOldest()
    {
        await using var db = await TestDatabase.CreateAsync();         EmbeddingStoreService store = db.Embeddings;
        var options = new CyberPipeOptions { Router = new RouterOptions { SemanticCacheMaxEntries = 2 } };
        var cache = new SemanticCache(store, options);

        for (int i = 0; i < 3; i++)
        {
            await cache.PutAsync([1.0f, 0.0f, 0.0f], new RouteResult
            {
                Status = RouteStatus.Matched,
                Tier = IntentTier.Tier1,
                Intent = $"intent.{i}",
                Confidence = 0.95
            });
        }

        CacheStatsDto stats = await cache.GetStatsAsync();
        Assert.Equal(2, stats.EntryCount);
    }

    [Fact]
    public async Task SemanticCache_QueryBelowThreshold_ReturnsNullMiss()
    {
        await using var db = await TestDatabase.CreateAsync();         EmbeddingStoreService store = db.Embeddings;
        var cache = new SemanticCache(store);

        await cache.PutAsync([1.0f, 0.0f, 0.0f], new RouteResult
        {
            Status = RouteStatus.Matched,
            Tier = IntentTier.Tier1,
            Intent = "system.help",
            Confidence = 0.95
        });

        // Orthogonal vector (similarity 0.0 < 0.90)
        float[] distantQuery = [0.0f, 1.0f, 0.0f];
        RouteResult? cached = await cache.TryGetAsync(distantQuery);

        Assert.Null(cached);

        CacheStatsDto stats = await cache.GetStatsAsync();
        Assert.Equal(0, stats.Hits);
        Assert.Equal(1, stats.Misses);
    }

    [Fact]
    public async Task SemanticCache_Policy_DoesNotStore_BudgetDenied_Or_Failed_Or_LowConfidence()
    {
        await using var db = await TestDatabase.CreateAsync();         EmbeddingStoreService store = db.Embeddings;
        var cache = new SemanticCache(store);

        // 1. BudgetDenied
        await cache.PutAsync([1.0f, 0.0f, 0.0f], new RouteResult
        {
            Status = RouteStatus.BudgetDenied,
            Tier = IntentTier.Tier1,
            Confidence = 0.95
        });

        // 2. Failed
        await cache.PutAsync([1.0f, 0.0f, 0.0f], new RouteResult
        {
            Status = RouteStatus.Failed,
            Tier = IntentTier.Tier1,
            Confidence = 0.95
        });

        // 3. Low confidence (0.80 < default 0.90 cache threshold)
        await cache.PutAsync([1.0f, 0.0f, 0.0f], new RouteResult
        {
            Status = RouteStatus.Matched,
            Tier = IntentTier.Tier1,
            Intent = "low.conf",
            Confidence = 0.80
        });

        CacheStatsDto stats = await cache.GetStatsAsync();
        Assert.Equal(0, stats.EntryCount);
    }

    [Fact]
    public async Task SemanticCache_ExpiredTTL_ReturnsNullMiss_AndEvictsEntry()
    {
        await using var db = await TestDatabase.CreateAsync();         EmbeddingStoreService store = db.Embeddings;
        var timeProvider = new TestTimeProvider();
        var options = new CyberPipeOptions
        {
            Router = new RouterOptions
            {
                SemanticCacheTtlMinutes = 60 // 1 hour TTL
            }
        };

        var cache = new SemanticCache(store, options, timeProvider);

        await cache.PutAsync([1.0f, 0.0f, 0.0f], new RouteResult
        {
            Status = RouteStatus.Matched,
            Tier = IntentTier.Tier1,
            Intent = "system.status",
            Confidence = 0.95
        });

        // Before expiry: hits
        RouteResult? fresh = await cache.TryGetAsync([1.0f, 0.0f, 0.0f]);
        Assert.NotNull(fresh);

        // Advance time by 61 minutes (past TTL)
        timeProvider.Advance(TimeSpan.FromMinutes(61));

        RouteResult? expired = await cache.TryGetAsync([1.0f, 0.0f, 0.0f]);
        Assert.Null(expired);

        CacheStatsDto stats = await cache.GetStatsAsync();
        Assert.Equal(1, stats.Hits);
        Assert.Equal(1, stats.Misses);
    }

    [Fact]
    public async Task SemanticCache_DifferentModelVersion_IsIgnored()
    {
        await using var db = await TestDatabase.CreateAsync();         EmbeddingStoreService store = db.Embeddings;

        // Model 1 writes entry
        var options1 = new CyberPipeOptions
        {
            Models = new ModelOptions { EmbeddingModelId = "model-v1" }
        };
        var cache1 = new SemanticCache(store, options1);

        await cache1.PutAsync([1.0f, 0.0f, 0.0f], new RouteResult
        {
            Status = RouteStatus.Matched,
            Tier = IntentTier.Tier1,
            Intent = "model.v1.intent",
            Confidence = 0.95
        });

        // Model 2 tries to read
        var options2 = new CyberPipeOptions
        {
            Models = new ModelOptions { EmbeddingModelId = "model-v2" }
        };
        var cache2 = new SemanticCache(store, options2);

        RouteResult? cached = await cache2.TryGetAsync([1.0f, 0.0f, 0.0f]);
        Assert.Null(cached);
    }

    [Fact]
    public async Task SemanticCache_DisabledInConfiguration_AlwaysMisses_AndPutIsNoOp()
    {
        await using var db = await TestDatabase.CreateAsync();         EmbeddingStoreService store = db.Embeddings;
        var options = new CyberPipeOptions
        {
            Router = new RouterOptions { SemanticCacheEnabled = false }
        };

        var cache = new SemanticCache(store, options);

        await cache.PutAsync([1.0f, 0.0f, 0.0f], new RouteResult
        {
            Status = RouteStatus.Matched,
            Tier = IntentTier.Tier1,
            Intent = "system.help",
            Confidence = 0.95
        });

        RouteResult? cached = await cache.TryGetAsync([1.0f, 0.0f, 0.0f]);
        Assert.Null(cached);

        CacheStatsDto stats = await cache.GetStatsAsync();
        Assert.Equal(0, stats.EntryCount);
        Assert.Equal(1, stats.Misses);
    }

    [Fact]
    public async Task SemanticCache_ClearAsync_RemovesAllCacheEntries()
    {
        await using var db = await TestDatabase.CreateAsync();         EmbeddingStoreService store = db.Embeddings;
        var cache = new SemanticCache(store);

        await cache.PutAsync([1.0f, 0.0f, 0.0f], new RouteResult
        {
            Status = RouteStatus.Matched,
            Tier = IntentTier.Tier1,
            Intent = "intent.one",
            Confidence = 0.95
        });

        await cache.PutAsync([0.0f, 1.0f, 0.0f], new RouteResult
        {
            Status = RouteStatus.Matched,
            Tier = IntentTier.Tier1,
            Intent = "intent.two",
            Confidence = 0.95
        });

        CacheStatsDto beforeClear = await cache.GetStatsAsync();
        Assert.Equal(2, beforeClear.EntryCount);

        await cache.ClearAsync();

        CacheStatsDto afterClear = await cache.GetStatsAsync();
        Assert.Equal(0, afterClear.EntryCount);
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

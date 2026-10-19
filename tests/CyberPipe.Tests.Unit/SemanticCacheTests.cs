using CyberPipe.Contracts;
using CyberPipe.Router;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class SemanticCacheTests
{
    [Fact]
    public async Task SemanticCache_RepeatedCloseQuery_ReturnsCachedMatched()
    {
        var store = new InMemoryEmbeddingStore();
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
    public async Task SemanticCache_QueryBelowThreshold_ReturnsNullMiss()
    {
        var store = new InMemoryEmbeddingStore();
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
        var store = new InMemoryEmbeddingStore();
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
        var store = new InMemoryEmbeddingStore();
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
        var store = new InMemoryEmbeddingStore();

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
        var store = new InMemoryEmbeddingStore();
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
        var store = new InMemoryEmbeddingStore();
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

    private class InMemoryEmbeddingStore : IEmbeddingStore
    {
        protected readonly List<EmbeddingRecord> Records = [];

        public Task UpsertAsync(EmbeddingRecord record, CancellationToken cancellationToken = default)
        {
            Records.RemoveAll(r => r.Namespace == record.Namespace && r.SourceRef == record.SourceRef);
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task UpsertManyAsync(IReadOnlyList<EmbeddingRecord> records, CancellationToken cancellationToken = default)
        {
            foreach (var r in records)
            {
                UpsertAsync(r, cancellationToken);
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EmbeddingMatch>> SearchAsync(EmbeddingQuery query, CancellationToken cancellationToken = default)
        {
            var matches = new List<EmbeddingMatch>();

            foreach (EmbeddingRecord r in Records)
            {
                if (r.Namespace != query.Namespace) continue;
                if (!string.IsNullOrEmpty(query.ModelId) && r.ModelId != query.ModelId) continue;

                double score = Cosine(query.Vector, r.Vector);
                if (score >= query.MinScore)
                {
                    matches.Add(new EmbeddingMatch
                    {
                        Id = r.Id,
                        SourceRef = r.SourceRef,
                        Score = score,
                        Metadata = r.Metadata
                    });
                }
            }

            return Task.FromResult<IReadOnlyList<EmbeddingMatch>>(
                matches.OrderByDescending(m => m.Score).Take(query.TopK).ToList());
        }

        public Task<bool> DeleteAsync(string @namespace, string sourceRef, CancellationToken cancellationToken = default)
        {
            int removed = Records.RemoveAll(r => r.Namespace == @namespace && r.SourceRef == sourceRef);
            return Task.FromResult(removed > 0);
        }

        public Task<int> DeleteByOwnerScopeAsync(string @namespace, CancellationToken cancellationToken = default)
        {
            int removed = Records.RemoveAll(r => r.Namespace == @namespace);
            return Task.FromResult(removed);
        }

        public Task<long> CountAsync(string @namespace, CancellationToken cancellationToken = default)
        {
            long count = Records.Count(r => r.Namespace == @namespace);
            return Task.FromResult(count);
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
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;

namespace CyberPipe.Router;

/// <summary>
/// Реализация семантического кэша маршрутизации поверх IEmbeddingStore (TASK-034).
/// Ключ — эмбеддинг запроса, значение — сохранённое решение RouteResult.
/// Поддерживает порог близости, TTL, вытеснение по лимиту объёма и версионирование по модели.
/// </summary>
public sealed class SemanticCache : ISemanticCache
{
    private readonly IEmbeddingStore _store;
    private readonly CyberPipeOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SemanticCache>? _logger;

    private long _hits;
    private long _misses;

    public SemanticCache(
        IEmbeddingStore store,
        CyberPipeOptions? options = null,
        TimeProvider? timeProvider = null,
        ILogger<SemanticCache>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? new CyberPipeOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public async Task<RouteResult?> TryGetAsync(float[] embedding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(embedding);

        if (!_options.Router.SemanticCacheEnabled)
        {
            Interlocked.Increment(ref _misses);
            return null;
        }

        try
        {
            var query = new EmbeddingQuery
            {
                Vector = embedding,
                Namespace = EmbeddingNamespaces.SemanticCache,
                TopK = 1,
                MinScore = _options.Router.SemanticCacheThreshold,
                ModelId = _options.Models.EmbeddingModelId
            };

            IReadOnlyList<EmbeddingMatch> matches = await _store.SearchAsync(query, cancellationToken);
            if (matches.Count == 0)
            {
                Interlocked.Increment(ref _misses);
                return null;
            }

            EmbeddingMatch match = matches[0];

            // 1. Check TTL expiration
            if (match.Metadata.TryGetValue("timestamp", out string? tsStr)
                && long.TryParse(tsStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixSeconds))
            {
                DateTimeOffset createdAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                TimeSpan age = _timeProvider.GetUtcNow() - createdAt;
                TimeSpan ttl = TimeSpan.FromMinutes(_options.Router.SemanticCacheTtlMinutes);

                if (age > ttl)
                {
                    _logger?.LogDebug("Evicting expired semantic cache entry '{SourceRef}' (age: {Age})", match.SourceRef, age);
                    _ = _store.DeleteAsync(EmbeddingNamespaces.SemanticCache, match.SourceRef, cancellationToken);
                    Interlocked.Increment(ref _misses);
                    return null;
                }
            }

            // 2. Check model version compatibility
            if (match.Metadata.TryGetValue("modelId", out string? entryModel)
                && !string.Equals(entryModel, _options.Models.EmbeddingModelId, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogDebug("Ignoring cache entry from different model version '{EntryModel}' vs '{CurrentModel}'",
                    entryModel, _options.Models.EmbeddingModelId);
                Interlocked.Increment(ref _misses);
                return null;
            }

            // 3. Deserialize parameters and build RouteResult
            IReadOnlyDictionary<string, string> parameters = new Dictionary<string, string>();
            if (match.Metadata.TryGetValue("parameters", out string? paramsJson) && !string.IsNullOrWhiteSpace(paramsJson))
            {
                try
                {
                    parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(paramsJson)
                        ?? new Dictionary<string, string>();
                }
                catch (JsonException)
                {
                    // Fallback to empty parameters on corrupt json
                }
            }

            string intent = match.Metadata.TryGetValue("intent", out string? inVal) ? inVal : match.SourceRef;

            Interlocked.Increment(ref _hits);

            return new RouteResult
            {
                Status = RouteStatus.Matched,
                Tier = IntentTier.Tier1,
                Intent = intent,
                Parameters = parameters,
                Confidence = match.Score,
                MatchedRuleName = "SemanticCache",
                Reason = "Semantic cache hit"
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Semantic cache lookup failed. Treating as miss.");
            Interlocked.Increment(ref _misses);
            return null;
        }
    }

    public async Task PutAsync(float[] embedding, RouteResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentNullException.ThrowIfNull(result);

        if (!_options.Router.SemanticCacheEnabled)
        {
            return;
        }

        // Cache policy: only confident Matched results are cached
        if (result.Status != RouteStatus.Matched || result.Confidence < _options.Router.SemanticCacheThreshold)
        {
            _logger?.LogDebug(
                "Skipping cache store: result status is {Status} with confidence {Confidence:F3} < threshold {Threshold:F3}",
                result.Status,
                result.Confidence,
                _options.Router.SemanticCacheThreshold);
            return;
        }

        try
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            string sourceRef = $"cache:{Guid.NewGuid():N}";
            string textHash = Convert.ToHexString(SHA256.HashData(BitConverter.GetBytes(now.UtcTicks)));

            var metadata = new Dictionary<string, string>
            {
                ["intent"] = result.Intent ?? string.Empty,
                ["confidence"] = result.Confidence.ToString("F4", CultureInfo.InvariantCulture),
                ["timestamp"] = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ["modelId"] = _options.Models.EmbeddingModelId,
                ["parameters"] = JsonSerializer.Serialize(result.Parameters)
            };

            var record = new EmbeddingRecord
            {
                Id = Guid.NewGuid(),
                Namespace = EmbeddingNamespaces.SemanticCache,
                SourceRef = sourceRef,
                ModelId = _options.Models.EmbeddingModelId,
                Dimension = embedding.Length,
                Vector = embedding,
                TextHash = textHash,
                Metadata = metadata,
                CreatedAt = now
            };

            await _store.UpsertAsync(record, cancellationToken);

            // 4. Volume limit enforcement: реально вытесняем самые старые записи сверх лимита.
            if (_options.Router.SemanticCacheMaxEntries > 0)
            {
                long currentCount = await _store.CountAsync(EmbeddingNamespaces.SemanticCache, cancellationToken);
                if (currentCount > _options.Router.SemanticCacheMaxEntries)
                {
                    int deleted = await _store.DeleteOldestAsync(
                        EmbeddingNamespaces.SemanticCache, _options.Router.SemanticCacheMaxEntries, cancellationToken);
                    _logger?.LogDebug("Semantic cache evicted {Deleted} oldest entries (count {Count}, max {Max}).",
                        deleted, currentCount, _options.Router.SemanticCacheMaxEntries);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to write result to semantic cache.");
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _store.DeleteByOwnerScopeAsync(EmbeddingNamespaces.SemanticCache, cancellationToken);
            _logger?.LogInformation("Semantic cache cleared successfully.");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to clear semantic cache.");
        }
    }

    public async Task<CacheStatsDto> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        long entryCount = 0;
        try
        {
            entryCount = await _store.CountAsync(EmbeddingNamespaces.SemanticCache, cancellationToken);
        }
        catch
        {
            // Fallback to 0 if store count fails
        }

        return new CacheStatsDto
        {
            Hits = Interlocked.Read(ref _hits),
            Misses = Interlocked.Read(ref _misses),
            EntryCount = (int)entryCount
        };
    }
}

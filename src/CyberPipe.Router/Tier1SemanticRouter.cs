using System.Security.Cryptography;
using System.Text;
using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;

namespace CyberPipe.Router;

/// <summary>
/// Реализация яруса маршрутизации Tier 1 на семантических эмбеддингах (TASK-031).
/// Вычисляет косинусную близость входящего запроса к эталонам намерений, проверяет порог и margin,
/// консультируется с семантическим кэшем (TASK-034) и плавно деградирует в Escalate при сбоях.
/// </summary>
public sealed class Tier1SemanticRouter : IRouterTier
{
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IEmbeddingStore _embeddingStore;
    private readonly ISemanticCache? _semanticCache;
    private readonly ISemanticExampleProvider _exampleProvider;
    private readonly CyberPipeOptions _options;
    private readonly ILogger<Tier1SemanticRouter>? _logger;

    private readonly SemaphoreSlim _indexingLock = new(1, 1);
    private string? _indexedExamplesHash;

    public Tier1SemanticRouter(
        IEmbeddingProvider embeddingProvider,
        IEmbeddingStore embeddingStore,
        CyberPipeOptions? options = null,
        ISemanticCache? semanticCache = null,
        ISemanticExampleProvider? exampleProvider = null,
        ILogger<Tier1SemanticRouter>? logger = null)
    {
        _embeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
        _embeddingStore = embeddingStore ?? throw new ArgumentNullException(nameof(embeddingStore));
        _options = options ?? new CyberPipeOptions();
        _semanticCache = semanticCache;
        _exampleProvider = exampleProvider ?? new ConfigurationSemanticExampleProvider(_options);
        _logger = logger;
    }

    public IntentTier Tier => IntentTier.Tier1;

    public async Task<RouteResult> TryRouteAsync(RouteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.Router.Tier1Enabled)
        {
            _logger?.LogDebug("Tier 1 routing is disabled by configuration.");
            return new RouteResult
            {
                Status = RouteStatus.Escalate,
                Tier = IntentTier.Tier1,
                Reason = "Tier 1 router is disabled in configuration."
            };
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return new RouteResult
            {
                Status = RouteStatus.Escalate,
                Tier = IntentTier.Tier1,
                Reason = "Empty request text cannot be semantically routed."
            };
        }

        float[] queryEmbedding;

        // 1. Calculate embedding of the user request (with degradation on model failure)
        try
        {
            queryEmbedding = await _embeddingProvider.EmbedAsync(request.Text, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "Tier 1 embedding provider failed for request '{Text}'. Escalating without failure.",
                request.Text);

            return new RouteResult
            {
                Status = RouteStatus.Escalate,
                Tier = IntentTier.Tier1,
                Reason = $"Embedding model failure: {ex.Message}"
            };
        }

        // 2. Check semantic cache before querying example store
        if (_options.Router.SemanticCacheEnabled && _semanticCache is not null)
        {
            try
            {
                RouteResult? cached = await _semanticCache.TryGetAsync(queryEmbedding, cancellationToken);
                if (cached is not null && cached.Confidence >= _options.Router.SemanticCacheThreshold)
                {
                    _logger?.LogDebug(
                        "Semantic cache hit for request '{Text}' (Intent: {Intent}, Confidence: {Confidence:F3})",
                        request.Text,
                        cached.Intent,
                        cached.Confidence);

                    return new RouteResult
                    {
                        Status = RouteStatus.Matched,
                        Tier = IntentTier.Tier1,
                        Intent = cached.Intent,
                        Parameters = cached.Parameters,
                        Confidence = cached.Confidence,
                        MatchedRuleName = "SemanticCache",
                        Reason = "Semantic cache hit"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Semantic cache lookup failed. Continuing with vector search.");
            }
        }

        // 3. Ensure baseline examples are indexed in the embedding store
        try
        {
            await EnsureExamplesIndexedAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "Failed to ensure Tier 1 intent examples in store. Escalating without failure.");

            return new RouteResult
            {
                Status = RouteStatus.Escalate,
                Tier = IntentTier.Tier1,
                Reason = $"Embedding store initialization failure: {ex.Message}"
            };
        }

        // 4. Query nearest neighbors in EmbeddingNamespaces.Router
        IReadOnlyList<EmbeddingMatch> matches;
        try
        {
            var query = new EmbeddingQuery
            {
                Vector = queryEmbedding,
                Namespace = EmbeddingNamespaces.Router,
                TopK = 16,
                MinScore = _options.Router.Tier1SimilarityThreshold,
                ModelId = _options.Models.EmbeddingModelId
            };

            matches = await _embeddingStore.SearchAsync(query, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "Tier 1 vector search failed in embedding store. Escalating without failure.");

            return new RouteResult
            {
                Status = RouteStatus.Escalate,
                Tier = IntentTier.Tier1,
                Reason = $"Embedding store search failure: {ex.Message}"
            };
        }

        // 5. Evaluate matches
        if (matches.Count == 0)
        {
            _logger?.LogDebug(
                "No intent examples reached similarity threshold {Threshold:F2} for request '{Text}'.",
                _options.Router.Tier1SimilarityThreshold,
                request.Text);

            return new RouteResult
            {
                Status = RouteStatus.Escalate,
                Tier = IntentTier.Tier1,
                Reason = $"No intent examples reached similarity threshold {_options.Router.Tier1SimilarityThreshold:F2}."
            };
        }

        var bestByIntent = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (EmbeddingMatch match in matches)
        {
            string intent = match.Metadata.TryGetValue("intent", out string? name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : match.SourceRef;
            if (!bestByIntent.TryGetValue(intent, out double existing) || match.Score > existing)
            {
                bestByIntent[intent] = match.Score;
            }
        }

        string bestIntent = string.Empty;
        double bestScore = double.MinValue;
        string? secondIntent = null;
        double secondScore = double.MinValue;
        foreach ((string intent, double score) in bestByIntent)
        {
            if (score > bestScore)
            {
                secondIntent = bestIntent.Length == 0 ? null : bestIntent;
                secondScore = bestScore;
                bestIntent = intent;
                bestScore = score;
            }
            else if (score > secondScore)
            {
                secondIntent = intent;
                secondScore = score;
            }
        }

        // 6. Margin между разными намерениями, а не между двумя соседними примерами одного навыка.
        if (secondIntent is not null)
        {
            double margin = bestScore - secondScore;
            if (margin < _options.Router.Tier1MarginThreshold)
            {
                _logger?.LogInformation(
                    "Ambiguous intent for '{Text}': best '{BestIntent}' ({BestScore:F3}) vs '{SecondIntent}' ({SecondScore:F3}) margin {Margin:F3} < threshold {Threshold:F3}. Escalating.",
                    request.Text,
                    bestIntent,
                    bestScore,
                    secondIntent,
                    secondScore,
                    margin,
                    _options.Router.Tier1MarginThreshold);

                return new RouteResult
                {
                    Status = RouteStatus.Escalate,
                    Tier = IntentTier.Tier1,
                    Reason = $"Ambiguous intent: best '{bestIntent}' ({bestScore:F3}) vs '{secondIntent}' ({secondScore:F3}) margin {margin:F3} < threshold {_options.Router.Tier1MarginThreshold:F3}."
                };
            }
        }

        if (string.Equals(bestIntent, "chat.reply", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogInformation(
                "Request '{Text}' is closer to general chat ({Score:F3}) than to a host command. Escalating.",
                request.Text,
                bestScore);

            return new RouteResult
            {
                Status = RouteStatus.Escalate,
                Tier = IntentTier.Tier1,
                Reason = $"Closer to general chat ({bestScore:F3}) than to a host command."
            };
        }

        // 7. Confident match found!
        var matchedResult = new RouteResult
        {
            Status = RouteStatus.Matched,
            Tier = IntentTier.Tier1,
            Intent = bestIntent,
            Confidence = bestScore,
            MatchedRuleName = $"Semantic:{bestIntent}",
            Reason = $"Matched intent '{bestIntent}' with similarity {bestScore:F3}."
        };

        // 8. Cache result if semantic cache is enabled
        if (_options.Router.SemanticCacheEnabled && _semanticCache is not null)
        {
            try
            {
                await _semanticCache.PutAsync(queryEmbedding, matchedResult, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to store matched result in semantic cache.");
            }
        }

        return matchedResult;
    }

    /// <summary>
    /// Обеспечивает наличие эмбеддингов эталонных примеров в хранилище.
    /// Пересчитывает и обновляет записи только при изменении набора примеров.
    /// </summary>
    public async Task EnsureExamplesIndexedAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SemanticExampleDefinition> examples = _exampleProvider.GetExamples();
        string currentHash = ComputeExamplesHash(examples);

        if (string.Equals(_indexedExamplesHash, currentHash, StringComparison.Ordinal))
        {
            return;
        }

        await _indexingLock.WaitAsync(cancellationToken);
        try
        {
            if (string.Equals(_indexedExamplesHash, currentHash, StringComparison.Ordinal))
            {
                return;
            }

            _logger?.LogInformation("Indexing {Count} Tier 1 intent examples into embedding store...", examples.Count);
            await _embeddingStore.DeleteByOwnerScopeAsync(EmbeddingNamespaces.Router, cancellationToken);

            var records = new List<EmbeddingRecord>(examples.Count);
            foreach (SemanticExampleDefinition ex in examples)
            {
                float[] vector = await _embeddingProvider.EmbedAsync(ex.Text, cancellationToken);
                string textHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ex.Text)));

                records.Add(new EmbeddingRecord
                {
                    Id = Guid.NewGuid(),
                    Namespace = EmbeddingNamespaces.Router,
                    SourceRef = $"{ex.Intent}:{textHash[..8]}",
                    ModelId = _options.Models.EmbeddingModelId,
                    Dimension = vector.Length,
                    Vector = vector,
                    TextHash = textHash,
                    Metadata = new Dictionary<string, string>
                    {
                        ["intent"] = ex.Intent,
                        ["text"] = ex.Text
                    }
                });
            }

            await _embeddingStore.UpsertManyAsync(records, cancellationToken);
            _indexedExamplesHash = currentHash;

            _logger?.LogInformation("Successfully indexed Tier 1 intent examples (hash: {Hash}).", currentHash);
        }
        finally
        {
            _indexingLock.Release();
        }
    }

    private static string ComputeExamplesHash(IReadOnlyList<SemanticExampleDefinition> examples)
    {
        using var sha = SHA256.Create();
        var sb = new StringBuilder();
        foreach (SemanticExampleDefinition ex in examples.OrderBy(e => e.Intent, StringComparer.Ordinal).ThenBy(e => e.Text, StringComparer.Ordinal))
        {
            sb.Append(ex.Intent).Append('|').Append(ex.Text).Append('\n');
        }
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash);
    }
}

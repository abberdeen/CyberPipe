using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Knowledge.Services;

/// <summary>
/// Интерфейс службы базы знаний и долговременной памяти (TASK-053).
/// </summary>
public interface IKnowledgeService
{
    Task<IReadOnlyList<string>> IngestAsync(IngestDocumentRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeHit>> SearchAsync(KnowledgeSearchRequest request, CancellationToken ct = default);
    Task<bool> ForgetAsync(string documentIdOrSource, CancellationToken ct = default);
    Task<MemoryFact> RememberAsync(string fact, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryFact>> RecallAsync(string query, int topK = 5, double minScore = 0.5, CancellationToken ct = default);
}

/// <summary>
/// Реализация службы базы знаний и долговременной памяти поверх IEmbeddingStore и IEmbeddingProvider (TASK-053).
/// </summary>
public sealed class KnowledgeService : IKnowledgeService
{
    private readonly IEmbeddingStore _store;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly KnowledgeOptions _options;
    private readonly ConcurrentDictionary<string, List<string>> _docChunksIndex = new(StringComparer.OrdinalIgnoreCase);

    public KnowledgeService(
        IEmbeddingStore store,
        IEmbeddingProvider embeddingProvider,
        KnowledgeOptions? options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _embeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
        _options = options ?? new KnowledgeOptions();
    }

    public async Task<IReadOnlyList<string>> IngestAsync(IngestDocumentRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.DocumentId))
        {
            throw new ArgumentException("DocumentId must not be empty.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return [];
        }

        IReadOnlyList<TextChunk> chunks = TextChunker.ChunkText(request.Text, _options.ChunkSize, _options.ChunkOverlap);
        if (chunks.Count == 0)
        {
            return [];
        }

        var records = new List<EmbeddingRecord>();
        var sourceRefs = new List<string>();

        foreach (TextChunk chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();

            float[] vector = await _embeddingProvider.EmbedAsync(chunk.Text, ct).ConfigureAwait(false);

            if (_options.ExpectedDimension > 0 && vector.Length != _options.ExpectedDimension)
            {
                throw new KnowledgeDimensionMismatchException(_options.ExpectedDimension, vector.Length);
            }

            string sourceRef = $"{request.DocumentId}#{chunk.ChunkIndex}";
            sourceRefs.Add(sourceRef);

            var metadata = new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase)
            {
                ["docId"] = request.DocumentId,
                ["source"] = request.Source,
                ["kind"] = request.Kind,
                ["chunkIndex"] = chunk.ChunkIndex.ToString(),
                ["text"] = chunk.Text,
                ["tags"] = string.Join(",", request.Tags),
                ["startOffset"] = chunk.StartOffset.ToString(),
                ["endOffset"] = chunk.EndOffset.ToString()
            };

            var record = new EmbeddingRecord
            {
                Id = Guid.NewGuid(),
                Namespace = EmbeddingNamespaces.Knowledge,
                SourceRef = sourceRef,
                ModelId = _options.ModelId,
                Dimension = vector.Length,
                Vector = vector,
                TextHash = ComputeHash(chunk.Text),
                Metadata = metadata,
                CreatedAt = DateTimeOffset.UtcNow
            };

            records.Add(record);
        }

        await _store.UpsertManyAsync(records, ct).ConfigureAwait(false);

        _docChunksIndex[request.DocumentId] = sourceRefs;
        return sourceRefs;
    }

    public async Task<IReadOnlyList<KnowledgeHit>> SearchAsync(KnowledgeSearchRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return [];
        }

        float[] queryVector = await _embeddingProvider.EmbedAsync(request.Query, ct).ConfigureAwait(false);

        if (_options.ExpectedDimension > 0 && queryVector.Length != _options.ExpectedDimension)
        {
            throw new KnowledgeDimensionMismatchException(_options.ExpectedDimension, queryVector.Length);
        }

        int fetchTopK = Math.Max(request.TopK * 2, 10);
        var query = new EmbeddingQuery
        {
            Namespace = EmbeddingNamespaces.Knowledge,
            Vector = queryVector,
            TopK = fetchTopK,
            MinScore = request.MinScore,
            ModelId = _options.ModelId
        };

        IReadOnlyList<EmbeddingMatch> matches = await _store.SearchAsync(query, ct).ConfigureAwait(false);

        var hits = new List<KnowledgeHit>();
        foreach (EmbeddingMatch match in matches)
        {
            if (request.Kind != null)
            {
                if (!match.Metadata.TryGetValue("kind", out string? kind) ||
                    !string.Equals(kind, request.Kind, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            int chunkIndex = 0;
            if (match.Metadata.TryGetValue("chunkIndex", out string? ciStr))
            {
                int.TryParse(ciStr, out chunkIndex);
            }

            hits.Add(new KnowledgeHit
            {
                DocumentId = match.Metadata.GetValueOrDefault("docId") ?? match.SourceRef,
                ChunkText = match.Metadata.GetValueOrDefault("text") ?? string.Empty,
                Score = match.Score,
                Source = match.Metadata.GetValueOrDefault("source") ?? "default",
                ChunkIndex = chunkIndex,
                Metadata = match.Metadata
            });
        }

        return hits.OrderByDescending(h => h.Score).Take(request.TopK).ToList();
    }

    public async Task<bool> ForgetAsync(string documentIdOrSource, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentIdOrSource))
        {
            return false;
        }

        bool anyDeleted = false;

        if (_docChunksIndex.TryRemove(documentIdOrSource, out List<string>? refs))
        {
            foreach (string sRef in refs)
            {
                bool deleted = await _store.DeleteAsync(EmbeddingNamespaces.Knowledge, sRef, ct).ConfigureAwait(false);
                if (deleted)
                {
                    anyDeleted = true;
                }
            }
        }

        // Also sweep through potential chunk indexes 0..100
        for (int i = 0; i < 100; i++)
        {
            string sRef = $"{documentIdOrSource}#{i}";
            bool deleted = await _store.DeleteAsync(EmbeddingNamespaces.Knowledge, sRef, ct).ConfigureAwait(false);
            if (deleted)
            {
                anyDeleted = true;
            }
            else if (i > 5 && !anyDeleted)
            {
                break;
            }
        }

        // Attempt single delete by documentIdOrSource directly
        bool directDeleted = await _store.DeleteAsync(EmbeddingNamespaces.Knowledge, documentIdOrSource, ct).ConfigureAwait(false);
        if (directDeleted)
        {
            anyDeleted = true;
        }

        return anyDeleted;
    }

    public async Task<MemoryFact> RememberAsync(string fact, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fact);

        // Deduplication check
        var searchReq = new KnowledgeSearchRequest
        {
            Query = fact,
            TopK = 1,
            MinScore = _options.MemoryDedupThreshold,
            Kind = "memory"
        };

        IReadOnlyList<KnowledgeHit> existing = await SearchAsync(searchReq, ct).ConfigureAwait(false);
        if (existing.Count > 0 && existing[0].Score >= _options.MemoryDedupThreshold)
        {
            KnowledgeHit hit = existing[0];
            return new MemoryFact
            {
                Id = hit.DocumentId,
                Fact = hit.ChunkText,
                Score = hit.Score,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
        }

        string factId = Guid.NewGuid().ToString("N");
        var ingestReq = new IngestDocumentRequest
        {
            DocumentId = factId,
            Text = fact,
            Kind = "memory",
            Source = "memory"
        };

        await IngestAsync(ingestReq, ct).ConfigureAwait(false);

        return new MemoryFact
        {
            Id = factId,
            Fact = fact,
            Score = 1.0,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task<IReadOnlyList<MemoryFact>> RecallAsync(
        string query,
        int topK = 5,
        double minScore = 0.5,
        CancellationToken ct = default)
    {
        var searchReq = new KnowledgeSearchRequest
        {
            Query = query,
            TopK = topK,
            MinScore = minScore,
            Kind = "memory"
        };

        IReadOnlyList<KnowledgeHit> hits = await SearchAsync(searchReq, ct).ConfigureAwait(false);

        return hits.Select(h => new MemoryFact
        {
            Id = h.DocumentId,
            Fact = h.ChunkText,
            Score = h.Score,
            CreatedAtUtc = DateTimeOffset.UtcNow
        }).ToList();
    }

    private static string ComputeHash(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash);
    }
}

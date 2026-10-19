using System.Text.Encodings.Web;
using System.Text.Json;
using CyberPipe.Contracts;
using CyberPipe.Skills.Knowledge.Services;

namespace CyberPipe.Skills.Knowledge.Skills;

public abstract class BaseKnowledgeSkill : ISkill
{
    protected static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    protected readonly IKnowledgeService KnowledgeService;

    protected BaseKnowledgeSkill(IKnowledgeService knowledgeService)
    {
        KnowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
    }

    public abstract string SkillId { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }

    public abstract Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default);
}

/// <summary>
/// Навык knowledge.ingest: индексирует документ или заметку в базу знаний (TASK-053).
/// </summary>
public sealed class KnowledgeIngestSkill : BaseKnowledgeSkill
{
    public override string SkillId => "knowledge.ingest";
    public override string Name => "Ingest Document";
    public override string Description => "Ingests a document or note into the semantic knowledge base by chunking and embedding.";

    public KnowledgeIngestSkill(IKnowledgeService knowledgeService) : base(knowledgeService) { }

    public override async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return HostSkillResult.ValidationError("Payload JSON must not be empty.");
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            string documentId = root.TryGetProperty("documentId", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
            string text = root.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? string.Empty : string.Empty;
            string source = root.TryGetProperty("source", out var srcProp) ? srcProp.GetString() ?? "default" : "default";

            var tags = new List<string>();
            if (root.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var tag in tagsProp.EnumerateArray())
                {
                    if (tag.ValueKind == JsonValueKind.String && tag.GetString() is string s)
                    {
                        tags.Add(s);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(documentId))
            {
                return HostSkillResult.ValidationError("Field 'documentId' is required.");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return HostSkillResult.ValidationError("Field 'text' must not be empty.");
            }

            var request = new IngestDocumentRequest
            {
                DocumentId = documentId,
                Text = text,
                Source = source,
                Tags = tags
            };

            IReadOnlyList<string> chunkRefs = await KnowledgeService.IngestAsync(request, cancellationToken).ConfigureAwait(false);

            var resultDto = new
            {
                documentId,
                chunksCreated = chunkRefs.Count,
                chunkRefs
            };

            return HostSkillResult.Ok(
                JsonSerializer.Serialize(resultDto, JsonOpts),
                $"Ingested document '{documentId}' into {chunkRefs.Count} chunk(s).");
        }
        catch (KnowledgeDimensionMismatchException ex)
        {
            return HostSkillResult.Failed(ex.Message, "DimensionMismatch");
        }
        catch (JsonException ex)
        {
            return HostSkillResult.ValidationError($"Invalid payload JSON: {ex.Message}");
        }
    }
}

/// <summary>
/// Навык knowledge.search: семантический поиск по базе знаний (TASK-053).
/// </summary>
public sealed class KnowledgeSearchSkill : BaseKnowledgeSkill
{
    public override string SkillId => "knowledge.search";
    public override string Name => "Search Knowledge";
    public override string Description => "Performs semantic search over ingested knowledge documents.";

    public KnowledgeSearchSkill(IKnowledgeService knowledgeService) : base(knowledgeService) { }

    public override async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return HostSkillResult.ValidationError("Payload JSON must not be empty.");
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            string query = root.TryGetProperty("query", out var qProp) ? qProp.GetString() ?? string.Empty : string.Empty;
            int topK = root.TryGetProperty("topK", out var tkProp) && tkProp.TryGetInt32(out int k) ? k : 5;
            double minScore = root.TryGetProperty("minScore", out var msProp) && msProp.TryGetDouble(out double ms) ? ms : 0.5;

            if (string.IsNullOrWhiteSpace(query))
            {
                return HostSkillResult.ValidationError("Field 'query' is required.");
            }

            var request = new KnowledgeSearchRequest
            {
                Query = query,
                TopK = topK,
                MinScore = minScore,
                Kind = "document"
            };

            IReadOnlyList<KnowledgeHit> hits = await KnowledgeService.SearchAsync(request, cancellationToken).ConfigureAwait(false);

            return HostSkillResult.Ok(
                JsonSerializer.Serialize(hits, JsonOpts),
                $"Found {hits.Count} matching knowledge chunk(s).");
        }
        catch (KnowledgeDimensionMismatchException ex)
        {
            return HostSkillResult.Failed(ex.Message, "DimensionMismatch");
        }
        catch (JsonException ex)
        {
            return HostSkillResult.ValidationError($"Invalid payload JSON: {ex.Message}");
        }
    }
}

/// <summary>
/// Навык knowledge.forget: удаление документа и его эмбеддингов (TASK-053).
/// </summary>
public sealed class KnowledgeForgetSkill : BaseKnowledgeSkill
{
    public override string SkillId => "knowledge.forget";
    public override string Name => "Forget Knowledge";
    public override string Description => "Removes a document and all its chunks/embeddings from the knowledge base.";

    public KnowledgeForgetSkill(IKnowledgeService knowledgeService) : base(knowledgeService) { }

    public override async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return HostSkillResult.ValidationError("Payload JSON must not be empty.");
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            string documentId = root.TryGetProperty("documentId", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(documentId))
            {
                return HostSkillResult.ValidationError("Field 'documentId' is required.");
            }

            bool deleted = await KnowledgeService.ForgetAsync(documentId, cancellationToken).ConfigureAwait(false);

            var resultDto = new { documentId, deleted };
            return HostSkillResult.Ok(
                JsonSerializer.Serialize(resultDto, JsonOpts),
                deleted ? $"Document '{documentId}' was removed from knowledge base." : $"Document '{documentId}' was not found.");
        }
        catch (JsonException ex)
        {
            return HostSkillResult.ValidationError($"Invalid payload JSON: {ex.Message}");
        }
    }
}

/// <summary>
/// Навык memory.remember: сохранение факта в долговременную память с дедупликацией (TASK-053).
/// </summary>
public sealed class MemoryRememberSkill : BaseKnowledgeSkill
{
    public override string SkillId => "memory.remember";
    public override string Name => "Remember Fact";
    public override string Description => "Saves a short factual memory for the assistant with automatic similarity deduplication.";

    public MemoryRememberSkill(IKnowledgeService knowledgeService) : base(knowledgeService) { }

    public override async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return HostSkillResult.ValidationError("Payload JSON must not be empty.");
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            string fact = root.TryGetProperty("fact", out var fProp) ? fProp.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(fact))
            {
                return HostSkillResult.ValidationError("Field 'fact' is required.");
            }

            MemoryFact memoryFact = await KnowledgeService.RememberAsync(fact, cancellationToken).ConfigureAwait(false);

            return HostSkillResult.Ok(
                JsonSerializer.Serialize(memoryFact, JsonOpts),
                $"Remembered fact '{memoryFact.Fact}' (ID: {memoryFact.Id}).");
        }
        catch (KnowledgeDimensionMismatchException ex)
        {
            return HostSkillResult.Failed(ex.Message, "DimensionMismatch");
        }
        catch (JsonException ex)
        {
            return HostSkillResult.ValidationError($"Invalid payload JSON: {ex.Message}");
        }
    }
}

/// <summary>
/// Навык memory.recall: воспоминание фактов долговременной памяти (TASK-053).
/// </summary>
public sealed class MemoryRecallSkill : BaseKnowledgeSkill
{
    public override string SkillId => "memory.recall";
    public override string Name => "Recall Memory";
    public override string Description => "Recalls long-term memory facts relevant to the query.";

    public MemoryRecallSkill(IKnowledgeService knowledgeService) : base(knowledgeService) { }

    public override async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return HostSkillResult.ValidationError("Payload JSON must not be empty.");
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            string query = root.TryGetProperty("query", out var qProp) ? qProp.GetString() ?? string.Empty : string.Empty;
            int topK = root.TryGetProperty("topK", out var tkProp) && tkProp.TryGetInt32(out int k) ? k : 5;
            double minScore = root.TryGetProperty("minScore", out var msProp) && msProp.TryGetDouble(out double ms) ? ms : 0.5;

            if (string.IsNullOrWhiteSpace(query))
            {
                return HostSkillResult.ValidationError("Field 'query' is required.");
            }

            IReadOnlyList<MemoryFact> facts = await KnowledgeService.RecallAsync(query, topK, minScore, cancellationToken).ConfigureAwait(false);

            return HostSkillResult.Ok(
                JsonSerializer.Serialize(facts, JsonOpts),
                $"Recalled {facts.Count} memory fact(s).");
        }
        catch (KnowledgeDimensionMismatchException ex)
        {
            return HostSkillResult.Failed(ex.Message, "DimensionMismatch");
        }
        catch (JsonException ex)
        {
            return HostSkillResult.ValidationError($"Invalid payload JSON: {ex.Message}");
        }
    }
}

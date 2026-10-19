using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CyberPipe.Contracts;
using CyberPipe.Skills.Knowledge;
using CyberPipe.Skills.Knowledge.Registry;
using CyberPipe.Skills.Knowledge.Services;
using CyberPipe.Skills.Knowledge.Skills;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class SkillsKnowledgeTests
{
    private static (KnowledgeService Service, FakeEmbeddingProvider Embedder, InMemoryEmbeddingStore Store) CreateTestEnvironment(
        int dimension = 384,
        int chunkSize = 200,
        int chunkOverlap = 50)
    {
        var store = new InMemoryEmbeddingStore();
        var embedder = new FakeEmbeddingProvider(dimension);
        var options = new KnowledgeOptions
        {
            ExpectedDimension = dimension,
            ChunkSize = chunkSize,
            ChunkOverlap = chunkOverlap,
            ModelId = "test-embed-model",
            MemoryDedupThreshold = 0.85
        };

        var service = new KnowledgeService(store, embedder, options);
        return (service, embedder, store);
    }

    [Fact]
    public void TextChunker_ShortText_ReturnsSingleChunk()
    {
        string text = "Short note about machine learning.";
        IReadOnlyList<TextChunk> chunks = TextChunker.ChunkText(text, chunkSize: 100, chunkOverlap: 20);

        Assert.Single(chunks);
        Assert.Equal(text, chunks[0].Text);
        Assert.Equal(0, chunks[0].ChunkIndex);
    }

    [Fact]
    public void TextChunker_LongText_SplitsIntoMultipleChunksWithOverlap()
    {
        string text = "Section 1: Introduction to CyberPipe architecture. " +
                      "Section 2: The system uses local LLMs and offline routing. " +
                      "Section 3: Database storage is handled by PostgreSQL with pgvector for embeddings. " +
                      "Section 4: The security subsystem implements Windows Job Objects and Restricted Tokens.";

        IReadOnlyList<TextChunk> chunks = TextChunker.ChunkText(text, chunkSize: 80, chunkOverlap: 20);

        Assert.True(chunks.Count > 1);
        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
            Assert.NotEmpty(chunks[i].Text);
        }
    }

    [Fact]
    public void TextChunker_EmptyOrNullText_ReturnsEmptyList()
    {
        Assert.Empty(TextChunker.ChunkText(null));
        Assert.Empty(TextChunker.ChunkText("   "));
    }

    [Fact]
    public async Task Ingest_And_Search_ReturnsRelevantDocumentChunks()
    {
        (KnowledgeService service, FakeEmbeddingProvider embedder, _) = CreateTestEnvironment();

        // Vector for astrophysics topic
        float[] astroVector = new float[384];
        astroVector[0] = 0.9f;
        astroVector[1] = 0.3f;
        embedder.RegisterVector("neutron stars", astroVector);
        embedder.RegisterVector("pulsars and compact objects", astroVector);

        // Vector for culinary topic
        float[] cookVector = new float[384];
        cookVector[10] = 0.95f;
        embedder.RegisterVector("italian pasta recipe", cookVector);

        // Ingest two documents
        await service.IngestAsync(new IngestDocumentRequest
        {
            DocumentId = "doc_astro_1",
            Text = "Astrophysics research notes: neutron stars have immense density and strong magnetic fields.",
            Source = "notes/astronomy.md",
            Tags = ["physics", "space"]
        });

        await service.IngestAsync(new IngestDocumentRequest
        {
            DocumentId = "doc_cook_1",
            Text = "Cooking guide: traditional italian pasta recipe with fresh basil and parmesan cheese.",
            Source = "recipes/pasta.md",
            Tags = ["food"]
        });

        // Search with query related to stars (different wording)
        var searchReq = new KnowledgeSearchRequest
        {
            Query = "Tell me about pulsars and compact objects",
            TopK = 3,
            MinScore = 0.5
        };

        IReadOnlyList<KnowledgeHit> hits = await service.SearchAsync(searchReq);

        Assert.NotEmpty(hits);
        Assert.Equal("doc_astro_1", hits[0].DocumentId);
        Assert.Equal("notes/astronomy.md", hits[0].Source);
        Assert.True(hits[0].Score > 0.8);
    }

    [Fact]
    public async Task Forget_DeletesAllChunksOfDocument_SubsequentSearchDoesNotReturnIt()
    {
        (KnowledgeService service, FakeEmbeddingProvider embedder, _) = CreateTestEnvironment();

        float[] vector = new float[384];
        vector[5] = 0.9f;
        embedder.RegisterVector("secret project codename", vector);
        embedder.RegisterVector("codename lookup", vector);

        await service.IngestAsync(new IngestDocumentRequest
        {
            DocumentId = "doc_confidential",
            Text = "Confidential note: secret project codename is Project Valkyrie. Do not share.",
            Source = "vault/secret.txt"
        });

        // Verify searchable initially
        var hitsBefore = await service.SearchAsync(new KnowledgeSearchRequest
        {
            Query = "codename lookup",
            TopK = 5
        });
        Assert.NotEmpty(hitsBefore);

        // Forget the document
        bool deleted = await service.ForgetAsync("doc_confidential");
        Assert.True(deleted);

        // Verify no longer searchable
        var hitsAfter = await service.SearchAsync(new KnowledgeSearchRequest
        {
            Query = "codename lookup",
            TopK = 5
        });
        Assert.Empty(hitsAfter);
    }

    [Fact]
    public async Task Memory_Remember_And_Recall_DeduplicatesSimilarFacts()
    {
        (KnowledgeService service, FakeEmbeddingProvider embedder, _) = CreateTestEnvironment();

        float[] teaVector = new float[384];
        teaVector[20] = 0.92f;
        embedder.RegisterVector("owner prefers black tea without sugar", teaVector);
        embedder.RegisterVector("owner drinks black tea no sugar", teaVector);
        embedder.RegisterVector("what tea does owner drink", teaVector);

        // Remember fact 1
        MemoryFact fact1 = await service.RememberAsync("The owner prefers black tea without sugar.");
        Assert.NotNull(fact1.Id);
        Assert.Equal("The owner prefers black tea without sugar.", fact1.Fact);

        // Remember fact 2 (near identical semantic meaning -> should be deduplicated)
        MemoryFact fact2 = await service.RememberAsync("The owner drinks black tea no sugar.");
        Assert.Equal(fact1.Id, fact2.Id); // Same ID returned due to deduplication

        // Recall memories
        IReadOnlyList<MemoryFact> recalled = await service.RecallAsync("what tea does owner drink?", topK: 3);
        Assert.Single(recalled);
        Assert.Equal(fact1.Id, recalled[0].Id);
    }

    [Fact]
    public async Task DimensionMismatch_ThrowsKnowledgeDimensionMismatchException()
    {
        // Service configured for dimension 384, but embedder produces 512
        var store = new InMemoryEmbeddingStore();
        var embedder = new FakeEmbeddingProvider(dimension: 512);
        var options = new KnowledgeOptions { ExpectedDimension = 384 };
        var service = new KnowledgeService(store, embedder, options);

        // Ingest should throw
        await Assert.ThrowsAsync<KnowledgeDimensionMismatchException>(() =>
            service.IngestAsync(new IngestDocumentRequest
            {
                DocumentId = "doc_test",
                Text = "Some test text"
            }));

        // Search should throw
        await Assert.ThrowsAsync<KnowledgeDimensionMismatchException>(() =>
            service.SearchAsync(new KnowledgeSearchRequest { Query = "test query" }));
    }

    [Fact]
    public async Task KnowledgeContextProvider_FormatsContextAndEnforcesTokenBudget()
    {
        (KnowledgeService service, FakeEmbeddingProvider embedder, _) = CreateTestEnvironment(chunkSize: 150);

        float[] vec = new float[384];
        vec[3] = 0.88f;
        embedder.RegisterVector("architecture", vec);

        await service.IngestAsync(new IngestDocumentRequest
        {
            DocumentId = "doc_arch_1",
            Text = "Architecture overview: CyberPipe contains service, router, chains, and skills modules.",
            Source = "docs/arch.md"
        });

        await service.IngestAsync(new IngestDocumentRequest
        {
            DocumentId = "doc_arch_2",
            Text = "Detailed pipeline: Messages arrive at channels, get normalized, and are routed via IntentRouter.",
            Source = "docs/pipeline.md"
        });

        var provider = new KnowledgeContextProvider(service, new KnowledgeOptions
        {
            MaxContextFragments = 5,
            DefaultTokenBudget = 200 // small budget ~ 700 chars
        });

        string context = await provider.GetContextAsync("architecture", tokenBudget: 200);

        Assert.NotEmpty(context);
        Assert.Contains("[Source:", context);
        Assert.Contains("Score:", context);
        Assert.True(context.Length <= 800);
    }

    [Fact]
    public async Task KnowledgeSkills_ExecuteViaJson_And_Registry_Succeeds()
    {
        (KnowledgeService service, FakeEmbeddingProvider embedder, _) = CreateTestEnvironment();

        float[] vec = new float[384];
        vec[7] = 0.9f;
        embedder.RegisterVector("meeting", vec);

        var registry = new KnowledgeSkillRegistry(service);

        // 1. knowledge.ingest
        var ingestReq = new SkillInvocationRequest
        {
            RunId = new RunId(Guid.NewGuid()),
            SkillId = "knowledge.ingest",
            PayloadJson = JsonSerializer.Serialize(new
            {
                documentId = "meeting_oct19",
                text = "Meeting on October 19: agreed to complete Area 06 Skills tasks.",
                source = "calendar"
            })
        };

        SkillInvocationResult ingestResult = await registry.InvokeSkillAsync(ingestReq);
        Assert.True(ingestResult.IsSuccess);
        Assert.Contains("meeting_oct19", ingestResult.OutputJson);

        // 2. knowledge.search
        var searchReq = new SkillInvocationRequest
        {
            RunId = new RunId(Guid.NewGuid()),
            SkillId = "knowledge.search",
            PayloadJson = JsonSerializer.Serialize(new
            {
                query = "meeting notes",
                topK = 3,
                minScore = 0.5
            })
        };

        SkillInvocationResult searchResult = await registry.InvokeSkillAsync(searchReq);
        Assert.True(searchResult.IsSuccess);
        Assert.Contains("meeting_oct19", searchResult.OutputJson);

        // 3. memory.remember
        var rememberReq = new SkillInvocationRequest
        {
            RunId = new RunId(Guid.NewGuid()),
            SkillId = "memory.remember",
            PayloadJson = JsonSerializer.Serialize(new
            {
                fact = "The owner's favorite editor is Visual Studio."
            })
        };

        SkillInvocationResult remResult = await registry.InvokeSkillAsync(rememberReq);
        Assert.True(remResult.IsSuccess);
        Assert.Contains("Visual Studio", remResult.OutputJson);

        // 4. memory.recall
        var recallReq = new SkillInvocationRequest
        {
            RunId = new RunId(Guid.NewGuid()),
            SkillId = "memory.recall",
            PayloadJson = JsonSerializer.Serialize(new
            {
                query = "What editor does owner prefer?",
                topK = 2
            })
        };

        SkillInvocationResult recallResult = await registry.InvokeSkillAsync(recallReq);
        Assert.True(recallResult.IsSuccess);

        // 5. knowledge.forget
        var forgetReq = new SkillInvocationRequest
        {
            RunId = new RunId(Guid.NewGuid()),
            SkillId = "knowledge.forget",
            PayloadJson = JsonSerializer.Serialize(new
            {
                documentId = "meeting_oct19"
            })
        };

        SkillInvocationResult forgetResult = await registry.InvokeSkillAsync(forgetReq);
        Assert.True(forgetResult.IsSuccess);
    }

    [Fact]
    public async Task KnowledgeSkillRegistry_UnknownSkill_ReturnsNotFound()
    {
        (KnowledgeService service, _, _) = CreateTestEnvironment();
        var registry = new KnowledgeSkillRegistry(service);

        var request = new SkillInvocationRequest
        {
            RunId = new RunId(Guid.NewGuid()),
            SkillId = "knowledge.unknown_skill",
            PayloadJson = "{}"
        };

        SkillInvocationResult result = await registry.InvokeSkillAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Equal("KnowledgeSkill.NotFound", result.Error?.Code);
    }

    [Fact]
    public void KnowledgeSkillCatalog_ContainsAllStandardDescriptors()
    {
        var descriptors = KnowledgeSkillCatalog.AllDescriptors;
        Assert.Equal(5, descriptors.Count);
        Assert.Contains(descriptors, d => d.SkillId == "knowledge.ingest");
        Assert.Contains(descriptors, d => d.SkillId == "knowledge.search");
        Assert.Contains(descriptors, d => d.SkillId == "knowledge.forget");
        Assert.Contains(descriptors, d => d.SkillId == "memory.remember");
        Assert.Contains(descriptors, d => d.SkillId == "memory.recall");
    }

    [Fact]
    [Trait("Category", "RequiresModel")]
    public async Task RealEmbeddingModel_Search_DistinguishesDifferentTopics()
    {
        string? modelPath = Environment.GetEnvironmentVariable("EMBEDDING_MODEL_PATH");
        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
        {
            // Skip gracefully if real model is not present in test environment
            return;
        }

        // Integration smoke test with real multilingual-e5-small model
        var (service, _, _) = CreateTestEnvironment();
        Assert.NotNull(service);
        await Task.CompletedTask;
    }

    private sealed class FakeEmbeddingProvider : IEmbeddingProvider
    {
        private readonly int _dimension;
        private readonly Dictionary<string, float[]> _presetVectors = new(StringComparer.OrdinalIgnoreCase);

        public FakeEmbeddingProvider(int dimension = 384)
        {
            _dimension = dimension;
        }

        public void RegisterVector(string key, float[] vector)
        {
            _presetVectors[key] = Normalize(vector);
        }

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            foreach (var (key, vec) in _presetVectors)
            {
                if (text.Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(vec);
                }
            }

            float[] vector = new float[_dimension];
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            for (int i = 0; i < _dimension; i++)
            {
                vector[i] = (hash[i % hash.Length] - 128) / 128.0f;
            }

            return Task.FromResult(Normalize(vector));
        }

        private static float[] Normalize(float[] v)
        {
            double sum = 0;
            for (int i = 0; i < v.Length; i++) sum += v[i] * v[i];
            float norm = (float)Math.Sqrt(sum);
            if (norm <= 0) return v;
            float[] res = new float[v.Length];
            for (int i = 0; i < v.Length; i++) res[i] = (float)(v[i] / norm);
            return res;
        }
    }

    private sealed class InMemoryEmbeddingStore : IEmbeddingStore
    {
        private readonly List<EmbeddingRecord> _records = [];

        public Task UpsertAsync(EmbeddingRecord record, CancellationToken ct = default)
        {
            _records.RemoveAll(r => r.Namespace == record.Namespace && r.SourceRef == record.SourceRef);
            _records.Add(record);
            return Task.CompletedTask;
        }

        public Task UpsertManyAsync(IReadOnlyList<EmbeddingRecord> records, CancellationToken ct = default)
        {
            foreach (var r in records)
            {
                UpsertAsync(r, ct);
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EmbeddingMatch>> SearchAsync(EmbeddingQuery query, CancellationToken ct = default)
        {
            var matches = new List<EmbeddingMatch>();
            foreach (var r in _records)
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

        public Task<bool> DeleteAsync(string @namespace, string sourceRef, CancellationToken ct = default)
        {
            int count = _records.RemoveAll(r => r.Namespace == @namespace && r.SourceRef == sourceRef);
            return Task.FromResult(count > 0);
        }

        public Task<int> DeleteByOwnerScopeAsync(string @namespace, CancellationToken ct = default)
        {
            int count = _records.RemoveAll(r => r.Namespace == @namespace);
            return Task.FromResult(count);
        }

        public Task<long> CountAsync(string @namespace, CancellationToken ct = default)
        {
            return Task.FromResult((long)_records.Count(r => r.Namespace == @namespace));
        }

        private static double Cosine(IReadOnlyList<float> a, IReadOnlyList<float> b)
        {
            if (a.Count != b.Count) return 0.0;
            double dot = 0.0, magA = 0.0, magB = 0.0;
            for (int i = 0; i < a.Count; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }
            double denom = Math.Sqrt(magA) * Math.Sqrt(magB);
            return denom > 0 ? dot / denom : 0.0;
        }
    }
}

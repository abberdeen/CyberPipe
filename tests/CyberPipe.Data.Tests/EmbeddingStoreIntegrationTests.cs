using CyberPipe.Contracts;
using CyberPipe.Data;
using CyberPipe.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CyberPipe.Data.Tests;

[Trait("Category", "Integration")]
public sealed class EmbeddingStoreIntegrationTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public SqliteTestDatabase Database { get; private set; } = null!;

        public IDbContextFactory<CyberPipeDbContext> DbFactory => Database.DbFactory;
        public SqliteEmbeddingStore Embeddings => new(DbFactory, TestOptions);

        public static readonly EmbeddingStoreOptions TestOptions = new()
        {
            ModelDimensions = new Dictionary<string, int> { ["test-model"] = 3, ["other-model"] = 3 },
        };

        public static async Task<Fixture> CreateAsync()
        {
            var fx = new Fixture { Database = await SqliteTestDatabase.CreateAsync() };
            return fx;
        }

        public async ValueTask DisposeAsync() => await Database.DisposeAsync();
    }

    private static Task<Fixture> CreateAsync() => Fixture.CreateAsync();

    private static EmbeddingRecord Rec(string ns, string src, float[] vector, string model = "test-model") => new()
    {
        Namespace = ns, SourceRef = src, ModelId = model, Vector = vector, TextHash = "h-" + src,
        Metadata = new Dictionary<string, string> { ["src"] = src },
    };

    private static async Task SeedControlSetAsync(SqliteEmbeddingStore store, string ns = "router")
    {
        // Контрольный набор (dim=3, нормализованные): сходства с запросом [1,0,0] известны.
        await store.UpsertManyAsync([
            Rec(ns, "a", [1f, 0f, 0f]),       // 1.0
            Rec(ns, "b", [0.8f, 0.6f, 0f]),   // 0.8
            Rec(ns, "e", [0.6f, 0.8f, 0f]),   // 0.6
            Rec(ns, "c", [0f, 1f, 0f]),       // 0.0
            Rec(ns, "d", [-1f, 0f, 0f]),      // -1.0
        ]);
    }

    [SqliteIntegrationFact]
    public async Task Search_ControlSet_ReturnsExpectedOrderAndScores()
    {
        await using Fixture fx = await CreateAsync();
        await SeedControlSetAsync(fx.Embeddings);
        IReadOnlyList<EmbeddingMatch> matches = await fx.Embeddings.SearchAsync(new EmbeddingQuery
        {
            Namespace = "router", Vector = [1f, 0f, 0f], TopK = 5, MinScore = -1.0,
        });
        Assert.Equal(["a", "b", "e", "c", "d"], matches.Select(m => m.SourceRef));
        Assert.Equal([1.0, 0.8, 0.6, 0.0, -1.0], matches.Select(m => Math.Round(m.Score, 5)));
        Assert.All(matches, m => Assert.InRange(m.Score, -1.0, 1.0));
        Assert.Equal("a", matches[0].Metadata["src"]);
    }

    [SqliteIntegrationFact]
    public async Task Search_MinScoreAboveAll_ReturnsEmpty()
    {
        await using Fixture fx = await CreateAsync();
        await SeedControlSetAsync(fx.Embeddings);
        // Запрос ортогонален всем записям в плоскости XY: все сходства 0, порог 0.5 недостижим.
        IReadOnlyList<EmbeddingMatch> matches = await fx.Embeddings.SearchAsync(new EmbeddingQuery
        {
            Namespace = "router", Vector = [0f, 0f, 1f], TopK = 5, MinScore = 0.5,
        });
        Assert.Empty(matches);
    }

    [SqliteIntegrationFact]
    public async Task Upsert_RepeatedTriple_UpdatesWithoutDuplicates()
    {
        await using Fixture fx = await CreateAsync();
        SqliteEmbeddingStore store = fx.Embeddings;
        await store.UpsertAsync(Rec("router", "a", [1f, 0f, 0f]));
        await store.UpsertAsync(Rec("router", "a", [0f, 1f, 0f]));
        Assert.Equal(1, await store.CountAsync("router"));
        IReadOnlyList<EmbeddingMatch> matches = await store.SearchAsync(new EmbeddingQuery
        {
            Namespace = "router", Vector = [0f, 1f, 0f], TopK = 5, MinScore = -1,
        });
        Assert.Single(matches);
        Assert.Equal(1.0, Math.Round(matches[0].Score, 5));
    }

    [SqliteIntegrationFact]
    public async Task Search_IsolatedByNamespaceAndModelFilter()
    {
        await using Fixture fx = await CreateAsync();
        SqliteEmbeddingStore store = fx.Embeddings;
        await store.UpsertAsync(Rec("router", "shared", [1f, 0f, 0f]));
        await store.UpsertAsync(Rec("knowledge", "shared", [1f, 0f, 0f]));
        await store.UpsertAsync(Rec("router", "other-model-rec", [1f, 0f, 0f], model: "other-model"));

        IReadOnlyList<EmbeddingMatch> routerOnly = await store.SearchAsync(new EmbeddingQuery
        {
            Namespace = "router", Vector = [1f, 0f, 0f], TopK = 10, MinScore = -1,
        });
        Assert.Equal(2, routerOnly.Count);
        Assert.Equal(
            new[] { "other-model-rec", "shared" },
            routerOnly.Select(m => m.SourceRef).OrderBy(s => s, StringComparer.Ordinal).ToArray());
        Assert.Equal(1, await store.CountAsync("knowledge"));

        IReadOnlyList<EmbeddingMatch> modelFiltered = await store.SearchAsync(new EmbeddingQuery
        {
            Namespace = "router", Vector = [1f, 0f, 0f], TopK = 10, MinScore = -1, ModelId = "test-model",
        });
        Assert.Single(modelFiltered);
        Assert.Equal("shared", modelFiltered[0].SourceRef);
    }

    [SqliteIntegrationFact]
    public async Task Delete_AndDeleteByOwnerScope_Work()
    {
        await using Fixture fx = await CreateAsync();
        SqliteEmbeddingStore store = fx.Embeddings;
        await SeedControlSetAsync(store, "router");
        await SeedControlSetAsync(store, "knowledge");
        Assert.True(await store.DeleteAsync("router", "a"));
        Assert.False(await store.DeleteAsync("router", "a"));
        Assert.Equal(4, await store.CountAsync("router"));
        Assert.Equal(5, await store.DeleteByOwnerScopeAsync("knowledge"));
        Assert.Equal(0, await store.CountAsync("knowledge"));
    }

    [SqliteIntegrationFact]
    public async Task Search_ApproximateStrategy_ReturnsSameOrder()
    {
        await using Fixture fx = await CreateAsync();
        var store = new SqliteEmbeddingStore(fx.DbFactory, new EmbeddingStoreOptions
        {
            ModelDimensions = Fixture.TestOptions.ModelDimensions,
            SearchStrategy = EmbeddingSearchStrategy.ApproximateHnsw,
        });
        await SeedControlSetAsync(store);
        IReadOnlyList<EmbeddingMatch> matches = await store.SearchAsync(new EmbeddingQuery
        {
            Namespace = "router", Vector = [1f, 0f, 0f], TopK = 3, MinScore = -1,
        });
        Assert.Equal(["a", "b", "e"], matches.Select(m => m.SourceRef));
    }
}

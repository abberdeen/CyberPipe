using CyberPipe.Contracts;
using CyberPipe.Data;
using Npgsql;
using Xunit;

namespace CyberPipe.Data.Tests;

/// <summary>Unit: валидация до БД и преобразование расстояние→score. Фейк фабрики падает при обращении к БД.</summary>
public sealed class EmbeddingStoreValidationTests
{
    private sealed class ThrowingFactory : IPostgresConnectionFactory
    {
        public int CallCount { get; private set; }
        public Task<NpgsqlConnection> CreateOpenedConnectionAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("DB must not be called for invalid input.");
        }
    }

    private static readonly EmbeddingStoreOptions Options = new()
    {
        ModelDimensions = new Dictionary<string, int> { ["test-model"] = 3 },
    };

    private static EmbeddingRecord Record(IReadOnlyList<float> vector, int dimension = 0, string modelId = "test-model") => new()
    {
        Namespace = EmbeddingNamespaces.Router,
        SourceRef = "intent:hello",
        ModelId = modelId,
        Dimension = dimension,
        Vector = vector,
        TextHash = "hash",
    };

    [Fact]
    public async Task Upsert_EmptyVector_ThrowsArgumentBeforeDb()
    {
        var factory = new ThrowingFactory();
        var store = new PgVectorEmbeddingStore(factory, Options);
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertAsync(Record([])));
        Assert.Equal(0, factory.CallCount);
    }

    [Fact]
    public async Task Upsert_WrongDimensionForConfiguredModel_ThrowsMismatch()
    {
        var store = new PgVectorEmbeddingStore(new ThrowingFactory(), Options);
        EmbeddingDimensionMismatchException ex = await Assert.ThrowsAsync<EmbeddingDimensionMismatchException>(
            () => store.UpsertAsync(Record([1f, 0f])));
        Assert.Equal(3, ex.Expected);
        Assert.Equal(2, ex.Actual);
        Assert.Equal("test-model", ex.ModelId);
    }

    [Fact]
    public async Task Upsert_DeclaredDimensionDiffersFromVector_ThrowsMismatch()
    {
        // Модель не в конфигурации: ожидаемая размерность — поле Dimension записи.
        var store = new PgVectorEmbeddingStore(new ThrowingFactory(), new EmbeddingStoreOptions());
        EmbeddingDimensionMismatchException ex = await Assert.ThrowsAsync<EmbeddingDimensionMismatchException>(
            () => store.UpsertAsync(Record([1f, 0f], dimension: 4, modelId: "unknown-model")));
        Assert.Equal(4, ex.Expected);
        Assert.Equal(2, ex.Actual);
    }

    [Fact]
    public async Task Search_EmptyVectorAndZeroTopK_ThrowArgumentBeforeDb()
    {
        var factory = new ThrowingFactory();
        var store = new PgVectorEmbeddingStore(factory, Options);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SearchAsync(new EmbeddingQuery
        {
            Namespace = "router", Vector = [], TopK = 5,
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SearchAsync(new EmbeddingQuery
        {
            Namespace = "router", Vector = [1f, 0f, 0f], TopK = 0,
        }));
        Assert.Equal(0, factory.CallCount);
    }

    [Fact]
    public async Task Search_WrongDimensionForModelFilter_ThrowsMismatch()
    {
        var store = new PgVectorEmbeddingStore(new ThrowingFactory(), Options);
        EmbeddingDimensionMismatchException ex = await Assert.ThrowsAsync<EmbeddingDimensionMismatchException>(
            () => store.SearchAsync(new EmbeddingQuery
            {
                Namespace = "router", Vector = [1f, 0f], TopK = 5, ModelId = "test-model",
            }));
        Assert.Equal(3, ex.Expected);
        Assert.Equal(2, ex.Actual);
    }

    [Fact]
    public async Task UpsertMany_OneBadRecord_ThrowsBeforeDb()
    {
        var factory = new ThrowingFactory();
        var store = new PgVectorEmbeddingStore(factory, Options);
        await Assert.ThrowsAsync<EmbeddingDimensionMismatchException>(() => store.UpsertManyAsync(
            [Record([1f, 0f, 0f]), Record([1f, 0f])]));
        Assert.Equal(0, factory.CallCount);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(1.0, 0.0)]
    [InlineData(2.0, -1.0)]
    [InlineData(0.25, 0.75)]
    [InlineData(3.0, -1.0)]
    [InlineData(-1.0, 1.0)]
    public void CosineScore_FromDistance_ConvertsAndClamps(double distance, double expected) =>
        Assert.Equal(expected, CosineScore.FromDistance(distance), precision: 10);
}

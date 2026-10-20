using System.Globalization;
using System.Text.Json;
using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace CyberPipe.Data;

/// <summary>Стратегия поиска ближайших соседей (TASK-012). Смена стратегии не меняет контракт.</summary>
public enum EmbeddingSearchStrategy
{
    /// <summary>Точный поиск: полное сканирование с сортировкой по косинусному расстоянию.</summary>
    Exact = 0,

    /// <summary>
    /// Приближённый поиск (выбран ANN-метод HNSW): тот же запрос с параметром hnsw.ef_search.
    /// При отсутствии HNSW-индекса PostgreSQL выполняет тот же точный порядок, поэтому
    /// корректность и порядок результатов не зависят от наличия индекса.
    /// </summary>
    ApproximateHnsw = 1,
}

/// <summary>
/// Опции хранилища эмбеддингов. Размерность модели берётся отсюда (конфигурация модели),
/// а не зашивается в код: <see cref="ModelDimensions" /> отображает ModelId в размерность.
/// Если модель в карте отсутствует, ожидаемая размерность берётся из поля Dimension записи;
/// Dimension=0 означает «размерность равна длине вектора».
/// </summary>
public sealed record EmbeddingStoreOptions
{
    /// <summary>Известные размерности по ModelId; пусто — размерность не зафиксирована конфигурацией.</summary>
    public IReadOnlyDictionary<string, int> ModelDimensions { get; init; } =
        new Dictionary<string, int>();

    /// <summary>Стратегия поиска; по умолчанию точный поиск для малых объёмов.</summary>
    public EmbeddingSearchStrategy SearchStrategy { get; init; } = EmbeddingSearchStrategy.Exact;

    /// <summary>Параметр hnsw.ef_search для приближённой стратегии (больше — точнее и медленнее).</summary>
    public int HnswEfSearch { get; init; } = 100;

    /// <summary>Проверяет опции; ошибки содержат имена полей.</summary>
    /// <returns>Список ошибок; пуст при валидных опциях.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        foreach ((string modelId, int dimension) in ModelDimensions)
        {
            if (string.IsNullOrWhiteSpace(modelId))
                errors.Add("Embeddings:ModelDimensions must not contain an empty model id.");
            if (dimension <= 0)
                errors.Add($"Embeddings:ModelDimensions[{modelId}] must be positive.");
        }

        if (HnswEfSearch <= 0)
            errors.Add("Embeddings:HnswEfSearch must be positive.");
        return errors;
    }
}

/// <summary>Несовпадение размерности вектора с ожидаемой для модели.</summary>
public sealed class EmbeddingDimensionMismatchException : Exception
{
    /// <summary>Создаёт исключение несовпадения размерности.</summary>
    /// <param name="expected">Ожидаемая размерность.</param>
    /// <param name="actual">Фактическая длина вектора.</param>
    /// <param name="modelId">Идентификатор модели.</param>
    public EmbeddingDimensionMismatchException(int expected, int actual, string modelId)
        : base($"Embedding dimension mismatch for model '{modelId}': expected {expected.ToString(CultureInfo.InvariantCulture)}, actual {actual.ToString(CultureInfo.InvariantCulture)}. Vectors are never trimmed or padded.")
    {
        Expected = expected;
        Actual = actual;
        ModelId = modelId;
    }

    /// <summary>Ожидаемая размерность.</summary>
    public int Expected { get; }

    /// <summary>Фактическая длина вектора.</summary>
    public int Actual { get; }

    /// <summary>Идентификатор модели.</summary>
    public string ModelId { get; }
}

/// <summary>Преобразование расстояния pgvector в косинусное сходство контракта.</summary>
public static class CosineScore
{
    /// <summary>
    /// Оператор pgvector &lt;=&gt; возвращает косинусное расстояние (1 - сходство).
    /// Score контракта — сходство в [-1, 1]; значения вне диапазона обрезаются.
    /// </summary>
    /// <param name="cosineDistance">Косинусное расстояние pgvector.</param>
    /// <returns>Косинусное сходство в [-1, 1].</returns>
    public static double FromDistance(double cosineDistance) =>
        Math.Clamp(1.0 - cosineDistance, -1.0, 1.0);
}

/// <summary>Валидация входных данных хранилища до обращения к БД (TASK-012).</summary>
public static class EmbeddingValidation
{
    /// <summary>Проверяет запись; размерность сверяется с Dimension записи и конфигурацией модели.</summary>
    /// <param name="record">Запись для проверки.</param>
    /// <param name="options">Опции с известными размерностями моделей.</param>
    /// <exception cref="ArgumentException">Пустой вектор или пустые обязательные поля.</exception>
    /// <exception cref="EmbeddingDimensionMismatchException">Несовпадение размерности.</exception>
    public static void ValidateRecord(EmbeddingRecord record, EmbeddingStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(options);
        RequireText(record.Namespace, nameof(record.Namespace));
        RequireText(record.SourceRef, nameof(record.SourceRef));
        RequireText(record.ModelId, nameof(record.ModelId));
        RequireText(record.TextHash, nameof(record.TextHash));
        int actual = ValidateVector(record.Vector, nameof(record.Vector));
        int expected = ExpectedDimension(record.ModelId, record.Dimension, options);
        if (expected != actual)
            throw new EmbeddingDimensionMismatchException(expected, actual, record.ModelId);
    }

    /// <summary>Проверяет запрос поиска до обращения к БД.</summary>
    /// <param name="query">Запрос для проверки.</param>
    /// <param name="options">Опции с известными размерностями моделей.</param>
    /// <exception cref="ArgumentException">Пустой вектор, TopK &lt;= 0 или MinScore вне [-1, 1].</exception>
    /// <exception cref="EmbeddingDimensionMismatchException">Несовпадение размерности с моделью фильтра.</exception>
    public static void ValidateQuery(EmbeddingQuery query, EmbeddingStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(options);
        RequireText(query.Namespace, nameof(query.Namespace));
        int actual = ValidateVector(query.Vector, nameof(query.Vector));
        if (query.TopK <= 0)
            throw new ArgumentException("TopK must be positive.", nameof(query.TopK));
        if (query.MinScore is < -1.0 or > 1.0)
            throw new ArgumentException("MinScore must be between -1 and 1.", nameof(query.MinScore));
        if (!string.IsNullOrWhiteSpace(query.ModelId)
            && options.ModelDimensions.TryGetValue(query.ModelId, out int expected)
            && expected != actual)
        {
            throw new EmbeddingDimensionMismatchException(expected, actual, query.ModelId);
        }
    }

    /// <summary>Ожидаемая размерность: конфигурация модели имеет приоритет над полем записи.</summary>
    internal static int ExpectedDimension(string modelId, int declaredDimension, EmbeddingStoreOptions options)
    {
        if (options.ModelDimensions.TryGetValue(modelId, out int configured))
            return configured;
        return declaredDimension;
    }

    private static int ValidateVector(IReadOnlyList<float>? vector, string paramName)
    {
        if (vector is null || vector.Count == 0)
            throw new ArgumentException("Vector must not be empty.", paramName);
        return vector.Count;
    }

    private static void RequireText(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must not be empty.", paramName);
    }
}

/// <summary>
/// Хранилище эмбеддингов на pgvector (TASK-012). Потребители: TASK-031, TASK-034, TASK-053, TASK-071.
/// Только хранилище: без генерации эмбеддингов, классификации и политики кэша.

/// <summary>
/// Реализация <see cref="IEmbeddingStore" /> поверх app.embeddings (TASK-011) и pgvector.
/// Косинусное расстояние оператора &lt;=&gt; преобразуется в Score-сходство внутри реализации.
/// ANN-выбор зафиксирован как HNSW (<see cref="EmbeddingSearchStrategy.ApproximateHnsw" />):
/// отдельной миграции под ANN-индекс здесь нет, потому что колонка вектора не имеет фиксированной
/// размерности (размерность задаёт модель), а единый HNSW-индекс по разноразмерным векторам
/// не строится; до фиксации размерности модели точный поиск даёт тот же контракт и порядок.
/// </summary>
public sealed class PgVectorEmbeddingStore : IEmbeddingStore
{
    private const string UpsertSql = """
        INSERT INTO app.embeddings (id, namespace, source_ref, model_id, dimension, embedding, text_hash, metadata, created_at, updated_at)
        VALUES (@id, @namespace, @source_ref, @model_id, @dimension, @embedding::vector, @text_hash, @metadata, COALESCE(@created_at, now()), now())
        ON CONFLICT (namespace, source_ref, model_id) DO UPDATE
        SET dimension = EXCLUDED.dimension,
            embedding = EXCLUDED.embedding,
            text_hash = EXCLUDED.text_hash,
            metadata = EXCLUDED.metadata,
            updated_at = now()
        """;

    private readonly IPostgresConnectionFactory _connectionFactory;
    private readonly EmbeddingStoreOptions _options;
    private readonly ILogger<PgVectorEmbeddingStore> _logger;

    /// <summary>Создаёт хранилище поверх фабрики соединений TASK-010.</summary>
    /// <param name="connectionFactory">Фабрика открытых соединений.</param>
    /// <param name="options">Опции размерностей и стратегии; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; векторы и метаданные в логи не пишутся.</param>
    public PgVectorEmbeddingStore(
        IPostgresConnectionFactory connectionFactory,
        EmbeddingStoreOptions? options = null,
        ILogger<PgVectorEmbeddingStore>? logger = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _options = options ?? new EmbeddingStoreOptions();
        _logger = logger ?? NullLogger<PgVectorEmbeddingStore>.Instance;
        IReadOnlyList<string> errors = _options.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(options));
    }

    /// <inheritdoc />
    public async Task UpsertAsync(EmbeddingRecord record, CancellationToken cancellationToken = default)
    {
        EmbeddingValidation.ValidateRecord(record, _options);
        await using NpgsqlConnection connection = await _connectionFactory
            .CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateUpsertCommand(connection, null, record);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertManyAsync(IReadOnlyList<EmbeddingRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        foreach (EmbeddingRecord record in records)
            EmbeddingValidation.ValidateRecord(record, _options);
        if (records.Count == 0)
            return;

        await using NpgsqlConnection connection = await _connectionFactory
            .CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (EmbeddingRecord record in records)
        {
            await using var command = CreateUpsertCommand(connection, transaction, record);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Upserted {Count} embeddings.", records.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmbeddingMatch>> SearchAsync(EmbeddingQuery query, CancellationToken cancellationToken = default)
    {
        EmbeddingValidation.ValidateQuery(query, _options);
        string vectorLiteral = FormatVector(query.Vector);
        await using NpgsqlConnection connection = await _connectionFactory
            .CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);

        NpgsqlTransaction? transaction = null;
        try
        {
            if (_options.SearchStrategy == EmbeddingSearchStrategy.ApproximateHnsw)
            {
                transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                await using var set = new NpgsqlCommand(
                    $"SET LOCAL hnsw.ef_search = {_options.HnswEfSearch.ToString(CultureInfo.InvariantCulture)}",
                    connection, transaction);
                await set.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // MinScore применяется до TopK: score = 1 - distance, поэтому порог — в WHERE,
            // а не LIMIT-then-filter (иначе результат мог содержать меньше TopK совпадений).
            const string sql = """
                SELECT id, source_ref, embedding <=> @vector::vector AS distance, metadata
                FROM app.embeddings
                WHERE namespace = @namespace
                  AND dimension = @dimension
                  AND (@model_id IS NULL OR model_id = @model_id)
                  AND (embedding <=> @vector::vector) <= @max_distance
                ORDER BY embedding <=> @vector::vector ASC
                LIMIT @top_k
                """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("vector", vectorLiteral);
            command.Parameters.AddWithValue("namespace", query.Namespace);
            command.Parameters.AddWithValue("dimension", query.Vector.Count);
            command.Parameters.Add("model_id", NpgsqlDbType.Text).Value =
                string.IsNullOrWhiteSpace(query.ModelId) ? DBNull.Value : query.ModelId;
            command.Parameters.AddWithValue("top_k", query.TopK);
            command.Parameters.AddWithValue("max_distance", 1.0 - query.MinScore);

            var matches = new List<EmbeddingMatch>();
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    double score = CosineScore.FromDistance(reader.GetDouble(2));
                    if (score < query.MinScore)
                        continue;
                    matches.Add(new EmbeddingMatch
                    {
                        Id = reader.GetGuid(0),
                        SourceRef = reader.GetString(1),
                        Score = score,
                        Metadata = DeserializeMetadata(reader.GetString(3)),
                    });
                }
            }

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            // SQL уже сортирует по расстоянию (score убывает); MinScore применён после преобразования.
            return matches;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string @namespace, string sourceRef, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(@namespace))
            throw new ArgumentException("Namespace must not be empty.", nameof(@namespace));
        if (string.IsNullOrWhiteSpace(sourceRef))
            throw new ArgumentException("SourceRef must not be empty.", nameof(sourceRef));
        await using NpgsqlConnection connection = await _connectionFactory
            .CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "DELETE FROM app.embeddings WHERE namespace = @namespace AND source_ref = @source_ref", connection);
        command.Parameters.AddWithValue("namespace", @namespace);
        command.Parameters.AddWithValue("source_ref", sourceRef);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async Task<int> DeleteByOwnerScopeAsync(string @namespace, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(@namespace))
            throw new ArgumentException("Namespace must not be empty.", nameof(@namespace));
        await using NpgsqlConnection connection = await _connectionFactory
            .CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "DELETE FROM app.embeddings WHERE namespace = @namespace", connection);
        command.Parameters.AddWithValue("namespace", @namespace);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(string @namespace, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(@namespace))
            throw new ArgumentException("Namespace must not be empty.", nameof(@namespace));
        await using NpgsqlConnection connection = await _connectionFactory
            .CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM app.embeddings WHERE namespace = @namespace", connection);
        command.Parameters.AddWithValue("namespace", @namespace);
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<int> DeleteOldestAsync(string @namespace, int keepCount, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(@namespace))
            throw new ArgumentException("Namespace must not be empty.", nameof(@namespace));
        if (keepCount < 0)
            throw new ArgumentOutOfRangeException(nameof(keepCount));
        await using NpgsqlConnection connection = await _connectionFactory
            .CreateOpenedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("""
            DELETE FROM app.embeddings
            WHERE namespace = @namespace
              AND id IN (
                  SELECT id FROM app.embeddings
                  WHERE namespace = @namespace
                  ORDER BY created_at ASC, id ASC
                  LIMIT (SELECT GREATEST(count(*) - @keep, 0) FROM app.embeddings WHERE namespace = @namespace))
            """, connection);
        command.Parameters.AddWithValue("namespace", @namespace);
        command.Parameters.AddWithValue("keep", keepCount);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static string FormatVector(IReadOnlyList<float> vector) =>
        "[" + string.Join(",", vector.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]";

    private static NpgsqlCommand CreateUpsertCommand(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, EmbeddingRecord record)
    {
        var command = new NpgsqlCommand(UpsertSql, connection, transaction);
        command.Parameters.AddWithValue("id", record.Id == Guid.Empty ? Guid.NewGuid() : record.Id);
        command.Parameters.AddWithValue("namespace", record.Namespace);
        command.Parameters.AddWithValue("source_ref", record.SourceRef);
        command.Parameters.AddWithValue("model_id", record.ModelId);
        command.Parameters.AddWithValue("dimension", record.Vector.Count);
        command.Parameters.AddWithValue("embedding", FormatVector(record.Vector));
        command.Parameters.AddWithValue("text_hash", record.TextHash);
        command.Parameters.Add("metadata", NpgsqlDbType.Jsonb).Value =
            JsonSerializer.Serialize(record.Metadata ?? new Dictionary<string, string>());
        command.Parameters.Add("created_at", NpgsqlDbType.TimestampTz).Value =
            record.CreatedAt == default ? DBNull.Value : record.CreatedAt;
        return command;
    }

    private static IReadOnlyDictionary<string, string> DeserializeMetadata(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? new Dictionary<string, string>();
    }
}

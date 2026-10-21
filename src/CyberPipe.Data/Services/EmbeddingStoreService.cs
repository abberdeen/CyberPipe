using System.Globalization;
using System.Text.Json;
using CyberPipe.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CyberPipe.Data.Persistence;

namespace CyberPipe.Data;

/// <summary>Стратегия поиска ближайших соседей. Смена стратегии не меняет контракт.</summary>
public enum EmbeddingSearchStrategy
{
    /// <summary>Точный поиск: полное сканирование с сортировкой по косинусному расстоянию.</summary>
    Exact = 0,

    /// <summary>
    /// Приближённый поиск: зарезервировано для совместимости; SQLite выполняет тот же
    /// точный перебор, поэтому корректность и порядок результатов не зависят от стратегии.
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

/// <summary>Преобразование косинусного расстояния в косинусное сходство контракта.</summary>
public static class CosineScore
{
    /// <summary>
    /// Косинусное расстояние (1 - сходство) преобразуется в Score контракта —
    /// сходство в [-1, 1]; значения вне диапазона обрезаются.
    /// </summary>
    /// <param name="cosineDistance">Косинусное расстояние.</param>
    /// <returns>Косинусное сходство в [-1, 1].</returns>
    public static double FromDistance(double cosineDistance) =>
        Math.Clamp(1.0 - cosineDistance, -1.0, 1.0);

    /// <summary>Вычисляет косинусное сходство двух векторов одинаковой длины; нулевые нормы дают 0.</summary>
    /// <param name="left">Первый вектор.</param>
    /// <param name="right">Второй вектор.</param>
    /// <returns>Сходство в [-1, 1].</returns>
    public static double Similarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Count != right.Count)
            throw new ArgumentException("Vectors must have equal length.", nameof(right));
        double dot = 0, normLeft = 0, normRight = 0;
        for (int i = 0; i < left.Count; i++)
        {
            dot += (double)left[i] * right[i];
            normLeft += (double)left[i] * left[i];
            normRight += (double)right[i] * right[i];
        }

        if (normLeft <= 0 || normRight <= 0)
            return 0;
        return Math.Clamp(dot / (Math.Sqrt(normLeft) * Math.Sqrt(normRight)), -1.0, 1.0);
    }
}

/// <summary>Валидация входных данных хранилища до обращения к БД.</summary>
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
/// Хранилище эмбеддингов на SQLite поверх таблицы embeddings.
/// Вектор хранится текстом (JSON-массив float), косинусное сходство считается в C# полным
/// перебором кандидатов; порядок и контракт не зависят от стратегии поиска.
/// Только хранилище: без генерации эмбеддингов, классификации и политики кэша.
/// Сырых SQL-строк нет; все чтения — LINQ с AsNoTracking.
/// </summary>
public sealed class EmbeddingStoreService
{
    private readonly IDbContextFactory<CyberPipeDbContext> _dbFactory;
    private readonly RepositoryOptions _options;
    private readonly EmbeddingStoreOptions _storeOptions;
    private readonly ILogger _logger;
    private readonly ILogger _storeLogger;
    private readonly UnitOfWorkMetrics _metrics;

    /// <summary>Создаёт хранилище поверх фабрики контекстов SQLite.</summary>
    /// <param name="dbFactory">Фабрика контекстов SQLite (пул).</param>
    /// <param name="options">Опции репозиториев; null — значения по умолчанию.</param>
    /// <param name="storeOptions">Опции размерностей и стратегии; null — значения по умолчанию.</param>
    /// <param name="logger">Логгер; векторы и метаданные в логи не пишутся.</param>
    /// <param name="metrics">Общие счётчики; null — отдельный экземпляр.</param>
    public EmbeddingStoreService(
        IDbContextFactory<CyberPipeDbContext> dbFactory,
        RepositoryOptions? options = null,
        EmbeddingStoreOptions? storeOptions = null,
        ILogger<EmbeddingStoreService>? logger = null,
        UnitOfWorkMetrics? metrics = null)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _options = options ?? new RepositoryOptions();
        IReadOnlyList<string> errors = _options.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(options));
        _storeOptions = storeOptions ?? new EmbeddingStoreOptions();
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _storeLogger = (ILogger?)logger ?? NullLogger.Instance;
        _metrics = metrics ?? new UnitOfWorkMetrics();
        IReadOnlyList<string> storeErrors = _storeOptions.Validate();
        if (storeErrors.Count > 0)
            throw new ArgumentException(string.Join(" ", storeErrors), nameof(storeOptions));
    }

    /// <summary>Создаёт или обновляет запись эмбеддинга.</summary>
    /// <param name="record">Запись для сохранения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача сохранения.</returns>
    public Task UpsertAsync(EmbeddingRecord record, CancellationToken cancellationToken = default)
    {
        EmbeddingValidation.ValidateRecord(record, _storeOptions);
        return EfServiceHelper.ExecuteAsync(_dbFactory, _options, _logger, _metrics, "embeddings.upsert", async (db, token) =>
        {
            await UpsertCoreAsync(db, record, token).ConfigureAwait(false);
            await db.SaveChangesAsync(token).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>
    /// Создаёт или обновляет несколько записей одним SaveChanges внутри явной транзакции
    /// метода; отмена откатывает все записи целиком.
    /// </summary>
    /// <param name="records">Записи для сохранения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача сохранения.</returns>
    public Task UpsertManyAsync(IReadOnlyList<EmbeddingRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        foreach (EmbeddingRecord record in records)
            EmbeddingValidation.ValidateRecord(record, _storeOptions);
        if (records.Count == 0)
            return Task.CompletedTask;
        return EfServiceHelper.ExecuteAsync(_dbFactory, _options, _logger, _metrics, "embeddings.upsert_many", async (db, token) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
            try
            {
                foreach (EmbeddingRecord record in records)
                    await UpsertCoreAsync(db, record, token).ConfigureAwait(false);
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                _metrics.IncrementCommitted();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                _metrics.IncrementRolledBack();
                throw;
            }

            _storeLogger.LogDebug("Upserted {Count} embeddings.", records.Count);
        }, cancellationToken);
    }

    /// <summary>Ищет ближайшие эмбеддинги полным перебором кандидатов с косинусным сходством.</summary>
    /// <param name="query">Запрос поиска.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Совпадения по убыванию сходства, не более TopK.</returns>
    public Task<IReadOnlyList<EmbeddingMatch>> SearchAsync(EmbeddingQuery query, CancellationToken cancellationToken = default)
    {
        EmbeddingValidation.ValidateQuery(query, _storeOptions);
        return ExecuteAsync<IReadOnlyList<EmbeddingMatch>>("embeddings.search", async (db, token) =>
        {
            // SQLite без pgvector: кандидаты фильтруются LINQ, сходство считается в C#.
            string? modelId = string.IsNullOrWhiteSpace(query.ModelId) ? null : query.ModelId;
            List<EmbeddingEntity> candidates = await db.Embeddings
                .AsNoTracking()
                .Where(e => e.Namespace == query.Namespace
                    && e.Dimension == query.Vector.Count
                    && (modelId == null || e.ModelId == modelId))
                .ToListAsync(token).ConfigureAwait(false);
            return candidates
                .Select(e => (Entity: e, Stored: ParseVector(e.Embedding)))
                .Where(t => t.Stored.Length == query.Vector.Count)
                .Select(t => (t.Entity, Score: CosineScore.Similarity(query.Vector, t.Stored)))
                .Where(t => t.Score >= query.MinScore)
                .OrderByDescending(t => t.Score)
                .Take(query.TopK)
                .Select(t => new EmbeddingMatch
                {
                    Id = Guid.Parse(t.Entity.Id),
                    SourceRef = t.Entity.SourceRef,
                    Score = t.Score,
                    Metadata = DeserializeMetadata(t.Entity.Metadata),
                })
                .ToList();
        }, cancellationToken);
    }

    /// <summary>Удаляет запись эмбеддинга.</summary>
    /// <param name="namespace">Пространство имён.</param>
    /// <param name="sourceRef">Ссылка на источник.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись существовала и удалена.</returns>
    public Task<bool> DeleteAsync(string @namespace, string sourceRef, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(@namespace))
            throw new ArgumentException("Namespace must not be empty.", nameof(@namespace));
        if (string.IsNullOrWhiteSpace(sourceRef))
            throw new ArgumentException("SourceRef must not be empty.", nameof(sourceRef));
        return ExecuteAsync("embeddings.delete", async (db, token) =>
        {
            int affected = await db.Embeddings
                .Where(e => e.Namespace == @namespace && e.SourceRef == sourceRef)
                .ExecuteDeleteAsync(token).ConfigureAwait(false);
            return affected > 0;
        }, cancellationToken);
    }

    /// <summary>Удаляет все записи пространства имён.</summary>
    /// <param name="namespace">Пространство имён.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число удалённых записей.</returns>
    public Task<int> DeleteByOwnerScopeAsync(string @namespace, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(@namespace))
            throw new ArgumentException("Namespace must not be empty.", nameof(@namespace));
        return ExecuteAsync("embeddings.delete_scope", (db, token) => db.Embeddings
            .Where(e => e.Namespace == @namespace)
            .ExecuteDeleteAsync(token), cancellationToken);
    }

    /// <summary>Возвращает число записей пространства имён.</summary>
    /// <param name="namespace">Пространство имён.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число записей.</returns>
    public Task<long> CountAsync(string @namespace, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(@namespace))
            throw new ArgumentException("Namespace must not be empty.", nameof(@namespace));
        return ExecuteAsync("embeddings.count", (db, token) => db.Embeddings
            .AsNoTracking()
            .LongCountAsync(e => e.Namespace == @namespace, token), cancellationToken);
    }

    /// <summary>Удаляет старейшие записи пространства, оставляя keepCount свежих.</summary>
    /// <param name="namespace">Пространство имён.</param>
    /// <param name="keepCount">Сколько свежих записей оставить.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число удалённых записей.</returns>
    public Task<int> DeleteOldestAsync(string @namespace, int keepCount, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(@namespace))
            throw new ArgumentException("Namespace must not be empty.", nameof(@namespace));
        if (keepCount < 0)
            throw new ArgumentOutOfRangeException(nameof(keepCount));
        return ExecuteAsync("embeddings.delete_oldest", async (db, token) =>
        {
            long total = await db.Embeddings
                .AsNoTracking()
                .LongCountAsync(e => e.Namespace == @namespace, token).ConfigureAwait(false);
            long excess = total - keepCount;
            if (excess <= 0)
                return 0;
            List<string> oldestIds = await db.Embeddings
                .AsNoTracking()
                .Where(e => e.Namespace == @namespace)
                .OrderBy(e => e.CreatedAt)
                .ThenBy(e => e.Id)
                .Take((int)excess)
                .Select(e => e.Id)
                .ToListAsync(token).ConfigureAwait(false);
            return await db.Embeddings
                .Where(e => oldestIds.Contains(e.Id))
                .ExecuteDeleteAsync(token).ConfigureAwait(false);
        }, cancellationToken);
    }

    private static async Task UpsertCoreAsync(CyberPipeDbContext db, EmbeddingRecord record, CancellationToken token)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        EmbeddingEntity? existing = await db.Embeddings
            .FirstOrDefaultAsync(e => e.Namespace == record.Namespace
                && e.SourceRef == record.SourceRef
                && e.ModelId == record.ModelId, token).ConfigureAwait(false);
        if (existing is not null)
        {
            existing.Dimension = record.Vector.Count;
            existing.Embedding = FormatVector(record.Vector);
            existing.TextHash = record.TextHash;
            existing.Metadata = JsonSerializer.Serialize(record.Metadata ?? new Dictionary<string, string>());
            existing.UpdatedAt = now;
            return;
        }

        Guid id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id;
        db.Embeddings.Add(new EmbeddingEntity
        {
            Id = EfServiceHelper.GuidKey(id),
            Namespace = record.Namespace,
            SourceRef = record.SourceRef,
            ModelId = record.ModelId,
            Dimension = record.Vector.Count,
            Embedding = FormatVector(record.Vector),
            TextHash = record.TextHash,
            Metadata = JsonSerializer.Serialize(record.Metadata ?? new Dictionary<string, string>()),
            CreatedAt = record.CreatedAt == default ? now : record.CreatedAt,
            UpdatedAt = now,
        });
    }

    internal static string FormatVector(IReadOnlyList<float> vector) =>
        "[" + string.Join(",", vector.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]";

    internal static float[] ParseVector(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            trimmed = trimmed[1..^1];
        if (string.IsNullOrWhiteSpace(trimmed))
            return [];
        return trimmed.Split(',').Select(p => float.Parse(p.Trim(), CultureInfo.InvariantCulture)).ToArray();
    }

    private static IReadOnlyDictionary<string, string> DeserializeMetadata(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? new Dictionary<string, string>();
    }

    private Task<T> ExecuteAsync<T>(
        string operation,
        Func<CyberPipeDbContext, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken) =>
        EfServiceHelper.ExecuteAsync(_dbFactory, _options, _logger, _metrics, operation, body, cancellationToken);
}

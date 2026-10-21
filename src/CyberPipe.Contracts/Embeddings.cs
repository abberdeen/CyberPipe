namespace CyberPipe.Contracts;

/// <summary>
/// Известные пространства имён эмбеддингов; потребители изолированы по namespace.
/// </summary>
public static class EmbeddingNamespaces
{
    /// <summary>Эталоны намерений семантического роутера (TASK-031).</summary>
    public const string Router = "router";

    /// <summary>Семантический кэш (TASK-034).</summary>
    public const string SemanticCache = "semantic-cache";

    /// <summary>Поиск по знаниям / второй мозг (TASK-053).</summary>
    public const string Knowledge = "knowledge";

    /// <summary>Эвристическая оценка риска (TASK-071).</summary>
    public const string Safety = "safety";
}

/// <summary>
/// Запись эмбеддинга. Хранилище хранит вектор как есть; для косинусной метрики ожидаются нормализованные векторы.
/// </summary>
public sealed record EmbeddingRecord
{
    /// <summary>Идентификатор записи; Guid.Empty — сгенерировать при записи.</summary>
    public Guid Id { get; init; }

    /// <summary>Пространство имён потребителя, например router / semantic-cache / knowledge / safety.</summary>
    public required string Namespace { get; init; }

    /// <summary>Ссылка на источник (намерение, документ, запись кэша).</summary>
    public required string SourceRef { get; init; }

    /// <summary>Идентификатор модели эмбеддингов.</summary>
    public required string ModelId { get; init; }

    /// <summary>Размерность; 0 — равна длине вектора, иначе должна совпадать с длиной вектора и конфигурацией модели.</summary>
    public int Dimension { get; init; }

    /// <summary>Вектор эмбеддинга как есть (ожидается нормализованным вызывающим).</summary>
    public required IReadOnlyList<float> Vector { get; init; }

    /// <summary>Хеш исходного текста.</summary>
    public required string TextHash { get; init; }

    /// <summary>Метаданные записи (jsonb).</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Время создания; default — now() базы данных.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Запрос поиска ближайших соседей.</summary>
public sealed record EmbeddingQuery
{
    /// <summary>Вектор запроса (ожидается нормализованным вызывающим).</summary>
    public required IReadOnlyList<float> Vector { get; init; }

    /// <summary>Пространство имён для поиска.</summary>
    public required string Namespace { get; init; }

    /// <summary>Максимум возвращаемых соседей; должен быть положительным.</summary>
    public int TopK { get; init; } = 10;

    /// <summary>Минимальное косинусное сходство в [-1, 1]; ниже порога результаты не возвращаются.</summary>
    public double MinScore { get; init; } = -1.0;

    /// <summary>Необязательный фильтр по модели.</summary>
    public string? ModelId { get; init; }
}

/// <summary>Один сосед из результата поиска.</summary>
public sealed record EmbeddingMatch
{
    /// <summary>Идентификатор записи.</summary>
    public required Guid Id { get; init; }

    /// <summary>Ссылка на источник записи.</summary>
    public required string SourceRef { get; init; }

    /// <summary>Косинусное сходство в [-1, 1] (не расстояние косинусного расстояния).</summary>
    public required double Score { get; init; }

    /// <summary>Метаданные записи.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// Хранилище эмбеддингов (TASK-012). Потребители: TASK-031, TASK-034, TASK-053, TASK-071.
/// Только хранилище: без генерации эмбеддингов, классификации и политики кэша.
/// </summary>
public interface IEmbeddingStore
{
    /// <summary>Идемпотентно записывает запись по (Namespace, SourceRef, ModelId): повтор обновляет вектор и метаданные.</summary>
    /// <param name="record">Запись эмбеддинга.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task UpsertAsync(EmbeddingRecord record, CancellationToken cancellationToken = default);

    /// <summary>Пакетная идемпотентная запись в одной транзакции; все записи валидируются до транзакции.</summary>
    /// <param name="records">Записи эмбеддингов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task UpsertManyAsync(IReadOnlyList<EmbeddingRecord> records, CancellationToken cancellationToken = default);

    /// <summary>Поиск TopK ближайших соседей по косинусному расстоянию.</summary>
    /// <param name="query">Параметры поиска.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список соседей, упорядоченный по убыванию сходства (Score).</returns>
    Task<IReadOnlyList<EmbeddingMatch>> SearchAsync(EmbeddingQuery query, CancellationToken cancellationToken = default);

    /// <summary>Удаляет запись по составному ключу (Namespace, SourceRef).</summary>
    /// <param name="namespace">Пространство имён.</param>
    /// <param name="sourceRef">Ссылка на источник.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true если запись существовала и удалена; false если не найдена.</returns>
    Task<bool> DeleteAsync(string @namespace, string sourceRef, CancellationToken cancellationToken = default);

    /// <summary>Массовое удаление записей владельца по пространству имён.</summary>
    /// <param name="namespace">Пространство имён.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Количество удалённых записей.</returns>
    Task<int> DeleteByOwnerScopeAsync(string @namespace, CancellationToken cancellationToken = default);

    /// <summary>Считает записи пространства имён.</summary>
    /// <param name="namespace">Пространство имён.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число записей.</returns>
    Task<long> CountAsync(string @namespace, CancellationToken cancellationToken = default);

    /// <summary>Удаляет самые старые записи пространства имён, оставляя не более keepCount новейших (по CreatedAt).</summary>
    /// <param name="namespace">Пространство имён.</param>
    /// <param name="keepCount">Сколько новейших записей оставить; 0 — удалить все.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число удалённых записей.</returns>
    Task<int> DeleteOldestAsync(string @namespace, int keepCount, CancellationToken cancellationToken = default);
}

/// <summary>
/// Провайдер вычисления эмбеддингов текста. Потребители: TASK-031, TASK-034, TASK-053.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Вычисляет L2-нормализованный вектор эмбеддинга для переданного текста.</summary>
    /// <param name="text">Текст для векторизации.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Нормализованный вектор вещественных чисел.</returns>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// Статистика обращений к семантическому кэшу маршрутизации (TASK-034).
/// </summary>
public sealed record CacheStatsDto
{
    /// <summary>Число успешных попаданий в кэш.</summary>
    public long Hits { get; init; }

    /// <summary>Число промахов кэша.</summary>
    public long Misses { get; init; }

    /// <summary>Текущее количество записей в кэше.</summary>
    public int EntryCount { get; init; }
}

/// <summary>
/// Семантический кэш маршрутизации (TASK-034). Потребители: TASK-031, TASK-032.
/// </summary>
public interface ISemanticCache
{
    /// <summary>Пытается получить кэшированный результат по вектору запроса.</summary>
    /// <param name="embedding">Нормализованный вектор запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Кэшированный результат или null при промахе.</returns>
    Task<RouteResult?> TryGetAsync(float[] embedding, CancellationToken cancellationToken = default);

    /// <summary>Сохраняет подтверждённое решение маршрутизации в кэш.</summary>
    /// <param name="embedding">Нормализованный вектор запроса.</param>
    /// <param name="result">Решение маршрутизации.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task PutAsync(float[] embedding, RouteResult result, CancellationToken cancellationToken = default);

    /// <summary>Очищает все записи семантического кэша.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>Возвращает текущую статистику кэша.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<CacheStatsDto> GetStatsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Определение эталонного примера для семантического роутера Tier 1 (TASK-031).
/// </summary>
public sealed record SemanticExampleDefinition
{
    /// <summary>Эталонный текст фразы (русский или английский).</summary>
    public required string Text { get; init; }

    /// <summary>Намерение (intent), соответствующее фразе.</summary>
    public required string Intent { get; init; }

    /// <summary>Параметры намерения по умолчанию.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// Поставщик эталонных примеров для семантического роутера Tier 1 (TASK-031).
/// </summary>
public interface ISemanticExampleProvider
{
    /// <summary>Возвращает список эталонных примеров.</summary>
    IReadOnlyList<SemanticExampleDefinition> GetExamples();
}

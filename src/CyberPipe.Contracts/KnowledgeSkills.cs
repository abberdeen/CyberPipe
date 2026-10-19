namespace CyberPipe.Contracts;

/// <summary>
/// Результат совпадения семантического поиска по базе знаний (TASK-053).
/// </summary>
public sealed record KnowledgeHit
{
    /// <summary>Идентификатор документа.</summary>
    public required string DocumentId { get; init; }

    /// <summary>Текст чанка/фрагмента.</summary>
    public required string ChunkText { get; init; }

    /// <summary>Оценка сходства косинусной метрики (от -1.0 до 1.0).</summary>
    public required double Score { get; init; }

    /// <summary>Источник документа (заметка, файл, URL и т.д.).</summary>
    public required string Source { get; init; }

    /// <summary>Индекс чанка в исходном документе.</summary>
    public int ChunkIndex { get; init; }

    /// <summary>Метаданные чанка.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// Запрос на индексирование документа в базу знаний (TASK-053).
/// </summary>
public sealed record IngestDocumentRequest
{
    /// <summary>Идентификатор документа.</summary>
    public required string DocumentId { get; init; }

    /// <summary>Текст документа.</summary>
    public required string Text { get; init; }

    /// <summary>Источник документа.</summary>
    public string Source { get; init; } = "default";

    /// <summary>Вид записи ("document" или "memory").</summary>
    public string Kind { get; init; } = "document";

    /// <summary>Теги документа.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Дополнительные метаданные.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// Запрос на семантический поиск по базе знаний (TASK-053).
/// </summary>
public sealed record KnowledgeSearchRequest
{
    /// <summary>Текстовый поисковый запрос.</summary>
    public required string Query { get; init; }

    /// <summary>Максимальное количество возвращаемых результатов.</summary>
    public int TopK { get; init; } = 5;

    /// <summary>Минимальный порог косинусного сходства.</summary>
    public double MinScore { get; init; } = 0.5;

    /// <summary>Опциональный фильтр по виду ("document", "memory" или null для всех).</summary>
    public string? Kind { get; init; }
}

/// <summary>
/// Факт долговременной памяти ассистента (TASK-053).
/// </summary>
public sealed record MemoryFact
{
    /// <summary>Идентификатор факта.</summary>
    public required string Id { get; init; }

    /// <summary>Содержимое факта.</summary>
    public required string Fact { get; init; }

    /// <summary>Оценка релевантности.</summary>
    public double Score { get; init; }

    /// <summary>Время сохранения (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }
}

/// <summary>
/// Провайдер контекста из базы знаний для подмешивания в промпты LLM (TASK-053).
/// </summary>
public interface IKnowledgeContextProvider
{
    /// <summary>
    /// Возвращает текстовый контекст с источниками в пределах заданного бюджета токенов.
    /// </summary>
    Task<string> GetContextAsync(string query, int tokenBudget, CancellationToken ct = default);
}

/// <summary>
/// Опции конфигурации поиска по знаниям и памяти (TASK-053).
/// </summary>
public sealed record KnowledgeOptions
{
    public int ChunkSize { get; init; } = 500;
    public int ChunkOverlap { get; init; } = 100;
    public int MaxContextFragments { get; init; } = 5;
    public int DefaultTokenBudget { get; init; } = 1500;
    public double DefaultMinScore { get; init; } = 0.6;
    public double MemoryDedupThreshold { get; init; } = 0.85;
    public string ModelId { get; init; } = "multilingual-e5-small";
    public int ExpectedDimension { get; init; } = 384;
}

/// <summary>
/// Исключение несовпадения размерности вектора эмбеддинга (TASK-053).
/// </summary>
public class KnowledgeDimensionMismatchException : Exception
{
    public int ExpectedDimension { get; }
    public int ActualDimension { get; }

    public KnowledgeDimensionMismatchException(int expected, int actual)
        : base($"Embedding dimension mismatch: expected {expected}, but embedding provider produced {actual}. Re-indexing required.")
    {
        ExpectedDimension = expected;
        ActualDimension = actual;
    }
}

/// <summary>
/// Реестр навыков базы знаний (TASK-053).
/// </summary>
public interface IKnowledgeSkillRegistry : ISkillInvoker
{
    void RegisterSkill(ISkill skill);
    ISkill? FindSkill(string skillId);
    IReadOnlyList<ISkill> GetAllSkills();
}

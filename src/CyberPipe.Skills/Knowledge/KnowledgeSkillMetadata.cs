namespace CyberPipe.Skills.Knowledge;

/// <summary>
/// Описание метаданных навыка базы знаний и памяти (TASK-053).
/// </summary>
public sealed record KnowledgeSkillDescriptor
{
    public required string SkillId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public string InputSchemaJson { get; init; } = "{}";
}

/// <summary>
/// Каталог дескрипторов навыков базы знаний (TASK-053).
/// </summary>
public static class KnowledgeSkillCatalog
{
    public static readonly IReadOnlyList<KnowledgeSkillDescriptor> AllDescriptors =
    [
        new()
        {
            SkillId = "knowledge.ingest",
            Name = "Ingest Document",
            Description = "Ingests a document or note into the semantic knowledge base by chunking and embedding.",
            Category = "knowledge",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"documentId\":{\"type\":\"string\"},\"text\":{\"type\":\"string\"},\"source\":{\"type\":\"string\"},\"tags\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"required\":[\"documentId\",\"text\"]}",
        },
        new()
        {
            SkillId = "knowledge.search",
            Name = "Search Knowledge",
            Description = "Performs semantic search over ingested knowledge documents.",
            Category = "knowledge",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"},\"topK\":{\"type\":\"integer\",\"default\":5},\"minScore\":{\"type\":\"number\",\"default\":0.5}},\"required\":[\"query\"]}",
        },
        new()
        {
            SkillId = "knowledge.forget",
            Name = "Forget Knowledge",
            Description = "Removes a document and all its chunks/embeddings from the knowledge base.",
            Category = "knowledge",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"documentId\":{\"type\":\"string\"}},\"required\":[\"documentId\"]}",
        },
        new()
        {
            SkillId = "memory.remember",
            Name = "Remember Fact",
            Description = "Saves a short factual memory for the assistant with automatic similarity deduplication.",
            Category = "memory",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"fact\":{\"type\":\"string\"}},\"required\":[\"fact\"]}",
        },
        new()
        {
            SkillId = "memory.recall",
            Name = "Recall Memory",
            Description = "Recalls long-term memory facts relevant to the query.",
            Category = "memory",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"},\"topK\":{\"type\":\"integer\",\"default\":5},\"minScore\":{\"type\":\"number\",\"default\":0.5}},\"required\":[\"query\"]}",
        }
    ];
}

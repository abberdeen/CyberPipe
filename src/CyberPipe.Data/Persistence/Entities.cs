using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CyberPipe.Data.Persistence;

// Таблицы повторяют схему SQL-миграций 0001–0007 один в один (имена таблиц и колонок),
// чтобы существующие файлы БД продолжали работать без пересоздания.

/// <summary>Строка таблицы embeddings.</summary>
internal sealed class EmbeddingEntity
{
    public string Id { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public int Dimension { get; set; }
    public string Embedding { get; set; } = string.Empty;
    public string TextHash { get; set; } = string.Empty;
    public string Metadata { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Строка таблицы semantic_cache (зарезервирована схемой; поиск идёт через embeddings).</summary>
internal sealed class SemanticCacheEntity
{
    public string Id { get; set; } = string.Empty;
    public string CacheKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? Embedding { get; set; }
    public string ResponseText { get; set; } = string.Empty;
    public string Metadata { get; set; } = "{}";
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Строка таблицы chains.</summary>
internal sealed class ChainEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DefinitionJson { get; set; } = "{}";
    public bool IsEnabled { get; set; }
    public bool RequiresHitl { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Строка таблицы chain_runs.</summary>
internal sealed class ChainRunEntity
{
    public string Id { get; set; } = string.Empty;
    public string ChainId { get; set; } = string.Empty;
    public string? TriggerEventId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? ErrorJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Строка таблицы chain_checkpoints.</summary>
internal sealed class CheckpointEntity
{
    public string Id { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string ChainId { get; set; } = string.Empty;
    public string Step { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? ErrorJson { get; set; }
    public DateTimeOffset CheckpointAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Строка таблицы approvals.</summary>
internal sealed class ApprovalEntity
{
    public string Id { get; set; } = string.Empty;
    public string? RunId { get; set; }
    public string? ChainId { get; set; }
    public string Step { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string? Decision { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Строка таблицы token_budget_ledger.</summary>
internal sealed class TokenBudgetEntity
{
    public string Id { get; set; } = string.Empty;
    public DateOnly BudgetDate { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public string HostingType { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Строка таблицы kv_settings.</summary>
internal sealed class SettingEntity
{
    public string Key { get; set; } = string.Empty;
    public string ValueJson { get; set; } = "{}";
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Строка таблицы audit_log.</summary>
internal sealed class AuditEntity
{
    public long Id { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string DetailsJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Строка таблицы chain_hitl_approvals.</summary>
internal sealed class HitlEntity
{
    public string Id { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string ChainId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string PreviewText { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string AttachFilesJson { get; set; } = "[]";
    public string Status { get; set; } = string.Empty;
    public string? ResolutionJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>Строка таблицы netrunner_packages.</summary>
internal sealed class PackageEntity
{
    public string Slug { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ManifestHash { get; set; } = string.Empty;
    public string PackageDir { get; set; } = string.Empty;
    public string ManifestJson { get; set; } = "{}";
    public DateTimeOffset RegisteredAt { get; set; }
}

/// <summary>Строка таблицы personal_notes.</summary>
internal sealed class NoteEntity
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Строка таблицы personal_finance.</summary>
internal sealed class FinanceEntity
{
    public string Id { get; set; } = string.Empty;
    public int Kind { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Строка таблицы personal_reminders.</summary>
internal sealed class ReminderEntity
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset? DueTime { get; set; }
    public string? CronExpression { get; set; }
    public int Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>Строка таблицы personal_contacts.</summary>
internal sealed class ContactEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Channels { get; set; } = new();
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Строка таблицы personal_interactions.</summary>
internal sealed class InteractionEntity
{
    public string Id { get; set; } = string.Empty;
    public string ContactId { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>JSON-конвертеры колонок TEXT для коллекций.</summary>
internal static class JsonColumnConversions
{
    public static ValueConverter<List<string>, string> StringList { get; } = new(
        v => JsonSerializer.Serialize(v),
        v => string.IsNullOrWhiteSpace(v)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(v) ?? new List<string>());

    public static ValueConverter<Dictionary<string, string>, string> StringMap { get; } = new(
        v => JsonSerializer.Serialize(v),
        v => string.IsNullOrWhiteSpace(v)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(v) ?? new Dictionary<string, string>());
}

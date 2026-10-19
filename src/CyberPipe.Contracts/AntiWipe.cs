using System.Security.Cryptography;
using System.Text;

namespace CyberPipe.Contracts;

/// <summary>
/// Вид потенциально деструктивной операции (TASK-070).
/// </summary>
public enum DestructiveOperationKind
{
    /// <summary>SQL-выражение (DROP, TRUNCATE, DELETE, UPDATE).</summary>
    SqlStatement = 0,

    /// <summary>Удаление файла или каталога.</summary>
    FileDelete = 1,

    /// <summary>Перезапись существующего файла или конфигурации.</summary>
    FileOverwrite = 2,

    /// <summary>Массовая очистка данных (сброс кэша, удаление всех записей таблицы).</summary>
    BulkDataClear = 3,

    /// <summary>Сброс параметров службы или цепочек к заводским.</summary>
    ServiceReset = 4,
}

/// <summary>
/// Решение Anti-wipe guard (TASK-070).
/// </summary>
public enum WipeGuardDecisionOutcome
{
    /// <summary>Операция безопасна и разрешена к выполнению.</summary>
    Allow = 0,

    /// <summary>Операция запрещена безусловно (fail-closed, удаление критических каталогов).</summary>
    Deny = 1,

    /// <summary>Операция деструктивна, но допустима при явном подтверждении владельцем через HITL.</summary>
    RequireApproval = 2,
}

/// <summary>
/// Стандартные коды причин решений Anti-wipe guard.
/// </summary>
public static class WipeGuardReasonCodes
{
    public const string SafeOperation = "SafeOperation";
    public const string ProtectedPath = "ProtectedPath";
    public const string DropStatement = "DropStatement";
    public const string TruncateStatement = "TruncateStatement";
    public const string MissingWhereClause = "MissingWhereClause";
    public const string BulkThresholdExceeded = "BulkThresholdExceeded";
    public const string UnclassifiedDestructive = "UnclassifiedDestructive";
    public const string ClassifierFailure = "ClassifierFailure";
    public const string ServiceReset = "ServiceReset";
    public const string ProofAccepted = "ProofAccepted";
    public const string InvalidProof = "InvalidProof";
    public const string ProofAlreadyUsed = "ProofAlreadyUsed";
}

/// <summary>
/// Описание операции для проверки Anti-wipe guard.
/// </summary>
public sealed record DestructiveOperation
{
    /// <summary>Вид операции.</summary>
    public required DestructiveOperationKind Kind { get; init; }

    /// <summary>Цель операции (имя таблицы, путь к файлу/папке, идентификатор ресурса).</summary>
    public required string Target { get; init; }

    /// <summary>Текст команды или SQL-выражения (может быть null для файловых операций).</summary>
    public string? StatementOrCommand { get; init; }

    /// <summary>Оценка объёма затронутых строк или файлов (если известна).</summary>
    public int? EstimatedCount { get; init; }

    /// <summary>Признак обратимости операции со стороны вызывающего кода.</summary>
    public bool IsReversible { get; init; }

    /// <summary>Источник вызова (канал, цепочка, навык).</summary>
    public string Source { get; init; } = "unknown";

    /// <summary>
    /// Вычисляет криптографический отпечаток операции для привязки подтверждения HITL.
    /// Отпечаток зависит от вида, нормализованной цели и нормализованной команды.
    /// </summary>
    public string ComputeFingerprint()
    {
        string normalizedTarget = (Target ?? string.Empty).Trim().Replace('\\', '/').ToLowerInvariant();
        string normalizedCmd = (StatementOrCommand ?? string.Empty).Trim().ToLowerInvariant();
        string raw = $"{Kind}:{normalizedTarget}:{normalizedCmd}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// Подтверждение HITL-решения владельца для выполнения деструктивной операции (TASK-046 / TASK-070).
/// </summary>
public sealed record ApprovalProof
{
    /// <summary>Идентификатор запроса подтверждения HITL.</summary>
    public required Guid ApprovalId { get; init; }

    /// <summary>Отпечаток операции, на которую выдано подтверждение.</summary>
    public required string OperationFingerprint { get; init; }

    /// <summary>Метка времени решения в UTC.</summary>
    public DateTimeOffset ApprovedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Идентификатор владельца, выдавшего подтверждение.</summary>
    public string ApprovedBy { get; init; } = "owner";
}

/// <summary>
/// Результат оценки операции защитным барьером Anti-wipe guard.
/// </summary>
public sealed record WipeGuardDecision
{
    /// <summary>Решение: разрешить, запретить или запросить подтверждение.</summary>
    public required WipeGuardDecisionOutcome Outcome { get; init; }

    /// <summary>Код причины из <see cref="WipeGuardReasonCodes"/>.</summary>
    public required string ReasonCode { get; init; }

    /// <summary>Человекочитаемое объяснение решения.</summary>
    public required string Message { get; init; }

    /// <summary>Отпечаток операции для связывания с ApprovalProof при запросе подтверждения.</summary>
    public required string OperationFingerprint { get; init; }
}

/// <summary>
/// Параметры конфигурации Anti-wipe guard (TASK-003 / TASK-070).
/// </summary>
public sealed record AntiWipeOptions
{
    /// <summary>Список защищённых путей и префиксов, удаление которых безусловно запрещено или требует подтверждения.</summary>
    public IReadOnlyList<string> ProtectedPaths { get; init; } =
    [
        "data",
        "data/",
        "config",
        "backups",
        "backups/",
        "packages",
        "appsettings.json",
        "secrets.json",
        "cyberpipe.db",
    ];

    /// <summary>Порог количества затронутых строк/файлов для массового удаления, требующего подтверждения.</summary>
    public int BulkDeleteThreshold { get; init; } = 50;

    /// <summary>Запрещать безусловно (Deny) критические операции вроде DROP DATABASE и удаление корневых каталогов.</summary>
    public bool StrictDenyForCriticalTargets { get; init; } = true;

    /// <summary>Режим по умолчанию для неклассифицированных операций (fail-closed = Deny).</summary>
    public WipeGuardDecisionOutcome DefaultUnclassifiedOutcome { get; init; } = WipeGuardDecisionOutcome.Deny;
}

/// <summary>
/// Семантический барьер Anti-wipe guard (TASK-070).
/// Проверяет намерения операций до их исполнения. Сам guard операций не выполняет.
/// </summary>
public interface IAntiWipeGuard
{
    /// <summary>Оценивает потенциально деструктивную операцию до исполнения.</summary>
    Task<WipeGuardDecision> EvaluateAsync(DestructiveOperation operation, CancellationToken cancellationToken = default);

    /// <summary>Оценивает операцию с предъявленным подтверждением HITL от владельца.</summary>
    Task<WipeGuardDecision> EvaluateConfirmedAsync(DestructiveOperation operation, ApprovalProof proof, CancellationToken cancellationToken = default);
}

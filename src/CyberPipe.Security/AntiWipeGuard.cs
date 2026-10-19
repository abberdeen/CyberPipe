using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CyberPipe.Security;

/// <summary>
/// Семантический барьер Anti-wipe guard (TASK-070).
/// Классифицирует операции на предмет необратимого уничтожения данных до их исполнения.
/// Сам guard операций не выполняет (нет вызовов ФС, SQL или запуска процессов).
/// </summary>
public sealed class AntiWipeGuard : IAntiWipeGuard
{
    private static readonly Regex DropDatabaseOrSchemaRegex = new(
        @"\bDROP\s+(DATABASE|SCHEMA)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DropTableRegex = new(
        @"\bDROP\s+TABLE\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TruncateRegex = new(
        @"\bTRUNCATE(\s+TABLE)?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DeleteRegex = new(
        @"\bDELETE(\s+FROM)?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UpdateRegex = new(
        @"\bUPDATE\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WhereClauseRegex = new(
        @"\bWHERE\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly AntiWipeOptions _options;
    private readonly ILogger<AntiWipeGuard> _logger;
    private readonly ConcurrentDictionary<Guid, string> _usedProofs = new();

    public AntiWipeGuard(AntiWipeOptions? options = null, ILogger<AntiWipeGuard>? logger = null)
    {
        _options = options ?? new AntiWipeOptions();
        _logger = logger ?? NullLogger<AntiWipeGuard>.Instance;
    }

    /// <inheritdoc />
    public Task<WipeGuardDecision> EvaluateAsync(DestructiveOperation operation, CancellationToken cancellationToken = default)
    {
        // Fail-closed при невалидном вводе
        if (operation is null || string.IsNullOrWhiteSpace(operation.Target))
        {
            _logger.LogWarning("AntiWipe: Fail-closed due to null or empty operation.");
            return Task.FromResult(new WipeGuardDecision
            {
                Outcome = WipeGuardDecisionOutcome.Deny,
                ReasonCode = WipeGuardReasonCodes.ClassifierFailure,
                Message = "Operation or target is null or empty. Rejected under fail-closed policy.",
                OperationFingerprint = string.Empty,
            });
        }

        string fingerprint = operation.ComputeFingerprint();

        try
        {
            WipeGuardDecision decision = ClassifyOperation(operation, fingerprint);
            _logger.LogInformation(
                "AntiWipe evaluated {Kind} on target {Target}: Outcome={Outcome}, Reason={Reason}",
                operation.Kind,
                SanitizeTargetForLog(operation.Target),
                decision.Outcome,
                decision.ReasonCode);
            return Task.FromResult(decision);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AntiWipe classifier failed. Defaulting to fail-closed Deny.");
            return Task.FromResult(new WipeGuardDecision
            {
                Outcome = WipeGuardDecisionOutcome.Deny,
                ReasonCode = WipeGuardReasonCodes.ClassifierFailure,
                Message = "Classifier exception occurred. Rejected under fail-closed policy.",
                OperationFingerprint = fingerprint,
            });
        }
    }

    /// <inheritdoc />
    public Task<WipeGuardDecision> EvaluateConfirmedAsync(DestructiveOperation operation, ApprovalProof proof, CancellationToken cancellationToken = default)
    {
        if (operation is null || proof is null)
        {
            return Task.FromResult(new WipeGuardDecision
            {
                Outcome = WipeGuardDecisionOutcome.Deny,
                ReasonCode = WipeGuardReasonCodes.ClassifierFailure,
                Message = "Operation or proof is null.",
                OperationFingerprint = string.Empty,
            });
        }

        string currentFingerprint = operation.ComputeFingerprint();

        // Сверка отпечатков операции
        if (!string.Equals(currentFingerprint, proof.OperationFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Approval proof fingerprint mismatch for approval {ApprovalId}: proof={ProofFingerprint}, current={CurrentFingerprint}",
                proof.ApprovalId,
                proof.OperationFingerprint,
                currentFingerprint);

            return Task.FromResult(new WipeGuardDecision
            {
                Outcome = WipeGuardDecisionOutcome.RequireApproval,
                ReasonCode = WipeGuardReasonCodes.InvalidProof,
                Message = "Approval proof fingerprint does not match target operation parameters.",
                OperationFingerprint = currentFingerprint,
            });
        }

        // Одноразовость: проверяем, не использовался ли уже proof
        if (!_usedProofs.TryAdd(proof.ApprovalId, currentFingerprint))
        {
            _logger.LogWarning("Approval proof {ApprovalId} was already consumed.", proof.ApprovalId);
            return Task.FromResult(new WipeGuardDecision
            {
                Outcome = WipeGuardDecisionOutcome.RequireApproval,
                ReasonCode = WipeGuardReasonCodes.ProofAlreadyUsed,
                Message = "Approval proof has already been consumed and cannot be replayed.",
                OperationFingerprint = currentFingerprint,
            });
        }

        _logger.LogInformation("Approval proof {ApprovalId} accepted for operation {Fingerprint}.", proof.ApprovalId, currentFingerprint);

        return Task.FromResult(new WipeGuardDecision
        {
            Outcome = WipeGuardDecisionOutcome.Allow,
            ReasonCode = WipeGuardReasonCodes.ProofAccepted,
            Message = $"Operation explicitly confirmed by owner via approval {proof.ApprovalId}.",
            OperationFingerprint = currentFingerprint,
        });
    }

    private WipeGuardDecision ClassifyOperation(DestructiveOperation operation, string fingerprint)
    {
        // 1. Проверка защищённых путей для файловых операций и массовой очистки
        if (operation.Kind is DestructiveOperationKind.FileDelete or DestructiveOperationKind.FileOverwrite or DestructiveOperationKind.BulkDataClear)
        {
            if (IsProtectedPath(operation.Target, out bool isStrictCritical))
            {
                if (isStrictCritical && _options.StrictDenyForCriticalTargets)
                {
                    return new WipeGuardDecision
                    {
                        Outcome = WipeGuardDecisionOutcome.Deny,
                        ReasonCode = WipeGuardReasonCodes.ProtectedPath,
                        Message = $"Target path '{operation.Target}' is strictly protected from deletion or modification.",
                        OperationFingerprint = fingerprint,
                    };
                }

                return new WipeGuardDecision
                {
                    Outcome = WipeGuardDecisionOutcome.RequireApproval,
                    ReasonCode = WipeGuardReasonCodes.ProtectedPath,
                    Message = $"Target path '{operation.Target}' is a protected resource. Explicit owner approval required.",
                    OperationFingerprint = fingerprint,
                };
            }
        }

        // 2. Классификация SQL-операций
        if (operation.Kind == DestructiveOperationKind.SqlStatement)
        {
            string sql = operation.StatementOrCommand ?? string.Empty;

            if (DropDatabaseOrSchemaRegex.IsMatch(sql))
            {
                if (_options.StrictDenyForCriticalTargets)
                {
                    return new WipeGuardDecision
                    {
                        Outcome = WipeGuardDecisionOutcome.Deny,
                        ReasonCode = WipeGuardReasonCodes.DropStatement,
                        Message = "Dropping database or schema is forbidden.",
                        OperationFingerprint = fingerprint,
                    };
                }

                return new WipeGuardDecision
                {
                    Outcome = WipeGuardDecisionOutcome.RequireApproval,
                    ReasonCode = WipeGuardReasonCodes.DropStatement,
                    Message = "Dropping database or schema requires explicit owner approval.",
                    OperationFingerprint = fingerprint,
                };
            }

            if (DropTableRegex.IsMatch(sql))
            {
                return new WipeGuardDecision
                {
                    Outcome = WipeGuardDecisionOutcome.RequireApproval,
                    ReasonCode = WipeGuardReasonCodes.DropStatement,
                    Message = "Dropping table requires explicit owner approval.",
                    OperationFingerprint = fingerprint,
                };
            }

            if (TruncateRegex.IsMatch(sql))
            {
                return new WipeGuardDecision
                {
                    Outcome = WipeGuardDecisionOutcome.RequireApproval,
                    ReasonCode = WipeGuardReasonCodes.TruncateStatement,
                    Message = "Truncating table requires explicit owner approval.",
                    OperationFingerprint = fingerprint,
                };
            }

            if (DeleteRegex.IsMatch(sql))
            {
                bool hasWhere = WhereClauseRegex.IsMatch(sql);
                if (!hasWhere)
                {
                    return new WipeGuardDecision
                    {
                        Outcome = WipeGuardDecisionOutcome.RequireApproval,
                        ReasonCode = WipeGuardReasonCodes.MissingWhereClause,
                        Message = "DELETE statement without WHERE clause affects all rows and requires explicit owner approval.",
                        OperationFingerprint = fingerprint,
                    };
                }

                if (operation.EstimatedCount.HasValue && operation.EstimatedCount.Value > _options.BulkDeleteThreshold)
                {
                    return new WipeGuardDecision
                    {
                        Outcome = WipeGuardDecisionOutcome.RequireApproval,
                        ReasonCode = WipeGuardReasonCodes.BulkThresholdExceeded,
                        Message = $"Estimated affected rows ({operation.EstimatedCount.Value}) exceeds safety threshold ({_options.BulkDeleteThreshold}).",
                        OperationFingerprint = fingerprint,
                    };
                }

                return new WipeGuardDecision
                {
                    Outcome = WipeGuardDecisionOutcome.Allow,
                    ReasonCode = WipeGuardReasonCodes.SafeOperation,
                    Message = "Targeted DELETE with WHERE clause within safety threshold.",
                    OperationFingerprint = fingerprint,
                };
            }

            if (UpdateRegex.IsMatch(sql))
            {
                bool hasWhere = WhereClauseRegex.IsMatch(sql);
                if (!hasWhere)
                {
                    return new WipeGuardDecision
                    {
                        Outcome = WipeGuardDecisionOutcome.RequireApproval,
                        ReasonCode = WipeGuardReasonCodes.MissingWhereClause,
                        Message = "UPDATE statement without WHERE clause affects all rows and requires explicit owner approval.",
                        OperationFingerprint = fingerprint,
                    };
                }

                if (operation.EstimatedCount.HasValue && operation.EstimatedCount.Value > _options.BulkDeleteThreshold)
                {
                    return new WipeGuardDecision
                    {
                        Outcome = WipeGuardDecisionOutcome.RequireApproval,
                        ReasonCode = WipeGuardReasonCodes.BulkThresholdExceeded,
                        Message = $"Estimated affected rows ({operation.EstimatedCount.Value}) exceeds safety threshold ({_options.BulkDeleteThreshold}).",
                        OperationFingerprint = fingerprint,
                    };
                }

                return new WipeGuardDecision
                {
                    Outcome = WipeGuardDecisionOutcome.Allow,
                    ReasonCode = WipeGuardReasonCodes.SafeOperation,
                    Message = "Targeted UPDATE with WHERE clause within safety threshold.",
                    OperationFingerprint = fingerprint,
                };
            }

            // Прочие SQL (SELECT, INSERT, CREATE TABLE и т.д.)
            return new WipeGuardDecision
            {
                Outcome = WipeGuardDecisionOutcome.Allow,
                ReasonCode = WipeGuardReasonCodes.SafeOperation,
                Message = "Safe non-destructive SQL statement.",
                OperationFingerprint = fingerprint,
            };
        }

        // 3. Файловые операции вне защищённых путей
        if (operation.Kind == DestructiveOperationKind.FileDelete)
        {
            if (operation.EstimatedCount.HasValue && operation.EstimatedCount.Value > _options.BulkDeleteThreshold)
            {
                return new WipeGuardDecision
                {
                    Outcome = WipeGuardDecisionOutcome.RequireApproval,
                    ReasonCode = WipeGuardReasonCodes.BulkThresholdExceeded,
                    Message = $"Recursive file deletion count ({operation.EstimatedCount.Value}) exceeds bulk threshold ({_options.BulkDeleteThreshold}).",
                    OperationFingerprint = fingerprint,
                };
            }

            return new WipeGuardDecision
            {
                Outcome = WipeGuardDecisionOutcome.Allow,
                ReasonCode = WipeGuardReasonCodes.SafeOperation,
                Message = "File deletion within safety threshold.",
                OperationFingerprint = fingerprint,
            };
        }

        // 4. Сброс службы или цепочек
        if (operation.Kind == DestructiveOperationKind.ServiceReset)
        {
            return new WipeGuardDecision
            {
                Outcome = WipeGuardDecisionOutcome.RequireApproval,
                ReasonCode = WipeGuardReasonCodes.ServiceReset,
                Message = "Resetting service configuration or active schedules requires explicit owner approval.",
                OperationFingerprint = fingerprint,
            };
        }

        // 5. Массовая очистка данных
        if (operation.Kind == DestructiveOperationKind.BulkDataClear)
        {
            return new WipeGuardDecision
            {
                Outcome = WipeGuardDecisionOutcome.RequireApproval,
                ReasonCode = WipeGuardReasonCodes.BulkThresholdExceeded,
                Message = "Bulk data clear operation requires confirmation.",
                OperationFingerprint = fingerprint,
            };
        }

        // 6. Неизвестная/неклассифицированная деструктивная операция — fail-closed
        return new WipeGuardDecision
        {
            Outcome = _options.DefaultUnclassifiedOutcome,
            ReasonCode = WipeGuardReasonCodes.UnclassifiedDestructive,
            Message = "Unclassified destructive operation. Decision governed by fail-closed policy.",
            OperationFingerprint = fingerprint,
        };
    }

    private bool IsProtectedPath(string target, out bool isStrictCritical)
    {
        isStrictCritical = false;
        string normalized = target.Trim().Replace('\\', '/').Trim('/');

        string[] criticalTargets = ["data", "config", "backups", "secrets.json", "appsettings.json", "cyberpipe.db"];
        foreach (string critical in criticalTargets)
        {
            if (string.Equals(normalized, critical, StringComparison.OrdinalIgnoreCase))
            {
                isStrictCritical = true;
                return true;
            }
        }

        foreach (string path in _options.ProtectedPaths)
        {
            string normPath = path.Trim().Replace('\\', '/').Trim('/');
            if (string.Equals(normalized, normPath, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(normPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string SanitizeTargetForLog(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return "<empty>";
        // Обезличиваем пути: заменяем имена пользователей в путях Users/username
        return Regex.Replace(target, @"([\\/]Users[\\/])[^\\/]+", "$1[REDACTED]", RegexOptions.IgnoreCase);
    }
}

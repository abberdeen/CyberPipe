using System.Text.Json;
using CyberPipe.Contracts;
using CyberPipe.Data;

namespace CyberPipe.Chains.Hitl;

/// <summary>
/// Реализация сервиса HITL-подтверждений (TASK-046).
/// Координирует сохранение запросов, рассылку карточек, проверку владельца, валидацию правок и вызовы исполнителя цепочек.
/// </summary>
public sealed class HitlApprovalService : IHitlApprovalService
{
    private readonly IHitlApprovalStoreService _repository;
    private readonly IReadOnlyList<IHitlCardSender> _cardSenders;
    private readonly IOwnerGuard _ownerGuard;
    private readonly IChainExecutor _executor;
    private readonly ISchemaValidator? _schemaValidator;
    private readonly IChainCatalogService? _chainDefinitions;
    private readonly INetrunnerPackageStoreService? _packages;
    private readonly IChainExecutionStoreService? _executions;

    /// <summary>
    /// Событие разрешения решения (для оповещения гейтов в том же процессе).
    /// </summary>
    public event Action<HitlApprovalResult>? DecisionResolved;

    /// <summary>
    /// Инициализирует сервис HITL-подтверждений.
    /// </summary>
    public HitlApprovalService(
        IHitlApprovalStoreService repository,
        IEnumerable<IHitlCardSender> cardSenders,
        IOwnerGuard ownerGuard,
        IChainExecutor executor,
        ISchemaValidator? schemaValidator = null,
        IChainCatalogService? chainDefinitions = null,
        INetrunnerPackageStoreService? packages = null,
        IChainExecutionStoreService? executions = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _cardSenders = cardSenders?.ToList() ?? throw new ArgumentNullException(nameof(cardSenders));
        _ownerGuard = ownerGuard ?? throw new ArgumentNullException(nameof(ownerGuard));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _schemaValidator = schemaValidator;
        _chainDefinitions = chainDefinitions;
        _packages = packages;
        _executions = executions;
    }

    /// <inheritdoc />
    public async Task<HitlApprovalRecord> CreateAsync(HitlApprovalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var record = new HitlApprovalRecord
        {
            ApprovalId = request.ApprovalId,
            RunId = request.RunId,
            ChainId = request.ChainId,
            Title = request.Title,
            PreviewText = request.PreviewText,
            PayloadJson = request.PayloadJson,
            AttachFiles = request.AttachFiles,
            CreatedAtUtc = request.CreatedAtUtc,
            ExpiresAtUtc = request.ExpiresAtUtc,
            Status = HitlStatus.Pending,
        };

        HitlApprovalRecord created = await _repository.CreateAsync(record, cancellationToken).ConfigureAwait(false);

        // Рассылка по всем доступным каналам. Отказ одного канала не отменяет запрос
        foreach (IHitlCardSender sender in _cardSenders)
        {
            try
            {
                await sender.SendCardAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Ошибка отправки карточки логируется, но не прерывает создание запроса
            }
        }

        return created;
    }

    /// <inheritdoc />
    public async Task<HitlApprovalResult> ApproveAsync(
        Guid approvalId,
        string ownerId,
        ChannelKind channel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        if (!await _ownerGuard.IsOwnerAsync(ownerId, cancellationToken).ConfigureAwait(false))
        {
            throw new HitlAccessDeniedException($"User '{ownerId}' is not authorized as system owner.");
        }

        HitlApprovalRecord record = await ValidatePendingRecordAsync(approvalId, cancellationToken).ConfigureAwait(false);

        var result = new HitlApprovalResult
        {
            ApprovalId = approvalId,
            Decision = HitlDecision.Approved,
            OwnerId = ownerId,
            Channel = channel,
            DecidedAtUtc = DateTimeOffset.UtcNow,
        };

        bool resolved = await _repository.TryResolveAsync(approvalId, result, cancellationToken).ConfigureAwait(false);
        if (!resolved)
        {
            throw new HitlAlreadyResolvedException($"Approval request '{approvalId}' has already been resolved or expired.");
        }

        if (_executions is not null)
        {
            try
            {
                await _executions.SaveCheckpointAsync(new ChainCheckpointDto
                {
                    RunId = record.RunId,
                    ChainId = record.ChainId,
                    Step = ChainStepKind.Hitl,
                    State = ChainStepState.Completed,
                    OutputJson = record.PayloadJson,
                    CheckpointAtUtc = DateTimeOffset.UtcNow,
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Standalone invocation without chain run record in DB
            }
        }

        await _executor.ResumeAsync(record.RunId, cancellationToken).ConfigureAwait(false);
        DecisionResolved?.Invoke(result);
        return result;
    }

    /// <inheritdoc />
    public async Task<HitlApprovalResult> EditAsync(
        Guid approvalId,
        string ownerId,
        string editedPayloadJson,
        ChannelKind channel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(editedPayloadJson);

        if (!await _ownerGuard.IsOwnerAsync(ownerId, cancellationToken).ConfigureAwait(false))
        {
            throw new HitlAccessDeniedException($"User '{ownerId}' is not authorized as system owner.");
        }

        HitlApprovalRecord record = await ValidatePendingRecordAsync(approvalId, cancellationToken).ConfigureAwait(false);

        // Проверка синтаксиса JSON
        try
        {
            using var doc = JsonDocument.Parse(editedPayloadJson);
        }
        catch (JsonException ex)
        {
            throw new HitlInvalidEditedPayloadException($"Edited payload is not valid JSON: {ex.Message}");
        }

        // Валидация по schema.output.json пакета скрипта, если валидатор доступен
        if (_schemaValidator is not null && _chainDefinitions is not null && _packages is not null)
        {
            ChainDefinitionDto? chain = await _chainDefinitions.GetByIdAsync(record.ChainId, cancellationToken).ConfigureAwait(false);
            if (chain is not null)
            {
                PackageRegistrationRecord? pkg = await _packages.GetPackageAsync(chain.Script.PackageSlug, chain.Script.PackageVersion, cancellationToken).ConfigureAwait(false);
                if (pkg is not null && !string.IsNullOrWhiteSpace(pkg.Manifest.Io.OutputSchemaFile))
                {
                    string schemaPath = Path.Combine(pkg.PackageDir, pkg.Manifest.Io.OutputSchemaFile);
                    if (File.Exists(schemaPath))
                    {
                        string schemaContent = await File.ReadAllTextAsync(schemaPath, cancellationToken).ConfigureAwait(false);
                        IReadOnlyList<string> errors = _schemaValidator.Validate(schemaContent, editedPayloadJson);
                        if (errors.Count > 0)
                        {
                            string details = string.Join("; ", errors);
                            throw new HitlInvalidEditedPayloadException($"Edited payload does not match package output schema: {details}");
                        }
                    }
                }
            }
        }

        var result = new HitlApprovalResult
        {
            ApprovalId = approvalId,
            Decision = HitlDecision.Edited,
            EditedPayloadJson = editedPayloadJson,
            OwnerId = ownerId,
            Channel = channel,
            DecidedAtUtc = DateTimeOffset.UtcNow,
        };

        bool resolved = await _repository.TryResolveAsync(approvalId, result, cancellationToken).ConfigureAwait(false);
        if (!resolved)
        {
            throw new HitlAlreadyResolvedException($"Approval request '{approvalId}' has already been resolved or expired.");
        }

        if (_executions is not null)
        {
            try
            {
                await _executions.SaveCheckpointAsync(new ChainCheckpointDto
                {
                    RunId = record.RunId,
                    ChainId = record.ChainId,
                    Step = ChainStepKind.Hitl,
                    State = ChainStepState.Completed,
                    OutputJson = editedPayloadJson,
                    CheckpointAtUtc = DateTimeOffset.UtcNow,
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Standalone invocation without chain run record in DB
            }
        }

        await _executor.ResumeAsync(record.RunId, cancellationToken).ConfigureAwait(false);
        DecisionResolved?.Invoke(result);
        return result;
    }

    /// <inheritdoc />
    public async Task<HitlApprovalResult> RejectAsync(
        Guid approvalId,
        string ownerId,
        ChannelKind channel,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        if (!await _ownerGuard.IsOwnerAsync(ownerId, cancellationToken).ConfigureAwait(false))
        {
            throw new HitlAccessDeniedException($"User '{ownerId}' is not authorized as system owner.");
        }

        HitlApprovalRecord record = await ValidatePendingRecordAsync(approvalId, cancellationToken).ConfigureAwait(false);

        string rejectionReason = string.IsNullOrWhiteSpace(reason) ? "HitlRejected" : reason;
        var result = new HitlApprovalResult
        {
            ApprovalId = approvalId,
            Decision = HitlDecision.Rejected,
            Reason = rejectionReason,
            OwnerId = ownerId,
            Channel = channel,
            DecidedAtUtc = DateTimeOffset.UtcNow,
        };

        bool resolved = await _repository.TryResolveAsync(approvalId, result, cancellationToken).ConfigureAwait(false);
        if (!resolved)
        {
            throw new HitlAlreadyResolvedException($"Approval request '{approvalId}' has already been resolved or expired.");
        }

        if (_executions is not null)
        {
            try
            {
                await _executions.SaveCheckpointAsync(new ChainCheckpointDto
                {
                    RunId = record.RunId,
                    ChainId = record.ChainId,
                    Step = ChainStepKind.Hitl,
                    State = ChainStepState.Failed,
                    Error = new ErrorDto { Code = "HitlRejected", Message = rejectionReason },
                    CheckpointAtUtc = DateTimeOffset.UtcNow,
                }, cancellationToken).ConfigureAwait(false);

                ChainRunState? existingRun = await _executions.GetRunAsync(record.RunId, cancellationToken).ConfigureAwait(false);
                if (existingRun is not null)
                {
                    await _executions.UpdateRunStatusAsync(record.RunId, ChainRunStatus.Cancelled, new ErrorDto
                    {
                        Code = "HitlRejected",
                        Message = rejectionReason,
                    }, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // Standalone invocation without chain run record in DB
            }
        }

        await _executor.CancelAsync(record.RunId, cancellationToken).ConfigureAwait(false);
        DecisionResolved?.Invoke(result);
        return result;
    }

    /// <inheritdoc />
    public async Task<int> ExpireOverdueAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<HitlApprovalRecord> expired = await _repository.ListPendingExpiredAsync(nowUtc, cancellationToken).ConfigureAwait(false);
        int resolvedCount = 0;

        foreach (HitlApprovalRecord record in expired)
        {
            var result = new HitlApprovalResult
            {
                ApprovalId = record.ApprovalId,
                Decision = HitlDecision.Expired,
                Reason = "HitlTimeout",
                OwnerId = "system",
                Channel = ChannelKind.Desktop,
                DecidedAtUtc = nowUtc,
            };

            bool resolved = await _repository.TryResolveAsync(record.ApprovalId, result, cancellationToken).ConfigureAwait(false);
            if (resolved)
            {
                resolvedCount++;

                if (_executions is not null)
                {
                    try
                    {
                        await _executions.SaveCheckpointAsync(new ChainCheckpointDto
                        {
                            RunId = record.RunId,
                            ChainId = record.ChainId,
                            Step = ChainStepKind.Hitl,
                            State = ChainStepState.Failed,
                            Error = new ErrorDto { Code = "HitlTimeout", Message = "HITL approval timed out." },
                            CheckpointAtUtc = nowUtc,
                        }, cancellationToken).ConfigureAwait(false);

                        ChainRunState? existingRun = await _executions.GetRunAsync(record.RunId, cancellationToken).ConfigureAwait(false);
                        if (existingRun is not null)
                        {
                            await _executions.UpdateRunStatusAsync(record.RunId, ChainRunStatus.Cancelled, new ErrorDto
                            {
                                Code = "HitlTimeout",
                                Message = "HITL approval timed out.",
                            }, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (Exception)
                    {
                        // Standalone invocation without chain run record in DB
                    }
                }

                await _executor.CancelAsync(record.RunId, cancellationToken).ConfigureAwait(false);
                DecisionResolved?.Invoke(result);
            }
        }

        return resolvedCount;
    }

    private async Task<HitlApprovalRecord> ValidatePendingRecordAsync(Guid approvalId, CancellationToken cancellationToken)
    {
        HitlApprovalRecord? record = await _repository.GetAsync(approvalId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            throw new HitlNotFoundException($"HITL approval request '{approvalId}' not found.");
        }

        if (record.Status == HitlStatus.Expired || record.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new HitlExpiredException($"HITL approval request '{approvalId}' has expired.");
        }

        if (record.Status == HitlStatus.Resolved)
        {
            throw new HitlAlreadyResolvedException($"HITL approval request '{approvalId}' is already resolved.");
        }

        return record;
    }
}

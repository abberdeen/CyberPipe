using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CyberPipe.Contracts;
using CyberPipe.Logging.Telemetry;

namespace CyberPipe.Chains.Execution;

/// <summary>
/// Исполнитель линейной цепочки Trigger → Skill → Script → (HITL-гейт) → Delivery (TASK-044).
/// Обеспечивает пошаговое исполнение с фиксацией чекпоинтов в репозитории после каждого шага.
/// При повторном старте с тем же ключом идемпотентности возвращает существующий запуск.
/// При возобновлении (ResumeAsync) продолжает с первого незавершённого шага, не повторяя завершённые.
/// </summary>
public sealed class ChainExecutor : IChainExecutor
{
    private readonly IChainExecutionRepository _repository;
    private readonly IChainDefinitionRepository _chainDefinitions;
    private readonly ISkillInvoker _skillInvoker;
    private readonly IScriptRunnerRegistry _scriptRunners;
    private readonly IPackageRegistryRepository _packageRegistry;
    private readonly IDeliveryPort _deliveryPort;
    private readonly IHitlApprovalGate? _hitlGate;
    private readonly IDelayedResumeScheduler? _resumeScheduler;
    private readonly string _artifactsRoot;
    private readonly ConcurrentDictionary<RunId, CancellationTokenSource> _activeCts = new();

    /// <summary>
    /// Создаёт экземпляр исполнителя цепочек.
    /// </summary>
    public ChainExecutor(
        IChainExecutionRepository repository,
        IChainDefinitionRepository chainDefinitions,
        ISkillInvoker skillInvoker,
        IScriptRunnerRegistry scriptRunners,
        IPackageRegistryRepository packageRegistry,
        IDeliveryPort deliveryPort,
        IHitlApprovalGate? hitlGate = null,
        IDelayedResumeScheduler? resumeScheduler = null,
        string? artifactsRoot = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _chainDefinitions = chainDefinitions ?? throw new ArgumentNullException(nameof(chainDefinitions));
        _skillInvoker = skillInvoker ?? throw new ArgumentNullException(nameof(skillInvoker));
        _scriptRunners = scriptRunners ?? throw new ArgumentNullException(nameof(scriptRunners));
        _packageRegistry = packageRegistry ?? throw new ArgumentNullException(nameof(packageRegistry));
        _deliveryPort = deliveryPort ?? throw new ArgumentNullException(nameof(deliveryPort));
        _hitlGate = hitlGate;
        _resumeScheduler = resumeScheduler;
        _artifactsRoot = artifactsRoot ?? Path.Combine(Path.GetTempPath(), "cyberpipe_artifacts");
    }

    /// <inheritdoc />
    public async Task<RunId> StartAsync(TriggerEvent triggerEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerEvent);

        // 1. Проверка идемпотентности по (ChainId, IdempotencyKey)
        RunId? existing = await _repository.FindRunByIdempotencyKeyAsync(triggerEvent.ChainId, triggerEvent.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return existing.Value;
        }

        // 2. Получение и валидация определения цепочки
        ChainDefinitionDto? definition = await _chainDefinitions.GetByIdAsync(triggerEvent.ChainId, cancellationToken);
        if (definition is null)
        {
            throw new ChainNotFoundException(triggerEvent.ChainId);
        }

        if (!definition.IsEnabled)
        {
            throw new InvalidOperationException($"Chain '{triggerEvent.ChainId}' is disabled and cannot be started.");
        }

        // 3. Создание записи запуска
        RunId runId = RunId.New();
        await _repository.CreateRunAsync(runId, triggerEvent.ChainId, triggerEvent, cancellationToken);

        // 4. Фиксация шага Trigger
        var triggerCheckpoint = new ChainCheckpointDto
        {
            RunId = runId,
            ChainId = triggerEvent.ChainId,
            Step = ChainStepKind.Trigger,
            State = ChainStepState.Completed,
            AttemptCount = 1,
            InputJson = triggerEvent.PayloadJson,
            OutputJson = triggerEvent.PayloadJson,
            CheckpointAtUtc = DateTimeOffset.UtcNow,
        };
        await _repository.SaveCheckpointAsync(triggerCheckpoint, cancellationToken);

        // 5. Исполнение конвейера шагов
        await ExecutePipelineAsync(runId, definition, triggerEvent, cancellationToken);

        return runId;
    }

    /// <inheritdoc />
    public async Task ResumeAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        ChainRunState? run = await _repository.GetRunAsync(runId, cancellationToken);
        if (run is null)
        {
            throw new KeyNotFoundException($"Chain run '{runId}' not found.");
        }

        if (run.Status is ChainRunStatus.Completed or ChainRunStatus.Failed or ChainRunStatus.Cancelled or ChainRunStatus.CancelledByCrash)
        {
            return;
        }

        ChainDefinitionDto? definition = await _chainDefinitions.GetByIdAsync(run.ChainId, cancellationToken);
        if (definition is null)
        {
            throw new ChainNotFoundException(run.ChainId);
        }

        await _repository.UpdateRunStatusAsync(runId, ChainRunStatus.Running, null, cancellationToken);
        await ExecutePipelineAsync(runId, definition, run.Trigger, cancellationToken);
    }

    /// <inheritdoc />
    public async Task CancelAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        if (_activeCts.TryRemove(runId, out CancellationTokenSource? cts))
        {
            cts.Cancel();
        }

        await _repository.UpdateRunStatusAsync(runId, ChainRunStatus.Cancelled, null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ChainRunState?> GetRunAsync(RunId runId, CancellationToken cancellationToken = default) =>
        _repository.GetRunAsync(runId, cancellationToken);

    private async Task ExecutePipelineAsync(
        RunId runId,
        ChainDefinitionDto def,
        TriggerEvent trigger,
        CancellationToken callerToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        _activeCts[runId] = linkedCts;
        CancellationToken ct = linkedCts.Token;

        using ChainRunTrace trace = ChainRunTrace.Start(runId, def.Id, trigger);
        try
        {
            IReadOnlyList<ChainCheckpointDto> rawCheckpoints = await _repository.GetCheckpointsAsync(runId, ct);
            var checkpoints = rawCheckpoints.ToDictionary(c => c.Step);

            string currentPayload = trigger.PayloadJson;
            IReadOnlyList<string> currentFiles = trigger.FileRefs;

            // --- ШАГ 1: SKILL ---
            if (checkpoints.TryGetValue(ChainStepKind.Skill, out ChainCheckpointDto? skillCp) && skillCp.State == ChainStepState.Completed)
            {
                currentPayload = skillCp.OutputJson ?? currentPayload;
            }
            else
            {
                int maxAttempts = Math.Max(1, def.RetryPolicy.MaxAttempts);
                int attempt = 0;
                bool success = false;
                SkillInvocationResult? result = null;

                while (attempt < maxAttempts)
                {
                    attempt++;
                    await _repository.SaveCheckpointAsync(new ChainCheckpointDto
                    {
                        RunId = runId,
                        ChainId = def.Id,
                        Step = ChainStepKind.Skill,
                        State = ChainStepState.Running,
                        AttemptCount = attempt,
                        InputJson = currentPayload,
                        CheckpointAtUtc = DateTimeOffset.UtcNow,
                    }, ct);

                    var req = new SkillInvocationRequest
                    {
                        SkillId = def.Skill.SkillId,
                        InputMapping = def.Skill.InputMapping,
                        PayloadJson = currentPayload,
                        FileRefs = currentFiles,
                        RunId = runId,
                    };

                    result = await _skillInvoker.InvokeSkillAsync(req, ct);
                    if (result.IsSuccess)
                    {
                        success = true;
                        break;
                    }

                    if (attempt < maxAttempts && def.RetryPolicy.BackoffMs > 0)
                    {
                        await Task.Delay(def.RetryPolicy.BackoffMs, ct);
                    }
                }

                if (!success)
                {
                    var err = result?.Error ?? new ErrorDto { Code = "Skill.Failed", Message = "Skill execution failed." };
                    await _repository.SaveCheckpointAsync(new ChainCheckpointDto
                    {
                        RunId = runId,
                        ChainId = def.Id,
                        Step = ChainStepKind.Skill,
                        State = ChainStepState.Failed,
                        AttemptCount = attempt,
                        InputJson = currentPayload,
                        Error = err,
                        CheckpointAtUtc = DateTimeOffset.UtcNow,
                    }, ct);

                    await _repository.UpdateRunStatusAsync(runId, ChainRunStatus.Failed, err, ct);
                    trace.SetOutcome("Failed", err.Code);
                    return;
                }

                currentPayload = result!.OutputJson;
                currentFiles = result.OutputFiles;

                await _repository.SaveCheckpointAsync(new ChainCheckpointDto
                {
                    RunId = runId,
                    ChainId = def.Id,
                    Step = ChainStepKind.Skill,
                    State = ChainStepState.Completed,
                    AttemptCount = attempt,
                    InputJson = trigger.PayloadJson,
                    OutputJson = currentPayload,
                    CheckpointAtUtc = DateTimeOffset.UtcNow,
                }, ct);
                trace.Event("skill.completed");
            }

            // --- ШАГ 2: SCRIPT ---
            if (checkpoints.TryGetValue(ChainStepKind.Script, out ChainCheckpointDto? scriptCp) && scriptCp.State == ChainStepState.Completed)
            {
                currentPayload = scriptCp.OutputJson ?? currentPayload;
            }
            else
            {
                PackageRegistrationRecord? pkg = await _packageRegistry.GetPackageAsync(def.Script.PackageSlug, def.Script.PackageVersion, ct);
                if (pkg is null)
                {
                    var err = new ErrorDto
                    {
                        Code = "Package.NotFound",
                        Message = $"Script package '{def.Script.PackageSlug}@{def.Script.PackageVersion}' is not registered.",
                    };
                    await _repository.SaveCheckpointAsync(new ChainCheckpointDto
                    {
                        RunId = runId,
                        ChainId = def.Id,
                        Step = ChainStepKind.Script,
                        State = ChainStepState.Failed,
                        AttemptCount = 1,
                        Error = err,
                        CheckpointAtUtc = DateTimeOffset.UtcNow,
                    }, ct);

                    await _repository.UpdateRunStatusAsync(runId, ChainRunStatus.Failed, err, ct);
                    trace.SetOutcome("Failed", err.Code);
                    return;
                }

                string scriptWorkspace = Path.Combine(_artifactsRoot, runId.ToString(), "script");

                int maxAttempts = Math.Max(1, def.RetryPolicy.MaxAttempts);
                int attempt = 0;
                bool success = false;
                ScriptRunResult? runResult = null;

                while (attempt < maxAttempts)
                {
                    attempt++;
                    await _repository.SaveCheckpointAsync(new ChainCheckpointDto
                    {
                        RunId = runId,
                        ChainId = def.Id,
                        Step = ChainStepKind.Script,
                        State = ChainStepState.Running,
                        AttemptCount = attempt,
                        InputJson = currentPayload,
                        CheckpointAtUtc = DateTimeOffset.UtcNow,
                    }, ct);

                    var inputFiles = new Dictionary<string, string>();
                    foreach (var (alias, pattern) in def.Script.FileInputBindings)
                    {
                        string? match = currentFiles.FirstOrDefault(f => Path.GetFileName(f).Equals(pattern, StringComparison.OrdinalIgnoreCase) || f.Contains(pattern));
                        if (match is not null)
                        {
                            inputFiles[alias] = match;
                        }
                    }

                    var scriptReq = new ScriptRunRequest
                    {
                        Manifest = pkg.Manifest,
                        PackageDir = pkg.PackageDir,
                        InputJson = currentPayload,
                        InputFiles = inputFiles,
                        RunWorkspaceDir = scriptWorkspace,
                    };

                    runResult = await _scriptRunners.RunAsync(scriptReq, ct);

                    if (runResult.Status == ScriptRunStatus.DeferRetry)
                    {
                        await _repository.SaveCheckpointAsync(new ChainCheckpointDto
                        {
                            RunId = runId,
                            ChainId = def.Id,
                            Step = ChainStepKind.Script,
                            State = ChainStepState.WaitingApproval,
                            AttemptCount = attempt,
                            OutputJson = runResult.OutputJson,
                            CheckpointAtUtc = DateTimeOffset.UtcNow,
                        }, ct);

                        await _repository.UpdateRunStatusAsync(runId, ChainRunStatus.WaitingForRetry, null, ct);

                        if (_resumeScheduler is not null)
                        {
                            await _resumeScheduler.ScheduleResumeAsync(
                                runId,
                                TimeSpan.FromSeconds(runResult.RetryAfterSeconds ?? 60),
                                ct);
                        }

                        trace.SetOutcome("WaitingForRetry");
                        return; // Память и поток освобождены
                    }

                    if (runResult.Status == ScriptRunStatus.Succeeded)
                    {
                        success = true;
                        break;
                    }

                    if (runResult.Status == ScriptRunStatus.Cancelled)
                    {
                        await _repository.UpdateRunStatusAsync(runId, ChainRunStatus.Cancelled, null, ct);
                        trace.SetOutcome("Cancelled");
                        return;
                    }

                    if (attempt < maxAttempts && def.RetryPolicy.BackoffMs > 0)
                    {
                        await Task.Delay(def.RetryPolicy.BackoffMs, ct);
                    }
                }

                if (!success)
                {
                    var err = new ErrorDto
                    {
                        Code = "Script." + (runResult?.Status.ToString() ?? "Failed"),
                        Message = runResult?.ErrorMessage ?? "Script failed.",
                    };

                    await _repository.SaveCheckpointAsync(new ChainCheckpointDto
                    {
                        RunId = runId,
                        ChainId = def.Id,
                        Step = ChainStepKind.Script,
                        State = ChainStepState.Failed,
                        AttemptCount = attempt,
                        Error = err,
                        CheckpointAtUtc = DateTimeOffset.UtcNow,
                    }, ct);

                    await _repository.UpdateRunStatusAsync(runId, ChainRunStatus.Failed, err, ct);
                    trace.SetOutcome("Failed", err.Code);
                    return;
                }

                currentPayload = runResult!.OutputJson ?? "{}";
                currentFiles = runResult.OutputFiles.Select(f => f.Path).ToArray();

                await _repository.SaveCheckpointAsync(new ChainCheckpointDto
                {
                    RunId = runId,
                    ChainId = def.Id,
                    Step = ChainStepKind.Script,
                    State = ChainStepState.Completed,
                    AttemptCount = attempt,
                    OutputJson = currentPayload,
                    CheckpointAtUtc = DateTimeOffset.UtcNow,
                }, ct);
                trace.Event("script.completed");
            }

            // --- ШАГ 3: HITL (если требуется) ---
            if (def.RequiresHitl)
            {
                if (checkpoints.TryGetValue(ChainStepKind.Hitl, out ChainCheckpointDto? hitlCp) && hitlCp.State == ChainStepState.Completed)
                {
                    currentPayload = hitlCp.OutputJson ?? currentPayload;
                }
                else
                {
                    if (_hitlGate is null)
                    {
                        var err = new ErrorDto
                        {
                            Code = "Hitl.GateMissing",
                            Message = "Chain requires HITL approval, but no IHitlApprovalGate configured.",
                        };
                        await _repository.UpdateRunStatusAsync(runId, ChainRunStatus.Failed, err, ct);
                        trace.SetOutcome("Failed", err.Code);
                        return;
                    }

                    await _repository.SaveCheckpointAsync(new ChainCheckpointDto
                    {
                        RunId = runId,
                        ChainId = def.Id,
                        Step = ChainStepKind.Hitl,
                        State = ChainStepState.WaitingApproval,
                        InputJson = currentPayload,
                        CheckpointAtUtc = DateTimeOffset.UtcNow,
                    }, ct);

                    await _repository.UpdateRunStatusAsync(runId, ChainRunStatus.WaitingForHitl, null, ct);

                    var approvalReq = new HitlApprovalRequest
                    {
                        ApprovalId = Guid.NewGuid(),
                        RunId = runId,
                        ChainId = def.Id,
                        Title = $"Approval for {def.Name}",
                        PreviewText = FormatDeliveryMessage(def.Delivery.MessageTemplate, currentPayload),
                        PayloadJson = currentPayload,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                        ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(24),
                    };

                    trace.Event("hitl.requested");
                    HitlApprovalResult decision = await _hitlGate.RequestApprovalAsync(approvalReq, ct);
                    trace.SetTag("hitl.decision", decision.Decision.ToString());

                    if (decision.Decision == HitlDecision.Approved)
                    {
                        await _repository.SaveCheckpointAsync(new ChainCheckpointDto
                        {
                            RunId = runId,
                            ChainId = def.Id,
                            Step = ChainStepKind.Hitl,
                            State = ChainStepState.Completed,
                            OutputJson = currentPayload,
                            CheckpointAtUtc = DateTimeOffset.UtcNow,
                        }, ct);

                        await _repository.UpdateRunStatusAsync(runId, ChainRunStatus.Running, null, ct);
                    }
                    else if (decision.Decision == HitlDecision.Edited)
                    {
                        currentPayload = decision.EditedPayloadJson ?? currentPayload;

                        await _repository.SaveCheckpointAsync(new ChainCheckpointDto
                        {
                            RunId = runId,
                            ChainId = def.Id,
                            Step = ChainStepKind.Hitl,
                            State = ChainStepState.Completed,
                            OutputJson = currentPayload,
                            CheckpointAtUtc = DateTimeOffset.UtcNow,
                        }, ct);

                        await _repository.UpdateRunStatusAsync(runId, ChainRunStatus.Running, null, ct);
                    }
                    else
                    {
                        var err = new ErrorDto
                        {
                            Code = "Hitl." + decision.Decision,
                            Message = $"HITL was {decision.Decision}: {decision.Reason}",
                        };

                        await _repository.SaveCheckpointAsync(new ChainCheckpointDto
                        {
                            RunId = runId,
                            ChainId = def.Id,
                            Step = ChainStepKind.Hitl,
                            State = ChainStepState.Failed,
                            Error = err,
                            CheckpointAtUtc = DateTimeOffset.UtcNow,
                        }, ct);

                        await _repository.UpdateRunStatusAsync(runId, ChainRunStatus.Failed, err, ct);
                        trace.SetOutcome("Failed", err.Code);
                        return;
                    }
                }
            }

            // --- ШАГ 4: DELIVERY ---
            if (checkpoints.TryGetValue(ChainStepKind.Delivery, out ChainCheckpointDto? delCp) && delCp.State == ChainStepState.Completed)
            {
                await _repository.UpdateRunStatusAsync(runId, ChainRunStatus.Completed, null, ct);
            }
            else
            {
                await _repository.SaveCheckpointAsync(new ChainCheckpointDto
                {
                    RunId = runId,
                    ChainId = def.Id,
                    Step = ChainStepKind.Delivery,
                    State = ChainStepState.Running,
                    AttemptCount = 1,
                    InputJson = currentPayload,
                    CheckpointAtUtc = DateTimeOffset.UtcNow,
                }, ct);

                var deliveryReq = new DeliveryRequest
                {
                    Channel = def.Delivery.Channel,
                    Target = def.Delivery.Target,
                    Text = FormatDeliveryMessage(def.Delivery.MessageTemplate, currentPayload),
                    PayloadJson = currentPayload,
                    RunId = runId,
                    AttachFiles = def.Delivery.SendFiles
                        ? currentFiles.Select(f => new MessageAttachmentDto
                        {
                            FileName = Path.GetFileName(f),
                            ContentType = "application/octet-stream",
                            FileRef = f,
                            SizeBytes = File.Exists(f) ? new FileInfo(f).Length : null,
                        }).ToArray()
                        : [],
                };

                DeliveryResult delRes = await _deliveryPort.SendAsync(deliveryReq, ct);

                if (delRes.IsSuccess)
                {
                    await _repository.SaveCheckpointAsync(new ChainCheckpointDto
                    {
                        RunId = runId,
                        ChainId = def.Id,
                        Step = ChainStepKind.Delivery,
                        State = ChainStepState.Completed,
                        AttemptCount = 1,
                        OutputJson = currentPayload,
                        CheckpointAtUtc = DateTimeOffset.UtcNow,
                    }, ct);

                    await _repository.UpdateRunStatusAsync(runId, ChainRunStatus.Completed, null, ct);
                    trace.Event("delivery.sent");
                    CyberPipeTelemetry.DeliveriesSent.Add(1, new TagList
                    {
                        { "chain.id", def.Id.Value.ToString() },
                        { "chain.run.id", runId.Value.ToString() },
                        { "delivery.channel", def.Delivery.Channel.ToString() },
                    });
                    trace.SetOutcome("Completed");
                }
                else
                {
                    var err = delRes.Error ?? new ErrorDto { Code = "Delivery.Failed", Message = "Delivery failed." };
                    await _repository.SaveCheckpointAsync(new ChainCheckpointDto
                    {
                        RunId = runId,
                        ChainId = def.Id,
                        Step = ChainStepKind.Delivery,
                        State = ChainStepState.Failed,
                        AttemptCount = 1,
                        Error = err,
                        CheckpointAtUtc = DateTimeOffset.UtcNow,
                    }, ct);

                    await _repository.UpdateRunStatusAsync(runId, ChainRunStatus.Failed, err, ct);
                    trace.SetOutcome("Failed", err.Code);
                }
            }
        }
        catch (OperationCanceledException)
        {
            trace.SetOutcome("Cancelled");
            await _repository.UpdateRunStatusAsync(runId, ChainRunStatus.Cancelled, null, CancellationToken.None);
        }
        finally
        {
            _activeCts.TryRemove(runId, out _);
        }
    }

    private static string FormatDeliveryMessage(string template, string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return payloadJson;
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                string result = template;
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    string placeholder = "{{" + prop.Name + "}}";
                    string placeholderWithDollar = "{{$." + prop.Name + "}}";
                    string val = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? string.Empty
                        : prop.Value.GetRawText();

                    result = result.Replace(placeholder, val, StringComparison.OrdinalIgnoreCase);
                    result = result.Replace(placeholderWithDollar, val, StringComparison.OrdinalIgnoreCase);
                }
                return result;
            }
        }
        catch
        {
            // If payload is not JSON, return template as is
        }

        return template;
    }
}

using CyberPipe.Contracts;
using CyberPipe.Data;

namespace CyberPipe.Chains.Execution;

/// <summary>
/// Сервис восстановления незавершённых запусков цепочек при старте системы (TASK-044).
/// Сканирует незавершённые запуски и применяет настроенную политику:
/// - ResumeFromCheckpoint: продолжить с первого незавершённого шага (завершённые не повторяются).
/// - StopAndMarkCancelledByCrash: пометить запуск и прерванный шаг как CancelledByCrash.
/// </summary>
public sealed class ChainRecoveryService
{
    private readonly ChainExecutionStoreService _repository;
    private readonly IChainExecutor _executor;
    private readonly CrashRecoveryPolicy _policy;

    /// <summary>
    /// Создаёт экземпляр сервиса восстановления.
    /// </summary>
    public ChainRecoveryService(
        ChainExecutionStoreService repository,
        IChainExecutor executor,
        CrashRecoveryPolicy policy = CrashRecoveryPolicy.ResumeFromCheckpoint)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _policy = policy;
    }

    /// <summary>
    /// Выполняет восстановление всех незавершённых запусков.
    /// </summary>
    public async Task<int> RecoverAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RunId> unfinishedRuns = await _repository.ListUnfinishedRunsAsync(cancellationToken);
        int recoveredCount = 0;

        foreach (RunId runId in unfinishedRuns)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_policy == CrashRecoveryPolicy.ResumeFromCheckpoint)
            {
                await _executor.ResumeAsync(runId, cancellationToken);
                recoveredCount++;
            }
            else if (_policy == CrashRecoveryPolicy.StopAndMarkCancelledByCrash)
            {
                IReadOnlyList<ChainCheckpointDto> checkpoints = await _repository.GetCheckpointsAsync(runId, cancellationToken);
                foreach (ChainCheckpointDto cp in checkpoints.Where(c => c.State == ChainStepState.Running))
                {
                    await _repository.SaveCheckpointAsync(cp with
                    {
                        State = ChainStepState.Failed,
                        Error = new ErrorDto
                        {
                            Code = "Crash.Interrupted",
                            Message = "Step was interrupted by process crash.",
                        },
                        CheckpointAtUtc = DateTimeOffset.UtcNow,
                    }, cancellationToken);
                }

                var crashError = new ErrorDto
                {
                    Code = "Crash.CancelledByCrash",
                    Message = "Run was marked CancelledByCrash during crash recovery.",
                };

                await _repository.UpdateRunStatusAsync(runId, ChainRunStatus.CancelledByCrash, crashError, cancellationToken);
                recoveredCount++;
            }
        }

        return recoveredCount;
    }
}

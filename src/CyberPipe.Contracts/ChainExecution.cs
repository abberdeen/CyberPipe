namespace CyberPipe.Contracts;

/// <summary>
/// Политика восстановления прерванных запусков цепочек при старте хоста (TASK-044).
/// </summary>
public enum CrashRecoveryPolicy
{
    /// <summary>Продолжить выполнение с первого незавершённого шага.</summary>
    ResumeFromCheckpoint,

    /// <summary>Остановить запуск и пометить как CancelledByCrash.</summary>
    StopAndMarkCancelledByCrash,
}

/// <summary>
/// Снимок состояния запуска цепочки (TASK-044).
/// </summary>
public sealed record ChainRunState
{
    /// <summary>Идентификатор запуска.</summary>
    public required RunId RunId { get; init; }

    /// <summary>Идентификатор исполняемой цепочки.</summary>
    public required ChainId ChainId { get; init; }

    /// <summary>Текущий статус запуска.</summary>
    public required ChainRunStatus Status { get; init; }

    /// <summary>Событие триггера, инициировавшее запуск.</summary>
    public required TriggerEvent Trigger { get; init; }

    /// <summary>Время начала запуска (UTC).</summary>
    public required DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>Время завершения запуска (UTC); null для активных запусков.</summary>
    public DateTimeOffset? FinishedAtUtc { get; init; }

    /// <summary>Ошибка запуска при статусе Failed.</summary>
    public ErrorDto? Error { get; init; }

    /// <summary>Список зафиксированных чекпоинтов шагов.</summary>
    public IReadOnlyList<ChainCheckpointDto> Checkpoints { get; init; } = [];
}

/// <summary>
/// Запрос на вызов C#-скилла в цепочке (TASK-044).
/// </summary>
public sealed record SkillInvocationRequest
{
    /// <summary>Идентификатор зарегистрированного навыка.</summary>
    public required string SkillId { get; init; }

    /// <summary>Отображение входов навыка.</summary>
    public IReadOnlyDictionary<string, string> InputMapping { get; init; } = new Dictionary<string, string>();

    /// <summary>Входная полезная нагрузка в JSON.</summary>
    public required string PayloadJson { get; init; }

    /// <summary>Входные файлы.</summary>
    public IReadOnlyList<string> FileRefs { get; init; } = [];

    /// <summary>Идентификатор запуска цепочки.</summary>
    public required RunId RunId { get; init; }
}

/// <summary>
/// Результат вызова навыка (TASK-044).
/// </summary>
public sealed record SkillInvocationResult
{
    /// <summary>Признак успешного завершения навыка.</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>Выходная полезная нагрузка в JSON.</summary>
    public string OutputJson { get; init; } = "{}";

    /// <summary>Выходные файлы навыка.</summary>
    public IReadOnlyList<string> OutputFiles { get; init; } = [];

    /// <summary>Ошибка при неудачном вызове.</summary>
    public ErrorDto? Error { get; init; }
}

/// <summary>
/// Интерфейс вызова хост-навыков цепочки (TASK-044).
/// </summary>
public interface ISkillInvoker
{
    /// <summary>Вызывает зарегистрированный навык с отображением входных данных.</summary>
    Task<SkillInvocationResult> InvokeSkillAsync(SkillInvocationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Планировщик отложенного возобновления цепочки после сигнала DeferRetry (TASK-044, TASK-045).
/// </summary>
public interface IDelayedResumeScheduler
{
    /// <summary>Планирует отложенное возобновление запуска цепочки.</summary>
    Task ScheduleResumeAsync(RunId runId, TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>Планирует отложенное возобновление конкретного шага запуска цепочки.</summary>
    Task ScheduleResumeAsync(RunId runId, TimeSpan delay, string stepId, CancellationToken cancellationToken = default) =>
        ScheduleResumeAsync(runId, delay, cancellationToken);
}

/// <summary>
/// Репозиторий состояния исполнения цепочек и чекпоинтов шагов (TASK-044).
/// </summary>
public interface IChainExecutionRepository
{
    /// <summary>Возвращает снимок состояния запуска по идентификатору.</summary>
    Task<ChainRunState?> GetRunAsync(RunId runId, CancellationToken cancellationToken = default);

    /// <summary>Ищет существующий запуск по ключу идемпотентности триггера.</summary>
    Task<RunId?> FindRunByIdempotencyKeyAsync(ChainId chainId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Создаёт новую запись запуска.</summary>
    Task<ChainRunState> CreateRunAsync(RunId runId, ChainId chainId, TriggerEvent trigger, CancellationToken cancellationToken = default);

    /// <summary>Обновляет статус запуска и опциональную ошибку.</summary>
    Task UpdateRunStatusAsync(RunId runId, ChainRunStatus status, ErrorDto? error = null, CancellationToken cancellationToken = default);

    /// <summary>Сохраняет чекпоинт шага цепочки.</summary>
    Task SaveCheckpointAsync(ChainCheckpointDto checkpoint, CancellationToken cancellationToken = default);

    /// <summary>Возвращает список всех чекпоинтов запуска.</summary>
    Task<IReadOnlyList<ChainCheckpointDto>> GetCheckpointsAsync(RunId runId, CancellationToken cancellationToken = default);

    /// <summary>Возвращает список идентификаторов всех незавершённых запусков.</summary>
    Task<IReadOnlyList<RunId>> ListUnfinishedRunsAsync(CancellationToken cancellationToken = default);
}

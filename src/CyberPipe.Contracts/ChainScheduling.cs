namespace CyberPipe.Contracts;

/// <summary>
/// Информация о запланированной цепочке по cron-расписанию (TASK-045).
/// </summary>
public sealed record ScheduledChainInfo
{
    /// <summary>Идентификатор цепочки.</summary>
    public required ChainId ChainId { get; init; }

    /// <summary>Cron-выражение расписания.</summary>
    public required string CronExpression { get; init; }

    /// <summary>Время следующего планового срабатывания (UTC); null если расписание неактивно или завершено.</summary>
    public DateTimeOffset? NextFireTimeUtc { get; init; }

    /// <summary>Признак приостановленного расписания.</summary>
    public bool IsPaused { get; init; }

    /// <summary>Политика пропусков расписания.</summary>
    public ChainMisfirePolicy MisfirePolicy { get; init; } = ChainMisfirePolicy.FireOnceNow;
}

/// <summary>
/// Исключение при попытке зарегистрировать невалидное cron-выражение расписания (TASK-045).
/// </summary>
public sealed class InvalidCronExpressionException : Exception
{
    /// <summary>Код ошибки.</summary>
    public string Code { get; } = "Scheduler.InvalidCronExpression";

    /// <summary>Невалидное cron-выражение.</summary>
    public string Expression { get; }

    /// <summary>Создаёт исключение с кодом Scheduler.InvalidCronExpression.</summary>
    public InvalidCronExpressionException(string expression, string message)
        : base(message)
    {
        Expression = expression;
    }

    /// <summary>Создаёт исключение с кодом Scheduler.InvalidCronExpression и внутренним исключением.</summary>
    public InvalidCronExpressionException(string expression, string message, Exception innerException)
        : base(message, innerException)
    {
        Expression = expression;
    }
}

/// <summary>
/// Сервис управления cron-расписанием цепочек (TASK-045).
/// </summary>
public interface IChainSchedulerService
{
    /// <summary>
    /// Регистрирует или заменяет расписание цепочки.
    /// При невалидном cron-выражении выбрасывает <see cref="InvalidCronExpressionException"/>.
    /// </summary>
    Task ScheduleChainAsync(
        ChainId chainId,
        string cronExpression,
        ChainMisfirePolicy misfirePolicy = ChainMisfirePolicy.FireOnceNow,
        CancellationToken cancellationToken = default);

    /// <summary>Приостанавливает срабатывания расписания цепочки.</summary>
    Task PauseChainAsync(ChainId chainId, CancellationToken cancellationToken = default);

    /// <summary>Возобновляет срабатывания расписания цепочки.</summary>
    Task ResumeChainAsync(ChainId chainId, CancellationToken cancellationToken = default);

    /// <summary>Удаляет цепочку из расписания планировщика.</summary>
    Task UnscheduleChainAsync(ChainId chainId, CancellationToken cancellationToken = default);

    /// <summary>Возвращает список всех зарегистрированных в расписании цепочек.</summary>
    Task<IReadOnlyList<ScheduledChainInfo>> ListScheduledAsync(CancellationToken cancellationToken = default);

    /// <summary>Выполняет ручной немедленный запуск цепочки через порт триггера.</summary>
    Task TriggerChainNowAsync(ChainId chainId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Синхронизирует расписание с репозиторием цепочек: регистрирует включённые Cron-цепочки
    /// и снимает отключённые или отсутствующие.
    /// </summary>
    Task SyncWithRepositoryAsync(
        IChainDefinitionRepository repository,
        CancellationToken cancellationToken = default);
}

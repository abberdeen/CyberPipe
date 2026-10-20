namespace CyberPipe.Contracts;

/// <summary>
/// Статус службы для health-проверки и Desktop. Потребители: TASK-020 (хостинг), TASK-021 (управление).
/// Допустимые значения <see cref="ServiceStatusDto.State" />: Running, Stopping, Stopped, Faulted.
/// </summary>
/// <param name="State">Состояние службы строкой.</param>
/// <param name="Version">Версия сборки службы.</param>
/// <param name="StartedAtUtc">Момент старта службы (UTC).</param>
/// <param name="LastError">Последняя ошибка службы; может отсутствовать.</param>
public sealed record ServiceStatusDto(
    string State,
    string Version,
    DateTimeOffset StartedAtUtc,
    string? LastError);

/// <summary>
/// Исход команды управления службой. Потребитель: TASK-021.
/// </summary>
public enum ServiceControlOutcome
{
    /// <summary>Команда выполнена.</summary>
    Ok = 0,

    /// <summary>Служба не установлена.</summary>
    ServiceNotInstalled = 1,

    /// <summary>Недостаточно прав.</summary>
    AccessDenied = 2,

    /// <summary>Истёк тайм-аут операции.</summary>
    Timeout = 3,

    /// <summary>Управляющий канал недоступен.</summary>
    ChannelUnavailable = 4,

    /// <summary>Операция завершена ошибкой.</summary>
    Faulted = 5,
}

/// <summary>
/// Результат команды управления службой. Потребитель: TASK-021, отображение TASK-061.
/// </summary>
/// <param name="Outcome">Исход команды.</param>
/// <param name="Error">Описание ошибки; может отсутствовать.</param>
public sealed record ServiceControlResult(ServiceControlOutcome Outcome, string? Error);

/// <summary>
/// Событие смены состояния службы. Публикуется через абстракцию сообщений; потребители: TASK-020, TASK-021.
/// </summary>
/// <param name="State">Новое состояние службы строкой.</param>
/// <param name="AtUtc">Момент смены состояния (UTC).</param>
public sealed record ServiceStateChanged(string State, DateTimeOffset AtUtc);

/// <summary>
/// Контракт управления службой для Desktop и CLI: Start, Stop, Restart и Status.
/// Реализация управления — TASK-021 поверх канала TASK-020; потребители — TASK-061, TASK-062.
/// </summary>
public interface IServiceControl
{
    /// <summary>Запускает службу.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат команды.</returns>
    Task<ServiceControlResult> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Останавливает службу.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат команды.</returns>
    Task<ServiceControlResult> StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Перезапускает службу: остановка до Stopped, затем запуск до Running.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат команды.</returns>
    Task<ServiceControlResult> RestartAsync(CancellationToken cancellationToken = default);

    /// <summary>Возвращает текущий статус службы.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Статус службы.</returns>
    Task<ServiceStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Расширенный контракт управления и непрерывного наблюдения за службой (TASK-021).
/// Потребитель: UI Desktop (TASK-061) и CLI (TASK-062).
/// </summary>
public interface IServiceController : IServiceControl
{
    /// <summary>
    /// Непрерывно наблюдает за состоянием службы с экспоненциальным бэкоффом при потере связи.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Асинхронный поток состояний службы.</returns>
    IAsyncEnumerable<ServiceStatusDto> WatchStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Команды RPC поверх управляющего Named Pipe канала (TASK-020/021/062):
/// Desktop и CLI вызывают контракты службы без ссылки на её реализацию.
/// Запрос: {"command": "...", "payload": "...json..."}; ответ — ServiceControlResponse со строкой-JSON.
/// </summary>
public static class ServicePipeCommands
{
    public const string Route = "route";
    public const string BudgetStatus = "budget.status";
    public const string BudgetAuthorize = "budget.authorize";
    public const string BudgetRecord = "budget.record";
    public const string ChainList = "chain.list";
    public const string ChainGet = "chain.get";
    public const string ChainRunsGet = "chain.runs.get";
    public const string ChainRunsUnfinished = "chain.runs.unfinished";
    public const string ChainRun = "chain.run";
    public const string ChainSchedule = "chain.schedule";
    public const string ChainSchedules = "chain.schedules";
    public const string ChainUnschedule = "chain.unschedule";
    public const string ChainPause = "chain.pause";
    public const string ChainResume = "chain.resume";
    public const string ChainTriggerNow = "chain.triggerNow";
    public const string HitlApprove = "hitl.approve";
    public const string HitlReject = "hitl.reject";
}

/// <summary>Запрос с одним Guid по pipe-каналу.</summary>
public sealed record GuidPipeRequest(Guid Id);

/// <summary>Запрос запуска цепочки по pipe-каналу.</summary>
public sealed record ChainRunPipeRequest(Guid ChainId, string PayloadJson);

/// <summary>Запрос планирования цепочки по pipe-каналу.</summary>
public sealed record ChainSchedulePipeRequest(Guid ChainId, string CronExpression);

/// <summary>Запрос HITL-решения по pipe-каналу.</summary>
public sealed record HitlDecisionPipeRequest(Guid ApprovalId, string OwnerId, ChannelKind Channel, string? Reason = null);

/// <summary>
/// Конверт ответа управляющего канала (сериализуется в JSON обеими сторонами).
/// </summary>
/// <param name="Success">Запрос обработан успешно.</param>
/// <param name="Data">Данные ответа.</param>
/// <param name="Message">Сообщение об ошибке; может отсутствовать.</param>
public sealed record ServiceControlResponse<T>(bool Success, T? Data, string? Message);

/// <summary>
/// Внутренний управляющий канал службы: health-проверка и запросы остановки/перезапуска.
/// Хостинг канала — TASK-020; клиент — TASK-021. Транспорт канала контрактом не фиксируется.
/// </summary>
public interface IServiceControlChannel
{
    /// <summary>Проверяет живость службы и возвращает её статус.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Статус службы.</returns>
    Task<ServiceStatusDto> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>Запрашивает остановку службы через канал.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача запроса.</returns>
    Task RequestStopAsync(CancellationToken cancellationToken = default);

    /// <summary>Запрашивает перезапуск службы через канал.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача запроса.</returns>
    Task RequestRestartAsync(CancellationToken cancellationToken = default);
}

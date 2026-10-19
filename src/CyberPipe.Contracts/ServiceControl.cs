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

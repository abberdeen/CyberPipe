namespace CyberPipe.Contracts;

/// <summary>Общее состояние здоровья процесса или компонента.</summary>
public enum HealthStatus
{
    /// <summary>Компонент работает штатно.</summary>
    Healthy = 0,

    /// <summary>Компонент работает частично или недоступна зависимость; работа не блокируется.</summary>
    Degraded = 1,

    /// <summary>Компонент не работает.</summary>
    Unhealthy = 2,
}

/// <summary>
/// Здоровье одного компонента для диагностики. Detail не содержит секретов и строк подключения.
/// </summary>
public sealed record ComponentHealthDto
{
    /// <summary>Имя компонента, например Database или MessageTransport.</summary>
    public required string Name { get; init; }

    /// <summary>Состояние компонента.</summary>
    public required HealthStatus Status { get; init; }

    /// <summary>Причина состояния без секретов; может отсутствовать при Healthy.</summary>
    public string? Detail { get; init; }

    /// <summary>Момент проверки (UTC).</summary>
    public required DateTimeOffset CheckedAtUtc { get; init; }
}

/// <summary>
/// Подключение к SQLite без строки подключения и без секретов: только хост, порт и имя базы.
/// Реальная проверка подключения — TASK-010 через <see cref="IDatabaseHealthProbe" />.
/// </summary>
public sealed record DatabaseHealthDto
{
    /// <summary>Состояние подключения.</summary>
    public required HealthStatus Status { get; init; }

    /// <summary>Путь к файлу SQLite из конфигурации; не секрет.</summary>
    public string? Path { get; init; }

    /// <summary>Хост сервера; для SQLite всегда null (встроенная база).</summary>
    public string? Host { get; init; }

    /// <summary>Порт сервера; для SQLite всегда null (встроенная база).</summary>
    public int? Port { get; init; }

    /// <summary>Имя базы данных из конфигурации; не секрет.</summary>
    public string? Database { get; init; }

    /// <summary>Причина состояния без секретов; может отсутствовать при Healthy.</summary>
    public string? Detail { get; init; }

    /// <summary>Момент проверки (UTC).</summary>
    public required DateTimeOffset CheckedAtUtc { get; init; }
}

/// <summary>Транспорт сообщений: только тип реализации, без секретов и адресов с учётными данными.</summary>
public sealed record TransportHealthDto
{
    /// <summary>Состояние транспорта.</summary>
    public required HealthStatus Status { get; init; }

    /// <summary>Полное имя типа реализации IMessageTransport; "none", если транспорт не настроен.</summary>
    public required string ImplementationType { get; init; }

    /// <summary>Причина состояния без секретов; может отсутствовать при Healthy.</summary>
    public string? Detail { get; init; }

    /// <summary>Момент проверки (UTC).</summary>
    public required DateTimeOffset CheckedAtUtc { get; init; }
}

/// <summary>
/// SLO-цели из конфигурации (targets, не ворота приёмки) рядом с фактическими замерами.
/// Actual-поля null, пока замер не выполнен: целей это не блокирует.
/// </summary>
public sealed record SloTargetsDto
{
    /// <summary>Целевое время ответа Tier0 в миллисекундах из конфигурации.</summary>
    public required int Tier0TargetMilliseconds { get; init; }

    /// <summary>Целевое время реакции на остановку в миллисекундах из конфигурации.</summary>
    public required int StopTargetMilliseconds { get; init; }

    /// <summary>Фактический замер Tier0 в миллисекундах; null — замера ещё не было.</summary>
    public int? Tier0ActualMilliseconds { get; init; }

    /// <summary>Фактический замер реакции на остановку в миллисекундах; null — замера ещё не было.</summary>
    public int? StopActualMilliseconds { get; init; }
}

/// <summary>
/// Отчёт диагностики процесса. Потребители: TASK-061, TASK-062 и задачи интеграции.
/// Не содержит секретов, строк подключения и значений из ISecretStore.
/// </summary>
public sealed record HealthReportDto
{
    /// <summary>Имя процесса: Service, Desktop, Cli или Brainstem.</summary>
    public required string ProcessName { get; init; }

    /// <summary>Состояние процесса строкой, например Running.</summary>
    public required string ProcessState { get; init; }

    /// <summary>Общее состояние: худшее из состояний компонентов.</summary>
    public required HealthStatus Status { get; init; }

    /// <summary>Версия приложения.</summary>
    public required string Version { get; init; }

    /// <summary>Момент старта процесса (UTC).</summary>
    public required DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>Момент сбора отчёта (UTC).</summary>
    public required DateTimeOffset CheckedAtUtc { get; init; }

    /// <summary>Подключение к SQLite без строки подключения.</summary>
    public required DatabaseHealthDto Database { get; init; }

    /// <summary>Транспорт сообщений: тип реализации без секретов.</summary>
    public required TransportHealthDto Transport { get; init; }

    /// <summary>SLO-цели из конфигурации и фактические замеры, если они есть.</summary>
    public required SloTargetsDto PerformanceTargets { get; init; }

    /// <summary>Компоненты, из которых собран отчёт.</summary>
    public IReadOnlyList<ComponentHealthDto> Components { get; init; } = [];
}

/// <summary>
/// Проверка подключения к SQLite для диагностики. Реальная реализация — TASK-010;
/// в тестах и до TASK-010 используются fake-реализации и заглушка недоступности.
/// </summary>
public interface IDatabaseHealthProbe
{
    /// <summary>Проверяет подключение и возвращает состояние без строки подключения.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Состояние базы данных.</returns>
    Task<DatabaseHealthDto> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Контракт диагностики состояния процесса. Реализация только читает состояние и не запускает
/// бизнес-логику. Потребители: TASK-061, TASK-062.
/// </summary>
public interface IDiagnosticsService
{
    /// <summary>
    /// Собирает отчёт о здоровье. Не бросает исключений при недоступной БД:
    /// недоступность отражается состоянием Degraded с причиной без секретов.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Отчёт о здоровье со всеми полями.</returns>
    Task<HealthReportDto> GetHealthAsync(CancellationToken cancellationToken = default);
}

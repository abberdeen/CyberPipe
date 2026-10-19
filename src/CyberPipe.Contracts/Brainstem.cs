namespace CyberPipe.Contracts;

/// <summary>
/// Запрос на выполнение задания в изолированном воркере Brainstem (TASK-022).
/// </summary>
/// <param name="JobId">Уникальный идентификатор задания.</param>
/// <param name="Kind">Вид задания (например, "Ping", "Whisper", "DocumentParse", "Script").</param>
/// <param name="Payload">Бинарные входные данные задания.</param>
/// <param name="TimeoutMs">Предельное время выполнения в миллисекундах.</param>
public sealed record BrainstemJobRequest(string JobId, string Kind, byte[] Payload, int TimeoutMs);

/// <summary>
/// Результат выполнения задания воркером Brainstem (TASK-022).
/// </summary>
/// <param name="JobId">Идентификатор задания.</param>
/// <param name="Ok">Признак успешного завершения.</param>
/// <param name="Payload">Бинарные выходные данные при успешном выполнении.</param>
/// <param name="Error">Сообщение об ошибке, если Ok == false.</param>
public sealed record BrainstemJobResult(string JobId, bool Ok, byte[]? Payload, string? Error);

/// <summary>
/// Интерфейс выполнения заданий воркером Brainstem.
/// </summary>
public interface IBrainstemWorker
{
    /// <summary>
    /// Выполняет задание в воркере с контролем отмены и тайм-аута.
    /// </summary>
    Task<BrainstemJobResult> ExecuteAsync(BrainstemJobRequest request, CancellationToken ct);
}

/// <summary>
/// Супервизор Brainstem-воркера (TASK-022).
/// Управляет жизненным циклом процесса воркера, следит за heartbeat,
/// перезапускает при сбоях и диспетчеризирует задания.
/// </summary>
public interface IBrainstemSupervisor : IBrainstemWorker
{
    /// <summary>
    /// Запускает воркер через песочницу и устанавливает канал связи.
    /// </summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Выполняет грациозную остановку воркера.
    /// </summary>
    Task StopAsync(CancellationToken ct);

    /// <summary>
    /// Текущее состояние супервизора: Stopped | Starting | Running | Restarting | Faulted.
    /// </summary>
    string State { get; }
}

/// <summary>
/// Событие смены состояния супервизора Brainstem.
/// </summary>
/// <param name="State">Новое состояние: Stopped | Starting | Running | Restarting | Faulted.</param>
/// <param name="Role">Роль воркера (например, "default").</param>
/// <param name="AtUtc">Время перехода в UTC.</param>
public sealed record BrainstemStateChanged(string State, string Role, DateTimeOffset AtUtc);

/// <summary>
/// Событие исчерпания попыток перезапуска Brainstem-воркера при переходе в Faulted.
/// </summary>
/// <param name="Role">Роль воркера.</param>
/// <param name="Attempts">Число выполненных неудачных попыток перезапуска.</param>
public sealed record BrainstemRestartExhausted(string Role, int Attempts);

namespace CyberPipe.Contracts;

/// <summary>
/// Запрос на изолированный запуск процесса в песочнице (TASK-023).
/// Потребители: воркер Brainstem (TASK-022) и раннеры скриптов (TASK-043).
/// </summary>
/// <param name="ExecutablePath">Абсолютный путь к исполняемому файлу.</param>
/// <param name="Arguments">Список аргументов командной строки (без конкатенации).</param>
/// <param name="WorkingDirectory">Рабочий каталог процесса.</param>
/// <param name="Environment">Разрешённые переменные окружения (allowlist, без секретов родителя).</param>
/// <param name="TimeoutMs">Тайм-аут исполнения в миллисекундах.</param>
/// <param name="MaxMemoryMb">Лимит памяти в мегабайтах (null = системное умолчание из конфигурации).</param>
/// <param name="RestrictToken">
/// Урезать токен процесса. Для недоверенных скриптов остаётся включённым.
/// Доверенный воркер Brainstem запускается без этого: отключённые привилегии не дают загрузить среду .NET.
/// </param>
public sealed record SandboxLaunchRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    int TimeoutMs,
    int? MaxMemoryMb,
    bool RestrictToken = true);

/// <summary>
/// Результат запуска процесса в песочнице (TASK-023).
/// </summary>
/// <param name="ExitCode">Код возврата процесса (null при аварийном завершении).</param>
/// <param name="TimedOut">Флаг завершения по тайм-ауту.</param>
/// <param name="KilledByJobLimit">Флаг принудительного завершения из-за превышения лимитов Job Object.</param>
/// <param name="Stdout">Захваченный стандартный вывод.</param>
/// <param name="Stderr">Захваченный поток ошибок.</param>
/// <param name="Error">Описание ошибки запуска; null при штатном старте.</param>
public sealed record SandboxLaunchResult(
    int? ExitCode,
    bool TimedOut,
    bool KilledByJobLimit,
    string Stdout,
    string Stderr,
    string? Error);

/// <summary>
/// Единый интерфейс запуска изолированных процессов (TASK-023).
/// </summary>
public interface ISandboxLauncher
{
    /// <summary>
    /// Запускает процесс с урезанными правами Windows: Restricted Token, Job Object и Deny-ACL.
    /// </summary>
    /// <param name="request">Параметры запуска.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат запуска.</returns>
    Task<SandboxLaunchResult> RunAsync(SandboxLaunchRequest request, CancellationToken cancellationToken = default);
}

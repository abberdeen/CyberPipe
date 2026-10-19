using System.Text.Json.Serialization;

namespace CyberPipe.Contracts;

/// <summary>
/// Статус завершения исполнения скрипта Netrunner (TASK-043).
/// </summary>
public enum ScriptRunStatus
{
    /// <summary>Скрипт успешно завершился, выходные данные соответствуют схеме.</summary>
    Succeeded,

    /// <summary>Входные или выходные данные нарушают JSON-схему или спецификацию файлов.</summary>
    SchemaValidationFailed,

    /// <summary>Скрипт завершился с ненулевым кодом возврата или внутренней ошибкой исполнения.</summary>
    ScriptFailed,

    /// <summary>Превышен лимит времени исполнения (тайм-аут).</summary>
    TimedOut,

    /// <summary>Исполнение отменено токеном отмены.</summary>
    Cancelled,

    /// <summary>Пара (язык, среда) не поддерживается зарегистрированными раннерами.</summary>
    UnsupportedRuntime,

    /// <summary>Сигнал отложенного повтора (exit code 75 или управляющий JSON DeferRetry).</summary>
    DeferRetry,
}

/// <summary>
/// Выходной файл, сгенерированный скриптом в рабочем каталоге запуска (TASK-043).
/// </summary>
public sealed record ScriptOutputFile
{
    /// <summary>Псевдоним файла из манифеста.</summary>
    public required string Alias { get; init; }

    /// <summary>Абсолютный путь к файлу на диске.</summary>
    public required string Path { get; init; }

    /// <summary>Контрольная сумма SHA-256 файла.</summary>
    public required string Sha256 { get; init; }

    /// <summary>Размер файла в байтах.</summary>
    public required long SizeBytes { get; init; }
}

/// <summary>
/// Запрос на исполнение скрипта пакета Netrunner (TASK-043).
/// </summary>
public sealed record ScriptRunRequest
{
    /// <summary>Разобранный манифест проверенного пакета (TASK-042).</summary>
    public required NetrunnerManifestDto Manifest { get; init; }

    /// <summary>Каталог пакета скрипта.</summary>
    public required string PackageDir { get; init; }

    /// <summary>Входной JSON-пейлоад.</summary>
    public string? InputJson { get; init; }

    /// <summary>Входные файлы: отображение alias -> локальный путь файла.</summary>
    public IReadOnlyDictionary<string, string> InputFiles { get; init; } = new Dictionary<string, string>();

    /// <summary>Изолированный рабочий каталог запуска (data/netrunner-artifacts/{RunId}/{StepId}/).</summary>
    public required string RunWorkspaceDir { get; init; }

    /// <summary>Тайм-аут исполнения в миллисекундах (null = из манифеста или глобальной настройки).</summary>
    public int? TimeoutMs { get; init; }

    /// <summary>Лимит памяти в мегабайтах (null = из манифеста или глобальной настройки).</summary>
    public int? MaxMemoryMb { get; init; }

    /// <summary>Дополнительные переменные окружения.</summary>
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }
}

/// <summary>
/// Результат исполнения скрипта Netrunner (TASK-043).
/// </summary>
public sealed record ScriptRunResult
{
    /// <summary>Статус завершения запуска.</summary>
    public required ScriptRunStatus Status { get; init; }

    /// <summary>Выходной JSON-пейлоад при успешном завершении.</summary>
    public string? OutputJson { get; init; }

    /// <summary>Список выходных файлов, созданных скриптом.</summary>
    public IReadOnlyList<ScriptOutputFile> OutputFiles { get; init; } = [];

    /// <summary>Код возврата процесса (null при аварийной отмене или тайм-ауте).</summary>
    public int? ExitCode { get; init; }

    /// <summary>Хвост стандартного вывода (с ограничением по размеру).</summary>
    public string StdoutTail { get; init; } = string.Empty;

    /// <summary>Хвост стандартного потока ошибок (с ограничением по размеру).</summary>
    public string StderrTail { get; init; } = string.Empty;

    /// <summary>Длительность исполнения в миллисекундах.</summary>
    public long DurationMs { get; init; }

    /// <summary>Сообщение об ошибке при неудачном запуске.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Задержка повтора в секундах при статусе DeferRetry.</summary>
    public int? RetryAfterSeconds { get; init; }

    /// <summary>Создаёт успешный результат исполнения.</summary>
    public static ScriptRunResult Success(
        string? outputJson,
        IReadOnlyList<ScriptOutputFile> outputFiles,
        int exitCode,
        string stdoutTail,
        string stderrTail,
        long durationMs) =>
        new()
        {
            Status = ScriptRunStatus.Succeeded,
            OutputJson = outputJson,
            OutputFiles = outputFiles,
            ExitCode = exitCode,
            StdoutTail = stdoutTail,
            StderrTail = stderrTail,
            DurationMs = durationMs,
        };

    /// <summary>Создаёт результат неудачного исполнения.</summary>
    public static ScriptRunResult Failed(
        ScriptRunStatus status,
        string errorMessage,
        int? exitCode = null,
        string stdoutTail = "",
        string stderrTail = "",
        long durationMs = 0,
        int? retryAfterSeconds = null) =>
        new()
        {
            Status = status,
            ErrorMessage = errorMessage,
            ExitCode = exitCode,
            StdoutTail = stdoutTail,
            StderrTail = stderrTail,
            DurationMs = durationMs,
            RetryAfterSeconds = retryAfterSeconds,
        };
}

/// <summary>
/// Интерфейс раннера скриптов конкретного языка и среды исполнения (TASK-043).
/// Исполнение происходит исключительно через порт песочницы <see cref="ISandboxLauncher" />.
/// Прямой вызов Process.Start на хосте запрещён.
/// </summary>
public interface IScriptRunner
{
    /// <summary>Имя поддерживаемого языка (например, "CSharp", "Python", "JavaScript").</summary>
    string Language { get; }

    /// <summary>Имя поддерживаемой среды (например, "Roslyn", "CPython", "Node").</summary>
    string Environment { get; }

    /// <summary>Проверяет, может ли данный раннер исполнить скрипт по манифесту.</summary>
    bool CanRun(NetrunnerManifestDto manifest);

    /// <summary>Исполняет скрипт в песочнице с валидацией схем и сбором артефактов.</summary>
    Task<ScriptRunResult> RunAsync(ScriptRunRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Реестр раннеров скриптов по языкам и средам (TASK-043).
/// </summary>
public interface IScriptRunnerRegistry
{
    /// <summary>Находит подходящий раннер по языку и среде (без учёта регистра).</summary>
    IScriptRunner? FindRunner(string language, string environment);

    /// <summary>Регистрирует раннер скриптов.</summary>
    void RegisterRunner(IScriptRunner runner);

    /// <summary>Возвращает все зарегистрированные раннеры.</summary>
    IReadOnlyCollection<IScriptRunner> GetAllRunners();

    /// <summary>Исполняет запрос через зарегистрированный раннер или возвращает UnsupportedRuntime.</summary>
    Task<ScriptRunResult> RunAsync(ScriptRunRequest request, CancellationToken cancellationToken = default);

    /// <summary>Выполняет dry-run проверку пакета скрипта по его тестовой фикстуре (tests/sample.fixture.json).</summary>
    Task<ScriptRunResult> DryRunPackageAsync(string packageDir, string workspaceDir, CancellationToken cancellationToken = default);
}

/// <summary>
/// Валидатор данных по схеме JSON Schema Draft 2020-12 (TASK-043).
/// </summary>
public interface ISchemaValidator
{
    /// <summary>
    /// Валидирует JSON-пейлоад по указанной схеме JSON.
    /// Возвращает список текстовых ошибок валидации (пустой при успешной проверке).
    /// </summary>
    IReadOnlyList<string> Validate(string schemaJson, string payloadJson);
}

/// <summary>
/// Порт песочницы для запуска изолированных процессов скриптов (TASK-002, TASK-043).
/// </summary>
public interface ISandboxProvider : ISandboxLauncher
{
}

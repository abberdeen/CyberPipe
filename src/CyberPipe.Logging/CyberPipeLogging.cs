using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Formatting.Json;

namespace CyberPipe.Logging;

/// <summary>
/// Единая точка входа логирования для Service, Desktop, CLI и Brainstem. Выбор стека зафиксирован
/// здесь: Serilog поверх Microsoft.Extensions.Logging, потребители пишут только через ILogger&lt;T&gt;.
/// События структурированные (JSON), enrich: ProcessName, RunId/ChainId/ChannelKind/IntentTier из
/// OperationContext; секреты маскируются SecretRedactionDestructuringPolicy и SecretRedactionEnricher.
/// </summary>
public static class CyberPipeLogging
{
    /// <summary>Каталог логов по умолчанию: каталог данных пользователя CyberPipe/logs.</summary>
    /// <returns>Абсолютный путь каталога логов.</returns>
    public static string GetDefaultLogDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CyberPipe",
            "logs");

    /// <summary>Разбирает имя уровня конфигурации Logging:MinimumLevel.</summary>
    /// <param name="minimumLevel">Имя уровня из конфигурации.</param>
    /// <returns>Уровень Serilog; Information при неизвестном имени.</returns>
    public static LogEventLevel ParseMinimumLevel(string? minimumLevel) =>
        Enum.TryParse(minimumLevel, ignoreCase: true, out LogEventLevel level) ? level : LogEventLevel.Information;

    /// <summary>
    /// Создаёт фабрику ILogger для процесса. Уровень берётся из конфигурации TASK-003
    /// (Logging:MinimumLevel) и меняется без пересборки; файл пишется в каталог данных
    /// пользователя с ротацией по дням и размеру.
    /// </summary>
    /// <param name="processName">Имя процесса для поля ProcessName и имени файла.</param>
    /// <param name="options">Конфигурация приложения из TASK-003.</param>
    /// <param name="inMemorySink">Необязательный in-memory приёмник для тестов; может быть null.</param>
    /// <param name="logDirectory">Необязательный каталог лога вместо конфигурационного и умолчания.</param>
    /// <param name="writeToConsole">Необязательное переопределение консольного вывода хостом.</param>
    /// <returns>Фабрика логеров; владелец освобождает её при завершении процесса.</returns>
    public static ILoggerFactory CreateLoggerFactory(
        string processName,
        CyberPipeOptions options,
        InMemoryLogSink? inMemorySink = null,
        string? logDirectory = null,
        bool? writeToConsole = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        ArgumentNullException.ThrowIfNull(options);

        LoggingOptions logging = options.Logging;
        string directory = logDirectory ?? logging.LogDirectory ?? GetDefaultLogDirectory();
        Directory.CreateDirectory(directory);

        string safeProcessName = string.Join("_", processName.Split(Path.GetInvalidFileNameChars()));
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(ParseMinimumLevel(logging.MinimumLevel))
            .Enrich.WithProperty("ProcessName", processName)
            .Enrich.With<OperationContextEnricher>()
            .Enrich.With<SecretRedactionEnricher>()
            .Destructure.With<SecretRedactionDestructuringPolicy>()
            .WriteTo.File(
                new JsonFormatter(),
                Path.Combine(directory, $"{safeProcessName}-.log"),
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: logging.FileSizeLimitBytes,
                retainedFileCountLimit: logging.RetainedFileCountLimit,
                shared: true);

        if (writeToConsole ?? logging.WriteToConsole)
            configuration.WriteTo.Console(new JsonFormatter());

        if (inMemorySink is not null)
            configuration.WriteTo.Sink(inMemorySink);

        Serilog.Core.Logger serilogLogger = configuration.CreateLogger();
        return new SerilogLoggerFactory(serilogLogger, dispose: true);
    }
}

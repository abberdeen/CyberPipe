using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CyberPipe.Logging.Telemetry;

/// <summary>
/// Общие источники телеметрии CyberPipe: ActivitySource именуют спаны,
/// Meter — счётчики и гистограммы. Без зарегистрированных провайдеров
/// вызовы дешёвые no-op; экспорт включается секцией Telemetry.
/// </summary>
public static class CyberPipeTelemetry
{
    /// <summary>Источник спанов конвейера цепочек (chain.run и события шагов).</summary>
    public static readonly ActivitySource Chains = new("CyberPipe.Chains");

    /// <summary>Источник спанов жизненного цикла службы.</summary>
    public static readonly ActivitySource Service = new("CyberPipe.Service");

    /// <summary>Источник спанов команд CLI.</summary>
    public static readonly ActivitySource Cli = new("CyberPipe.Cli");

    /// <summary>Метр домена цепочек.</summary>
    public static readonly Meter ChainsMeter = new("CyberPipe.Chains");

    /// <summary>Запуски конвейера (включая resume после пауз).</summary>
    public static readonly Counter<long> RunsStarted =
        ChainsMeter.CreateCounter<long>("chain.runs.started", description: "Chain pipeline executions started.");

    /// <summary>Завершения конвейера с тегом status (Completed/Failed/Cancelled/...).</summary>
    public static readonly Counter<long> RunsFinished =
        ChainsMeter.CreateCounter<long>("chain.runs.finished", description: "Chain pipeline executions finished.");

    /// <summary>Успешные отправки шага Delivery.</summary>
    public static readonly Counter<long> DeliveriesSent =
        ChainsMeter.CreateCounter<long>("chain.deliveries.sent", description: "Successful chain delivery sends.");

    /// <summary>Длительность исполнения конвейера в миллисекундах.</summary>
    public static readonly Histogram<double> RunDurationMs =
        ChainsMeter.CreateHistogram<double>("chain.run.duration", unit: "ms", description: "Chain pipeline duration.");
}

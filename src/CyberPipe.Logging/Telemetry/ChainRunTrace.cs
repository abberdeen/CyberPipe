using CyberPipe.Contracts;
using System.Diagnostics;

namespace CyberPipe.Logging.Telemetry;

/// <summary>
/// Скоуп спана одного исполнения конвейера цепочки: открывается на входе
/// ExecutePipelineAsync, живёт через паузы (HITL/retry) и закрывается на
/// терминальном исходе. Шаги отмечаются событиями; счётчики и длительность
/// пишутся здесь же, чтобы доменный код не знал про OpenTelemetry.
/// </summary>
public sealed class ChainRunTrace : IDisposable
{
    private readonly Activity? _activity;
    private readonly Stopwatch _stopwatch;
    private readonly TagList _tags;
    private bool _outcomeSet;
    private bool _disposed;

    private ChainRunTrace(Activity? activity, TagList tags)
    {
        _activity = activity;
        _tags = tags;
        _stopwatch = Stopwatch.StartNew();
    }

    /// <summary>Открыть спан chain.run с базовыми тегами.</summary>
    public static ChainRunTrace Start(RunId runId, ChainId chainId, TriggerEvent trigger)
    {
        var tags = new TagList
        {
            { "chain.id", chainId.Value.ToString() },
            { "chain.run.id", runId.Value.ToString() },
            { "chain.trigger.kind", trigger.Kind.ToString() },
        };
        Activity? activity = CyberPipeTelemetry.Chains.StartActivity(
            "chain.run", ActivityKind.Internal, parentContext: default);
        if (activity is not null)
        {
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                activity.SetTag(tag.Key, tag.Value);
            }
        }

        CyberPipeTelemetry.RunsStarted.Add(1, tags);
        return new ChainRunTrace(activity, tags);
    }

    /// <summary>Отметить событие шага (skill.completed, script.completed, ...).</summary>
    public void Event(string name)
    {
        _activity?.AddEvent(new ActivityEvent(name));
    }

    /// <summary>Произвольный тег спана.</summary>
    public void SetTag(string key, object? value)
    {
        _activity?.SetTag(key, value);
    }

    /// <summary>
    /// Терминальный исход конвейера (Completed/Failed/Cancelled/...):
    /// статус спана, счётчик finished и длительность.
    /// </summary>
    public void SetOutcome(string status, string? errorCode = null)
    {
        if (_outcomeSet)
        {
            return;
        }

        _outcomeSet = true;
        _stopwatch.Stop();

        if (_activity is not null)
        {
            _activity.SetTag("chain.run.status", status);
            if (errorCode is not null)
            {
                _activity.SetTag("chain.run.error_code", errorCode);
            }

            _activity.SetStatus(status == "Completed" ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        }

        var finished = new TagList();
        foreach (KeyValuePair<string, object?> tag in _tags)
        {
            finished.Add(tag.Key, tag.Value);
        }

        finished.Add("chain.run.status", status);
        CyberPipeTelemetry.RunsFinished.Add(1, finished);
        CyberPipeTelemetry.RunDurationMs.Record(_stopwatch.Elapsed.TotalMilliseconds, finished);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_outcomeSet)
        {
            SetOutcome("Abandoned");
        }

        _activity?.Dispose();
    }
}

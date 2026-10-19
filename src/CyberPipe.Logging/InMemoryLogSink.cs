using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace CyberPipe.Logging;

/// <summary>In-memory приёмник событий для unit-тестов: хранит события в том же JSON-виде, что и файл.</summary>
public sealed class InMemoryLogSink : ILogEventSink
{
    private readonly object _gate = new();
    private readonly List<string> _lines = [];
    private readonly JsonFormatter _formatter = new();

    /// <summary>События в JSON по одному на строку, в порядке записи.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_gate)
                return _lines.ToArray();
        }
    }

    /// <summary>Все события одним текстом, как в файле лога.</summary>
    public string Text => string.Join(Environment.NewLine, Lines);

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        _formatter.Format(logEvent, writer);
        lock (_gate)
            _lines.Add(writer.ToString().TrimEnd());
    }
}

using System.IO;
using System.Text;
using System.Text.Json;
using CyberPipe.Logging;

namespace CyberPipe.Desktop.Services;

public sealed class OpenTelemetryLogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string FormattedTimestamp => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
    public string FormattedDate => Timestamp.ToLocalTime().ToString("yyyy-MM-dd");
    public string Level { get; init; } = "Information";
    public string ProcessName { get; init; } = "Unknown";
    public string Message { get; init; } = string.Empty;
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public string? SourceContext { get; init; }
    public string? Exception { get; init; }
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();
    public string RawJson { get; init; } = string.Empty;

    public bool HasTrace => !string.IsNullOrWhiteSpace(TraceId);
    public string ShortTraceId => !string.IsNullOrEmpty(TraceId) && TraceId.Length > 12 ? TraceId[..12] + "…" : (TraceId ?? string.Empty);
    public bool HasException => !string.IsNullOrWhiteSpace(Exception);

    public string LevelColor => Level.ToUpperInvariant() switch
    {
        "FATAL" or "ERROR" => "#E53935",
        "WARNING" or "WARN" => "#FB8C00",
        "INFORMATION" or "INFO" => "#1E88E5",
        "DEBUG" or "VERBOSE" => "#757575",
        _ => "#9E9E9E",
    };

    public string ProcessColor => ProcessName.ToLowerInvariant() switch
    {
        "service" => "#00897B",
        "brainstem" => "#8E24AA",
        "desktop" => "#1565C0",
        "cli" => "#546E7A",
        _ => "#424242",
    };
}

public interface ITelemetryLogReaderService
{
    Task<IReadOnlyList<OpenTelemetryLogEntry>> ReadLogsAsync(
        string? logDirectory = null,
        int maxEntries = 2000,
        CancellationToken ct = default);
}

public sealed class TelemetryLogReaderService : ITelemetryLogReaderService
{
    public async Task<IReadOnlyList<OpenTelemetryLogEntry>> ReadLogsAsync(
        string? logDirectory = null,
        int maxEntries = 2000,
        CancellationToken ct = default)
    {
        string dir = logDirectory ?? CyberPipeLogging.GetDefaultLogDirectory();
        if (!Directory.Exists(dir))
        {
            return Array.Empty<OpenTelemetryLogEntry>();
        }

        string[] logFiles;
        try
        {
            logFiles = Directory.GetFiles(dir, "*.log", SearchOption.TopDirectoryOnly);
        }
        catch (Exception)
        {
            return Array.Empty<OpenTelemetryLogEntry>();
        }

        var entries = new List<OpenTelemetryLogEntry>();

        foreach (string file in logFiles)
        {
            ct.ThrowIfCancellationRequested();
            string fileName = Path.GetFileName(file);
            string defaultProcess = InferProcessFromFileName(fileName);

            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs, Encoding.UTF8);

                string? line;
                while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    OpenTelemetryLogEntry? entry = ParseLogLine(line, defaultProcess);
                    if (entry != null)
                    {
                        entries.Add(entry);
                    }
                }
            }
            catch (IOException)
            {
                // File might be actively rotated or written
            }
        }

        return entries
            .OrderBy(e => e.Timestamp)
            .TakeLast(maxEntries)
            .ToList();
    }

    public static string InferProcessFromFileName(string fileName)
    {
        if (fileName.StartsWith("Service", StringComparison.OrdinalIgnoreCase)) return "Service";
        if (fileName.StartsWith("Brainstem", StringComparison.OrdinalIgnoreCase)) return "Brainstem";
        if (fileName.StartsWith("Desktop", StringComparison.OrdinalIgnoreCase)) return "Desktop";
        if (fileName.StartsWith("Cli", StringComparison.OrdinalIgnoreCase)) return "Cli";
        return "Unknown";
    }

    public static OpenTelemetryLogEntry? ParseLogLine(string jsonLine, string defaultProcess)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            JsonElement root = doc.RootElement;

            DateTimeOffset timestamp = DateTimeOffset.UtcNow;
            if (root.TryGetProperty("Timestamp", out JsonElement tsElem) && tsElem.TryGetDateTimeOffset(out var parsedTs))
            {
                timestamp = parsedTs;
            }

            string level = root.TryGetProperty("Level", out JsonElement lvlElem) && lvlElem.ValueKind == JsonValueKind.String
                ? lvlElem.GetString() ?? "Information"
                : "Information";

            string messageTemplate = root.TryGetProperty("MessageTemplate", out JsonElement msgElem) && msgElem.ValueKind == JsonValueKind.String
                ? msgElem.GetString() ?? string.Empty
                : string.Empty;

            string? traceId = root.TryGetProperty("TraceId", out JsonElement trElem) && trElem.ValueKind == JsonValueKind.String
                ? trElem.GetString()
                : null;

            string? spanId = root.TryGetProperty("SpanId", out JsonElement spElem) && spElem.ValueKind == JsonValueKind.String
                ? spElem.GetString()
                : null;

            string? exception = root.TryGetProperty("Exception", out JsonElement exElem) && exElem.ValueKind == JsonValueKind.String
                ? exElem.GetString()
                : null;

            string processName = defaultProcess;
            string? sourceContext = null;
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (root.TryGetProperty("Properties", out JsonElement propsElem) && propsElem.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty prop in propsElem.EnumerateObject())
                {
                    string propVal = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                        JsonValueKind.Number => prop.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => "null",
                        _ => prop.Value.GetRawText(),
                    };

                    props[prop.Name] = propVal;

                    if (string.Equals(prop.Name, "ProcessName", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(propVal))
                    {
                        processName = propVal;
                    }
                    else if (string.Equals(prop.Name, "SourceContext", StringComparison.OrdinalIgnoreCase))
                    {
                        sourceContext = propVal;
                    }
                    else if (string.Equals(prop.Name, "TraceId", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(traceId))
                    {
                        traceId = propVal;
                    }
                    else if (string.Equals(prop.Name, "SpanId", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(spanId))
                    {
                        spanId = propVal;
                    }
                }
            }

            string renderedMessage = RenderMessageTemplate(messageTemplate, props);

            return new OpenTelemetryLogEntry
            {
                Timestamp = timestamp,
                Level = level,
                ProcessName = processName,
                Message = renderedMessage,
                TraceId = traceId,
                SpanId = spanId,
                SourceContext = sourceContext,
                Exception = exception,
                Properties = props,
                RawJson = jsonLine,
            };
        }
        catch (JsonException)
        {
            return new OpenTelemetryLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = "Information",
                ProcessName = defaultProcess,
                Message = jsonLine,
                RawJson = jsonLine,
            };
        }
    }

    private static string RenderMessageTemplate(string template, Dictionary<string, string> properties)
    {
        if (string.IsNullOrEmpty(template) || properties.Count == 0)
        {
            return template;
        }

        string result = template;
        foreach (var (k, v) in properties)
        {
            result = result.Replace("{" + k + "}", v, StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }
}

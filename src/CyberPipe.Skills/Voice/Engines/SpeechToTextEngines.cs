using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Voice.Engines;

/// <summary>
/// Фейковый движок распознавания речи для unit-тестов и автономного режима (TASK-052).
/// Детерминированно возвращает транскрипты, поддерживает симуляцию тишины, ошибок и тайм-аутов.
/// </summary>
public sealed class FakeSpeechToTextEngine : ISpeechToTextEngine
{
    public Func<Stream, SttOptions, CancellationToken, Task<TranscriptionResult>>? CustomTranscribe { get; set; }
    public string DefaultRussianText { get; set; } = "Привет, как дела?";
    public string DefaultEnglishText { get; set; } = "Hello, how are you?";
    public bool SimulateSilence { get; set; }
    public bool SimulateEngineUnavailable { get; set; }
    public string? SimulateValidationError { get; set; }
    public TimeSpan? SimulateDelay { get; set; }

    public int InvocationCount { get; private set; }
    public SttOptions? LastOptions { get; private set; }
    public long LastStreamLength { get; private set; }

    public async Task<TranscriptionResult> TranscribeAsync(
        Stream audioStream,
        SttOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        InvocationCount++;
        LastOptions = options;

        var sw = Stopwatch.StartNew();

        if (SimulateDelay.HasValue && SimulateDelay.Value > TimeSpan.Zero)
        {
            await Task.Delay(SimulateDelay.Value, ct).ConfigureAwait(false);
        }

        if (CustomTranscribe != null)
        {
            return await CustomTranscribe(audioStream, options, ct).ConfigureAwait(false);
        }

        if (SimulateEngineUnavailable)
        {
            return TranscriptionResult.EngineUnavailable("Whisper model or engine not found (simulated).");
        }

        if (!string.IsNullOrEmpty(SimulateValidationError))
        {
            return TranscriptionResult.ValidationError(SimulateValidationError);
        }

        if (SimulateSilence)
        {
            return TranscriptionResult.NoSpeech(sw.Elapsed);
        }

        // Check if stream is seekable and inspect contents
        if (audioStream.CanSeek)
        {
            LastStreamLength = audioStream.Length;
            if (audioStream.Length == 0)
            {
                return TranscriptionResult.NoSpeech(sw.Elapsed);
            }

            // Inspect for silence (zero PCM payload after 44-byte WAV header)
            long originalPos = audioStream.Position;
            audioStream.Position = 0;
            byte[] buffer = new byte[Math.Min(audioStream.Length, 4096)];
            int read = await audioStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            audioStream.Position = originalPos;

            if (read > 44)
            {
                bool isAllZero = true;
                for (int i = 44; i < read; i++)
                {
                    if (buffer[i] != 0 && buffer[i] != 0x80)
                    {
                        isAllZero = false;
                        break;
                    }
                }

                if (isAllZero)
                {
                    return TranscriptionResult.NoSpeech(sw.Elapsed);
                }
            }
        }

        string language = options.Language?.ToLowerInvariant() switch
        {
            "ru" or "russian" => "ru",
            "en" or "english" => "en",
            _ => "ru" // Default auto-detect to Russian in test environment
        };

        string text = language == "ru" ? DefaultRussianText : DefaultEnglishText;

        var segments = new List<TranscriptionSegment>
        {
            new(
                Start: TimeSpan.Zero,
                End: TimeSpan.FromSeconds(2.5),
                Text: text,
                Confidence: 0.98)
        };

        return TranscriptionResult.Success(text, language, segments, sw.Elapsed);
    }
}

/// <summary>
/// Движок распознавания речи, делегирующий исполнение изолированному воркеру Brainstem (TASK-022, TASK-052).
/// Запускается вне процесса Service, предотвращая падение хоста при сбоях нативных библиотек STT.
/// </summary>
public sealed class BrainstemSpeechToTextEngine : ISpeechToTextEngine
{
    private readonly IBrainstemWorker _worker;
    private readonly string? _defaultModelPath;

    public BrainstemSpeechToTextEngine(IBrainstemWorker worker, string? defaultModelPath = null)
    {
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _defaultModelPath = defaultModelPath;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        Stream audioStream,
        SttOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        byte[] audioBytes;
        if (audioStream is MemoryStream ms)
        {
            audioBytes = ms.ToArray();
        }
        else
        {
            using var copyMs = new MemoryStream();
            await audioStream.CopyToAsync(copyMs, ct).ConfigureAwait(false);
            audioBytes = copyMs.ToArray();
        }

        string payloadJson = JsonSerializer.Serialize(new
        {
            audioBase64 = Convert.ToBase64String(audioBytes),
            language = options.Language ?? "auto",
            modelPath = options.ModelPath ?? _defaultModelPath
        });

        int timeoutMs = (int)(options.Timeout?.TotalMilliseconds ?? 60_000);

        var request = new BrainstemJobRequest(
            JobId: Guid.NewGuid().ToString("N"),
            Kind: "Whisper",
            Payload: Encoding.UTF8.GetBytes(payloadJson),
            TimeoutMs: timeoutMs);

        BrainstemJobResult result = await _worker.ExecuteAsync(request, ct).ConfigureAwait(false);

        if (!result.Ok)
        {
            string error = result.Error ?? "Brainstem worker execution failed.";
            if (error.Contains("EngineUnavailable", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return TranscriptionResult.EngineUnavailable(error);
            }

            return TranscriptionResult.ValidationError(error);
        }

        if (result.Payload == null || result.Payload.Length == 0)
        {
            return TranscriptionResult.NoSpeech();
        }

        try
        {
            var optionsJson = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var deserialized = JsonSerializer.Deserialize<TranscriptionResult>(result.Payload, optionsJson);
            return deserialized ?? TranscriptionResult.NoSpeech();
        }
        catch (JsonException)
        {
            string rawText = Encoding.UTF8.GetString(result.Payload);
            return TranscriptionResult.Success(rawText, options.Language ?? "auto", [], TimeSpan.Zero);
        }
    }
}

using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Voice.Skills;

/// <summary>
/// Навык voice.transcribe: нормализует аудио и транскрибирует речь в текст через локальный движок STT (TASK-052).
/// </summary>
public sealed class VoiceTranscribeSkill : IVoiceSkill
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ISpeechToTextEngine _engine;
    private readonly IAudioConverter _converter;
    private readonly VoiceSkillLimits _limits;

    public string SkillId => "voice.transcribe";
    public string Name => "Voice Transcription";
    public string Description => "Transcribes speech audio into text using local Whisper STT engine.";

    public VoiceTranscribeSkill(
        ISpeechToTextEngine engine,
        IAudioConverter converter,
        VoiceSkillLimits? limits = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        _limits = limits ?? new VoiceSkillLimits();
    }

    public async Task<TranscriptionResult> TranscribeAsync(AudioInput input, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (input == null)
        {
            return TranscriptionResult.ValidationError("Audio input cannot be null.");
        }

        if (input.Duration.HasValue && input.Duration.Value > _limits.MaxDuration)
        {
            return TranscriptionResult.ValidationError(
                $"Audio duration {input.Duration.Value.TotalSeconds:F1}s exceeds maximum limit of {_limits.MaxDuration.TotalSeconds:F1}s.");
        }

        Stream? streamToDispose = null;
        try
        {
            Stream inputStream;
            if (input.Stream != null)
            {
                inputStream = input.Stream;
            }
            else if (!string.IsNullOrWhiteSpace(input.FilePath))
            {
                if (!File.Exists(input.FilePath))
                {
                    return TranscriptionResult.ValidationError($"Audio file not found: '{input.FilePath}'.");
                }

                var fileInfo = new FileInfo(input.FilePath);
                if (fileInfo.Length > _limits.MaxSizeBytes)
                {
                    return TranscriptionResult.ValidationError(
                        $"Audio file size {fileInfo.Length} bytes exceeds limit of {_limits.MaxSizeBytes} bytes.");
                }

                inputStream = File.OpenRead(input.FilePath);
                streamToDispose = inputStream;
            }
            else
            {
                return TranscriptionResult.ValidationError("Either Stream or FilePath must be provided in AudioInput.");
            }

            if (inputStream.CanSeek && inputStream.Length > _limits.MaxSizeBytes)
            {
                return TranscriptionResult.ValidationError(
                    $"Audio stream size {inputStream.Length} bytes exceeds limit of {_limits.MaxSizeBytes} bytes.");
            }

            // Normalize audio to 16 kHz mono WAV
            Stream normalizedWav;
            try
            {
                normalizedWav = await _converter.ConvertToWav16kMonoAsync(inputStream, input.Format, ct).ConfigureAwait(false);
            }
            catch (AudioFormatNotSupportedException ex)
            {
                return TranscriptionResult.ValidationError($"Unsupported audio format: '{ex.Format}'.");
            }
            catch (AudioConversionException ex)
            {
                return TranscriptionResult.ValidationError($"Audio conversion failed: {ex.Message}");
            }

            await using (normalizedWav.ConfigureAwait(false))
            {
                var options = new SttOptions
                {
                    Language = input.LanguageHint ?? "auto"
                };

                return await _engine.TranscribeAsync(normalizedWav, options, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            if (streamToDispose != null)
            {
                await streamToDispose.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string filePath = string.Empty;
        string? audioBase64 = null;
        string format = "wav";
        string language = "auto";
        TimeSpan? duration = null;

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("filePath", out var fp) && fp.ValueKind == JsonValueKind.String)
                {
                    filePath = fp.GetString() ?? string.Empty;
                }

                if (root.TryGetProperty("audioBase64", out var ab) && ab.ValueKind == JsonValueKind.String)
                {
                    audioBase64 = ab.GetString();
                }

                if (root.TryGetProperty("format", out var fmt) && fmt.ValueKind == JsonValueKind.String)
                {
                    format = fmt.GetString() ?? "wav";
                }

                if (root.TryGetProperty("language", out var lang) && lang.ValueKind == JsonValueKind.String)
                {
                    language = lang.GetString() ?? "auto";
                }

                if (root.TryGetProperty("durationSeconds", out var dur) && dur.TryGetDouble(out double durSec))
                {
                    duration = TimeSpan.FromSeconds(durSec);
                }
            }
            catch (JsonException ex)
            {
                return HostSkillResult.ValidationError($"Invalid payload JSON: {ex.Message}");
            }
        }

        AudioInput input;
        Stream? base64Stream = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(audioBase64))
            {
                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(audioBase64);
                }
                catch (FormatException)
                {
                    return HostSkillResult.ValidationError("Invalid audioBase64 string encoding.");
                }

                base64Stream = new MemoryStream(bytes);
                input = new AudioInput
                {
                    Stream = base64Stream,
                    Format = format,
                    Duration = duration,
                    LanguageHint = language
                };
            }
            else
            {
                input = new AudioInput
                {
                    FilePath = filePath,
                    Format = format,
                    Duration = duration,
                    LanguageHint = language
                };
            }

            TranscriptionResult result = await TranscribeAsync(input, cancellationToken).ConfigureAwait(false);

            return result.Status switch
            {
                TranscriptionStatus.Ok => HostSkillResult.Ok(JsonSerializer.Serialize(result, JsonOpts), result.Text),
                TranscriptionStatus.NoSpeech => HostSkillResult.Ok(JsonSerializer.Serialize(result, JsonOpts), string.Empty),
                TranscriptionStatus.ValidationError => HostSkillResult.ValidationError(result.Error ?? "Validation error"),
                TranscriptionStatus.EngineUnavailable => new HostSkillResult
                {
                    Status = SkillResultStatus.Failed,
                    ErrorCode = "EngineUnavailable",
                    Message = result.Error ?? "Speech-to-text engine is unavailable.",
                    OutputJson = JsonSerializer.Serialize(result, JsonOpts)
                },
                _ => HostSkillResult.Failed(result.Error ?? "Unknown transcription status.")
            };
        }
        finally
        {
            if (base64Stream != null)
            {
                await base64Stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

namespace CyberPipe.Contracts;

/// <summary>
/// Статус результата распознавания речи (TASK-052).
/// </summary>
public enum TranscriptionStatus
{
    Ok = 0,
    NoSpeech = 1,
    ValidationError = 2,
    EngineUnavailable = 3
}

/// <summary>
/// Распознанный сегмент аудиодорожки с временными метками и уверенностью (TASK-052).
/// </summary>
public sealed record TranscriptionSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text,
    double Confidence = 1.0);

/// <summary>
/// Входные данные для распознавания аудио (TASK-052).
/// Передаётся потоком или файловой ссылкой, не byte[] в памяти для больших файлов.
/// </summary>
public sealed record AudioInput
{
    public Stream? Stream { get; init; }
    public string? FilePath { get; init; }
    public string Format { get; init; } = "wav";
    public TimeSpan? Duration { get; init; }
    public string LanguageHint { get; init; } = "auto";
}

/// <summary>
/// Параметры запуска движка распознавания речи (TASK-052).
/// </summary>
public sealed record SttOptions
{
    public string Language { get; init; } = "auto";
    public string? ModelPath { get; init; }
    public TimeSpan? Timeout { get; init; }
}

/// <summary>
/// Результат транскрипции аудиозаписи (TASK-052).
/// </summary>
public sealed record TranscriptionResult
{
    public required TranscriptionStatus Status { get; init; }
    public string Text { get; init; } = string.Empty;
    public string Language { get; init; } = "auto";
    public IReadOnlyList<TranscriptionSegment> Segments { get; init; } = [];
    public TimeSpan ProcessingDuration { get; init; } = TimeSpan.Zero;
    public string? Error { get; init; }

    public static TranscriptionResult Success(
        string text,
        string language,
        IReadOnlyList<TranscriptionSegment> segments,
        TimeSpan processingDuration) => new()
    {
        Status = TranscriptionStatus.Ok,
        Text = text,
        Language = language,
        Segments = segments,
        ProcessingDuration = processingDuration
    };

    public static TranscriptionResult NoSpeech(TimeSpan processingDuration = default) => new()
    {
        Status = TranscriptionStatus.NoSpeech,
        Text = string.Empty,
        Language = "auto",
        Segments = [],
        ProcessingDuration = processingDuration
    };

    public static TranscriptionResult ValidationError(string error) => new()
    {
        Status = TranscriptionStatus.ValidationError,
        Text = string.Empty,
        Error = error
    };

    public static TranscriptionResult EngineUnavailable(string diagnosticMessage) => new()
    {
        Status = TranscriptionStatus.EngineUnavailable,
        Text = string.Empty,
        Error = diagnosticMessage
    };
}

/// <summary>
/// Интерфейс движка распознавания речи (TASK-052).
/// </summary>
public interface ISpeechToTextEngine
{
    Task<TranscriptionResult> TranscribeAsync(
        Stream audioStream,
        SttOptions options,
        CancellationToken ct = default);
}

/// <summary>
/// Конвертер и нормализатор аудиопотоков в 16 kHz mono WAV (TASK-052).
/// </summary>
public interface IAudioConverter
{
    Task<Stream> ConvertToWav16kMonoAsync(
        Stream inputStream,
        string inputFormat,
        CancellationToken ct = default);
}

/// <summary>
/// Исключение при ошибке конвертации аудио (TASK-052).
/// </summary>
public class AudioConversionException : Exception
{
    public AudioConversionException(string message) : base(message) { }
    public AudioConversionException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Исключение для неподдерживаемого аудиоформата (TASK-052).
/// </summary>
public class AudioFormatNotSupportedException : AudioConversionException
{
    public string Format { get; }

    public AudioFormatNotSupportedException(string format)
        : base($"Audio format '{format}' is not supported.")
    {
        Format = format;
    }
}

/// <summary>
/// Ограничения на входное аудио голосового навыка (TASK-052).
/// </summary>
public sealed record VoiceSkillLimits
{
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromMinutes(5);
    public long MaxSizeBytes { get; init; } = 50 * 1024 * 1024; // 50 MB
}

/// <summary>
/// Интерфейс навыка транскрипции речи (TASK-052).
/// </summary>
public interface IVoiceSkill : ISkill
{
    Task<TranscriptionResult> TranscribeAsync(AudioInput input, CancellationToken ct = default);
}

/// <summary>
/// Реестр голосовых навыков (TASK-052).
/// </summary>
public interface IVoiceSkillRegistry : ISkillInvoker
{
    void RegisterSkill(ISkill skill);
    ISkill? FindSkill(string skillId);
    IReadOnlyList<ISkill> GetAllSkills();
}

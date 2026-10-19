namespace CyberPipe.Skills.Voice;

/// <summary>
/// Описание метаданных голосового навыка (TASK-052).
/// </summary>
public sealed record VoiceSkillDescriptor
{
    public required string SkillId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public string InputSchemaJson { get; init; } = "{}";
}

/// <summary>
/// Каталог метаданных голосовых навыков (TASK-052).
/// </summary>
public static class VoiceSkillCatalog
{
    public static readonly IReadOnlyList<VoiceSkillDescriptor> AllDescriptors =
    [
        new()
        {
            SkillId = "voice.transcribe",
            Name = "Voice Transcription",
            Description = "Transcribes speech audio into text using local Whisper STT engine.",
            Category = "voice",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"audioBase64\":{\"type\":\"string\"},\"format\":{\"type\":\"string\",\"default\":\"wav\"},\"language\":{\"type\":\"string\",\"default\":\"auto\"},\"durationSeconds\":{\"type\":\"number\"}}}",
        }
    ];
}

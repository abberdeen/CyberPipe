namespace CyberPipe.Skills.Host;

/// <summary>
/// Описание метаданных хост-навыка для регистрации в роутере Tier 0–3 и цепочках (TASK-050).
/// </summary>
public sealed record HostSkillDescriptor
{
    /// <summary>Идентификатор навыка (например, "media.play", "volume.set").</summary>
    public required string SkillId { get; init; }

    /// <summary>Человекочитаемое название навыка.</summary>
    public required string Name { get; init; }

    /// <summary>Описание для семантического поиска и подсказок LLM.</summary>
    public required string Description { get; init; }

    /// <summary>Категория хост-навыка ("media", "volume", "system", "timer", "alarm").</summary>
    public required string Category { get; init; }

    /// <summary>JSON-схема входной полезной нагрузки.</summary>
    public string InputSchemaJson { get; init; } = "{}";

    /// <summary>Признак того, что операция может потребовать HITL-подтверждения.</summary>
    public bool PotentiallyRequiresApproval { get; init; }
}

/// <summary>
/// Каталог описаний стандартных хост-навыков (TASK-050).
/// </summary>
public static class HostSkillCatalog
{
    /// <summary>Список всех доступных стандартных хост-навыков.</summary>
    public static readonly IReadOnlyList<HostSkillDescriptor> AllDescriptors =
    [
        new()
        {
            SkillId = "media.play",
            Name = "Media Play",
            Description = "Starts or resumes audio/media playback on the host.",
            Category = "media",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
        },
        new()
        {
            SkillId = "media.pause",
            Name = "Media Pause",
            Description = "Pauses active audio/media playback on the host.",
            Category = "media",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
        },
        new()
        {
            SkillId = "media.playpause",
            Name = "Media Play/Pause",
            Description = "Toggles playback state of audio/media on the host.",
            Category = "media",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
        },
        new()
        {
            SkillId = "media.next",
            Name = "Media Next Track",
            Description = "Skips to the next track in current media playlist.",
            Category = "media",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
        },
        new()
        {
            SkillId = "media.previous",
            Name = "Media Previous Track",
            Description = "Returns to the previous track in current media playlist.",
            Category = "media",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
        },
        new()
        {
            SkillId = "volume.get",
            Name = "Volume Get",
            Description = "Retrieves current master volume percentage (0-100) and mute status.",
            Category = "volume",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
        },
        new()
        {
            SkillId = "volume.set",
            Name = "Volume Set",
            Description = "Sets master audio volume percentage (0-100). Large increases require owner approval unless force=true.",
            Category = "volume",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"volume\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":100},\"force\":{\"type\":\"boolean\"}},\"required\":[\"volume\"]}",
            PotentiallyRequiresApproval = true,
        },
        new()
        {
            SkillId = "volume.up",
            Name = "Volume Up",
            Description = "Increases master volume by a step in percentage (default 5%). Large steps require approval.",
            Category = "volume",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"step\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":100},\"force\":{\"type\":\"boolean\"}}}",
            PotentiallyRequiresApproval = true,
        },
        new()
        {
            SkillId = "volume.down",
            Name = "Volume Down",
            Description = "Decreases master volume by a step in percentage (default 5%).",
            Category = "volume",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"step\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":100}}}",
        },
        new()
        {
            SkillId = "volume.mute",
            Name = "Volume Mute",
            Description = "Mutes master audio output on the host.",
            Category = "volume",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
        },
        new()
        {
            SkillId = "volume.unmute",
            Name = "Volume Unmute",
            Description = "Unmutes master audio output on the host.",
            Category = "volume",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
        },
        new()
        {
            SkillId = "volume.togglemute",
            Name = "Volume Toggle Mute",
            Description = "Toggles mute state of master audio output.",
            Category = "volume",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
        },
        new()
        {
            SkillId = "system.status",
            Name = "System Status",
            Description = "Inspects host CPU usage, available RAM, disk space, battery status, and uptime.",
            Category = "system",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
        },
        new()
        {
            SkillId = "timer.set",
            Name = "Timer Set",
            Description = "Sets a countdown timer for a specified duration and title.",
            Category = "timer",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"durationSeconds\":{\"type\":\"number\"},\"title\":{\"type\":\"string\"},\"payload\":{\"type\":\"object\"}},\"required\":[\"durationSeconds\"]}",
        },
        new()
        {
            SkillId = "timer.cancel",
            Name = "Timer Cancel",
            Description = "Cancels an active timer by its GUID.",
            Category = "timer",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"timerId\":{\"type\":\"string\",\"format\":\"uuid\"}},\"required\":[\"timerId\"]}",
        },
        new()
        {
            SkillId = "timer.list",
            Name = "Timer List",
            Description = "Lists all currently active countdown timers on the host.",
            Category = "timer",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
        },
        new()
        {
            SkillId = "alarm.set",
            Name = "Alarm Set",
            Description = "Sets an alarm for a specific time of day with optional recurring days of week.",
            Category = "alarm",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"time\":{\"type\":\"string\"},\"title\":{\"type\":\"string\"},\"daysOfWeek\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"required\":[\"time\"]}",
        },
        new()
        {
            SkillId = "alarm.cancel",
            Name = "Alarm Cancel",
            Description = "Cancels an alarm by its GUID.",
            Category = "alarm",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"alarmId\":{\"type\":\"string\",\"format\":\"uuid\"}},\"required\":[\"alarmId\"]}",
        },
        new()
        {
            SkillId = "alarm.list",
            Name = "Alarm List",
            Description = "Lists all configured alarms on the host.",
            Category = "alarm",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
        },
    ];
}

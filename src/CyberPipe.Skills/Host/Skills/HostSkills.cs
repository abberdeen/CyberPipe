using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

#region Media Skills

/// <summary>Навык запуска воспроизведения медиа (TASK-050); перед Play — аудио-оценка безопасности (TASK-071).</summary>
public sealed class MediaPlaySkill(IHostMediaController controller, IAudioContentSafetyEvaluator? safetyEvaluator = null) : IHostSkill
{
    public string SkillId => "media.play";
    public string Name => "Media Play";
    public string Description => "Starts or resumes media playback on the host.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        if (safetyEvaluator is not null)
        {
            AudioSafetyDecision decision = await safetyEvaluator
                .EvaluateAsync(ParseSafetyRequest(payloadJson), cancellationToken)
                .ConfigureAwait(false);
            if (decision.Action == AudioSafetyAction.DeferOrDeny)
            {
                return HostSkillResult.Failed($"Audio safety blocked playback: {decision.Explanation}", "AudioSafety.Blocked");
            }

            if (decision.Action == AudioSafetyAction.ReduceVolume && decision.RecommendedVolumeLevel is { } level)
            {
                int percent = (int)Math.Round(Math.Clamp(level, 0.0, 1.0) * 100);
                await controller.SetVolumeAsync(percent, cancellationToken).ConfigureAwait(false);
            }
        }

        return await controller.PlayAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AudioSafetyRequest ParseSafetyRequest(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new AudioSafetyRequest();
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(payloadJson);
            JsonElement root = doc.RootElement;
            return new AudioSafetyRequest
            {
                Title = HostSkillJson.OptionalString(root, "title"),
                Description = HostSkillJson.OptionalString(root, "description"),
                Transcript = HostSkillJson.OptionalString(root, "transcript"),
            };
        }
        catch (JsonException)
        {
            return new AudioSafetyRequest { Title = payloadJson };
        }
    }
}

/// <summary>Навык паузы воспроизведения медиа (TASK-050).</summary>
public sealed class MediaPauseSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "media.pause";
    public string Name => "Media Pause";
    public string Description => "Pauses media playback on the host.";

    public Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default) =>
        controller.PauseAsync(cancellationToken);
}

/// <summary>Навык переключения режима воспроизведения/паузы медиа (TASK-050).</summary>
public sealed class MediaPlayPauseSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "media.playpause";
    public string Name => "Media Play/Pause";
    public string Description => "Toggles media playback state on the host.";

    public Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default) =>
        controller.PlayPauseAsync(cancellationToken);
}

/// <summary>Навык перехода к следующему медиа-треку (TASK-050).</summary>
public sealed class MediaNextSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "media.next";
    public string Name => "Media Next Track";
    public string Description => "Skips to the next media track on the host.";

    public Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default) =>
        controller.NextTrackAsync(cancellationToken);
}

/// <summary>Навык возврата к предыдущему медиа-треку (TASK-050).</summary>
public sealed class MediaPreviousSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "media.previous";
    public string Name => "Media Previous Track";
    public string Description => "Returns to the previous media track on the host.";

    public Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default) =>
        controller.PreviousTrackAsync(cancellationToken);
}

#endregion

#region Volume Skills

/// <summary>Навык чтения текущей громкости и статуса mute (TASK-050).</summary>
public sealed class VolumeGetSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "volume.get";
    public string Name => "Volume Get";
    public string Description => "Gets current master audio volume (0-100) and mute status on the host.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        try
        {
            int volume = await controller.GetVolumeAsync(cancellationToken);
            bool isMuted = await controller.IsMutedAsync(cancellationToken);
            string json = $"{{\"volume\":{volume},\"isMuted\":{(isMuted ? "true" : "false")}}}";
            return HostSkillResult.Ok(json, $"Host volume is {volume}%, muted: {isMuted}.");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to retrieve volume: {ex.Message}");
        }
    }
}

/// <summary>Навык установки абсолютного уровня громкости с HITL-защитой при резком повышении (TASK-050).</summary>
public sealed class VolumeSetSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "volume.set";
    public string Name => "Volume Set";
    public string Description => "Sets host audio volume (0-100). Drastic changes require owner approval.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        if (!TryReadRequest(payloadJson, out int targetVolume, out bool force, out HostSkillResult? error))
        {
            return error!;
        }

        int currentVolume = await controller.GetVolumeAsync(cancellationToken);
        if (!force && IsDrasticChange(currentVolume, targetVolume))
        {
            return HostSkillResult.NeedsApproval(
                $"Changing volume from {currentVolume}% to {targetVolume}% is a drastic change and requires confirmation.",
                payloadJson);
        }

        return await controller.SetVolumeAsync(targetVolume, cancellationToken);
    }

    private static bool TryReadRequest(string payloadJson, out int targetVolume, out bool force, out HostSkillResult? error)
    {
        targetVolume = 0;
        force = false;
        error = null;

        int? volume = null;
        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(payloadJson);
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (!HostSkillJson.TryReadOptionalInt(root, "volume", out volume))
                    {
                        error = HostSkillResult.ValidationError("Invalid volume format in payload.");
                        return false;
                    }

                    if (volume is null && !HostSkillJson.TryReadOptionalInt(root, "volumePercent", out volume))
                    {
                        error = HostSkillResult.ValidationError("Invalid volumePercent format in payload.");
                        return false;
                    }

                    force = HostSkillJson.ReadBool(root, "force") ?? HostSkillJson.ReadBool(root, "isApproved") ?? false;
                }
                else if (root.ValueKind == JsonValueKind.Number && root.TryGetInt32(out int direct))
                {
                    volume = direct;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                error = HostSkillResult.ValidationError("Invalid JSON payload for volume.set.");
                return false;
            }
        }

        if (volume is null)
        {
            error = HostSkillResult.ValidationError("Missing volume or volumePercent in payload.");
            return false;
        }

        if (volume is < 0 or > 100)
        {
            error = HostSkillResult.ValidationError($"Volume must be between 0 and 100. Received: {volume}.");
            return false;
        }

        targetVolume = volume.Value;
        return true;
    }

    // Резкое изменение громкости требует подтверждения владельца (TASK-050).
    private static bool IsDrasticChange(int current, int target) =>
        Math.Abs(target - current) > 40 || (target > 80 && current <= 40);
}

/// <summary>Навык увеличения громкости на шаг (TASK-050).</summary>
public sealed class VolumeUpSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "volume.up";
    public string Name => "Volume Up";
    public string Description => "Increases host audio volume by a step (default 5%). Large steps require approval.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        if (!HostSkillJson.TryReadVolumeStep(payloadJson, readForce: true, "volume.up", out int step, out bool force, out HostSkillResult? error))
        {
            return error!;
        }

        if (!force && step > 40)
        {
            return HostSkillResult.NeedsApproval($"Volume increase step of {step}% requires confirmation.", payloadJson);
        }

        return await controller.VolumeUpAsync(step, cancellationToken);
    }
}

/// <summary>Навык уменьшения громкости на шаг (TASK-050).</summary>
public sealed class VolumeDownSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "volume.down";
    public string Name => "Volume Down";
    public string Description => "Decreases host audio volume by a step (default 5%).";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        if (!HostSkillJson.TryReadVolumeStep(payloadJson, readForce: false, "volume.down", out int step, out _, out HostSkillResult? error))
        {
            return error!;
        }

        return await controller.VolumeDownAsync(step, cancellationToken);
    }
}

/// <summary>Навык включения беззвучного режима (TASK-050).</summary>
public sealed class VolumeMuteSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "volume.mute";
    public string Name => "Volume Mute";
    public string Description => "Mutes host audio.";

    public Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default) =>
        controller.MuteAsync(cancellationToken);
}

/// <summary>Навык выключения беззвучного режима (TASK-050).</summary>
public sealed class VolumeUnmuteSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "volume.unmute";
    public string Name => "Volume Unmute";
    public string Description => "Unmutes host audio.";

    public Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default) =>
        controller.UnmuteAsync(cancellationToken);
}

/// <summary>Навык переключения беззвучного режима (TASK-050).</summary>
public sealed class VolumeToggleMuteSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "volume.togglemute";
    public string Name => "Volume Toggle Mute";
    public string Description => "Toggles host audio mute.";

    public Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default) =>
        controller.ToggleMuteAsync(cancellationToken);
}

#endregion

#region System Status Skills

/// <summary>Навык считывания системного статуса хоста (TASK-050).</summary>
public sealed class SystemStatusSkill(ISystemStatusProvider provider) : IHostSkill
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public string SkillId => "system.status";
    public string Name => "System Status";
    public string Description => "Retrieves host system status including CPU, memory, disks, battery, and uptime.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        try
        {
            SystemStatusDto status = await provider.GetStatusAsync(cancellationToken);
            return HostSkillResult.Ok(JsonSerializer.Serialize(status, JsonOpts), "System status retrieved.");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to inspect system status: {ex.Message}");
        }
    }
}

#endregion

#region Timer Skills

/// <summary>Навык установки таймера (TASK-050).</summary>
public sealed class TimerSetSkill(ITimerService timerService) : IHostSkill
{
    public string SkillId => "timer.set";
    public string Name => "Timer Set";
    public string Description => "Sets a timer for a given duration.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return HostSkillResult.ValidationError("Payload is required for timer.set.");
        }

        TimeSpan? duration;
        string title;
        string extraPayload;
        try
        {
            (duration, title, extraPayload) = ReadRequest(payloadJson);
        }
        catch (JsonException)
        {
            return HostSkillResult.ValidationError("Invalid JSON payload for timer.set.");
        }

        if (duration is null || duration.Value <= TimeSpan.Zero)
        {
            return HostSkillResult.ValidationError("Timer duration must be positive.");
        }

        try
        {
            TimerRecord record = await timerService.SetTimerAsync(duration.Value, title, extraPayload, cancellationToken);
            return HostSkillResult.Ok(
                JsonSerializer.Serialize(record),
                $"Timer '{title}' set for {duration.Value.TotalSeconds} seconds.");
        }
        catch (ArgumentException ex)
        {
            return HostSkillResult.ValidationError(ex.Message);
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to set timer: {ex.Message}");
        }
    }

    private static (TimeSpan? Duration, string Title, string ExtraPayload) ReadRequest(string payloadJson)
    {
        using JsonDocument doc = JsonDocument.Parse(payloadJson);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return (null, "Timer", "{}");
        }

        string extraPayload = root.TryGetProperty("payload", out JsonElement payloadProp)
            ? payloadProp.GetRawText()
            : "{}";
        return (HostSkillJson.ReadTimerDuration(root), HostSkillJson.ReadTitle(root, "Timer"), extraPayload);
    }
}

/// <summary>Навык отмены таймера по ID (TASK-050).</summary>
public sealed class TimerCancelSkill(ITimerService timerService) : IHostSkill
{
    public string SkillId => "timer.cancel";
    public string Name => "Timer Cancel";
    public string Description => "Cancels an active timer by ID.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        if (!HostSkillJson.TryReadId(
                payloadJson,
                "timerId",
                "Invalid JSON payload for timer.cancel.",
                "Invalid or missing timerId in payload.",
                out Guid timerId,
                out HostSkillResult? error))
        {
            return error!;
        }

        try
        {
            bool cancelled = await timerService.CancelTimerAsync(timerId, cancellationToken);
            return HostSkillResult.Ok(
                HostSkillJson.Cancelled("timerId", timerId, cancelled),
                cancelled ? "Timer successfully cancelled." : "Timer was not found or already completed.");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to cancel timer: {ex.Message}");
        }
    }
}

/// <summary>Навык получения списка активных таймеров (TASK-050).</summary>
public sealed class TimerListSkill(ITimerService timerService) : IHostSkill
{
    public string SkillId => "timer.list";
    public string Name => "Timer List";
    public string Description => "Lists all active timers on the host.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<TimerRecord> timers = await timerService.ListActiveTimersAsync(cancellationToken);
            return HostSkillResult.Ok(JsonSerializer.Serialize(timers), $"Found {timers.Count} active timer(s).");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to list timers: {ex.Message}");
        }
    }
}

#endregion

#region Alarm Skills

/// <summary>Навык установки будильника (TASK-050).</summary>
public sealed class AlarmSetSkill(IAlarmService alarmService) : IHostSkill
{
    public string SkillId => "alarm.set";
    public string Name => "Alarm Set";
    public string Description => "Sets an alarm for a specific time of day and optional days of week.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return HostSkillResult.ValidationError("Payload is required for alarm.set.");
        }

        TimeOnly? timeOfDay;
        string title;
        List<DayOfWeek>? daysOfWeek;
        try
        {
            (timeOfDay, title, daysOfWeek) = ReadRequest(payloadJson);
        }
        catch (JsonException)
        {
            return HostSkillResult.ValidationError("Invalid JSON payload for alarm.set.");
        }

        if (timeOfDay is null)
        {
            return HostSkillResult.ValidationError("Invalid or missing time in payload. Expected format 'HH:mm' or 'HH:mm:ss'.");
        }

        try
        {
            AlarmRecord record = await alarmService.SetAlarmAsync(timeOfDay.Value, title, daysOfWeek, cancellationToken);
            return HostSkillResult.Ok(JsonSerializer.Serialize(record), $"Alarm '{title}' set for {timeOfDay.Value}.");
        }
        catch (ArgumentException ex)
        {
            return HostSkillResult.ValidationError(ex.Message);
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to set alarm: {ex.Message}");
        }
    }

    private static (TimeOnly? Time, string Title, List<DayOfWeek>? Days) ReadRequest(string payloadJson)
    {
        using JsonDocument doc = JsonDocument.Parse(payloadJson);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return (null, "Alarm", null);
        }

        return (HostSkillJson.ReadTimeOfDay(root), HostSkillJson.ReadTitle(root, "Alarm"), HostSkillJson.ReadDaysOfWeek(root));
    }
}

/// <summary>Навык отмены будильника по ID (TASK-050).</summary>
public sealed class AlarmCancelSkill(IAlarmService alarmService) : IHostSkill
{
    public string SkillId => "alarm.cancel";
    public string Name => "Alarm Cancel";
    public string Description => "Cancels an alarm by ID.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        if (!HostSkillJson.TryReadId(
                payloadJson,
                "alarmId",
                "Invalid JSON payload for alarm.cancel.",
                "Invalid or missing alarmId in payload.",
                out Guid alarmId,
                out HostSkillResult? error))
        {
            return error!;
        }

        try
        {
            bool cancelled = await alarmService.CancelAlarmAsync(alarmId, cancellationToken);
            return HostSkillResult.Ok(
                HostSkillJson.Cancelled("alarmId", alarmId, cancelled),
                cancelled ? "Alarm cancelled." : "Alarm not found or already disabled.");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to cancel alarm: {ex.Message}");
        }
    }
}

/// <summary>Навык получения списка всех будильников (TASK-050).</summary>
public sealed class AlarmListSkill(IAlarmService alarmService) : IHostSkill
{
    public string SkillId => "alarm.list";
    public string Name => "Alarm List";
    public string Description => "Lists all configured alarms.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<AlarmRecord> alarms = await alarmService.ListAlarmsAsync(cancellationToken);
            return HostSkillResult.Ok(JsonSerializer.Serialize(alarms), $"Found {alarms.Count} alarm(s).");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to list alarms: {ex.Message}");
        }
    }
}

#endregion

/// <summary>Общий разбор JSON-нагрузки навыков хоста. Виден только этому файлу.</summary>
file static class HostSkillJson
{
    public static string? OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static string ReadTitle(JsonElement root, string fallback)
    {
        if (root.TryGetProperty("title", out JsonElement title) && !string.IsNullOrWhiteSpace(title.GetString()))
        {
            return title.GetString()!;
        }

        return fallback;
    }

    public static bool? ReadBool(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        return null;
    }

    /// <summary>true — свойства нет или оно прочитано. false — свойство есть, но это не целое число.</summary>
    public static bool TryReadOptionalInt(JsonElement root, string name, out int? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out JsonElement property))
        {
            return true;
        }

        if (!TryReadInt(property, out int number))
        {
            return false;
        }

        value = number;
        return true;
    }

    public static bool TryReadVolumeStep(
        string payloadJson,
        bool readForce,
        string skillId,
        out int step,
        out bool force,
        out HostSkillResult? error)
    {
        step = 5;
        force = false;
        error = null;
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return ValidateStep(step, out error);
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(payloadJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("step", out JsonElement stepProperty))
                {
                    if (!TryReadInt(stepProperty, out step))
                    {
                        error = HostSkillResult.ValidationError("Invalid step format in payload.");
                        return false;
                    }
                }

                if (readForce)
                {
                    force = ReadBool(root, "force") ?? false;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            error = HostSkillResult.ValidationError($"Invalid JSON payload for {skillId}.");
            return false;
        }

        return ValidateStep(step, out error);
    }

    public static TimeSpan? ReadTimerDuration(JsonElement root)
    {
        if (root.TryGetProperty("durationSeconds", out JsonElement seconds) && seconds.TryGetDouble(out double durationSeconds))
        {
            return TimeSpan.FromSeconds(durationSeconds);
        }

        if (root.TryGetProperty("seconds", out JsonElement shortSeconds) && shortSeconds.TryGetDouble(out double secondsValue))
        {
            return TimeSpan.FromSeconds(secondsValue);
        }

        if (!root.TryGetProperty("duration", out JsonElement duration))
        {
            return null;
        }

        if (duration.ValueKind == JsonValueKind.String && TimeSpan.TryParse(duration.GetString(), out TimeSpan parsed))
        {
            return parsed;
        }

        return duration.TryGetDouble(out double numericDuration)
            ? TimeSpan.FromSeconds(numericDuration)
            : null;
    }

    public static TimeOnly? ReadTimeOfDay(JsonElement root)
    {
        if (TryReadTime(root, "time", out TimeOnly time))
        {
            return time;
        }

        return TryReadTime(root, "timeOfDay", out TimeOnly timeOfDay) ? timeOfDay : null;
    }

    public static List<DayOfWeek>? ReadDaysOfWeek(JsonElement root)
    {
        if (!root.TryGetProperty("daysOfWeek", out JsonElement days) || days.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var result = new List<DayOfWeek>();
        foreach (JsonElement day in days.EnumerateArray())
        {
            if (day.ValueKind == JsonValueKind.String &&
                Enum.TryParse(day.GetString(), ignoreCase: true, out DayOfWeek parsed))
            {
                result.Add(parsed);
            }
        }

        return result;
    }

    public static bool TryReadId(
        string payloadJson,
        string propertyName,
        string invalidJsonMessage,
        string missingIdMessage,
        out Guid id,
        out HostSkillResult? error)
    {
        id = default;
        error = null;
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            error = HostSkillResult.ValidationError(missingIdMessage);
            return false;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(payloadJson);
            if (TryReadGuid(doc.RootElement, propertyName, out id))
            {
                return true;
            }
        }
        catch (JsonException)
        {
            error = HostSkillResult.ValidationError(invalidJsonMessage);
            return false;
        }

        error = HostSkillResult.ValidationError(missingIdMessage);
        return false;
    }

    public static string Cancelled(string idName, Guid id, bool cancelled) =>
        $"{{\"{idName}\":\"{id}\",\"cancelled\":{(cancelled ? "true" : "false")}}}";

    private static bool TryReadInt(JsonElement value, out int number)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number))
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
        {
            return true;
        }

        number = 0;
        return false;
    }

    private static bool TryReadGuid(JsonElement root, string propertyName, out Guid id)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out JsonElement property) &&
            Guid.TryParse(property.GetString(), out id))
        {
            return true;
        }

        if (root.ValueKind == JsonValueKind.String && Guid.TryParse(root.GetString(), out id))
        {
            return true;
        }

        id = default;
        return false;
    }

    private static bool TryReadTime(JsonElement root, string name, out TimeOnly time)
    {
        time = default;
        return root.TryGetProperty(name, out JsonElement value) &&
               value.ValueKind == JsonValueKind.String &&
               TimeOnly.TryParse(value.GetString(), out time);
    }

    private static bool ValidateStep(int step, out HostSkillResult? error)
    {
        if (step is <= 0 or > 100)
        {
            error = HostSkillResult.ValidationError($"Volume step must be between 1 and 100. Received: {step}.");
            return false;
        }

        error = null;
        return true;
    }
}

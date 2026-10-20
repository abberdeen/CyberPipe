using System.Text.Json;
using System.Text.Json.Nodes;
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
            AudioSafetyDecision decision = await safetyEvaluator.EvaluateAsync(ParseSafetyRequest(payloadJson), cancellationToken).ConfigureAwait(false);
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
            return new AudioSafetyRequest();
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            JsonElement root = doc.RootElement;
            return new AudioSafetyRequest
            {
                Title = GetString(root, "title"),
                Description = GetString(root, "description"),
                Transcript = GetString(root, "transcript"),
            };
        }
        catch (JsonException)
        {
            return new AudioSafetyRequest { Title = payloadJson };
        }
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
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
        int? targetVolume = null;
        bool force = false;

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                JsonElement root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("volume", out JsonElement volProp))
                    {
                        if (volProp.ValueKind == JsonValueKind.Number && volProp.TryGetInt32(out int vol))
                        {
                            targetVolume = vol;
                        }
                        else if (volProp.ValueKind == JsonValueKind.String && int.TryParse(volProp.GetString(), out int sVol))
                        {
                            targetVolume = sVol;
                        }
                        else
                        {
                            return HostSkillResult.ValidationError("Invalid volume format in payload.");
                        }
                    }
                    else if (root.TryGetProperty("volumePercent", out JsonElement pctProp))
                    {
                        if (pctProp.ValueKind == JsonValueKind.Number && pctProp.TryGetInt32(out int pct))
                        {
                            targetVolume = pct;
                        }
                        else if (pctProp.ValueKind == JsonValueKind.String && int.TryParse(pctProp.GetString(), out int sPct))
                        {
                            targetVolume = sPct;
                        }
                        else
                        {
                            return HostSkillResult.ValidationError("Invalid volumePercent format in payload.");
                        }
                    }

                    if (root.TryGetProperty("force", out JsonElement forceProp) && forceProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        force = forceProp.GetBoolean();
                    }
                    else if (root.TryGetProperty("isApproved", out JsonElement approvedProp) && approvedProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        force = approvedProp.GetBoolean();
                    }
                }
                else if (root.ValueKind == JsonValueKind.Number && root.TryGetInt32(out int directNum))
                {
                    targetVolume = directNum;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return HostSkillResult.ValidationError("Invalid JSON payload for volume.set.");
            }
        }

        if (targetVolume is null)
        {
            return HostSkillResult.ValidationError("Missing volume or volumePercent in payload.");
        }

        if (targetVolume is < 0 or > 100)
        {
            return HostSkillResult.ValidationError($"Volume must be between 0 and 100. Received: {targetVolume}.");
        }

        int currentVolume = await controller.GetVolumeAsync(cancellationToken);

        // Ограничения и подтверждение (TASK-050): резкое изменение громкости требует HITL-подтверждения
        if (!force)
        {
            int delta = Math.Abs(targetVolume.Value - currentVolume);
            bool isDrasticJump = delta > 40 || (targetVolume.Value > 80 && currentVolume <= 40);

            if (isDrasticJump)
            {
                return HostSkillResult.NeedsApproval(
                    $"Changing volume from {currentVolume}% to {targetVolume.Value}% is a drastic change and requires confirmation.",
                    payloadJson);
            }
        }

        return await controller.SetVolumeAsync(targetVolume.Value, cancellationToken);
    }
}

/// <summary>Навык увеличения громкости на шаг (TASK-050).</summary>
public sealed class VolumeUpSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "volume.up";
    public string Name => "Volume Up";
    public string Description => "Increases host audio volume by a step (default 5%). Large steps require approval.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        int step = 5;
        bool force = false;

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("step", out JsonElement stepProp))
                    {
                        if (stepProp.ValueKind == JsonValueKind.Number && stepProp.TryGetInt32(out int parsedStep))
                        {
                            step = parsedStep;
                        }
                        else if (stepProp.ValueKind == JsonValueKind.String && int.TryParse(stepProp.GetString(), out int sStep))
                        {
                            step = sStep;
                        }
                        else
                        {
                            return HostSkillResult.ValidationError("Invalid step format in payload.");
                        }
                    }

                    if (doc.RootElement.TryGetProperty("force", out JsonElement forceProp) && forceProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        force = forceProp.GetBoolean();
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return HostSkillResult.ValidationError("Invalid JSON payload for volume.up.");
            }
        }

        if (step is <= 0 or > 100)
        {
            return HostSkillResult.ValidationError($"Volume step must be between 1 and 100. Received: {step}.");
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
        int step = 5;

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("step", out JsonElement stepProp))
                {
                    if (stepProp.ValueKind == JsonValueKind.Number && stepProp.TryGetInt32(out int parsedStep))
                    {
                        step = parsedStep;
                    }
                    else if (stepProp.ValueKind == JsonValueKind.String && int.TryParse(stepProp.GetString(), out int sStep))
                    {
                        step = sStep;
                    }
                    else
                    {
                        return HostSkillResult.ValidationError("Invalid step format in payload.");
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return HostSkillResult.ValidationError("Invalid JSON payload for volume.down.");
            }
        }

        if (step is <= 0 or > 100)
        {
            return HostSkillResult.ValidationError($"Volume step must be between 1 and 100. Received: {step}.");
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
            string json = JsonSerializer.Serialize(status, JsonOpts);
            return HostSkillResult.Ok(json, "System status retrieved.");
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
        TimeSpan? duration = null;
        string title = "Timer";
        string extraPayload = "{}";

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return HostSkillResult.ValidationError("Payload is required for timer.set.");
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            JsonElement root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("durationSeconds", out JsonElement secProp) && secProp.TryGetDouble(out double sec))
                {
                    duration = TimeSpan.FromSeconds(sec);
                }
                else if (root.TryGetProperty("seconds", out JsonElement sProp) && sProp.TryGetDouble(out double s))
                {
                    duration = TimeSpan.FromSeconds(s);
                }
                else if (root.TryGetProperty("duration", out JsonElement durProp))
                {
                    if (durProp.ValueKind == JsonValueKind.String && TimeSpan.TryParse(durProp.GetString(), out TimeSpan parsed))
                    {
                        duration = parsed;
                    }
                    else if (durProp.TryGetDouble(out double durNum))
                    {
                        duration = TimeSpan.FromSeconds(durNum);
                    }
                }

                if (root.TryGetProperty("title", out JsonElement titleProp) && !string.IsNullOrWhiteSpace(titleProp.GetString()))
                {
                    title = titleProp.GetString()!;
                }

                if (root.TryGetProperty("payload", out JsonElement payloadProp))
                {
                    extraPayload = payloadProp.GetRawText();
                }
            }
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
            string json = JsonSerializer.Serialize(record);
            return HostSkillResult.Ok(json, $"Timer '{title}' set for {duration.Value.TotalSeconds} seconds.");
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
}

/// <summary>Навык отмены таймера по ID (TASK-050).</summary>
public sealed class TimerCancelSkill(ITimerService timerService) : IHostSkill
{
    public string SkillId => "timer.cancel";
    public string Name => "Timer Cancel";
    public string Description => "Cancels an active timer by ID.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        Guid? timerId = null;

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("timerId", out JsonElement idProp) &&
                    Guid.TryParse(idProp.GetString(), out Guid parsedId))
                {
                    timerId = parsedId;
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.String &&
                         Guid.TryParse(doc.RootElement.GetString(), out Guid directId))
                {
                    timerId = directId;
                }
            }
            catch (JsonException)
            {
                return HostSkillResult.ValidationError("Invalid JSON payload for timer.cancel.");
            }
        }

        if (timerId is null)
        {
            return HostSkillResult.ValidationError("Invalid or missing timerId in payload.");
        }

        try
        {
            bool cancelled = await timerService.CancelTimerAsync(timerId.Value, cancellationToken);
            string json = $"{{\"timerId\":\"{timerId.Value}\",\"cancelled\":{(cancelled ? "true" : "false")}}}";
            return HostSkillResult.Ok(json, cancelled ? "Timer successfully cancelled." : "Timer was not found or already completed.");
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
            string json = JsonSerializer.Serialize(timers);
            return HostSkillResult.Ok(json, $"Found {timers.Count} active timer(s).");
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
        TimeOnly? timeOfDay = null;
        string title = "Alarm";
        List<DayOfWeek>? daysOfWeek = null;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return HostSkillResult.ValidationError("Payload is required for alarm.set.");
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            JsonElement root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("time", out JsonElement timeProp) &&
                    timeProp.ValueKind == JsonValueKind.String &&
                    TimeOnly.TryParse(timeProp.GetString(), out TimeOnly parsedTime))
                {
                    timeOfDay = parsedTime;
                }
                else if (root.TryGetProperty("timeOfDay", out JsonElement todProp) &&
                         todProp.ValueKind == JsonValueKind.String &&
                         TimeOnly.TryParse(todProp.GetString(), out TimeOnly parsedTod))
                {
                    timeOfDay = parsedTod;
                }

                if (root.TryGetProperty("title", out JsonElement titleProp) && !string.IsNullOrWhiteSpace(titleProp.GetString()))
                {
                    title = titleProp.GetString()!;
                }

                if (root.TryGetProperty("daysOfWeek", out JsonElement daysProp) && daysProp.ValueKind == JsonValueKind.Array)
                {
                    daysOfWeek = [];
                    foreach (JsonElement dayElem in daysProp.EnumerateArray())
                    {
                        if (dayElem.ValueKind == JsonValueKind.String &&
                            Enum.TryParse<DayOfWeek>(dayElem.GetString(), ignoreCase: true, out DayOfWeek parsedDay))
                        {
                            daysOfWeek.Add(parsedDay);
                        }
                    }
                }
            }
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
            string json = JsonSerializer.Serialize(record);
            return HostSkillResult.Ok(json, $"Alarm '{title}' set for {timeOfDay.Value}.");
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
}

/// <summary>Навык отмены будильника по ID (TASK-050).</summary>
public sealed class AlarmCancelSkill(IAlarmService alarmService) : IHostSkill
{
    public string SkillId => "alarm.cancel";
    public string Name => "Alarm Cancel";
    public string Description => "Cancels an alarm by ID.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        Guid? alarmId = null;

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("alarmId", out JsonElement idProp) &&
                    Guid.TryParse(idProp.GetString(), out Guid parsedId))
                {
                    alarmId = parsedId;
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.String &&
                         Guid.TryParse(doc.RootElement.GetString(), out Guid directId))
                {
                    alarmId = directId;
                }
            }
            catch (JsonException)
            {
                return HostSkillResult.ValidationError("Invalid JSON payload for alarm.cancel.");
            }
        }

        if (alarmId is null)
        {
            return HostSkillResult.ValidationError("Invalid or missing alarmId in payload.");
        }

        try
        {
            bool cancelled = await alarmService.CancelAlarmAsync(alarmId.Value, cancellationToken);
            string json = $"{{\"alarmId\":\"{alarmId.Value}\",\"cancelled\":{(cancelled ? "true" : "false")}}}";
            return HostSkillResult.Ok(json, cancelled ? "Alarm cancelled." : "Alarm not found or already disabled.");
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
            string json = JsonSerializer.Serialize(alarms);
            return HostSkillResult.Ok(json, $"Found {alarms.Count} alarm(s).");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to list alarms: {ex.Message}");
        }
    }
}

#endregion

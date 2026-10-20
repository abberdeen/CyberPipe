using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

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

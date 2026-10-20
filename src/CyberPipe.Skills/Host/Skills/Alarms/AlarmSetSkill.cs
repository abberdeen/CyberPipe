using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

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

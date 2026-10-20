using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

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

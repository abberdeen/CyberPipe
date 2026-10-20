using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

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

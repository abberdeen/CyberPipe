using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

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

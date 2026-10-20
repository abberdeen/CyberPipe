using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

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

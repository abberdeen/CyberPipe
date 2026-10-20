using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

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

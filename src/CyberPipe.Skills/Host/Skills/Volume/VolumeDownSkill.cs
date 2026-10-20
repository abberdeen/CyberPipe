using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

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

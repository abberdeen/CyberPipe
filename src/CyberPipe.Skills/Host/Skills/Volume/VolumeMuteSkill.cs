using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

/// <summary>Навык включения беззвучного режима (TASK-050).</summary>
public sealed class VolumeMuteSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "volume.mute";
    public string Name => "Volume Mute";
    public string Description => "Mutes host audio.";

    public Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default) =>
        controller.MuteAsync(cancellationToken);
}

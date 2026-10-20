using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

/// <summary>Навык выключения беззвучного режима (TASK-050).</summary>
public sealed class VolumeUnmuteSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "volume.unmute";
    public string Name => "Volume Unmute";
    public string Description => "Unmutes host audio.";

    public Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default) =>
        controller.UnmuteAsync(cancellationToken);
}

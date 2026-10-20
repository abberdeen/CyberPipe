using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

/// <summary>Навык переключения беззвучного режима (TASK-050).</summary>
public sealed class VolumeToggleMuteSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "volume.togglemute";
    public string Name => "Volume Toggle Mute";
    public string Description => "Toggles host audio mute.";

    public Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default) =>
        controller.ToggleMuteAsync(cancellationToken);
}

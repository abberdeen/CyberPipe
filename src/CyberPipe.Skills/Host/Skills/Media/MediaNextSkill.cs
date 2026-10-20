using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

/// <summary>Навык перехода к следующему медиа-треку (TASK-050).</summary>
public sealed class MediaNextSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "media.next";
    public string Name => "Media Next Track";
    public string Description => "Skips to the next media track on the host.";

    public Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default) =>
        controller.NextTrackAsync(cancellationToken);
}

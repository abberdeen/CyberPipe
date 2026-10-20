using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

/// <summary>Навык возврата к предыдущему медиа-треку (TASK-050).</summary>
public sealed class MediaPreviousSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "media.previous";
    public string Name => "Media Previous Track";
    public string Description => "Returns to the previous media track on the host.";

    public Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default) =>
        controller.PreviousTrackAsync(cancellationToken);
}

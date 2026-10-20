using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

/// <summary>Навык паузы воспроизведения медиа (TASK-050).</summary>
public sealed class MediaPauseSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "media.pause";
    public string Name => "Media Pause";
    public string Description => "Pauses media playback on the host.";

    public Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default) =>
        controller.PauseAsync(cancellationToken);
}

using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

/// <summary>Навык переключения режима воспроизведения/паузы медиа (TASK-050).</summary>
public sealed class MediaPlayPauseSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "media.playpause";
    public string Name => "Media Play/Pause";
    public string Description => "Toggles media playback state on the host.";

    public Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default) =>
        controller.PlayPauseAsync(cancellationToken);
}

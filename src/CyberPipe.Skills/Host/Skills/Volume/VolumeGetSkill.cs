using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

/// <summary>Навык чтения текущей громкости и статуса mute (TASK-050).</summary>
public sealed class VolumeGetSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "volume.get";
    public string Name => "Volume Get";
    public string Description => "Gets current master audio volume (0-100) and mute status on the host.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        try
        {
            int volume = await controller.GetVolumeAsync(cancellationToken);
            bool isMuted = await controller.IsMutedAsync(cancellationToken);
            string json = $"{{\"volume\":{volume},\"isMuted\":{(isMuted ? "true" : "false")}}}";
            return HostSkillResult.Ok(json, $"Host volume is {volume}%, muted: {isMuted}.");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to retrieve volume: {ex.Message}");
        }
    }
}

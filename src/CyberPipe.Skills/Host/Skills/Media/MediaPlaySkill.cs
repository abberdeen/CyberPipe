using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

/// <summary>Навык запуска воспроизведения медиа (TASK-050); перед Play — аудио-оценка безопасности (TASK-071).</summary>
public sealed class MediaPlaySkill(IHostMediaController controller, IAudioContentSafetyEvaluator? safetyEvaluator = null) : IHostSkill
{
    public string SkillId => "media.play";
    public string Name => "Media Play";
    public string Description => "Starts or resumes media playback on the host.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        if (safetyEvaluator is not null)
        {
            AudioSafetyDecision decision = await safetyEvaluator
                .EvaluateAsync(ParseSafetyRequest(payloadJson), cancellationToken)
                .ConfigureAwait(false);
            if (decision.Action == AudioSafetyAction.DeferOrDeny)
            {
                return HostSkillResult.Failed($"Audio safety blocked playback: {decision.Explanation}", "AudioSafety.Blocked");
            }

            if (decision.Action == AudioSafetyAction.ReduceVolume && decision.RecommendedVolumeLevel is { } level)
            {
                int percent = (int)Math.Round(Math.Clamp(level, 0.0, 1.0) * 100);
                await controller.SetVolumeAsync(percent, cancellationToken).ConfigureAwait(false);
            }
        }

        return await controller.PlayAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AudioSafetyRequest ParseSafetyRequest(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new AudioSafetyRequest();
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(payloadJson);
            JsonElement root = doc.RootElement;
            return new AudioSafetyRequest
            {
                Title = HostSkillJson.OptionalString(root, "title"),
                Description = HostSkillJson.OptionalString(root, "description"),
                Transcript = HostSkillJson.OptionalString(root, "transcript"),
            };
        }
        catch (JsonException)
        {
            return new AudioSafetyRequest { Title = payloadJson };
        }
    }
}

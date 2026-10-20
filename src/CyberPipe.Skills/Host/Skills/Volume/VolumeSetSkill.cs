using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

/// <summary>Навык установки абсолютного уровня громкости с HITL-защитой при резком повышении (TASK-050).</summary>
public sealed class VolumeSetSkill(IHostMediaController controller) : IHostSkill
{
    public string SkillId => "volume.set";
    public string Name => "Volume Set";
    public string Description => "Sets host audio volume (0-100). Drastic increases require owner approval.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        if (!TryReadRequest(payloadJson, out int targetVolume, out bool force, out HostSkillResult? error))
        {
            return error!;
        }

        int currentVolume = await controller.GetVolumeAsync(cancellationToken);
        if (!force && IsDrasticChange(currentVolume, targetVolume))
        {
            return HostSkillResult.NeedsApproval(
                $"Increasing volume from {currentVolume}% to {targetVolume}% is a drastic change and requires confirmation.",
                payloadJson);
        }

        return await controller.SetVolumeAsync(targetVolume, cancellationToken);
    }

    private static bool TryReadRequest(string payloadJson, out int targetVolume, out bool force, out HostSkillResult? error)
    {
        targetVolume = 0;
        force = false;
        error = null;

        int? volume = null;
        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(payloadJson);
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (!HostSkillJson.TryReadOptionalInt(root, "volume", out volume))
                    {
                        error = HostSkillResult.ValidationError("Invalid volume format in payload.");
                        return false;
                    }

                    if (volume is null && !HostSkillJson.TryReadOptionalInt(root, "volumePercent", out volume))
                    {
                        error = HostSkillResult.ValidationError("Invalid volumePercent format in payload.");
                        return false;
                    }

                    force = HostSkillJson.ReadBool(root, "force") ?? HostSkillJson.ReadBool(root, "isApproved") ?? false;
                }
                else if (root.ValueKind == JsonValueKind.Number && root.TryGetInt32(out int direct))
                {
                    volume = direct;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                error = HostSkillResult.ValidationError("Invalid JSON payload for volume.set.");
                return false;
            }
        }

        if (volume is null)
        {
            error = HostSkillResult.ValidationError("Missing volume or volumePercent in payload.");
            return false;
        }

        if (volume is < 0 or > 100)
        {
            error = HostSkillResult.ValidationError($"Volume must be between 0 and 100. Received: {volume}.");
            return false;
        }

        targetVolume = volume.Value;
        return true;
    }

    // Резкое повышение громкости требует подтверждения владельца (TASK-050).
    // Уменьшение громкости безопасно для слуха и не требует подтверждения.
    private static bool IsDrasticChange(int current, int target) =>
        target > current && (target - current > 40 || (target > 80 && current <= 40));
}

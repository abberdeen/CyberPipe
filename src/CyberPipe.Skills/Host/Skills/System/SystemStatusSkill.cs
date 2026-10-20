using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

/// <summary>Навык считывания системного статуса хоста (TASK-050).</summary>
public sealed class SystemStatusSkill(ISystemStatusProvider provider) : IHostSkill
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public string SkillId => "system.status";
    public string Name => "System Status";
    public string Description => "Retrieves host system status including CPU, memory, disks, battery, and uptime.";

    public async Task<HostSkillResult> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        try
        {
            SystemStatusDto status = await provider.GetStatusAsync(cancellationToken);
            return HostSkillResult.Ok(JsonSerializer.Serialize(status, JsonOpts), "System status retrieved.");
        }
        catch (Exception ex)
        {
            return HostSkillResult.Failed($"Failed to inspect system status: {ex.Message}");
        }
    }
}

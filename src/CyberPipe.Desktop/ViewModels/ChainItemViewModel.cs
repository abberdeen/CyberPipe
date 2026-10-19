using CyberPipe.Contracts;
using CyberPipe.Desktop.Common;

namespace CyberPipe.Desktop.ViewModels;

public sealed class ChainItemViewModel : ViewModelBase
{
    public required ChainId Id { get; init; }
    public required string Name { get; init; }
    public required bool IsEnabled { get; init; }
    public required ChainTriggerKind TriggerKind { get; init; }
    public required string TriggerSummary { get; init; }
    public required string SkillId { get; init; }
    public required string ScriptSlug { get; init; }
    public required string ScriptVersion { get; init; }
    public required ChannelKind DeliveryChannel { get; init; }
    public required bool RequiresHitl { get; init; }
    public required int Version { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public static ChainItemViewModel FromDto(ChainDefinitionDto dto)
    {
        string triggerSummary = dto.Trigger.Kind switch
        {
            ChainTriggerKind.Cron => $"Cron: {dto.Trigger.CronExpression}",
            ChainTriggerKind.Webhook => $"Webhook: {dto.Trigger.WebhookPath}",
            ChainTriggerKind.Chat => "Chat Message",
            ChainTriggerKind.FunctionCall => "FunctionCall (Manual)",
            _ => dto.Trigger.Kind.ToString(),
        };

        return new ChainItemViewModel
        {
            Id = dto.Id,
            Name = dto.Name,
            IsEnabled = dto.IsEnabled,
            TriggerKind = dto.Trigger.Kind,
            TriggerSummary = triggerSummary,
            SkillId = dto.Skill.SkillId,
            ScriptSlug = dto.Script.PackageSlug,
            ScriptVersion = dto.Script.PackageVersion,
            DeliveryChannel = dto.Delivery.Channel,
            RequiresHitl = dto.RequiresHitl,
            Version = dto.Version,
            CreatedAtUtc = dto.CreatedAtUtc,
            UpdatedAtUtc = dto.UpdatedAtUtc,
        };
    }
}

public sealed class ChainRunStateViewModel : ViewModelBase
{
    public required RunId RunId { get; init; }
    public required ChainId ChainId { get; init; }
    public required ChainRunStatus Status { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? FinishedAtUtc { get; init; }
    public ErrorDto? Error { get; init; }
    public IReadOnlyList<ChainCheckpointDto> Checkpoints { get; init; } = [];

    public string FormattedDuration
    {
        get
        {
            DateTimeOffset end = FinishedAtUtc ?? DateTimeOffset.UtcNow;
            TimeSpan duration = end - StartedAtUtc;
            return $"{duration.TotalSeconds:F1}s";
        }
    }

    public static ChainRunStateViewModel FromDto(ChainRunState dto)
    {
        return new ChainRunStateViewModel
        {
            RunId = dto.RunId,
            ChainId = dto.ChainId,
            Status = dto.Status,
            StartedAtUtc = dto.StartedAtUtc,
            FinishedAtUtc = dto.FinishedAtUtc,
            Error = dto.Error,
            Checkpoints = dto.Checkpoints,
        };
    }
}

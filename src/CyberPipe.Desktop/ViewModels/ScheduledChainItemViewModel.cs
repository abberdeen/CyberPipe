using CyberPipe.Contracts;
using CyberPipe.Desktop.Common;

namespace CyberPipe.Desktop.ViewModels;

public sealed class ScheduledChainItemViewModel : ViewModelBase
{
    public required ChainId ChainId { get; init; }
    public required string CronExpression { get; init; }
    public DateTimeOffset? NextFireTimeUtc { get; init; }
    public bool IsPaused { get; set; }
    public ChainMisfirePolicy MisfirePolicy { get; init; }

    public string FormattedNextFire => NextFireTimeUtc?.ToLocalTime().ToString("g") ?? "—";

    public static ScheduledChainItemViewModel FromDto(ScheduledChainInfo dto)
    {
        return new ScheduledChainItemViewModel
        {
            ChainId = dto.ChainId,
            CronExpression = dto.CronExpression,
            NextFireTimeUtc = dto.NextFireTimeUtc,
            IsPaused = dto.IsPaused,
            MisfirePolicy = dto.MisfirePolicy,
        };
    }
}

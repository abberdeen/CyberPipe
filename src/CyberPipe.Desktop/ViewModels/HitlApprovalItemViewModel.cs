using CyberPipe.Contracts;
using CyberPipe.Desktop.Common;

namespace CyberPipe.Desktop.ViewModels;

public sealed class HitlApprovalItemViewModel : ViewModelBase
{
    public required Guid ApprovalId { get; init; }
    public required RunId RunId { get; init; }
    public required ChainId ChainId { get; init; }
    public required string Title { get; init; }
    public required string PreviewText { get; init; }
    public required string PayloadJson { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
    public HitlStatus Status { get; set; } = HitlStatus.Pending;

    public string FormattedExpiresAt => ExpiresAtUtc.ToLocalTime().ToString("g");

    public static HitlApprovalItemViewModel FromRecord(HitlApprovalRecord record)
    {
        return new HitlApprovalItemViewModel
        {
            ApprovalId = record.ApprovalId,
            RunId = record.RunId,
            ChainId = record.ChainId,
            Title = record.Title,
            PreviewText = record.PreviewText,
            PayloadJson = record.PayloadJson,
            CreatedAtUtc = record.CreatedAtUtc,
            ExpiresAtUtc = record.ExpiresAtUtc,
            Status = record.Status,
        };
    }
}

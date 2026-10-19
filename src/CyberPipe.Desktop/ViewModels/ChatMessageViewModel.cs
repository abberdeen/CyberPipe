using CyberPipe.Contracts;
using CyberPipe.Desktop.Common;

namespace CyberPipe.Desktop.ViewModels;

public sealed class ChatMessageViewModel : ViewModelBase
{
    public required bool IsUser { get; init; }
    public required string Sender { get; init; }
    public required string Text { get; init; }
    public string? AttachmentPath { get; init; }
    public IntentTier? Tier { get; init; }
    public double Confidence { get; init; }
    public string? MatchedRule { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string FormattedTimestamp => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    public string TierBadge => Tier switch
    {
        IntentTier.Tier0 => "Tier 0 (Regex)",
        IntentTier.Tier1 => "Tier 1 (Semantic)",
        IntentTier.Tier2 => "Tier 2 (Small LLM)",
        IntentTier.Tier3 => "Tier 3 (Large LLM)",
        _ => string.Empty,
    };
}

using Avalonia.Threading;
using CyberPipe.Contracts;
using CyberPipe.Desktop.Common;
using CyberPipe.Desktop.Services;

namespace CyberPipe.Desktop.ViewModels;

public sealed class ChatMessageViewModel : ViewModelBase
{
    private readonly IClipboardService? _clipboard;
    private bool _isCopied;
    private string _copyButtonText = "Copy";
    private string _copyButtonToolTip = "Скопировать сообщение в буфер обмена";

    public ChatMessageViewModel(IClipboardService? clipboard = null)
    {
        _clipboard = clipboard;
        CopyCommand = new AsyncRelayCommand(CopyAsync);
    }

    public required bool IsUser { get; init; }
    public required string Sender { get; init; }
    public required string Text { get; init; }
    public string? AttachmentPath { get; init; }
    public IntentTier? Tier { get; init; }
    public double Confidence { get; init; }
    public string? MatchedRule { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string FormattedTimestamp => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    public bool IsCopied
    {
        get => _isCopied;
        private set => SetProperty(ref _isCopied, value);
    }

    public string CopyButtonText
    {
        get => _copyButtonText;
        private set => SetProperty(ref _copyButtonText, value);
    }

    public string CopyButtonToolTip
    {
        get => _copyButtonToolTip;
        private set => SetProperty(ref _copyButtonToolTip, value);
    }

    public AsyncRelayCommand CopyCommand { get; }

    public async Task CopyAsync()
    {
        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        IClipboardService clipboard = _clipboard ?? AvaloniaClipboardService.Default;
        await clipboard.SetTextAsync(Text).ConfigureAwait(true);

        IsCopied = true;
        CopyButtonText = "Copied!";
        CopyButtonToolTip = "Скопировано в буфер обмена!";

        _ = Task.Run(async () =>
        {
            await Task.Delay(2000).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() =>
            {
                IsCopied = false;
                CopyButtonText = "Copy";
                CopyButtonToolTip = "Скопировать сообщение в буфер обмена";
            });
        });
    }

    public string TierBadge => Tier switch
    {
        IntentTier.Tier0 => "Tier 0 (Regex)",
        IntentTier.Tier1 => "Tier 1 (Semantic)",
        IntentTier.Tier2 => "Tier 2 (Small LLM)",
        IntentTier.Tier3 => "Tier 3 (Large LLM)",
        _ => string.Empty,
    };
}

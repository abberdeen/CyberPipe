using System.Collections.ObjectModel;
using CyberPipe.Contracts;
using CyberPipe.Desktop.Common;
using CyberPipe.Desktop.Localization;
using CyberPipe.Desktop.Services;

namespace CyberPipe.Desktop.ViewModels;

public sealed class ChatViewModel : ViewModelBase
{
    private readonly IIntentRouter _router;
    private readonly ITokenBudgetGovernor? _budgetGovernor;
    private readonly IDesktopNotificationService? _notifications;
    private readonly LocalizationManager _localization;

    private string _inputText = string.Empty;
    private string? _selectedAttachmentPath;
    private bool _isProcessing;
    private IntentTier? _currentTier;
    private BudgetStatusDto? _budgetStatus;
    private string? _statusMessage;
    private CancellationTokenSource? _cts;

    public ChatViewModel(
        IIntentRouter router,
        ITokenBudgetGovernor? budgetGovernor = null,
        IDesktopNotificationService? notifications = null,
        LocalizationManager? localization = null)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _budgetGovernor = budgetGovernor;
        _notifications = notifications;
        _localization = localization ?? LocalizationManager.Instance;

        SendCommand = new AsyncRelayCommand(SendMessageAsync, () => !IsProcessing && !string.IsNullOrWhiteSpace(InputText));
        AbortCommand = new RelayCommand(AbortCurrentTask, () => IsProcessing);
        ClearAttachmentCommand = new RelayCommand(() => SelectedAttachmentPath = null);
        RefreshBudgetCommand = new AsyncRelayCommand(RefreshBudgetAsync);

        _ = RefreshBudgetAsync();
    }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public string InputText
    {
        get => _inputText;
        set
        {
            if (SetProperty(ref _inputText, value))
            {
                SendCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? SelectedAttachmentPath
    {
        get => _selectedAttachmentPath;
        set => SetProperty(ref _selectedAttachmentPath, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        private set
        {
            if (SetProperty(ref _isProcessing, value))
            {
                SendCommand.RaiseCanExecuteChanged();
                AbortCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public IntentTier? CurrentTier
    {
        get => _currentTier;
        private set => SetProperty(ref _currentTier, value);
    }

    public BudgetStatusDto? BudgetStatus
    {
        get => _budgetStatus;
        private set => SetProperty(ref _budgetStatus, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public AsyncRelayCommand SendCommand { get; }
    public RelayCommand AbortCommand { get; }
    public RelayCommand ClearAttachmentCommand { get; }
    public AsyncRelayCommand RefreshBudgetCommand { get; }

    public async Task SendMessageAsync()
    {
        string text = InputText.Trim();
        if (string.IsNullOrWhiteSpace(text) || IsProcessing)
        {
            return;
        }

        string? attachment = SelectedAttachmentPath;
        InputText = string.Empty;
        SelectedAttachmentPath = null;

        Messages.Add(new ChatMessageViewModel
        {
            IsUser = true,
            Sender = "Owner",
            Text = text,
            AttachmentPath = attachment,
            Timestamp = DateTimeOffset.UtcNow,
        });

        IsProcessing = true;
        StatusMessage = _localization["Chat.Processing"];
        _cts = new CancellationTokenSource();

        try
        {
            var metadata = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(attachment))
            {
                metadata["attachment"] = attachment;
            }

            var request = new RouteRequest
            {
                Text = text,
                Channel = ChannelKind.Desktop,
                Locale = _localization.CurrentCulture,
                SenderId = "Owner",
                Metadata = metadata,
            };

            RouteResult result = await _router.RouteAsync(request, _cts.Token).ConfigureAwait(true);
            CurrentTier = result.Tier;

            string replyText = result.Status switch
            {
                RouteStatus.Matched => result.Intent ?? $"[Matched: {result.MatchedRuleName ?? "Rule"}]",
                RouteStatus.BudgetDenied => $"[Budget Denied: {result.Reason ?? "Exceeded"}]",
                RouteStatus.Escalate => $"[Escalated: {result.Reason ?? "Unknown"}]",
                _ => $"[Error: {result.Reason ?? "Routing failed"}]",
            };

            Messages.Add(new ChatMessageViewModel
            {
                IsUser = false,
                Sender = "CyberPipe",
                Text = replyText,
                Tier = result.Tier,
                Confidence = result.Confidence,
                MatchedRule = result.MatchedRuleName,
                Timestamp = DateTimeOffset.UtcNow,
            });

            StatusMessage = null;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization["Chat.TaskAborted"];
            Messages.Add(new ChatMessageViewModel
            {
                IsUser = false,
                Sender = "System",
                Text = _localization["Chat.TaskAborted"],
                Timestamp = DateTimeOffset.UtcNow,
            });
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            Messages.Add(new ChatMessageViewModel
            {
                IsUser = false,
                Sender = "System",
                Text = $"Error: {ex.Message}",
                Timestamp = DateTimeOffset.UtcNow,
            });
        }
        finally
        {
            IsProcessing = false;
            _cts?.Dispose();
            _cts = null;
            await RefreshBudgetAsync().ConfigureAwait(true);
        }
    }

    public void AbortCurrentTask()
    {
        _cts?.Cancel();
    }

    public async Task RefreshBudgetAsync()
    {
        if (_budgetGovernor == null)
        {
            return;
        }

        try
        {
            BudgetStatus = await _budgetGovernor.GetStatusAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Budget error: {ex.Message}";
        }
    }
}

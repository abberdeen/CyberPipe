using System.Collections.ObjectModel;
using CyberPipe.Contracts;
using CyberPipe.Desktop.Common;
using CyberPipe.Desktop.Localization;
using CyberPipe.Desktop.Services;

namespace CyberPipe.Desktop.ViewModels;

public sealed class HitlViewModel : ViewModelBase
{
    private readonly IHitlApprovalService _approvalService;
    private readonly IDesktopNotificationService? _notifications;
    private readonly LocalizationManager _localization;

    private HitlApprovalItemViewModel? _selectedApproval;
    private string _editedPayloadJson = string.Empty;
    private string _rejectReason = string.Empty;
    private bool _isBusy;
    private string? _statusMessage;

    public HitlViewModel(
        IHitlApprovalService approvalService,
        IDesktopNotificationService? notifications = null,
        LocalizationManager? localization = null)
    {
        _approvalService = approvalService ?? throw new ArgumentNullException(nameof(approvalService));
        _notifications = notifications;
        _localization = localization ?? LocalizationManager.Instance;

        ApproveCommand = new AsyncRelayCommand(ApproveAsync, () => SelectedApproval != null && !IsBusy);
        RejectCommand = new AsyncRelayCommand(RejectAsync, () => SelectedApproval != null && !IsBusy);
        EditCommand = new AsyncRelayCommand(EditAsync, () => SelectedApproval != null && !IsBusy && !string.IsNullOrWhiteSpace(EditedPayloadJson));
    }

    public ObservableCollection<HitlApprovalItemViewModel> PendingApprovals { get; } = [];

    public HitlApprovalItemViewModel? SelectedApproval
    {
        get => _selectedApproval;
        set
        {
            if (SetProperty(ref _selectedApproval, value))
            {
                EditedPayloadJson = value?.PayloadJson ?? string.Empty;
                RejectReason = string.Empty;
                ApproveCommand.RaiseCanExecuteChanged();
                RejectCommand.RaiseCanExecuteChanged();
                EditCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string EditedPayloadJson
    {
        get => _editedPayloadJson;
        set
        {
            if (SetProperty(ref _editedPayloadJson, value))
            {
                EditCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string RejectReason
    {
        get => _rejectReason;
        set => SetProperty(ref _rejectReason, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ApproveCommand.RaiseCanExecuteChanged();
                RejectCommand.RaiseCanExecuteChanged();
                EditCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public int PendingCount => PendingApprovals.Count;

    public AsyncRelayCommand ApproveCommand { get; }
    public AsyncRelayCommand RejectCommand { get; }
    public AsyncRelayCommand EditCommand { get; }

    public void EnqueueApproval(HitlApprovalRecord record)
    {
        var item = HitlApprovalItemViewModel.FromRecord(record);
        PendingApprovals.Add(item);
        if (SelectedApproval == null)
        {
            SelectedApproval = item;
        }

        OnPropertyChanged(nameof(PendingCount));

        _notifications?.ShowNotification(
            "HITL Approval Requested",
            $"{record.Title}: {record.PreviewText}",
            NotificationSeverity.Warning,
            () => SelectedApproval = item);
    }

    public async Task ApproveAsync()
    {
        if (SelectedApproval == null || IsBusy)
        {
            return;
        }

        HitlApprovalItemViewModel current = SelectedApproval;
        IsBusy = true;
        StatusMessage = "Approving...";
        try
        {
            HitlApprovalResult result = await _approvalService.ApproveAsync(
                current.ApprovalId,
                ownerId: "Owner",
                channel: ChannelKind.Desktop).ConfigureAwait(false);

            StatusMessage = $"Approved: {result.ApprovalId}";
            RemoveApproval(current);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Approve failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RejectAsync()
    {
        if (SelectedApproval == null || IsBusy)
        {
            return;
        }

        HitlApprovalItemViewModel current = SelectedApproval;
        IsBusy = true;
        StatusMessage = "Rejecting...";
        try
        {
            HitlApprovalResult result = await _approvalService.RejectAsync(
                current.ApprovalId,
                ownerId: "Owner",
                channel: ChannelKind.Desktop,
                reason: string.IsNullOrWhiteSpace(RejectReason) ? null : RejectReason).ConfigureAwait(false);

            StatusMessage = $"Rejected: {result.ApprovalId}";
            RemoveApproval(current);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Reject failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task EditAsync()
    {
        if (SelectedApproval == null || IsBusy || string.IsNullOrWhiteSpace(EditedPayloadJson))
        {
            return;
        }

        HitlApprovalItemViewModel current = SelectedApproval;
        IsBusy = true;
        StatusMessage = "Saving edit and approving...";
        try
        {
            HitlApprovalResult result = await _approvalService.EditAsync(
                current.ApprovalId,
                ownerId: "Owner",
                editedPayloadJson: EditedPayloadJson,
                channel: ChannelKind.Desktop).ConfigureAwait(false);

            StatusMessage = $"Edited & Approved: {result.ApprovalId}";
            RemoveApproval(current);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Edit failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RemoveApproval(HitlApprovalItemViewModel item)
    {
        PendingApprovals.Remove(item);
        SelectedApproval = PendingApprovals.FirstOrDefault();
        OnPropertyChanged(nameof(PendingCount));
    }
}

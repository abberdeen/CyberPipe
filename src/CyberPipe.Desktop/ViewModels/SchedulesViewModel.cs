using System.Collections.ObjectModel;
using CyberPipe.Contracts;
using CyberPipe.Desktop.Common;
using CyberPipe.Desktop.Localization;
using CyberPipe.Desktop.Services;

namespace CyberPipe.Desktop.ViewModels;

public sealed class SchedulesViewModel : ViewModelBase
{
    private readonly IChainSchedulerService _scheduler;
    private readonly IDesktopNotificationService? _notifications;
    private readonly LocalizationManager _localization;

    private ScheduledChainItemViewModel? _selectedSchedule;
    private string _chainIdInput = string.Empty;
    private string _cronExpressionInput = "0 0 12 * * ?";
    private string? _errorMessage;
    private string? _statusMessage;
    private bool _isBusy;

    public SchedulesViewModel(
        IChainSchedulerService scheduler,
        IDesktopNotificationService? notifications = null,
        LocalizationManager? localization = null)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _notifications = notifications;
        _localization = localization ?? LocalizationManager.Instance;

        LoadSchedulesCommand = new AsyncRelayCommand(LoadSchedulesAsync);
        ScheduleCommand = new AsyncRelayCommand(ScheduleChainAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(ChainIdInput) && !string.IsNullOrWhiteSpace(CronExpressionInput));
        PauseCommand = new AsyncRelayCommand(PauseScheduleAsync, () => SelectedSchedule != null && !SelectedSchedule.IsPaused && !IsBusy);
        ResumeCommand = new AsyncRelayCommand(ResumeScheduleAsync, () => SelectedSchedule != null && SelectedSchedule.IsPaused && !IsBusy);
        UnscheduleCommand = new AsyncRelayCommand(UnscheduleAsync, () => SelectedSchedule != null && !IsBusy);
        TriggerNowCommand = new AsyncRelayCommand(TriggerNowAsync, () => SelectedSchedule != null && !IsBusy);

        _ = LoadSchedulesAsync();
    }

    public ObservableCollection<ScheduledChainItemViewModel> Schedules { get; } = [];

    public ScheduledChainItemViewModel? SelectedSchedule
    {
        get => _selectedSchedule;
        set
        {
            if (SetProperty(ref _selectedSchedule, value))
            {
                PauseCommand.RaiseCanExecuteChanged();
                ResumeCommand.RaiseCanExecuteChanged();
                UnscheduleCommand.RaiseCanExecuteChanged();
                TriggerNowCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ChainIdInput
    {
        get => _chainIdInput;
        set
        {
            if (SetProperty(ref _chainIdInput, value))
            {
                ScheduleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string CronExpressionInput
    {
        get => _cronExpressionInput;
        set
        {
            if (SetProperty(ref _cronExpressionInput, value))
            {
                ScheduleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ScheduleCommand.RaiseCanExecuteChanged();
                PauseCommand.RaiseCanExecuteChanged();
                ResumeCommand.RaiseCanExecuteChanged();
                UnscheduleCommand.RaiseCanExecuteChanged();
                TriggerNowCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand LoadSchedulesCommand { get; }
    public AsyncRelayCommand ScheduleCommand { get; }
    public AsyncRelayCommand PauseCommand { get; }
    public AsyncRelayCommand ResumeCommand { get; }
    public AsyncRelayCommand UnscheduleCommand { get; }
    public AsyncRelayCommand TriggerNowCommand { get; }

    public async Task LoadSchedulesAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            IReadOnlyList<ScheduledChainInfo> items = await _scheduler.ListScheduledAsync().ConfigureAwait(false);
            Schedules.Clear();
            foreach (ScheduledChainInfo item in items)
            {
                Schedules.Add(ScheduledChainItemViewModel.FromDto(item));
            }

            if (SelectedSchedule == null && Schedules.Count > 0)
            {
                SelectedSchedule = Schedules[0];
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load schedules: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ScheduleChainAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;

        if (!Guid.TryParse(ChainIdInput.Trim(), out Guid guid))
        {
            ErrorMessage = "Invalid ChainId format. Must be a valid Guid.";
            return;
        }

        IsBusy = true;
        try
        {
            var chainId = new ChainId(guid);
            await _scheduler.ScheduleChainAsync(chainId, CronExpressionInput.Trim()).ConfigureAwait(false);
            StatusMessage = $"Scheduled chain {guid} with expression '{CronExpressionInput}'";
            await LoadSchedulesAsync().ConfigureAwait(false);
        }
        catch (InvalidCronExpressionException ex)
        {
            ErrorMessage = $"Invalid cron expression '{ex.Expression}': {ex.Message}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error scheduling: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task PauseScheduleAsync()
    {
        if (SelectedSchedule == null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _scheduler.PauseChainAsync(SelectedSchedule.ChainId).ConfigureAwait(false);
            SelectedSchedule.IsPaused = true;
            PauseCommand.RaiseCanExecuteChanged();
            ResumeCommand.RaiseCanExecuteChanged();
            StatusMessage = $"Paused chain {SelectedSchedule.ChainId.Value}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error pausing: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ResumeScheduleAsync()
    {
        if (SelectedSchedule == null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _scheduler.ResumeChainAsync(SelectedSchedule.ChainId).ConfigureAwait(false);
            SelectedSchedule.IsPaused = false;
            PauseCommand.RaiseCanExecuteChanged();
            ResumeCommand.RaiseCanExecuteChanged();
            StatusMessage = $"Resumed chain {SelectedSchedule.ChainId.Value}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error resuming: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UnscheduleAsync()
    {
        if (SelectedSchedule == null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _scheduler.UnscheduleChainAsync(SelectedSchedule.ChainId).ConfigureAwait(false);
            Schedules.Remove(SelectedSchedule);
            SelectedSchedule = Schedules.FirstOrDefault();
            StatusMessage = "Schedule removed.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error unscheduling: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task TriggerNowAsync()
    {
        if (SelectedSchedule == null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _scheduler.TriggerChainNowAsync(SelectedSchedule.ChainId).ConfigureAwait(false);
            StatusMessage = $"Chain {SelectedSchedule.ChainId.Value} triggered immediately.";
            _notifications?.ShowNotification(
                "Chain Triggered",
                $"Immediate execution triggered for {SelectedSchedule.ChainId.Value}",
                NotificationSeverity.Info);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error triggering now: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

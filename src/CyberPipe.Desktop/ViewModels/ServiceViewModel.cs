using CyberPipe.Contracts;
using CyberPipe.Desktop.Common;
using CyberPipe.Desktop.Localization;

namespace CyberPipe.Desktop.ViewModels;

public sealed class ServiceViewModel : ViewModelBase
{
    private readonly IServiceController _controller;
    private readonly LocalizationManager _localization;

    private string _state = "Stopped";
    private string _serviceVersion = "Unknown";
    private string _desktopVersion;
    private DateTimeOffset _startedAtUtc = DateTimeOffset.MinValue;
    private string? _lastError;
    private bool _isBusy;
    private string _actionPrompt = string.Empty;

    public ServiceViewModel(
        IServiceController controller,
        LocalizationManager? localization = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _localization = localization ?? LocalizationManager.Instance;
        _desktopVersion = typeof(ServiceViewModel).Assembly.GetName().Version?.ToString() ?? "1.0.0";

        StartCommand = new AsyncRelayCommand(StartAsync, () => CanStart);
        StopCommand = new AsyncRelayCommand(StopAsync, () => CanStop);
        RestartCommand = new AsyncRelayCommand(RestartAsync, () => CanRestart);
        RefreshCommand = new AsyncRelayCommand(RefreshStatusAsync, () => !IsBusy);

        UpdateComputedProperties();
        _ = RefreshStatusAsync();
    }

    public string State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                UpdateComputedProperties();
            }
        }
    }

    public string ServiceVersion
    {
        get => _serviceVersion;
        private set => SetProperty(ref _serviceVersion, value);
    }

    public string DesktopVersion
    {
        get => _desktopVersion;
        private set => SetProperty(ref _desktopVersion, value);
    }

    public DateTimeOffset StartedAtUtc
    {
        get => _startedAtUtc;
        private set => SetProperty(ref _startedAtUtc, value);
    }

    public string? LastError
    {
        get => _lastError;
        private set
        {
            if (SetProperty(ref _lastError, value))
            {
                UpdateComputedProperties();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                UpdateComputedProperties();
            }
        }
    }

    public string ActionPrompt
    {
        get => _actionPrompt;
        private set => SetProperty(ref _actionPrompt, value);
    }

    public bool CanStart => !IsBusy && State is "Stopped" or "Faulted" or "ServiceUnavailable";
    public bool CanStop => !IsBusy && State is "Running";
    public bool CanRestart => !IsBusy && State is "Running";
    public bool IsRunning => State == "Running";

    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand RestartCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    public async Task RefreshStatusAsync()
    {
        try
        {
            ServiceStatusDto status = await _controller.GetStatusAsync().ConfigureAwait(false);
            State = status.State;
            ServiceVersion = status.Version;
            StartedAtUtc = status.StartedAtUtc;
            LastError = status.LastError;
        }
        catch (Exception ex)
        {
            State = "ServiceUnavailable";
            LastError = ex.Message;
        }
    }

    public async Task StartAsync()
    {
        if (!CanStart)
        {
            return;
        }

        IsBusy = true;
        ActionPrompt = "Starting service...";
        try
        {
            ServiceControlResult result = await _controller.StartAsync().ConfigureAwait(false);
            if (result.Outcome != ServiceControlOutcome.Ok)
            {
                LastError = result.Error;
                State = result.Outcome.ToString();
            }
            else
            {
                await RefreshStatusAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            State = "ServiceUnavailable";
            LastError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task StopAsync()
    {
        if (!CanStop)
        {
            return;
        }

        IsBusy = true;
        ActionPrompt = "Stopping service...";
        try
        {
            ServiceControlResult result = await _controller.StopAsync().ConfigureAwait(false);
            if (result.Outcome != ServiceControlOutcome.Ok)
            {
                LastError = result.Error;
                State = result.Outcome.ToString();
            }
            else
            {
                await RefreshStatusAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            State = "ServiceUnavailable";
            LastError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RestartAsync()
    {
        if (!CanRestart)
        {
            return;
        }

        IsBusy = true;
        ActionPrompt = "Restarting service...";
        try
        {
            ServiceControlResult result = await _controller.RestartAsync().ConfigureAwait(false);
            if (result.Outcome != ServiceControlOutcome.Ok)
            {
                LastError = result.Error;
                State = result.Outcome.ToString();
            }
            else
            {
                await RefreshStatusAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            State = "ServiceUnavailable";
            LastError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateComputedProperties()
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanRestart));
        OnPropertyChanged(nameof(IsRunning));

        StartCommand?.RaiseCanExecuteChanged();
        StopCommand?.RaiseCanExecuteChanged();
        RestartCommand?.RaiseCanExecuteChanged();
        RefreshCommand?.RaiseCanExecuteChanged();

        ActionPrompt = State switch
        {
            "Running" => _localization["Service.Running"],
            "Stopped" => _localization["Service.Stopped"],
            "Faulted" => $"{_localization["Service.Faulted"]} {LastError}",
            "ServiceNotInstalled" => _localization["Service.NotInstalled"],
            "ServiceUnavailable" => _localization["Service.Unavailable"],
            _ => $"Status: {State}"
        };
    }
}

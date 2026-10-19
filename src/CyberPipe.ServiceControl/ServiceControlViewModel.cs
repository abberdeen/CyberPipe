using System.ComponentModel;
using System.Runtime.CompilerServices;
using CyberPipe.Contracts;

namespace CyberPipe.ServiceControl;

/// <summary>
/// UI-независимое ядро представления для управления службой (TASK-021, TASK-061).
/// Не зависит от Avalonia, реализует <see cref="INotifyPropertyChanged"/> для двусторонней привязки в Desktop UI.
/// </summary>
public sealed class ServiceControlViewModel : INotifyPropertyChanged
{
    private readonly IServiceController _controller;
    private string _state = "Stopped";
    private string _version = "Unknown";
    private DateTimeOffset _startedAtUtc = DateTimeOffset.MinValue;
    private string? _lastError;
    private bool _isBusy;
    private string _actionPrompt = string.Empty;

    public ServiceControlViewModel(IServiceController controller)
    {
        _controller = controller;
        UpdateComputedProperties();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                OnPropertyChanged();
                UpdateComputedProperties();
            }
        }
    }

    public string Version
    {
        get => _version;
        private set
        {
            if (_version != value)
            {
                _version = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTimeOffset StartedAtUtc
    {
        get => _startedAtUtc;
        private set
        {
            if (_startedAtUtc != value)
            {
                _startedAtUtc = value;
                OnPropertyChanged();
            }
        }
    }

    public string? LastError
    {
        get => _lastError;
        private set
        {
            if (_lastError != value)
            {
                _lastError = value;
                OnPropertyChanged();
                UpdateComputedProperties();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
                UpdateComputedProperties();
            }
        }
    }

    public bool CanStart => !IsBusy && State is "Stopped" or "Faulted";

    public bool CanStop => !IsBusy && State is "Running";

    public bool CanRestart => !IsBusy && State is "Running";

    public bool IsServiceInstalled => State != "ServiceNotInstalled";

    public string ActionPrompt
    {
        get => _actionPrompt;
        private set
        {
            if (_actionPrompt != value)
            {
                _actionPrompt = value;
                OnPropertyChanged();
            }
        }
    }

    public async Task RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            ServiceStatusDto status = await _controller.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            ApplyStatus(status);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    public async Task<ServiceControlResult> StartServiceAsync(CancellationToken cancellationToken = default)
    {
        if (!CanStart)
        {
            return new ServiceControlResult(ServiceControlOutcome.Faulted, "Cannot start service in current state.");
        }

        IsBusy = true;
        ActionPrompt = "Starting service...";
        try
        {
            ServiceControlResult result = await _controller.StartAsync(cancellationToken).ConfigureAwait(false);
            if (result.Outcome != ServiceControlOutcome.Ok)
            {
                LastError = result.Error;
            }
            await RefreshStatusAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<ServiceControlResult> StopServiceAsync(CancellationToken cancellationToken = default)
    {
        if (!CanStop)
        {
            return new ServiceControlResult(ServiceControlOutcome.Faulted, "Cannot stop service in current state.");
        }

        IsBusy = true;
        ActionPrompt = "Stopping service...";
        try
        {
            ServiceControlResult result = await _controller.StopAsync(cancellationToken).ConfigureAwait(false);
            if (result.Outcome != ServiceControlOutcome.Ok)
            {
                LastError = result.Error;
            }
            await RefreshStatusAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<ServiceControlResult> RestartServiceAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRestart)
        {
            return new ServiceControlResult(ServiceControlOutcome.Faulted, "Cannot restart service in current state.");
        }

        IsBusy = true;
        ActionPrompt = "Restarting service...";
        try
        {
            ServiceControlResult result = await _controller.RestartAsync(cancellationToken).ConfigureAwait(false);
            if (result.Outcome != ServiceControlOutcome.Ok)
            {
                LastError = result.Error;
            }
            await RefreshStatusAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task StartWatchingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (ServiceStatusDto status in _controller.WatchStatusAsync(cancellationToken).ConfigureAwait(false))
            {
                ApplyStatus(status);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal when watching is stopped
        }
    }

    private void ApplyStatus(ServiceStatusDto status)
    {
        State = status.State;
        Version = status.Version;
        StartedAtUtc = status.StartedAtUtc;
        LastError = status.LastError;
    }

    private void UpdateComputedProperties()
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanRestart));
        OnPropertyChanged(nameof(IsServiceInstalled));

        ActionPrompt = State switch
        {
            "Running" => "Service is running normally.",
            "Stopped" => "Service is stopped. Click Start to run.",
            "Starting" => "Service is starting...",
            "Stopping" => "Service is stopping...",
            "ServiceNotInstalled" => "Service is not installed. Please run scripts/install-service.ps1 as Administrator.",
            "Faulted" => $"Service encountered an error: {LastError ?? "Unknown error"}",
            _ => $"Service status: {State}"
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

using System.ComponentModel;
using System.Runtime.CompilerServices;
using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;

namespace CyberPipe.ServiceControl;

/// <summary>
/// Реализация <see cref="IServiceController"/> для управления службой CyberPipe из Desktop и CLI (TASK-021).
/// Взаимодействует через канал Named Pipe (TASK-020) и fallback через Windows SCM.
/// </summary>
public sealed class ServiceControllerManager : IServiceController
{
    private readonly IServiceControlChannel _channel;
    private readonly IScmManager _scm;
    private readonly CyberPipeOptions _options;
    private readonly ILogger? _logger;

    public ServiceControllerManager(
        IServiceControlChannel channel,
        IScmManager scm,
        CyberPipeOptions options,
        ILogger? logger = null)
    {
        _channel = channel;
        _scm = scm;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ServiceStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            ServiceStatusDto status = await _channel.PingAsync(cancellationToken).ConfigureAwait(false);
            return status;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Channel ping failed, querying SCM for service status.");
        }

        string serviceName = _options.Hosting.ServiceName;
        if (!_scm.IsServiceInstalled(serviceName))
        {
            return new ServiceStatusDto(
                State: "ServiceNotInstalled",
                Version: "Unknown",
                StartedAtUtc: DateTimeOffset.MinValue,
                LastError: "CyberPipe service is not installed.");
        }

        string scmStatus = _scm.GetServiceStatus(serviceName);
        // Канал молчит, а SCM ещё считает службу запущенной: процесс упал или завис
        // и не отвечает. Для Desktop это не Running.
        if (string.Equals(scmStatus, "Running", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scmStatus, "Starting", StringComparison.OrdinalIgnoreCase))
        {
            scmStatus = "Faulted";
        }

        return new ServiceStatusDto(
            State: scmStatus,
            Version: "Unknown",
            StartedAtUtc: DateTimeOffset.MinValue,
            LastError: "Control channel is unavailable.");
    }

    /// <inheritdoc />
    public async Task<ServiceControlResult> StartAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Attempting to start CyberPipe service.");

        ServiceStatusDto current = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(current.State, "Running", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogInformation("CyberPipe service is already running.");
            return new ServiceControlResult(ServiceControlOutcome.Ok, null);
        }

        string serviceName = _options.Hosting.ServiceName;
        if (!_scm.IsServiceInstalled(serviceName))
        {
            _logger?.LogWarning("Cannot start service: '{ServiceName}' is not installed.", serviceName);
            return new ServiceControlResult(
                ServiceControlOutcome.ServiceNotInstalled,
                "CyberPipe service is not installed. Please run scripts/install-service.ps1 as Administrator.");
        }

        try
        {
            await _scm.StartServiceAsync(serviceName, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5) // ERROR_ACCESS_DENIED
        {
            _logger?.LogError(ex, "Access denied starting service '{ServiceName}'. Administrator elevation required.", serviceName);
            return new ServiceControlResult(
                ServiceControlOutcome.AccessDenied,
                "Access denied. Administrator privileges required to start the service.");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogError(ex, "Unauthorized starting service '{ServiceName}'.", serviceName);
            return new ServiceControlResult(
                ServiceControlOutcome.AccessDenied,
                "Access denied. Administrator privileges required to start the service.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start service via SCM.");
            return new ServiceControlResult(ServiceControlOutcome.Faulted, ex.Message);
        }

        // Wait for channel to respond after SCM start
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        while (!linkedCts.Token.IsCancellationRequested)
        {
            try
            {
                ServiceStatusDto status = await _channel.PingAsync(linkedCts.Token).ConfigureAwait(false);
                if (string.Equals(status.State, "Running", StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogInformation("CyberPipe service successfully started and responded to ping.");
                    return new ServiceControlResult(ServiceControlOutcome.Ok, null);
                }
            }
            catch (Exception)
            {
                // Wait briefly before retrying
                await Task.Delay(250, linkedCts.Token).ConfigureAwait(false);
            }
        }

        return new ServiceControlResult(
            ServiceControlOutcome.Timeout,
            "Service was started by SCM, but Named Pipe control channel timed out responding.");
    }

    /// <inheritdoc />
    public async Task<ServiceControlResult> StopAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Attempting to stop CyberPipe service.");

        string serviceName = _options.Hosting.ServiceName;
        if (!_scm.IsServiceInstalled(serviceName))
        {
            return new ServiceControlResult(
                ServiceControlOutcome.ServiceNotInstalled,
                "CyberPipe service is not installed.");
        }

        // 1. Try stopping gracefully via Named Pipe channel
        bool stopRequestedViaChannel = false;
        try
        {
            await _channel.RequestStopAsync(cancellationToken).ConfigureAwait(false);
            stopRequestedViaChannel = true;
            _logger?.LogInformation("Stop request sent via Named Pipe control channel.");
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Channel RequestStopAsync failed or channel unavailable. Falling back to SCM stop.");
        }

        // 2. If channel stop was requested, wait for service to stop
        if (stopRequestedViaChannel)
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            while (!linkedCts.Token.IsCancellationRequested)
            {
                string status = _scm.GetServiceStatus(serviceName);
                if (string.Equals(status, "Stopped", StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogInformation("CyberPipe service stopped successfully.");
                    return new ServiceControlResult(ServiceControlOutcome.Ok, null);
                }

                try
                {
                    await Task.Delay(250, linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        // 3. Fallback to stopping via SCM
        try
        {
            await _scm.StopServiceAsync(serviceName, cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("CyberPipe service stopped via SCM.");
            return new ServiceControlResult(ServiceControlOutcome.Ok, null);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return new ServiceControlResult(
                ServiceControlOutcome.AccessDenied,
                "Access denied. Administrator privileges required to stop the service.");
        }
        catch (UnauthorizedAccessException)
        {
            return new ServiceControlResult(
                ServiceControlOutcome.AccessDenied,
                "Access denied. Administrator privileges required to stop the service.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to stop service via SCM.");
            return new ServiceControlResult(ServiceControlOutcome.Faulted, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<ServiceControlResult> RestartAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Attempting to restart CyberPipe service.");

        ServiceControlResult stopResult = await StopAsync(cancellationToken).ConfigureAwait(false);
        if (stopResult.Outcome != ServiceControlOutcome.Ok)
        {
            return new ServiceControlResult(
                ServiceControlOutcome.Faulted,
                $"Restart failed during stop phase: {stopResult.Error ?? stopResult.Outcome.ToString()}");
        }

        ServiceControlResult startResult = await StartAsync(cancellationToken).ConfigureAwait(false);
        if (startResult.Outcome != ServiceControlOutcome.Ok)
        {
            return new ServiceControlResult(
                ServiceControlOutcome.Faulted,
                $"Restart failed during start phase: {startResult.Error ?? startResult.Outcome.ToString()}");
        }

        _logger?.LogInformation("CyberPipe service restarted successfully.");
        return new ServiceControlResult(ServiceControlOutcome.Ok, null);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ServiceStatusDto> WatchStatusAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ServiceStatusDto? previous = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            ServiceStatusDto current;
            try
            {
                current = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            catch (Exception ex)
            {
                current = new ServiceStatusDto("Faulted", "Unknown", DateTimeOffset.MinValue, ex.Message);
            }

            if (previous == null ||
                !string.Equals(previous.State, current.State, StringComparison.OrdinalIgnoreCase) ||
                previous.LastError != current.LastError)
            {
                previous = current;
                yield return current;
            }

            // Adaptive backoff: fast while transitioning, slower when stable
            int delayMs = current.State switch
            {
                "Starting" or "Stopping" => 500,
                "Running" => 2000,
                "Stopped" => 4000,
                _ => 5000
            };

            try
            {
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }
}

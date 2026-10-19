using CyberPipe.Contracts;
using CyberPipe.Service.Composition;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CyberPipe.Service;

/// <summary>
/// Фоновый рабочий процесс службы CyberPipe (TASK-020).
/// Управляет жизненным циклом службы, хостингом Named Pipe канала и корректной остановкой.
/// </summary>
public sealed class CyberPipeWorker : BackgroundService
{
    private readonly ILogger<CyberPipeWorker> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly CyberPipeOptions _options;
    private readonly ServiceStatusTracker _tracker;
    private readonly ServiceControlPipeServer _server;
    private readonly ServiceStartupCoordinator? _startup;

    public CyberPipeWorker(
        ILogger<CyberPipeWorker> logger,
        IHostApplicationLifetime lifetime,
        CyberPipeOptions options,
        ServiceStatusTracker tracker,
        ServiceControlPipeServer server,
        ServiceStartupCoordinator? startup = null)
    {
        _logger = logger;
        _lifetime = lifetime;
        _options = options;
        _tracker = tracker;
        _server = server;
        _startup = startup;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            bool ready = true;
            if (_startup is not null)
            {
                ready = await RunStartupSequenceAsync(stoppingToken).ConfigureAwait(false);
                if (!ready)
                {
                    return;
                }
            }
            _tracker.MarkRunning();
            _logger.LogInformation(
                "CyberPipe Service started. Version: {Version}, Pipe: {PipeName}",
                _tracker.Version,
                _options.Hosting.PipeName);

            _server.Start(stoppingToken);

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using (stoppingToken.Register(() => tcs.TrySetResult()))
            {
                await tcs.Task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in CyberPipe Service worker");
            _tracker.MarkFaulted(ex.Message);
            throw;
        }
    }

    private async Task<bool> RunStartupSequenceAsync(CancellationToken stoppingToken)
    {
        StartupReport report = await _startup!.RunAsync(stoppingToken).ConfigureAwait(false);
        if (report.Steps.Any(s => !s.Ok && !s.Degraded))
        {
            string failed = string.Join("; ", report.Steps.Where(s => !s.Ok && !s.Degraded).Select(s => s.Step));
            _tracker.MarkFaulted($"Startup failed: {failed}");
            _logger.LogCritical("Service startup failed at mandatory steps: {Steps}. Stopping.", failed);
            _lifetime.StopApplication();
            return false;
        }

        if (report.NeedsConnectionRecovery)
        {
            _logger.LogWarning("Service started in limited mode: database connection recovery is required.");
        }

        _logger.LogInformation("Startup sequence completed. Ready: {Ready}.", report.IsReady);
        return true;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "CyberPipe Service stopping. Graceful shutdown timeout: {Timeout}s",
            _options.Hosting.ShutdownTimeoutSeconds);
        _tracker.MarkStopping("StopAsync requested");

        if (_startup is not null)
        {
            try
            {
                await _startup.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during ordered shutdown");
            }
        }

        try
        {
            await _server.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing Named Pipe server during shutdown");
        }

        _tracker.MarkStopped();
        _logger.LogInformation("CyberPipe Service stopped cleanly.");

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}

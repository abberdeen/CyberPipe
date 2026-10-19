using CyberPipe.Contracts;
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

    public CyberPipeWorker(
        ILogger<CyberPipeWorker> logger,
        IHostApplicationLifetime lifetime,
        CyberPipeOptions options,
        ServiceStatusTracker tracker,
        ServiceControlPipeServer server)
    {
        _logger = logger;
        _lifetime = lifetime;
        _options = options;
        _tracker = tracker;
        _server = server;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "CyberPipe Service stopping. Graceful shutdown timeout: {Timeout}s",
            _options.Hosting.ShutdownTimeoutSeconds);
        _tracker.MarkStopping("StopAsync requested");

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

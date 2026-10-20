using CyberPipe.Brainstem.Supervisor;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CyberPipe.Service.Composition;

/// <summary>
/// Hosted-обёртка супервизора Brainstem (TASK-022): недоступный бинарь воркера
/// не должен ронять всю службу — фиксируем Faulted в логе, служба продолжает работу,
/// а голосовой скилл честно вернёт EngineUnavailable.
/// </summary>
internal sealed class BrainstemHostedService : IHostedService
{
    private readonly BrainstemSupervisor _supervisor;
    private readonly ILogger<BrainstemHostedService>? _logger;

    public BrainstemHostedService(BrainstemSupervisor supervisor, ILogger<BrainstemHostedService>? logger = null)
    {
        _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _supervisor.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Brainstem supervisor failed to start; worker-dependent skills will report EngineUnavailable.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _supervisor.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Brainstem supervisor stop failed.");
        }
    }
}

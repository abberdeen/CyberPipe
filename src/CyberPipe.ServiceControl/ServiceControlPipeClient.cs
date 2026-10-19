using System.IO.Pipes;
using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.ServiceControl;

/// <summary>
/// Клиент управляющего канала службы CyberPipe поверх Named Pipe (TASK-020, TASK-021).
/// Реализует <see cref="IServiceControlChannel"/>; используется Desktop и CLI,
/// живее в проекте управления, а не в Service (Desktop не ссылается на Service).
/// </summary>
public sealed class ServiceControlPipeClient : IServiceControlChannel
{
    private readonly string _pipeName;
    private readonly int _connectTimeoutMs;

    public ServiceControlPipeClient(string? pipeName = null, int connectTimeoutMs = 3000)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? "CyberPipe.Service.Control.v1"
            : pipeName;
        _connectTimeoutMs = connectTimeoutMs;
    }

    /// <inheritdoc />
    public async Task<ServiceStatusDto> PingAsync(CancellationToken cancellationToken = default)
    {
        string responseJson = await SendCommandAsync("ping", cancellationToken).ConfigureAwait(false);
        var response = JsonSerializer.Deserialize<ServiceControlResponse<ServiceStatusDto>>(responseJson);

        if (response?.Success == true && response.Data != null)
        {
            return response.Data;
        }

        throw new InvalidOperationException(
            response?.Message ?? "Ping failed: empty or unsuccessful response received from service.");
    }

    /// <inheritdoc />
    public async Task RequestStopAsync(CancellationToken cancellationToken = default)
    {
        string responseJson = await SendCommandAsync("stop", cancellationToken).ConfigureAwait(false);
        var response = JsonSerializer.Deserialize<ServiceControlResponse<string>>(responseJson);

        if (response?.Success != true)
        {
            throw new InvalidOperationException(
                response?.Message ?? "RequestStop failed: unsuccessful response received from service.");
        }
    }

    /// <inheritdoc />
    public async Task RequestRestartAsync(CancellationToken cancellationToken = default)
    {
        string responseJson = await SendCommandAsync("restart", cancellationToken).ConfigureAwait(false);
        var response = JsonSerializer.Deserialize<ServiceControlResponse<string>>(responseJson);

        if (response?.Success != true)
        {
            throw new InvalidOperationException(
                response?.Message ?? "RequestRestart failed: unsuccessful response received from service.");
        }
    }

    private async Task<string> SendCommandAsync(string command, CancellationToken ct)
    {
        var clientStream = new NamedPipeClientStream(
            serverName: ".",
            pipeName: _pipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous);

        await using (clientStream.ConfigureAwait(false))
        {
            using var timeoutCts = new CancellationTokenSource(_connectTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                await clientStream.ConnectAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out connecting to Named Pipe '{_pipeName}' after {_connectTimeoutMs}ms.");
            }

            using var reader = new StreamReader(clientStream, System.Text.Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(clientStream, System.Text.Encoding.UTF8, leaveOpen: true)
            {
                AutoFlush = true
            };

            string requestJson = JsonSerializer.Serialize(new { command });
            await writer.WriteLineAsync(requestJson.AsMemory(), ct).ConfigureAwait(false);
            await clientStream.FlushAsync(ct).ConfigureAwait(false);

            string? responseLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                throw new InvalidOperationException($"Empty response received from pipe '{_pipeName}'.");
            }

            return responseLine.Trim();
        }
    }
}

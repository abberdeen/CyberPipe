using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;

namespace CyberPipe.Service;

/// <summary>
/// Сервер управляющего Named Pipe канала для Desktop и health-check (TASK-020).
/// Канал: \\.\pipe\CyberPipe.Service.Control.v1 (или имя из опций).
/// ACL: текущий пользователь-владелец и SYSTEM/LocalService.
/// </summary>
public sealed class ServiceControlPipeServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly ServiceStatusTracker _tracker;
    private readonly Func<Task>? _stopCallback;
    private readonly Func<Task>? _restartCallback;
    private readonly Func<string, string, Task<string>>? _rpcHandler;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;
    private bool _disposed;

    public ServiceControlPipeServer(
        string pipeName,
        ServiceStatusTracker tracker,
        Func<Task>? stopCallback = null,
        Func<Task>? restartCallback = null,
        ILogger? logger = null,
        Func<string, string, Task<string>>? rpcHandler = null)
    {
        _pipeName = pipeName;
        _tracker = tracker;
        _stopCallback = stopCallback;
        _restartCallback = restartCallback;
        _logger = logger;
        _rpcHandler = rpcHandler;
    }

    public string PipeName => _pipeName;

    /// <summary>
    /// Запускает прослушивание Named Pipe в фоновой задаче.
    /// </summary>
    public void Start(CancellationToken cancellationToken = default)
    {
        if (_listenTask != null)
        {
            return;
        }

        CancellationToken linkedToken = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token).Token
            : _cts.Token;

        _listenTask = Task.Run(() => ListenLoopAsync(linkedToken), linkedToken);
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        _logger?.LogInformation("Named Pipe control server listening on pipe: {PipeName}", _pipeName);

        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? serverStream = null;
            try
            {
                serverStream = CreateServerStream(_pipeName);
                await serverStream.WaitForConnectionAsync(ct).ConfigureAwait(false);

                // Handle each connection in background task so server can accept other requests
                NamedPipeServerStream streamToProcess = serverStream;
                _ = Task.Run(() => HandleClientAsync(streamToProcess, ct), ct);
                serverStream = null; // Ownership transferred to HandleClientAsync
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error accepting Named Pipe connection on {PipeName}", _pipeName);
                if (serverStream != null)
                {
                    await serverStream.DisposeAsync().ConfigureAwait(false);
                }

                try
                {
                    await Task.Delay(100, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger?.LogInformation("Named Pipe control server stopped on pipe: {PipeName}", _pipeName);
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken ct)
    {
        await using (stream.ConfigureAwait(false))
        {
            try
            {
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)
                {
                    AutoFlush = true
                };

                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                string response = await ProcessCommandAsync(line.Trim()).ConfigureAwait(false);
                await writer.WriteLineAsync(response.AsMemory(), ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal on shutdown
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error processing client request on pipe {PipeName}", _pipeName);
            }
        }
    }

    private async Task<string> ProcessCommandAsync(string line)
    {
        string command = string.Empty;
        string payload = "{}";
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("command", out var cmdProp) ||
                doc.RootElement.TryGetProperty("action", out cmdProp))
            {
                command = cmdProp.GetString() ?? string.Empty;
            }
            if (doc.RootElement.TryGetProperty("payload", out var payloadProp))
            {
                payload = payloadProp.ValueKind == JsonValueKind.String
                    ? payloadProp.GetString() ?? "{}"
                    : payloadProp.GetRawText();
            }
        }
        catch (JsonException)
        {
            // Simple plain-text fallback
            command = line;
        }

        // RPC контрактов службы (route/budget/chain/hitl) — не служебные ping/stop/restart.
        if (_rpcHandler is not null && command.ToLowerInvariant() is not ("ping" or "status" or "stop" or "restart"))
        {
            try
            {
                string resultJson = await _rpcHandler(command, payload).ConfigureAwait(false);
                return JsonSerializer.Serialize(new ServiceControlResponse<string>(Success: true, Data: resultJson, Message: "OK"));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Pipe RPC '{Command}' failed.", command);
                return JsonSerializer.Serialize(new ServiceControlResponse<string>(
                    Success: false, Data: null, Message: $"{ex.GetType().Name}: {ex.Message}"));
            }
        }

        switch (command.ToLowerInvariant())
        {
            case "ping":
            case "status":
            {
                ServiceStatusDto status = _tracker.ToDto();
                return JsonSerializer.Serialize(new ServiceControlResponse<ServiceStatusDto>(
                    Success: true,
                    Data: status,
                    Message: "OK"));
            }

            case "stop":
            {
                _logger?.LogInformation("Stop request received via Named Pipe channel.");
                if (_stopCallback != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _stopCallback().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Error executing stop callback");
                        }
                    });
                }

                return JsonSerializer.Serialize(new ServiceControlResponse<string>(
                    Success: true,
                    Data: "Stopping",
                    Message: "Stop request accepted"));
            }

            case "restart":
            {
                _logger?.LogInformation("Restart request received via Named Pipe channel.");
                if (_restartCallback != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _restartCallback().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Error executing restart callback");
                        }
                    });
                }

                return JsonSerializer.Serialize(new ServiceControlResponse<string>(
                    Success: true,
                    Data: "Restarting",
                    Message: "Restart request accepted"));
            }

            default:
            {
                return JsonSerializer.Serialize(new ServiceControlResponse<string>(
                    Success: false,
                    Data: null,
                    Message: $"Unknown command: {command}"));
            }
        }
    }

    private static NamedPipeServerStream CreateServerStream(string pipeName)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var pipeSecurity = new PipeSecurity();
                var currentUser = WindowsIdentity.GetCurrent().User;
                if (currentUser != null)
                {
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        currentUser,
                        PipeAccessRights.FullControl,
                        AccessControlType.Allow));
                }

                var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    systemSid,
                    PipeAccessRights.FullControl,
                    AccessControlType.Allow));

                var localServiceSid = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    localServiceSid,
                    PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                    AccessControlType.Allow));

                return NamedPipeServerStreamAcl.Create(
                    pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 4096,
                    outBufferSize: 4096,
                    pipeSecurity);
            }
            catch
            {
                // Fallback to standard creation if ACL fails
            }
        }

        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        if (_listenTask != null)
        {
            try
            {
                await _listenTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
    }
}

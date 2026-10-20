using System.Collections.Concurrent;
using System.IO.Pipes;
using CyberPipe.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CyberPipe.Brainstem.Supervisor;

/// <summary>
/// Супервизор изолированного воркера Brainstem (TASK-022).
/// Запускает воркер через ISandboxLauncher (TASK-023), контролирует heartbeat,
/// перезапускает при сбоях с экспоненциальным бэкоффом и диспетчеризирует задания.
/// </summary>
public sealed class BrainstemSupervisor : IBrainstemSupervisor, IHostedService, IAsyncDisposable
{
    private readonly ISandboxLauncher _sandboxLauncher;
    private readonly CyberPipeOptions _options;
    private readonly ILogger<BrainstemSupervisor>? _logger;
    private readonly IMessageBus? _messageBus;
    private readonly TimeProvider _timeProvider;
    private readonly string _workerBinaryPath;
    private readonly RestartPolicy _restartPolicy;

    private readonly SemaphoreSlim _pipeWriteLock = new(1, 1);
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BrainstemJobResult>> _pendingJobs = new();

    private volatile string _state = "Stopped";
    private CancellationTokenSource? _supervisorCts;
    private CancellationTokenSource? _workerCts;
    private NamedPipeServerStream? _serverPipe;
    private Task<SandboxLaunchResult>? _workerLaunchTask;
    private Task? _readLoopTask;
    private Task? _monitorLoopTask;
    private long _lastHeartbeatTicks;
    private bool _isDisposed;

    public BrainstemSupervisor(
        ISandboxLauncher sandboxLauncher,
        CyberPipeOptions options,
        ILogger<BrainstemSupervisor>? logger = null,
        IMessageBus? messageBus = null,
        TimeProvider? timeProvider = null,
        string? workerBinaryPath = null)
    {
        _sandboxLauncher = sandboxLauncher ?? throw new ArgumentNullException(nameof(sandboxLauncher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _messageBus = messageBus;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _workerBinaryPath = workerBinaryPath ?? ResolveDefaultWorkerBinaryPath();

        _restartPolicy = new RestartPolicy(
            maxAttempts: _options.Brainstem.MaxRestartAttempts,
            initialBackoff: TimeSpan.FromSeconds(_options.Brainstem.InitialBackoffSeconds),
            maxBackoff: TimeSpan.FromSeconds(_options.Brainstem.MaxBackoffSeconds),
            stableThreshold: TimeSpan.FromSeconds(30),
            timeProvider: _timeProvider);
    }

    public string State => _state;
    public RestartPolicy RestartPolicy => _restartPolicy;

    public async Task StartAsync(CancellationToken ct)
    {
        await _lifecycleLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_state is "Running" or "Starting")
            {
                return;
            }

            _supervisorCts?.Dispose();
            _supervisorCts = new CancellationTokenSource();
            _restartPolicy.Reset();

            await StartWorkerInternalAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _supervisorCts?.Cancel();
        await _lifecycleLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_state == "Stopped")
            {
                return;
            }

            SetState("Stopped");

            await TeardownCurrentWorkerAsync(graceful: true, ct).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<BrainstemJobResult> ExecuteAsync(BrainstemJobRequest request, CancellationToken ct)
    {
        if (_state != "Running")
        {
            return new BrainstemJobResult(
                request.JobId,
                Ok: false,
                Payload: null,
                Error: $"Brainstem worker is not running (Current state: {_state})");
        }

        NamedPipeServerStream? pipe = _serverPipe;
        if (pipe == null || !pipe.IsConnected)
        {
            return new BrainstemJobResult(
                request.JobId,
                Ok: false,
                Payload: null,
                Error: "Brainstem worker IPC pipe is not connected.");
        }

        var tcs = new TaskCompletionSource<BrainstemJobResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingJobs.TryAdd(request.JobId, tcs))
        {
            return new BrainstemJobResult(
                request.JobId,
                Ok: false,
                Payload: null,
                Error: $"Job with ID '{request.JobId}' is already in progress.");
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (request.TimeoutMs > 0)
        {
            linkedCts.CancelAfter(request.TimeoutMs);
        }

        try
        {
            // Send request frame over pipe
            await _pipeWriteLock.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            try
            {
                await BrainstemIpc.WriteJobRequestAsync(pipe, request, linkedCts.Token).ConfigureAwait(false);
            }
            finally
            {
                _pipeWriteLock.Release();
            }

            // Await response from worker
            return await tcs.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new BrainstemJobResult(request.JobId, false, null, "Job execution was cancelled.");
        }
        catch (OperationCanceledException)
        {
            return new BrainstemJobResult(request.JobId, false, null, $"Job timed out after {request.TimeoutMs}ms.");
        }
        catch (Exception ex)
        {
            _ = Task.Run(() => HandleWorkerFailureAsync($"IPC error: {ex.Message}"));
            return new BrainstemJobResult(request.JobId, false, null, $"IPC error executing job: {ex.Message}");
        }
        finally
        {
            _pendingJobs.TryRemove(request.JobId, out _);
        }
    }

    private async Task StartWorkerInternalAsync(CancellationToken ct)
    {
        SetState("Starting");

        string pipeName = $"{_options.Brainstem.PipePrefix}.{_options.Brainstem.Role}.{Guid.NewGuid():N}";
        _serverPipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        _workerCts?.Dispose();
        _workerCts = CancellationTokenSource.CreateLinkedTokenSource(_supervisorCts?.Token ?? CancellationToken.None);

        if (!File.Exists(_workerBinaryPath))
        {
            _logger?.LogWarning(
                "Brainstem worker binary not found on filesystem at {Path}. Relying on sandbox launcher to resolve or fail.",
                _workerBinaryPath);
        }

        string exePath;
        var args = new List<string>();

        if (_workerBinaryPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            exePath = Environment.ProcessPath ?? "dotnet";
            args.Add(_workerBinaryPath);
        }
        else
        {
            exePath = _workerBinaryPath;
        }

        args.AddRange([
            "--pipe", pipeName,
            "--role", _options.Brainstem.Role,
            "--heartbeat-interval-ms", (_options.Brainstem.HeartbeatIntervalSeconds * 1000).ToString()
        ]);

        string workingDir = Path.GetDirectoryName(_workerBinaryPath) ?? Path.GetTempPath();
        var launchRequest = new SandboxLaunchRequest(
            ExecutablePath: exePath,
            Arguments: args,
            WorkingDirectory: workingDir,
            Environment: RuntimeEnvironment(),
            TimeoutMs: int.MaxValue,
            MaxMemoryMb: (int?)(_options.Brainstem.MemoryLimitMb ?? _options.Sandbox.MemoryLimitMb),
            RestrictToken: false);

        _logger?.LogInformation(
            "Launching Brainstem worker '{Role}' via sandbox. Exe: {Exe}, Pipe: {Pipe}",
            _options.Brainstem.Role,
            exePath,
            pipeName);

        _workerLaunchTask = _sandboxLauncher.RunAsync(launchRequest, _workerCts.Token);

        // Wait for worker connection with timeout. If the process dies first, its
        // stderr is the reason — do not wait out the whole connect timeout.
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _workerCts.Token);
        connectCts.CancelAfter(TimeSpan.FromSeconds(15));

        Task connectTask = _serverPipe.WaitForConnectionAsync(connectCts.Token);
        Task finished = await Task.WhenAny(connectTask, _workerLaunchTask).ConfigureAwait(false);
        if (finished == _workerLaunchTask)
        {
            await ReportWorkerExitBeforeConnectAsync(lockAlreadyHeld: true).ConfigureAwait(false);
            return;
        }

        try
        {
            await connectTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || (_supervisorCts?.IsCancellationRequested ?? false))
        {
            _logger?.LogInformation("Brainstem worker connection was cancelled.");
            return;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect to Brainstem worker pipe within timeout.");
            await HandleWorkerFailureAsync("Connection timeout", lockAlreadyHeld: true).ConfigureAwait(false);
            return;
        }

        _lastHeartbeatTicks = _timeProvider.GetTimestamp();
        SetState("Running");
        _restartPolicy.OnWorkerStarted();

        _readLoopTask = Task.Run(() => ReadPipeLoopAsync(_workerCts.Token));
        _monitorLoopTask = Task.Run(() => MonitorHeartbeatLoopAsync(_workerCts.Token));
    }

    private async Task ReportWorkerExitBeforeConnectAsync(bool lockAlreadyHeld = true)
    {
        if (_workerLaunchTask is null)
        {
            await HandleWorkerFailureAsync("Worker launch did not start", lockAlreadyHeld).ConfigureAwait(false);
            return;
        }

        try
        {
            SandboxLaunchResult result = await _workerLaunchTask.ConfigureAwait(false);
            string stderr = result.Stderr.Trim();
            _logger?.LogError(
                "Brainstem worker exited before connecting to the pipe. ExitCode: {ExitCode}, Error: {Error}, Stderr: {Stderr}",
                result.ExitCode,
                result.Error,
                stderr);
            string detail = result.Error
                ?? (string.IsNullOrWhiteSpace(stderr) ? $"exit {result.ExitCode}" : stderr);
            await HandleWorkerFailureAsync(detail, lockAlreadyHeld).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (_supervisorCts?.IsCancellationRequested ?? false)
            {
                return;
            }
            await HandleWorkerFailureAsync("Worker launch canceled", lockAlreadyHeld).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Brainstem worker launch failed before the pipe connection.");
            await HandleWorkerFailureAsync(ex.Message, lockAlreadyHeld).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Переменные, без которых framework-dependent воркер не находит среду .NET.
    /// Секреты родителя сюда не копируются.
    /// </summary>
    private static Dictionary<string, string> RuntimeEnvironment()
    {
        string[] names =
        [
            "SystemRoot", "WINDIR", "PATH", "PATHEXT",
            "TEMP", "TMP", "USERPROFILE", "HOMEDRIVE", "HOMEPATH",
            "LOCALAPPDATA", "APPDATA", "PROGRAMDATA",
            "PROGRAMFILES", "PROGRAMFILES(X86)", "PROGRAMW6432",
            "COMPUTERNAME", "PROCESSOR_ARCHITECTURE", "PROCESSOR_IDENTIFIER", "NUMBER_OF_PROCESSORS",
            "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_MULTILEVEL_LOOKUP",
        ];
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
                environment[name] = value;
        }

        return environment;
    }

    private async Task MonitorHeartbeatLoopAsync(CancellationToken workerCt)
    {
        TimeSpan timeoutSpan = TimeSpan.FromSeconds(_options.Brainstem.HeartbeatTimeoutSeconds);
        TimeSpan checkInterval = TimeSpan.FromMilliseconds(500);

        try
        {
            while (!workerCt.IsCancellationRequested && _state == "Running")
            {
                await Task.Delay(checkInterval, workerCt).ConfigureAwait(false);

                _restartPolicy.CheckStabilityReset();

                TimeSpan elapsedSinceHeartbeat = _timeProvider.GetElapsedTime(_lastHeartbeatTicks);
                if (elapsedSinceHeartbeat > timeoutSpan)
                {
                    _logger?.LogWarning(
                        "Brainstem worker '{Role}' missed heartbeat ({Elapsed:F1}s > {Timeout:F1}s). Triggering recovery.",
                        _options.Brainstem.Role,
                        elapsedSinceHeartbeat.TotalSeconds,
                        timeoutSpan.TotalSeconds);

                    await HandleWorkerFailureAsync("Missed heartbeat timeout").ConfigureAwait(false);
                    return;
                }

                if (_workerLaunchTask != null && _workerLaunchTask.IsCompleted)
                {
                    _logger?.LogWarning(
                        "Brainstem worker '{Role}' process completed unexpectedly. Triggering recovery.",
                        _options.Brainstem.Role);

                    await HandleWorkerFailureAsync("Process terminated").ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during worker stop/restart
        }
    }

    private async Task ReadPipeLoopAsync(CancellationToken workerCt)
    {
        NamedPipeServerStream? pipe = _serverPipe;
        if (pipe == null)
        {
            return;
        }

        try
        {
            while (!workerCt.IsCancellationRequested && pipe.IsConnected)
            {
                (BrainstemMessageType msgType, byte[] payload) =
                    await BrainstemIpc.ReadFrameAsync(pipe, workerCt).ConfigureAwait(false);

                if (msgType == BrainstemMessageType.Heartbeat)
                {
                    Interlocked.Exchange(ref _lastHeartbeatTicks, _timeProvider.GetTimestamp());
                }
                else if (msgType == BrainstemMessageType.JobResult)
                {
                    try
                    {
                        BrainstemJobResult result = BrainstemIpc.DeserializeJobResult(payload);
                        if (_pendingJobs.TryGetValue(result.JobId, out var tcs))
                        {
                            tcs.TrySetResult(result);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to deserialize JobResult frame from worker.");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during stop/restart
        }
        catch (EndOfStreamException)
        {
            if (_state == "Running")
            {
                _logger?.LogWarning("Brainstem worker pipe disconnected unexpectedly.");
                await HandleWorkerFailureAsync("Pipe disconnected").ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            if (_state == "Running")
            {
                _logger?.LogError(ex, "Error reading from Brainstem worker pipe.");
                await HandleWorkerFailureAsync($"Pipe error: {ex.Message}").ConfigureAwait(false);
            }
        }
    }

    private async Task HandleWorkerFailureAsync(string reason, bool lockAlreadyHeld = false)
    {
        if (_state is "Stopped" or "Restarting" or "Faulted")
        {
            return;
        }

        if (!lockAlreadyHeld)
        {
            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        }

        try
        {
            if (_state is "Stopped" or "Restarting" or "Faulted")
            {
                return;
            }

            _logger?.LogWarning("Handling Brainstem worker failure: {Reason}", reason);

            await TeardownCurrentWorkerAsync(graceful: false, CancellationToken.None).ConfigureAwait(false);

            (bool canRestart, TimeSpan backoffDelay) = _restartPolicy.RecordFailure();
            if (!canRestart)
            {
                SetState("Faulted");
                _logger?.LogError(
                    "Brainstem worker '{Role}' exceeded maximum restart attempts ({Attempts}). Transitioning to Faulted.",
                    _options.Brainstem.Role,
                    _restartPolicy.MaxAttempts);

                if (_messageBus != null)
                {
                    await _messageBus.PublishAsync(
                        new BrainstemRestartExhausted(_options.Brainstem.Role, _restartPolicy.ConsecutiveFailures),
                        CancellationToken.None).ConfigureAwait(false);
                }
                return;
            }

            SetState("Restarting");
            _logger?.LogInformation(
                "Restarting Brainstem worker '{Role}' in {Delay:F1}s (attempt {Attempt}/{Max})",
                _options.Brainstem.Role,
                backoffDelay.TotalSeconds,
                _restartPolicy.ConsecutiveFailures,
                _restartPolicy.MaxAttempts);

            if (backoffDelay > TimeSpan.Zero)
            {
                await Task.Delay(backoffDelay, _supervisorCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            }

            if (_state == "Restarting" && !(_supervisorCts?.IsCancellationRequested ?? true))
            {
                await StartWorkerInternalAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Supervisor was stopped during backoff delay
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error during Brainstem worker restart sequence.");
            SetState("Faulted");
        }
        finally
        {
            if (!lockAlreadyHeld)
            {
                _lifecycleLock.Release();
            }
        }
    }

    private async Task TeardownCurrentWorkerAsync(bool graceful, CancellationToken ct)
    {
        // Cancel all pending jobs
        foreach (var kvp in _pendingJobs)
        {
            kvp.Value.TrySetResult(new BrainstemJobResult(
                kvp.Key,
                Ok: false,
                Payload: null,
                Error: "Worker was terminated."));
        }
        _pendingJobs.Clear();

        if (graceful && _serverPipe != null && _serverPipe.IsConnected)
        {
            try
            {
                using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await BrainstemIpc.WriteShutdownAsync(_serverPipe, shutdownCts.Token).ConfigureAwait(false);
            }
            catch
            {
                // Ignore error writing shutdown frame
            }
        }

        // Cancel worker cancellation token (kills sandbox process)
        if (_workerCts != null)
        {
            try
            {
                _workerCts.Cancel();
            }
            catch
            {
            }
        }

        // Wait shortly for launch task to complete
        if (_workerLaunchTask != null)
        {
            try
            {
                using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _workerLaunchTask.WaitAsync(waitCts.Token).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (_serverPipe != null)
        {
            try
            {
                await _serverPipe.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
            _serverPipe = null;
        }
    }

    private void SetState(string newState)
    {
        string oldState = _state;
        if (oldState == newState)
        {
            return;
        }

        _state = newState;
        _logger?.LogInformation(
            "Brainstem supervisor state transition: {OldState} -> {NewState} for role '{Role}'",
            oldState,
            newState,
            _options.Brainstem.Role);

        if (_messageBus != null)
        {
            _ = _messageBus.PublishAsync(
                new BrainstemStateChanged(newState, _options.Brainstem.Role, _timeProvider.GetUtcNow()),
                CancellationToken.None);
        }
    }

    public static string ResolveDefaultWorkerBinaryPath()
    {
        string baseDir = AppContext.BaseDirectory;
        string exeName = "CyberPipe.Brainstem.exe";
        string dllName = "CyberPipe.Brainstem.dll";

        string[] candidates =
        [
            Path.Combine(baseDir, exeName),
            Path.Combine(baseDir, dllName),
            Path.Combine(baseDir, "..", "..", "..", "..", "artifacts", "app", exeName),
            Path.Combine(baseDir, "..", "..", "..", "..", "artifacts", "app", dllName),
            Path.Combine(baseDir, "..", "..", "artifacts", "app", exeName),
            Path.Combine(baseDir, "..", "..", "artifacts", "app", dllName),
            Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "app", exeName),
            Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "app", dllName),
            Path.Combine(baseDir, "..", "..", "..", "..", "src", "Execution", "CyberPipe.Brainstem", "bin", "Debug", "net10.0", exeName),
            Path.Combine(baseDir, "..", "..", "..", "..", "src", "Execution", "CyberPipe.Brainstem", "bin", "Release", "net10.0", exeName),
            Path.Combine(baseDir, "..", "..", "..", "..", "src", "Execution", "CyberPipe.Brainstem", "bin", "Debug", "net10.0", dllName),
            Path.Combine(baseDir, "..", "..", "..", "..", "src", "Execution", "CyberPipe.Brainstem", "bin", "Release", "net10.0", dllName),
        ];

        foreach (string candidate in candidates)
        {
            string full = Path.GetFullPath(candidate);
            if (File.Exists(full))
            {
                return full;
            }
        }

        return Path.Combine(baseDir, exeName);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }

        _supervisorCts?.Dispose();
        _workerCts?.Dispose();
        _pipeWriteLock.Dispose();
        _lifecycleLock.Dispose();
    }
}

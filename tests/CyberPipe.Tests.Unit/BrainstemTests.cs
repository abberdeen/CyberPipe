using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using CyberPipe.Brainstem.Supervisor;
using CyberPipe.Contracts;
using CyberPipe.Execution.Sandbox;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class BrainstemTests
{
    [Fact]
    public void RestartPolicy_BackoffCalculations_AndStabilityReset()
    {
        var timeProvider = new TestTimeProvider();
        var policy = new RestartPolicy(
            maxAttempts: 3,
            initialBackoff: TimeSpan.FromSeconds(1),
            maxBackoff: TimeSpan.FromSeconds(16),
            stableThreshold: TimeSpan.FromSeconds(30),
            timeProvider: timeProvider);

        Assert.Equal(0, policy.ConsecutiveFailures);

        // Attempt 1: 1s
        var (canRestart1, delay1) = policy.RecordFailure();
        Assert.True(canRestart1);
        Assert.Equal(TimeSpan.FromSeconds(1), delay1);
        Assert.Equal(1, policy.ConsecutiveFailures);

        // Attempt 2: 2s
        var (canRestart2, delay2) = policy.RecordFailure();
        Assert.True(canRestart2);
        Assert.Equal(TimeSpan.FromSeconds(2), delay2);
        Assert.Equal(2, policy.ConsecutiveFailures);

        // Attempt 3: 4s
        var (canRestart3, delay3) = policy.RecordFailure();
        Assert.True(canRestart3);
        Assert.Equal(TimeSpan.FromSeconds(4), delay3);
        Assert.Equal(3, policy.ConsecutiveFailures);

        // Attempt 4: Exceeded maxAttempts (3)
        var (canRestart4, _) = policy.RecordFailure();
        Assert.False(canRestart4);
        Assert.Equal(4, policy.ConsecutiveFailures);

        // Reset
        policy.Reset();
        Assert.Equal(0, policy.ConsecutiveFailures);

        // Simulate 1 failure then stable run for 35 seconds
        policy.RecordFailure();
        Assert.Equal(1, policy.ConsecutiveFailures);
        policy.OnWorkerStarted();

        timeProvider.Advance(TimeSpan.FromSeconds(35));
        policy.CheckStabilityReset();
        Assert.Equal(0, policy.ConsecutiveFailures);
    }

    [Fact]
    public async Task Supervisor_Component_PingJob_ReturnsPong()
    {
        var fakeBus = new FakeMessageBus();
        var options = new CyberPipeOptions
        {
            Brainstem = new BrainstemOptions
            {
                Role = "test",
                HeartbeatIntervalSeconds = 1,
                HeartbeatTimeoutSeconds = 3,
                MaxRestartAttempts = 3
            }
        };

        var launcher = new FakeSandboxLauncher(async (req, ct) =>
        {
            string pipeName = GetPipeName(req);
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(ct).ConfigureAwait(false);

            // Respond to requests
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    (var type, byte[] payload) = await BrainstemIpc.ReadFrameAsync(client, ct).ConfigureAwait(false);
                    if (type == BrainstemMessageType.Shutdown)
                    {
                        break;
                    }
                    if (type == BrainstemMessageType.JobRequest)
                    {
                        var job = BrainstemIpc.DeserializeJobRequest(payload);
                        var res = new BrainstemJobResult(job.JobId, true, "PONG"u8.ToArray(), null);
                        await BrainstemIpc.WriteJobResultAsync(client, res, ct).ConfigureAwait(false);
                    }
                }
                catch
                {
                    break;
                }
            }

            return new SandboxLaunchResult(0, false, false, "", "", null);
        });

        await using var supervisor = new BrainstemSupervisor(
            launcher,
            options,
            messageBus: fakeBus,
            workerBinaryPath: "fake.exe");

        await supervisor.StartAsync(CancellationToken.None);
        Assert.Equal("Running", supervisor.State);

        var jobResult = await supervisor.ExecuteAsync(
            new BrainstemJobRequest("job-1", "Ping", [] , 5000),
            CancellationToken.None);

        Assert.True(jobResult.Ok);
        Assert.Equal("PONG", Encoding.UTF8.GetString(jobResult.Payload!));

        await supervisor.StopAsync(CancellationToken.None);
        Assert.Equal("Stopped", supervisor.State);
    }

    [Fact]
    public async Task Supervisor_Component_JobTimeout_ReturnsTimeoutError()
    {
        var options = new CyberPipeOptions
        {
            Brainstem = new BrainstemOptions { Role = "test" }
        };

        var launcher = new FakeSandboxLauncher(async (req, ct) =>
        {
            string pipeName = GetPipeName(req);
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(ct).ConfigureAwait(false);

            // Read but never respond to simulate slow/hanging job
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await BrainstemIpc.ReadFrameAsync(client, ct).ConfigureAwait(false);
                }
                catch
                {
                    break;
                }
            }

            return new SandboxLaunchResult(0, false, false, "", "", null);
        });

        await using var supervisor = new BrainstemSupervisor(
            launcher,
            options,
            workerBinaryPath: "fake.exe");

        await supervisor.StartAsync(CancellationToken.None);

        var jobResult = await supervisor.ExecuteAsync(
            new BrainstemJobRequest("slow-1", "Slow", [] , 150),
            CancellationToken.None);

        Assert.False(jobResult.Ok);
        Assert.Contains("timed out", jobResult.Error, StringComparison.OrdinalIgnoreCase);

        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Supervisor_Component_ExhaustedRestarts_TransitionsToFaulted()
    {
        var fakeBus = new FakeMessageBus();
        var options = new CyberPipeOptions
        {
            Brainstem = new BrainstemOptions
            {
                Role = "failing-role",
                MaxRestartAttempts = 2,
                InitialBackoffSeconds = 0,
                MaxBackoffSeconds = 0,
                HeartbeatTimeoutSeconds = 1
            }
        };

        // Every launch immediately crashes after pipe connection
        int launchCount = 0;
        var launcher = new FakeSandboxLauncher(async (req, ct) =>
        {
            launchCount++;
            string pipeName = GetPipeName(req);
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(ct).ConfigureAwait(false);

            // Close connection immediately to simulate crash
            client.Dispose();
            return new SandboxLaunchResult(1, false, false, "", "crash", null);
        });

        await using var supervisor = new BrainstemSupervisor(
            launcher,
            options,
            messageBus: fakeBus,
            workerBinaryPath: "fake.exe");

        await supervisor.StartAsync(CancellationToken.None);

        // Wait briefly for supervisor to cycle through restarts and transition to Faulted
        for (int i = 0; i < 50; i++)
        {
            if (supervisor.State == "Faulted")
            {
                break;
            }
            await Task.Delay(100);
        }

        Assert.Equal("Faulted", supervisor.State);

        // Verify BrainstemRestartExhausted event was published
        var exhaustedEvent = fakeBus.PublishedMessages.OfType<BrainstemRestartExhausted>().FirstOrDefault();
        Assert.NotNull(exhaustedEvent);
        Assert.Equal("failing-role", exhaustedEvent.Role);

        // Executing a job while faulted returns error
        var jobResult = await supervisor.ExecuteAsync(
            new BrainstemJobRequest("job-faulted", "Ping", [] , 1000),
            CancellationToken.None);

        Assert.False(jobResult.Ok);
        Assert.Contains("Faulted", jobResult.Error);
    }

    [Fact]
    public async Task Supervisor_WindowsIntegration_RealWorkerPingEchoAndStop()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await RunRealWorkerPingEchoAndStopAsync();
    }

    [SupportedOSPlatform("windows")]
    private static async Task RunRealWorkerPingEchoAndStopAsync()
    {
        var options = new CyberPipeOptions
        {
            Brainstem = new BrainstemOptions
            {
                Role = "integration-basic",
                HeartbeatIntervalSeconds = 1,
                HeartbeatTimeoutSeconds = 5,
                MaxRestartAttempts = 3
            }
        };
        var sandbox = new WindowsSandboxLauncher(options);

        await using var supervisor = new BrainstemSupervisor(
            sandbox,
            options);

        await supervisor.StartAsync(CancellationToken.None);
        Assert.Equal("Running", supervisor.State);

        // 1. Ping job
        var pingResult = await supervisor.ExecuteAsync(
            new BrainstemJobRequest("ping-1", "Ping", [] , 5000),
            CancellationToken.None);

        Assert.True(pingResult.Ok, pingResult.Error);
        Assert.Equal("PONG", Encoding.UTF8.GetString(pingResult.Payload!));

        // 2. Echo job
        byte[] inputData = "Hello from Supervisor Integration!"u8.ToArray();
        var echoResult = await supervisor.ExecuteAsync(
            new BrainstemJobRequest("echo-1", "Echo", inputData, 5000),
            CancellationToken.None);

        Assert.True(echoResult.Ok, echoResult.Error);
        Assert.Equal(inputData, echoResult.Payload);

        // 3. Graceful Stop
        await supervisor.StopAsync(CancellationToken.None);
        Assert.Equal("Stopped", supervisor.State);
    }

    [Fact]
    public async Task Supervisor_WindowsIntegration_WorkerCrash_RestartsAutomatically()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await RunWorkerCrashRestartTestAsync();
    }

    [SupportedOSPlatform("windows")]
    private static async Task RunWorkerCrashRestartTestAsync()
    {
        var options = new CyberPipeOptions
        {
            Brainstem = new BrainstemOptions
            {
                Role = "integration-crash",
                HeartbeatIntervalSeconds = 1,
                HeartbeatTimeoutSeconds = 5,
                MaxRestartAttempts = 3,
                InitialBackoffSeconds = 1
            }
        };
        var sandbox = new WindowsSandboxLauncher(options);

        await using var supervisor = new BrainstemSupervisor(
            sandbox,
            options);

        await supervisor.StartAsync(CancellationToken.None);
        Assert.Equal("Running", supervisor.State);

        // Trigger crash inside worker
        var crashResult = await supervisor.ExecuteAsync(
            new BrainstemJobRequest("crash-1", "Crash", [] , 5000),
            CancellationToken.None);

        // Wait until supervisor registers failure and restarts back to Running
        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(200);
            if (supervisor.RestartPolicy.ConsecutiveFailures >= 1 && supervisor.State == "Running")
            {
                break;
            }
        }

        Assert.Equal("Running", supervisor.State);

        // Subsequent job should succeed!
        var pingResult = await supervisor.ExecuteAsync(
            new BrainstemJobRequest("ping-after-crash", "Ping", [] , 5000),
            CancellationToken.None);

        Assert.True(pingResult.Ok, pingResult.Error);
        Assert.Equal("PONG", Encoding.UTF8.GetString(pingResult.Payload!));

        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Supervisor_WindowsIntegration_WorkerHang_TerminatesOnTimeoutAndRestarts()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await RunWorkerHangRestartTestAsync();
    }

    [SupportedOSPlatform("windows")]
    private static async Task RunWorkerHangRestartTestAsync()
    {
        var options = new CyberPipeOptions
        {
            Brainstem = new BrainstemOptions
            {
                Role = "integration-hang",
                HeartbeatIntervalSeconds = 1,
                HeartbeatTimeoutSeconds = 3, // short timeout for test
                MaxRestartAttempts = 3,
                InitialBackoffSeconds = 1
            }
        };
        var sandbox = new WindowsSandboxLauncher(options);

        await using var supervisor = new BrainstemSupervisor(
            sandbox,
            options);

        await supervisor.StartAsync(CancellationToken.None);
        Assert.Equal("Running", supervisor.State);

        // Send Hang job (suppresses heartbeat)
        _ = supervisor.ExecuteAsync(
            new BrainstemJobRequest("hang-1", "Hang", [] , 10000),
            CancellationToken.None);

        // Supervisor should detect missed heartbeats after 3 seconds, kill hung worker, and restart
        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(250);
            if (supervisor.RestartPolicy.ConsecutiveFailures > 0 && supervisor.State == "Running")
            {
                break;
            }
        }

        Assert.True(supervisor.RestartPolicy.ConsecutiveFailures >= 1);
        Assert.Equal("Running", supervisor.State);

        // Subsequent job succeeds on new worker
        var pingResult = await supervisor.ExecuteAsync(
            new BrainstemJobRequest("ping-after-hang", "Ping", [] , 5000),
            CancellationToken.None);

        Assert.True(pingResult.Ok, pingResult.Error);
        Assert.Equal("PONG", Encoding.UTF8.GetString(pingResult.Payload!));

        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Supervisor_WindowsIntegration_WorkerMemoryLimit_KillsAndRestarts()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await RunWorkerMemoryLimitTestAsync();
    }

    [SupportedOSPlatform("windows")]
    private static async Task RunWorkerMemoryLimitTestAsync()
    {
        var options = new CyberPipeOptions
        {
            Brainstem = new BrainstemOptions
            {
                Role = "integration-mem",
                MemoryLimitMb = 64, // 64 MB limit
                HeartbeatIntervalSeconds = 1,
                HeartbeatTimeoutSeconds = 4,
                MaxRestartAttempts = 3,
                InitialBackoffSeconds = 1
            }
        };
        var sandbox = new WindowsSandboxLauncher(options);

        await using var supervisor = new BrainstemSupervisor(
            sandbox,
            options);

        await supervisor.StartAsync(CancellationToken.None);
        Assert.Equal("Running", supervisor.State);

        // Tell worker to allocate 256 MB (exceeding 64 MB Job Object limit)
        byte[] payload = Encoding.UTF8.GetBytes("256");
        _ = supervisor.ExecuteAsync(
            new BrainstemJobRequest("alloc-1", "Allocate", payload, 10000),
            CancellationToken.None);

        // Job Object should kill the worker when memory exceeded, supervisor restarts it
        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(250);
            if (supervisor.RestartPolicy.ConsecutiveFailures > 0 && supervisor.State == "Running")
            {
                break;
            }
        }

        Assert.True(supervisor.RestartPolicy.ConsecutiveFailures >= 1);
        Assert.Equal("Running", supervisor.State);

        // Subsequent job succeeds on restarted worker
        var pingResult = await supervisor.ExecuteAsync(
            new BrainstemJobRequest("ping-after-oom", "Ping", [] , 5000),
            CancellationToken.None);

        Assert.True(pingResult.Ok, pingResult.Error);
        Assert.Equal("PONG", Encoding.UTF8.GetString(pingResult.Payload!));

        await supervisor.StopAsync(CancellationToken.None);
    }

    private static string GetPipeName(SandboxLaunchRequest req)
    {
        for (int i = 0; i < req.Arguments.Count; i++)
        {
            if (string.Equals(req.Arguments[i], "--pipe", StringComparison.OrdinalIgnoreCase) && i + 1 < req.Arguments.Count)
            {
                return req.Arguments[i + 1];
            }
        }
        throw new InvalidOperationException("Missing --pipe in arguments");
    }

    private sealed class FakeSandboxLauncher : ISandboxLauncher
    {
        private readonly Func<SandboxLaunchRequest, CancellationToken, Task<SandboxLaunchResult>> _handler;

        public FakeSandboxLauncher(Func<SandboxLaunchRequest, CancellationToken, Task<SandboxLaunchResult>> handler)
        {
            _handler = handler;
        }

        public Task<SandboxLaunchResult> RunAsync(SandboxLaunchRequest request, CancellationToken ct) =>
            _handler(request, ct);
    }

    private sealed class FakeMessageBus : IMessageBus
    {
        public List<object> PublishedMessages { get; } = new();

        public Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        {
            if (message != null)
            {
                PublishedMessages.Add(message);
            }
            return Task.CompletedTask;
        }

        public IDisposable Subscribe<TMessage>(IMessageHandler<TMessage> handler)
        {
            return new NoOpDisposable();
        }

        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        private long _timestamp = 0;

        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan delta)
        {
            _utcNow = _utcNow.Add(delta);
            _timestamp += (long)(delta.TotalSeconds * TimestampFrequency);
        }
    }
}

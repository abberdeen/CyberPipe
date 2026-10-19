using CyberPipe.Contracts;
using CyberPipe.Service;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class ServiceHostTests
{
    [Fact]
    public void ServiceStatusTracker_LifecycleTransitions_MatchesDto()
    {
        var tracker = new ServiceStatusTracker("1.2.3.4");

        Assert.Equal("Stopped", tracker.State);
        Assert.Equal("1.2.3.4", tracker.Version);
        Assert.Null(tracker.LastError);

        ServiceStatusDto initialDto = tracker.ToDto();
        Assert.Equal("Stopped", initialDto.State);
        Assert.Equal("1.2.3.4", initialDto.Version);
        Assert.Null(initialDto.LastError);

        tracker.MarkRunning();
        Assert.Equal("Running", tracker.State);
        Assert.Null(tracker.LastError);
        Assert.True(tracker.StartedAtUtc <= DateTimeOffset.UtcNow);

        tracker.MarkStopping("System shutdown");
        Assert.Equal("Stopping", tracker.State);
        Assert.Equal("System shutdown", tracker.LastError);

        tracker.MarkStopped();
        Assert.Equal("Stopped", tracker.State);

        tracker.MarkFaulted("Critical failure");
        Assert.Equal("Faulted", tracker.State);
        Assert.Equal("Critical failure", tracker.LastError);

        ServiceStatusDto faultedDto = tracker.ToDto();
        Assert.Equal("Faulted", faultedDto.State);
        Assert.Equal("Critical failure", faultedDto.LastError);
    }

    [Fact]
    public void ServiceStatusTracker_StateChangedEvent_NotifiesSubscribers()
    {
        var tracker = new ServiceStatusTracker("2.0.0");
        var transitions = new List<string>();

        tracker.StateChanged += notification =>
        {
            transitions.Add(notification.State);
        };

        tracker.MarkRunning();
        tracker.MarkStopping();
        tracker.MarkStopped();

        Assert.Equal(["Running", "Stopping", "Stopped"], transitions);
    }

    [Fact]
    public void HostingOptions_Validation_EnforcesRules()
    {
        var validOptions = new CyberPipeOptions
        {
            Hosting = new HostingOptions
            {
                ServiceName = "CyberPipe",
                PipeName = "CyberPipe.Service.Control.v1",
                ShutdownTimeoutSeconds = 30
            }
        };

        Assert.DoesNotContain(validOptions.Validate(), e => e.StartsWith("Hosting:"));

        var invalidOptions = new CyberPipeOptions
        {
            Hosting = new HostingOptions
            {
                ServiceName = "",
                PipeName = "   ",
                ShutdownTimeoutSeconds = 0
            }
        };

        IReadOnlyList<string> errors = invalidOptions.Validate();
        Assert.Contains("Hosting:ServiceName must not be empty.", errors);
        Assert.Contains("Hosting:PipeName must not be empty.", errors);
        Assert.Contains("Hosting:ShutdownTimeoutSeconds must be positive.", errors);
    }

    [Fact]
    public async Task ServiceHost_NamedPipe_Ping_ReturnsRunningStatus()
    {
        string pipeName = $"CyberPipe.Test.Control.{Guid.NewGuid():N}";
        var tracker = new ServiceStatusTracker("1.0.42");
        tracker.MarkRunning();

        await using var server = new ServiceControlPipeServer(
            pipeName,
            tracker,
            logger: NullLogger.Instance);
        server.Start();

        var client = new ServiceControlPipeClient(pipeName, connectTimeoutMs: 3000);
        ServiceStatusDto status = await client.PingAsync();

        Assert.Equal("Running", status.State);
        Assert.Equal("1.0.42", status.Version);
        Assert.Null(status.LastError);
    }

    [Fact]
    public async Task ServiceHost_NamedPipe_RequestStop_InvokesStopCallback()
    {
        string pipeName = $"CyberPipe.Test.Control.{Guid.NewGuid():N}";
        var tracker = new ServiceStatusTracker("1.0.42");
        tracker.MarkRunning();

        var stopTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var server = new ServiceControlPipeServer(
            pipeName,
            tracker,
            stopCallback: () =>
            {
                stopTcs.TrySetResult(true);
                return Task.CompletedTask;
            },
            logger: NullLogger.Instance);
        server.Start();

        var client = new ServiceControlPipeClient(pipeName, connectTimeoutMs: 10000);
        await client.RequestStopAsync();

        Task completed = await Task.WhenAny(stopTcs.Task, Task.Delay(10000));
        Assert.Same(stopTcs.Task, completed);
        Assert.True(await stopTcs.Task);
    }

    [Fact]
    public async Task ServiceHost_NamedPipe_RequestRestart_InvokesRestartCallback()
    {
        string pipeName = $"CyberPipe.Test.Control.{Guid.NewGuid():N}";
        var tracker = new ServiceStatusTracker("1.0.42");
        tracker.MarkRunning();

        var restartTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var server = new ServiceControlPipeServer(
            pipeName,
            tracker,
            restartCallback: () =>
            {
                restartTcs.TrySetResult(true);
                return Task.CompletedTask;
            },
            logger: NullLogger.Instance);
        server.Start();

        var client = new ServiceControlPipeClient(pipeName, connectTimeoutMs: 10000);
        await client.RequestRestartAsync();

        Task completed = await Task.WhenAny(restartTcs.Task, Task.Delay(10000));
        Assert.Same(restartTcs.Task, completed);
        Assert.True(await restartTcs.Task);
    }

    [Fact]
    public async Task ServiceHost_NamedPipe_WhenServerDisposed_PingTimesOutOrThrows()
    {
        string pipeName = $"CyberPipe.Test.Control.{Guid.NewGuid():N}";
        var tracker = new ServiceStatusTracker("1.0.42");

        var server = new ServiceControlPipeServer(pipeName, tracker, logger: NullLogger.Instance);
        server.Start();
        await server.DisposeAsync();

        var client = new ServiceControlPipeClient(pipeName, connectTimeoutMs: 500);

        await Assert.ThrowsAnyAsync<Exception>(() => client.PingAsync());
    }

    [Fact]
    public async Task CyberPipeWorker_ExecuteAndGracefulStop_TransitionsTrackerState()
    {
        string pipeName = $"CyberPipe.Test.Control.{Guid.NewGuid():N}";
        var tracker = new ServiceStatusTracker("1.0.0");
        var options = new CyberPipeOptions
        {
            Hosting = new HostingOptions
            {
                PipeName = pipeName,
                ShutdownTimeoutSeconds = 5
            }
        };

        var server = new ServiceControlPipeServer(pipeName, tracker, logger: NullLogger.Instance);
        var fakeLifetime = new TestHostApplicationLifetime();
        var worker = new CyberPipeWorker(
            NullLogger<CyberPipeWorker>.Instance,
            fakeLifetime,
            options,
            tracker,
            server);

        using var cts = new CancellationTokenSource();
        Task workerTask = worker.StartAsync(cts.Token);

        // Give it a brief moment to run
        await Task.Delay(100);
        Assert.Equal("Running", tracker.State);

        await worker.StopAsync(CancellationToken.None);
        Assert.Equal("Stopped", tracker.State);

        cts.Cancel();
        try
        {
            await workerTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _startedCts = new();
        private readonly CancellationTokenSource _stoppingCts = new();
        private readonly CancellationTokenSource _stoppedCts = new();

        public CancellationToken ApplicationStarted => _startedCts.Token;
        public CancellationToken ApplicationStopping => _stoppingCts.Token;
        public CancellationToken ApplicationStopped => _stoppedCts.Token;

        public bool StopRequested { get; private set; }

        public void StopApplication()
        {
            StopRequested = true;
            _stoppingCts.Cancel();
        }
    }
}

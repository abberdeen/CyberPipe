using System.ComponentModel;
using CyberPipe.Contracts;
using CyberPipe.ServiceControl;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class ServiceControlTests
{
    private readonly CyberPipeOptions _options = new()
    {
        Hosting = new HostingOptions { ServiceName = "CyberPipeTest" }
    };

    [Fact]
    public async Task GetStatusAsync_WhenChannelResponds_ReturnsChannelStatus()
    {
        var channel = new FakeServiceControlChannel
        {
            StatusToReturn = new ServiceStatusDto("Running", "1.0.0", DateTimeOffset.UtcNow, null)
        };
        var scm = new FakeScmManager { IsInstalled = true, CurrentStatus = "Running" };
        var controller = new ServiceControllerManager(channel, scm, _options, NullLogger.Instance);

        ServiceStatusDto status = await controller.GetStatusAsync();

        Assert.Equal("Running", status.State);
        Assert.Equal("1.0.0", status.Version);
        Assert.Null(status.LastError);
    }

    [Fact]
    public async Task GetStatusAsync_WhenChannelFails_AndServiceNotInstalled_ReturnsServiceNotInstalled()
    {
        var channel = new FakeServiceControlChannel { ShouldThrowOnPing = true };
        var scm = new FakeScmManager { IsInstalled = false };
        var controller = new ServiceControllerManager(channel, scm, _options, NullLogger.Instance);

        ServiceStatusDto status = await controller.GetStatusAsync();

        Assert.Equal("ServiceNotInstalled", status.State);
        Assert.Contains("not installed", status.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStatusAsync_WhenChannelFails_AndServiceInstalled_ReturnsScmStatus()
    {
        var channel = new FakeServiceControlChannel { ShouldThrowOnPing = true };
        var scm = new FakeScmManager { IsInstalled = true, CurrentStatus = "Stopped" };
        var controller = new ServiceControllerManager(channel, scm, _options, NullLogger.Instance);

        ServiceStatusDto status = await controller.GetStatusAsync();

        Assert.Equal("Stopped", status.State);
        Assert.Contains("unavailable", status.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStatusAsync_WhenChannelFails_AndScmStillRunning_ReportsFaulted()
    {
        var channel = new FakeServiceControlChannel { ShouldThrowOnPing = true };
        var scm = new FakeScmManager { IsInstalled = true, CurrentStatus = "Running" };
        var controller = new ServiceControllerManager(channel, scm, _options, NullLogger.Instance);

        ServiceStatusDto status = await controller.GetStatusAsync();

        Assert.Equal("Faulted", status.State);
        Assert.Contains("unavailable", status.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_ReturnsOk()
    {
        var channel = new FakeServiceControlChannel
        {
            StatusToReturn = new ServiceStatusDto("Running", "1.0.0", DateTimeOffset.UtcNow, null)
        };
        var scm = new FakeScmManager { IsInstalled = true, CurrentStatus = "Running" };
        var controller = new ServiceControllerManager(channel, scm, _options, NullLogger.Instance);

        ServiceControlResult result = await controller.StartAsync();

        Assert.Equal(ServiceControlOutcome.Ok, result.Outcome);
        Assert.False(scm.StartCalled);
    }

    [Fact]
    public async Task StartAsync_WhenServiceNotInstalled_ReturnsServiceNotInstalled()
    {
        var channel = new FakeServiceControlChannel { ShouldThrowOnPing = true };
        var scm = new FakeScmManager { IsInstalled = false };
        var controller = new ServiceControllerManager(channel, scm, _options, NullLogger.Instance);

        ServiceControlResult result = await controller.StartAsync();

        Assert.Equal(ServiceControlOutcome.ServiceNotInstalled, result.Outcome);
        Assert.Contains("not installed", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartAsync_WhenAccessDenied_ReturnsAccessDenied()
    {
        var channel = new FakeServiceControlChannel { ShouldThrowOnPing = true };
        var scm = new FakeScmManager
        {
            IsInstalled = true,
            CurrentStatus = "Stopped",
            ExceptionOnStart = new Win32Exception(5) // ERROR_ACCESS_DENIED
        };
        var controller = new ServiceControllerManager(channel, scm, _options, NullLogger.Instance);

        ServiceControlResult result = await controller.StartAsync();

        Assert.Equal(ServiceControlOutcome.AccessDenied, result.Outcome);
        Assert.Contains("Administrator privileges required", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartAsync_WhenStartSucceeds_AndChannelResponds_ReturnsOk()
    {
        var channel = new FakeServiceControlChannel
        {
            // Initial ping throws, but after SCM starts it returns Running
            ShouldThrowOnPing = true
        };
        var scm = new FakeScmManager
        {
            IsInstalled = true,
            CurrentStatus = "Stopped",
            OnStartCallback = () =>
            {
                channel.ShouldThrowOnPing = false;
                channel.StatusToReturn = new ServiceStatusDto("Running", "1.0.0", DateTimeOffset.UtcNow, null);
            }
        };
        var controller = new ServiceControllerManager(channel, scm, _options, NullLogger.Instance);

        ServiceControlResult result = await controller.StartAsync();

        Assert.Equal(ServiceControlOutcome.Ok, result.Outcome);
        Assert.True(scm.StartCalled);
    }

    [Fact]
    public async Task StopAsync_ViaChannel_ReturnsOk()
    {
        var channel = new FakeServiceControlChannel
        {
            StatusToReturn = new ServiceStatusDto("Running", "1.0.0", DateTimeOffset.UtcNow, null)
        };
        var scm = new FakeScmManager
        {
            IsInstalled = true,
            CurrentStatus = "Running"
        };
        channel.OnRequestStop = () =>
        {
            scm.CurrentStatus = "Stopped";
        };

        var controller = new ServiceControllerManager(channel, scm, _options, NullLogger.Instance);

        ServiceControlResult result = await controller.StopAsync();

        Assert.Equal(ServiceControlOutcome.Ok, result.Outcome);
        Assert.True(channel.RequestStopCalled);
    }

    [Fact]
    public async Task StopAsync_WhenChannelUnavailable_FallsBackToScm_ReturnsOk()
    {
        var channel = new FakeServiceControlChannel
        {
            ShouldThrowOnRequestStop = true
        };
        var scm = new FakeScmManager
        {
            IsInstalled = true,
            CurrentStatus = "Running"
        };
        var controller = new ServiceControllerManager(channel, scm, _options, NullLogger.Instance);

        ServiceControlResult result = await controller.StopAsync();

        Assert.Equal(ServiceControlOutcome.Ok, result.Outcome);
        Assert.True(scm.StopCalled);
    }

    [Fact]
    public async Task StopAsync_WhenServiceNotInstalled_ReturnsServiceNotInstalled()
    {
        var channel = new FakeServiceControlChannel { ShouldThrowOnRequestStop = true };
        var scm = new FakeScmManager { IsInstalled = false };
        var controller = new ServiceControllerManager(channel, scm, _options, NullLogger.Instance);

        ServiceControlResult result = await controller.StopAsync();

        Assert.Equal(ServiceControlOutcome.ServiceNotInstalled, result.Outcome);
    }

    [Fact]
    public async Task RestartAsync_WhenStopFails_ReturnsFaultedWithStopPhase()
    {
        var channel = new FakeServiceControlChannel
        {
            ShouldThrowOnRequestStop = true
        };
        var scm = new FakeScmManager
        {
            IsInstalled = true,
            CurrentStatus = "Running",
            ExceptionOnStop = new Win32Exception(5) // Access Denied
        };
        var controller = new ServiceControllerManager(channel, scm, _options, NullLogger.Instance);

        ServiceControlResult result = await controller.RestartAsync();

        Assert.Equal(ServiceControlOutcome.Faulted, result.Outcome);
        Assert.Contains("stop phase", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestartAsync_WhenStopSucceeds_AndStartFails_ReturnsFaultedWithStartPhase()
    {
        var channel = new FakeServiceControlChannel
        {
            StatusToReturn = new ServiceStatusDto("Running", "1.0.0", DateTimeOffset.UtcNow, null)
        };
        var scm = new FakeScmManager
        {
            IsInstalled = true,
            CurrentStatus = "Running",
            ExceptionOnStart = new Win32Exception(5)
        };
        channel.OnRequestStop = () =>
        {
            scm.CurrentStatus = "Stopped";
            channel.ShouldThrowOnPing = true;
        };

        var controller = new ServiceControllerManager(channel, scm, _options, NullLogger.Instance);

        ServiceControlResult result = await controller.RestartAsync();

        Assert.Equal(ServiceControlOutcome.Faulted, result.Outcome);
        Assert.Contains("start phase", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestartAsync_WhenBothPhasesSucceed_ReturnsOk()
    {
        var channel = new FakeServiceControlChannel
        {
            StatusToReturn = new ServiceStatusDto("Running", "1.0.0", DateTimeOffset.UtcNow, null)
        };
        var scm = new FakeScmManager
        {
            IsInstalled = true,
            CurrentStatus = "Running"
        };
        scm.OnStartCallback = () =>
        {
            channel.ShouldThrowOnPing = false;
            channel.StatusToReturn = new ServiceStatusDto("Running", "1.0.0", DateTimeOffset.UtcNow, null);
            scm.CurrentStatus = "Running";
        };
        channel.OnRequestStop = () =>
        {
            scm.CurrentStatus = "Stopped";
            channel.ShouldThrowOnPing = true;
        };

        var controller = new ServiceControllerManager(channel, scm, _options, NullLogger.Instance);

        ServiceControlResult result = await controller.RestartAsync();

        Assert.Equal(ServiceControlOutcome.Ok, result.Outcome);
        Assert.True(channel.RequestStopCalled);
        Assert.True(scm.StartCalled);
    }

    [Fact]
    public async Task WatchStatusAsync_YieldsStateChanges()
    {
        var channel = new FakeServiceControlChannel
        {
            StatusToReturn = new ServiceStatusDto("Running", "1.0.0", DateTimeOffset.UtcNow, null)
        };
        var scm = new FakeScmManager { IsInstalled = true, CurrentStatus = "Running" };
        var controller = new ServiceControllerManager(channel, scm, _options, NullLogger.Instance);

        using var cts = new CancellationTokenSource();
        var statuses = new List<ServiceStatusDto>();

        var watchTask = Task.Run(async () =>
        {
            await foreach (ServiceStatusDto status in controller.WatchStatusAsync(cts.Token))
            {
                statuses.Add(status);
                if (statuses.Count >= 2)
                {
                    cts.Cancel();
                    break;
                }
            }
        });

        // Give it a moment to yield first
        await Task.Delay(100);
        // Change status to trigger second yield
        channel.StatusToReturn = new ServiceStatusDto("Stopping", "1.0.0", DateTimeOffset.UtcNow, null);

        await Task.WhenAny(watchTask, Task.Delay(2000));
        cts.Cancel();

        Assert.NotEmpty(statuses);
        Assert.Equal("Running", statuses[0].State);
    }

    [Fact]
    public async Task ServiceControlViewModel_Properties_ReflectStateTransitions()
    {
        var channel = new FakeServiceControlChannel
        {
            StatusToReturn = new ServiceStatusDto("Stopped", "1.0.0", DateTimeOffset.UtcNow, null)
        };
        var scm = new FakeScmManager { IsInstalled = true, CurrentStatus = "Stopped" };
        var controller = new ServiceControllerManager(channel, scm, _options, NullLogger.Instance);

        var vm = new ServiceControlViewModel(controller);
        await vm.RefreshStatusAsync();

        Assert.Equal("Stopped", vm.State);
        Assert.True(vm.CanStart);
        Assert.False(vm.CanStop);
        Assert.False(vm.CanRestart);
        Assert.True(vm.IsServiceInstalled);
        Assert.Contains("stopped", vm.ActionPrompt, StringComparison.OrdinalIgnoreCase);

        // When transition to running
        channel.StatusToReturn = new ServiceStatusDto("Running", "1.0.0", DateTimeOffset.UtcNow, null);
        scm.CurrentStatus = "Running";
        await vm.RefreshStatusAsync();

        Assert.Equal("Running", vm.State);
        Assert.False(vm.CanStart);
        Assert.True(vm.CanStop);
        Assert.True(vm.CanRestart);
        Assert.Contains("normally", vm.ActionPrompt, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeServiceControlChannel : IServiceControlChannel
    {
        public bool ShouldThrowOnPing { get; set; }
        public bool ShouldThrowOnRequestStop { get; set; }
        public ServiceStatusDto? StatusToReturn { get; set; }
        public bool RequestStopCalled { get; private set; }
        public bool RequestRestartCalled { get; private set; }
        public Action? OnRequestStop { get; set; }

        public Task<ServiceStatusDto> PingAsync(CancellationToken cancellationToken = default)
        {
            if (ShouldThrowOnPing)
            {
                throw new IOException("Named pipe connection failed.");
            }

            return Task.FromResult(StatusToReturn
                ?? new ServiceStatusDto("Running", "1.0.0", DateTimeOffset.UtcNow, null));
        }

        public Task RequestStopAsync(CancellationToken cancellationToken = default)
        {
            if (ShouldThrowOnRequestStop)
            {
                throw new IOException("Named pipe connection failed.");
            }

            RequestStopCalled = true;
            OnRequestStop?.Invoke();
            return Task.CompletedTask;
        }

        public Task RequestRestartAsync(CancellationToken cancellationToken = default)
        {
            RequestRestartCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeScmManager : IScmManager
    {
        public bool IsInstalled { get; set; } = true;
        public string CurrentStatus { get; set; } = "Stopped";
        public bool StartCalled { get; private set; }
        public bool StopCalled { get; private set; }
        public Exception? ExceptionOnStart { get; set; }
        public Exception? ExceptionOnStop { get; set; }
        public Action? OnStartCallback { get; set; }

        public bool IsServiceInstalled(string serviceName) => IsInstalled;

        public string GetServiceStatus(string serviceName) => CurrentStatus;

        public Task StartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            StartCalled = true;
            if (ExceptionOnStart != null)
            {
                throw ExceptionOnStart;
            }

            CurrentStatus = "Running";
            OnStartCallback?.Invoke();
            return Task.CompletedTask;
        }

        public Task StopServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            StopCalled = true;
            if (ExceptionOnStop != null)
            {
                throw ExceptionOnStop;
            }

            CurrentStatus = "Stopped";
            return Task.CompletedTask;
        }
    }
}

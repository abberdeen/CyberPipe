using System.Text.Json;
using CyberPipe.Contracts;
using CyberPipe.Service;
using CyberPipe.ServiceControl;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class PipeRpcTests
{
    [Fact]
    public async Task PipeRpc_Route_RoundTripsThroughRealNamedPipe()
    {
        string pipeName = $"CyberPipe.Test.Rpc.{Guid.NewGuid():N}";
        var tracker = new ServiceStatusTracker("1.0.0");
        tracker.MarkRunning();

        await using var server = new ServiceControlPipeServer(
            pipeName,
            tracker,
            logger: NullLogger.Instance,
            rpcHandler: (command, payload) =>
            {
                Assert.Equal(ServicePipeCommands.Route, command);
                RouteRequest request = JsonSerializer.Deserialize<RouteRequest>(payload)!;
                var result = new RouteResult
                {
                    Status = RouteStatus.Matched,
                    Tier = IntentTier.Tier0,
                    Intent = "system.help",
                    Confidence = 0.99,
                    Reason = $"echo:{request.Text}"
                };
                return Task.FromResult(JsonSerializer.Serialize(result));
            });
        server.Start();

        var router = new PipeIntentRouter(new ServiceControlPipeClient(pipeName, connectTimeoutMs: 10000));
        RouteResult result = await router.RouteAsync(new RouteRequest { Text = "help", Channel = ChannelKind.Cli });

        Assert.Equal(RouteStatus.Matched, result.Status);
        Assert.Equal("system.help", result.Intent);
        Assert.Equal("echo:help", result.Reason);
    }

    [Fact]
    public async Task PipeRpc_UnknownCommand_ThrowsWithServerMessage()
    {
        string pipeName = $"CyberPipe.Test.Rpc.{Guid.NewGuid():N}";
        var tracker = new ServiceStatusTracker("1.0.0");
        tracker.MarkRunning();

        await using var server = new ServiceControlPipeServer(
            pipeName,
            tracker,
            logger: NullLogger.Instance,
            rpcHandler: (command, _) => throw new InvalidOperationException($"Unknown pipe command '{command}'."));
        server.Start();

        var client = new ServiceControlPipeClient(pipeName, connectTimeoutMs: 10000);
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CallRpcAsync("nope", "{}"));
        Assert.Contains("Unknown pipe command", ex.Message);
    }
}

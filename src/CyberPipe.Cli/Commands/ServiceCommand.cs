using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Cli.Commands;

public static class ServiceCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> ExecuteAsync(CliContext ctx, string[] args, CancellationToken ct = default)
    {
        if (args.Length == 0)
        {
            await ctx.Error.WriteLineAsync("Usage: cyberpipe service <status|start|stop|restart>").ConfigureAwait(false);
            return CliExitCodes.InvalidArgument;
        }

        string action = args[0].ToLowerInvariant();
        if (ctx.ServiceControl == null)
        {
            await PrintServiceUnavailableAsync(ctx).ConfigureAwait(false);
            return CliExitCodes.ServiceUnavailable;
        }

        try
        {
            return action switch
            {
                "status" => await GetStatusAsync(ctx, ct).ConfigureAwait(false),
                "start" => await StartServiceAsync(ctx, ct).ConfigureAwait(false),
                "stop" => await StopServiceAsync(ctx, ct).ConfigureAwait(false),
                "restart" => await RestartServiceAsync(ctx, ct).ConfigureAwait(false),
                _ => await UnknownActionAsync(ctx, action).ConfigureAwait(false),
            };
        }
        catch (Exception ex)
        {
            await PrintServiceUnavailableAsync(ctx, ex.Message).ConfigureAwait(false);
            return CliExitCodes.ServiceUnavailable;
        }
    }

    private static async Task<int> GetStatusAsync(CliContext ctx, CancellationToken ct)
    {
        ServiceStatusDto status = await ctx.ServiceControl!.GetStatusAsync(ct).ConfigureAwait(false);

        if (ctx.IsJson)
        {
            await ctx.Out.WriteLineAsync(JsonSerializer.Serialize(status, JsonOptions)).ConfigureAwait(false);
            return CliExitCodes.Success;
        }

        await ctx.Out.WriteLineAsync($"CyberPipe Service Status: {status.State}").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync($"  Version:   {status.Version}").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync($"  StartedAt: {status.StartedAtUtc:g}").ConfigureAwait(false);
        if (!string.IsNullOrEmpty(status.LastError))
        {
            await ctx.Out.WriteLineAsync($"  LastError: {status.LastError}").ConfigureAwait(false);
        }

        return CliExitCodes.Success;
    }

    private static async Task<int> StartServiceAsync(CliContext ctx, CancellationToken ct)
    {
        ServiceControlResult result = await ctx.ServiceControl!.StartAsync(ct).ConfigureAwait(false);
        if (result.Outcome != ServiceControlOutcome.Ok)
        {
            await ctx.Error.WriteLineAsync($"Failed to start service: {result.Outcome} ({result.Error ?? "No details"})").ConfigureAwait(false);
            return CliExitCodes.GeneralError;
        }

        await ctx.Out.WriteLineAsync("Service started successfully.").ConfigureAwait(false);
        return CliExitCodes.Success;
    }

    private static async Task<int> StopServiceAsync(CliContext ctx, CancellationToken ct)
    {
        ServiceControlResult result = await ctx.ServiceControl!.StopAsync(ct).ConfigureAwait(false);
        if (result.Outcome != ServiceControlOutcome.Ok)
        {
            await ctx.Error.WriteLineAsync($"Failed to stop service: {result.Outcome} ({result.Error ?? "No details"})").ConfigureAwait(false);
            return CliExitCodes.GeneralError;
        }

        await ctx.Out.WriteLineAsync("Service stopped successfully.").ConfigureAwait(false);
        return CliExitCodes.Success;
    }

    private static async Task<int> RestartServiceAsync(CliContext ctx, CancellationToken ct)
    {
        ServiceControlResult result = await ctx.ServiceControl!.RestartAsync(ct).ConfigureAwait(false);
        if (result.Outcome != ServiceControlOutcome.Ok)
        {
            await ctx.Error.WriteLineAsync($"Failed to restart service: {result.Outcome} ({result.Error ?? "No details"})").ConfigureAwait(false);
            return CliExitCodes.GeneralError;
        }

        await ctx.Out.WriteLineAsync("Service restarted successfully.").ConfigureAwait(false);
        return CliExitCodes.Success;
    }

    private static async Task PrintServiceUnavailableAsync(CliContext ctx, string? details = null)
    {
        await ctx.Error.WriteLineAsync("Error: Service is unavailable (communication channel disconnected).").ConfigureAwait(false);
        if (!string.IsNullOrEmpty(details) && ctx.IsVerbose)
        {
            await ctx.Error.WriteLineAsync($"Details: {details}").ConfigureAwait(false);
        }
        await ctx.Error.WriteLineAsync("Hint: Try starting the service with 'cyberpipe service start' or run scripts/install-service.ps1.").ConfigureAwait(false);
    }

    private static async Task<int> UnknownActionAsync(CliContext ctx, string action)
    {
        await ctx.Error.WriteLineAsync($"Error: Unknown service action '{action}'. Valid actions: status, start, stop, restart.").ConfigureAwait(false);
        return CliExitCodes.InvalidArgument;
    }
}

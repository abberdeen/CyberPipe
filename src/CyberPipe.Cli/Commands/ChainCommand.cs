using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Cli.Commands;

public static class ChainCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> ExecuteAsync(CliContext ctx, string[] args, CancellationToken ct = default)
    {
        if (args.Length == 0)
        {
            await ctx.Error.WriteLineAsync("Usage: cyberpipe chain <list|runs|run <id>> [payloadJson]").ConfigureAwait(false);
            return CliExitCodes.InvalidArgument;
        }

        string action = args[0].ToLowerInvariant();
        return action switch
        {
            "list" => await ListChainsAsync(ctx, ct).ConfigureAwait(false),
            "runs" => await ListRunsAsync(ctx, args.Skip(1).ToArray(), ct).ConfigureAwait(false),
            "run" => await RunChainAsync(ctx, args.Skip(1).ToArray(), ct).ConfigureAwait(false),
            _ => await UnknownActionAsync(ctx, action).ConfigureAwait(false),
        };
    }

    private static async Task<int> ListChainsAsync(CliContext ctx, CancellationToken ct)
    {
        if (ctx.ChainRepo == null)
        {
            await ctx.Error.WriteLineAsync("Error: Chain repository service is unavailable.").ConfigureAwait(false);
            return CliExitCodes.ServiceUnavailable;
        }

        IReadOnlyList<ChainDefinitionDto> list = await ctx.ChainRepo.ListEnabledAsync(ct).ConfigureAwait(false);

        if (ctx.IsJson)
        {
            await ctx.Out.WriteLineAsync(JsonSerializer.Serialize(list, JsonOptions)).ConfigureAwait(false);
            return CliExitCodes.Success;
        }

        await ctx.Out.WriteLineAsync($"Active Chains ({list.Count}):").ConfigureAwait(false);
        foreach (ChainDefinitionDto chain in list)
        {
            await ctx.Out.WriteLineAsync($"  [{chain.Id.Value}] {chain.Name}")
                .ConfigureAwait(false);
            await ctx.Out.WriteLineAsync($"    Trigger: {chain.Trigger.Kind} | Skill: {chain.Skill.SkillId} | Script: {chain.Script.PackageSlug} v{chain.Script.PackageVersion} | Delivery: {chain.Delivery.Channel}")
                .ConfigureAwait(false);
        }

        return CliExitCodes.Success;
    }

    private static async Task<int> ListRunsAsync(CliContext ctx, string[] args, CancellationToken ct)
    {
        if (ctx.ExecutionRepo == null)
        {
            await ctx.Error.WriteLineAsync("Error: Execution repository service is unavailable.").ConfigureAwait(false);
            return CliExitCodes.ServiceUnavailable;
        }

        if (args.Length > 0 && Guid.TryParse(args[0], out Guid runGuid))
        {
            var runId = new RunId(runGuid);
            ChainRunState? state = await ctx.ExecutionRepo.GetRunAsync(runId, ct).ConfigureAwait(false);
            if (state == null)
            {
                await ctx.Error.WriteLineAsync($"Error: Run '{runGuid}' not found.").ConfigureAwait(false);
                return CliExitCodes.NotFound;
            }

            if (ctx.IsJson)
            {
                await ctx.Out.WriteLineAsync(JsonSerializer.Serialize(state, JsonOptions)).ConfigureAwait(false);
                return CliExitCodes.Success;
            }

            await ctx.Out.WriteLineAsync($"Run: {state.RunId.Value} | Chain: {state.ChainId.Value} | Status: {state.Status}").ConfigureAwait(false);
            await ctx.Out.WriteLineAsync($"Started: {state.StartedAtUtc:g} | Finished: {state.FinishedAtUtc:g}").ConfigureAwait(false);
            foreach (ChainCheckpointDto cp in state.Checkpoints)
            {
                await ctx.Out.WriteLineAsync($"  - [{cp.Step}] State={cp.State} At={cp.CheckpointAtUtc:HH:mm:ss}").ConfigureAwait(false);
            }
            return CliExitCodes.Success;
        }

        IReadOnlyList<RunId> unfinished = await ctx.ExecutionRepo.ListUnfinishedRunsAsync(ct).ConfigureAwait(false);
        if (ctx.IsJson)
        {
            await ctx.Out.WriteLineAsync(JsonSerializer.Serialize(unfinished.Select(r => r.Value), JsonOptions)).ConfigureAwait(false);
            return CliExitCodes.Success;
        }

        await ctx.Out.WriteLineAsync($"Unfinished Runs ({unfinished.Count}):").ConfigureAwait(false);
        foreach (RunId id in unfinished)
        {
            await ctx.Out.WriteLineAsync($"  - {id.Value}").ConfigureAwait(false);
        }

        return CliExitCodes.Success;
    }

    private static async Task<int> RunChainAsync(CliContext ctx, string[] args, CancellationToken ct)
    {
        if (args.Length == 0 || !Guid.TryParse(args[0], out Guid chainGuid))
        {
            await ctx.Error.WriteLineAsync("Error: Valid Chain ID (Guid) required. Usage: cyberpipe chain run <guid> [payloadJson]").ConfigureAwait(false);
            return CliExitCodes.InvalidArgument;
        }

        if (ctx.ChainRepo == null || ctx.TriggerInvoker == null)
        {
            await ctx.Error.WriteLineAsync("Error: Chain execution service is unavailable.").ConfigureAwait(false);
            return CliExitCodes.ServiceUnavailable;
        }

        var chainId = new ChainId(chainGuid);
        ChainDefinitionDto? def = await ctx.ChainRepo.GetByIdAsync(chainId, ct).ConfigureAwait(false);
        if (def == null)
        {
            await ctx.Error.WriteLineAsync($"Error: Chain '{chainGuid}' not found.").ConfigureAwait(false);
            return CliExitCodes.NotFound;
        }

        string payloadJson = args.Length > 1 ? args[1] : "{}";
        try
        {
            RunId runId = await ctx.TriggerInvoker.InvokeFunctionCallAsync(chainId, payloadJson, ct).ConfigureAwait(false);

            if (ctx.IsJson)
            {
                var result = new { ChainId = chainGuid, RunId = runId.Value, Status = "Triggered", DeliveryChannel = def.Delivery.Channel.ToString() };
                await ctx.Out.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions)).ConfigureAwait(false);
                return CliExitCodes.Success;
            }

            await ctx.Out.WriteLineAsync($"Chain '{def.Name}' triggered successfully.").ConfigureAwait(false);
            await ctx.Out.WriteLineAsync($"  RunId: {runId.Value}").ConfigureAwait(false);
            await ctx.Out.WriteLineAsync($"  Delivery Target: {def.Delivery.Channel} ({def.Delivery.Target})").ConfigureAwait(false);
            return CliExitCodes.Success;
        }
        catch (Exception ex)
        {
            await ctx.Error.WriteLineAsync($"Error invoking chain: {ex.Message}").ConfigureAwait(false);
            return CliExitCodes.GeneralError;
        }
    }

    private static async Task<int> UnknownActionAsync(CliContext ctx, string action)
    {
        await ctx.Error.WriteLineAsync($"Error: Unknown chain action '{action}'. Valid actions: list, runs, run.").ConfigureAwait(false);
        return CliExitCodes.InvalidArgument;
    }
}

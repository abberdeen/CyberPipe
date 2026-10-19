using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Cli.Commands;

public static class BudgetCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> ExecuteAsync(CliContext ctx, string[] args, CancellationToken ct = default)
    {
        if (ctx.BudgetGovernor == null)
        {
            await ctx.Error.WriteLineAsync("Error: Budget governor service is unavailable.").ConfigureAwait(false);
            return CliExitCodes.ServiceUnavailable;
        }

        try
        {
            BudgetStatusDto status = await ctx.BudgetGovernor.GetStatusAsync(ct).ConfigureAwait(false);

            if (ctx.IsJson)
            {
                await ctx.Out.WriteLineAsync(JsonSerializer.Serialize(status, JsonOptions)).ConfigureAwait(false);
                return CliExitCodes.Success;
            }

            await ctx.Out.WriteLineAsync($"CyberPipe Token Budget Status ({status.BudgetDate}):").ConfigureAwait(false);
            await ctx.Out.WriteLineAsync($"  Daily Cap:       ${status.Limits.DailyCapUsd:F2}").ConfigureAwait(false);
            await ctx.Out.WriteLineAsync($"  Spent Today:     ${status.SpentTodayUsd:F2}").ConfigureAwait(false);
            await ctx.Out.WriteLineAsync($"  Remaining Today: ${status.RemainingTodayUsd:F2}").ConfigureAwait(false);
            await ctx.Out.WriteLineAsync($"  Tokens Today:    {status.TokensUsedToday:N0}").ConfigureAwait(false);
            await ctx.Out.WriteLineAsync($"  Soft Limit:      {status.Limits.SoftTokenLimit:N0} tokens").ConfigureAwait(false);
            await ctx.Out.WriteLineAsync($"  Hard Limit:      {status.Limits.HardTokenLimit:N0} tokens").ConfigureAwait(false);
            await ctx.Out.WriteLineAsync($"  Turns Used:      {status.CloudTurnsUsed} cloud / {status.LocalTurnsUsed} local").ConfigureAwait(false);

            return CliExitCodes.Success;
        }
        catch (Exception ex)
        {
            await ctx.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
            return CliExitCodes.GeneralError;
        }
    }
}

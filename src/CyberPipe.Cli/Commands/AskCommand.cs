using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Cli.Commands;

public static class AskCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> ExecuteAsync(CliContext ctx, string[] args, CancellationToken ct = default)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            await ctx.Error.WriteLineAsync("Error: Missing text for 'ask' command. Usage: cyberpipe ask \"<message>\"").ConfigureAwait(false);
            return CliExitCodes.InvalidArgument;
        }

        if (ctx.Router == null)
        {
            await ctx.Error.WriteLineAsync("Error: Router service is unavailable.").ConfigureAwait(false);
            return CliExitCodes.ServiceUnavailable;
        }

        string text = args[0];
        var request = new RouteRequest
        {
            Text = text,
            Channel = ChannelKind.Cli,
            SenderId = "Owner",
            Metadata = new Dictionary<string, string>(),
        };

        try
        {
            RouteResult result = await ctx.Router.RouteAsync(request, ct).ConfigureAwait(false);

            if (ctx.IsJson)
            {
                await ctx.Out.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions)).ConfigureAwait(false);
                return result.Status == RouteStatus.BudgetDenied ? CliExitCodes.BudgetDenied :
                       result.Status == RouteStatus.Failed ? CliExitCodes.GeneralError : CliExitCodes.Success;
            }

            if (result.Status == RouteStatus.BudgetDenied)
            {
                await ctx.Error.WriteLineAsync($"Budget denied: {result.Reason ?? "Daily budget or token limit exceeded."}").ConfigureAwait(false);
                return CliExitCodes.BudgetDenied;
            }

            if (result.Status == RouteStatus.Failed)
            {
                await ctx.Error.WriteLineAsync($"Routing error: {result.Reason ?? "Unknown failure."}").ConfigureAwait(false);
                return CliExitCodes.GeneralError;
            }

            string answer = result.ResponseText ?? result.Intent ?? result.MatchedRuleName ?? "OK";
            await ctx.Out.WriteLineAsync(answer).ConfigureAwait(false);
            return CliExitCodes.Success;
        }
        catch (Exception ex)
        {
            await ctx.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
            if (ctx.IsVerbose)
            {
                await ctx.Error.WriteLineAsync(ex.StackTrace).ConfigureAwait(false);
            }
            return CliExitCodes.GeneralError;
        }
    }
}

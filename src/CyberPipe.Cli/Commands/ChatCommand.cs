using CyberPipe.Contracts;

namespace CyberPipe.Cli.Commands;

public static class ChatCommand
{
    public static async Task<int> ExecuteAsync(CliContext ctx, string[] args, CancellationToken ct = default)
    {
        if (ctx.Router == null)
        {
            await ctx.Error.WriteLineAsync("Error: Router service is unavailable.").ConfigureAwait(false);
            return CliExitCodes.ServiceUnavailable;
        }

        string sessionId = Guid.NewGuid().ToString("N");
        int turnCount = 0;

        await ctx.Out.WriteLineAsync("CyberPipe Interactive Chat (Session: " + sessionId + ")").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync("Type your message and press Enter. Commands: '/abort' (cancel task), 'exit' or '/quit' (close chat).").ConfigureAwait(false);

        CancellationTokenSource? currentTaskCts = null;

        while (!ct.IsCancellationRequested)
        {
            await ctx.Out.WriteAsync("> ").ConfigureAwait(false);
            string? line;
            try
            {
                line = await ctx.In.ReadLineAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line == null)
            {
                // EOF
                break;
            }

            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("/quit", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("/exit", StringComparison.OrdinalIgnoreCase))
            {
                await ctx.Out.WriteLineAsync("Goodbye!").ConfigureAwait(false);
                break;
            }

            if (trimmed.Equals("/abort", StringComparison.OrdinalIgnoreCase))
            {
                if (currentTaskCts != null && !currentTaskCts.IsCancellationRequested)
                {
                    currentTaskCts.Cancel();
                    await ctx.Out.WriteLineAsync("[Task aborted]").ConfigureAwait(false);
                }
                else
                {
                    await ctx.Out.WriteLineAsync("[No active task to abort]").ConfigureAwait(false);
                }
                continue;
            }

            // Normal turn
            turnCount++;
            var metadata = new Dictionary<string, string>
            {
                ["sessionId"] = sessionId,
                ["turn"] = turnCount.ToString(),
            };

            var request = new RouteRequest
            {
                Text = trimmed,
                Channel = ChannelKind.Cli,
                SenderId = "Owner",
                Metadata = metadata,
            };

            currentTaskCts = new CancellationTokenSource();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, currentTaskCts.Token);

            try
            {
                RouteResult result = await ctx.Router.RouteAsync(request, linkedCts.Token).ConfigureAwait(false);
                string answer = !string.IsNullOrWhiteSpace(result.ResponseText)
                    ? result.ResponseText
                    : result.Status switch
                    {
                        RouteStatus.Matched => result.Intent ?? result.MatchedRuleName ?? "OK",
                        RouteStatus.BudgetDenied => $"[Budget Denied: {result.Reason ?? "Exceeded"}]",
                        RouteStatus.Escalate => $"[Escalated: {result.Reason ?? "Unknown"}]",
                        _ => $"[Error: {result.Reason ?? "Failed"}]",
                    };

                await ctx.Out.WriteLineAsync($"Assistant [{result.Tier}]: {answer}").ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Abort during execution does NOT terminate the CLI process!
                await ctx.Out.WriteLineAsync("[Task aborted by user]").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await ctx.Error.WriteLineAsync($"[Error: {ex.Message}]").ConfigureAwait(false);
            }
            finally
            {
                currentTaskCts.Dispose();
                currentTaskCts = null;
            }
        }

        return CliExitCodes.Success;
    }
}

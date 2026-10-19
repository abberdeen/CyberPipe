using System.Text.Json;
using CyberPipe.Cli.Commands;
using CyberPipe.Contracts;
using CyberPipe.Logging;

namespace CyberPipe.Cli;

/// <summary>
/// Диспетчер команд CLI CyberPipe (TASK-062).
/// Обрабатывает флаги, маршрутизирует подкоманды и возвращает стандартизированный код выхода.
/// </summary>
public static class CliApp
{
    public static async Task<int> RunAsync(string[] args, CliContext ctx, CancellationToken ct = default)
    {
        var rawArgs = new List<string>();

        // Parse global flags
        foreach (string arg in args)
        {
            if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                ctx.IsJson = true;
            }
            else if (arg.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
            {
                ctx.IsVerbose = true;
            }
            else
            {
                rawArgs.Add(arg);
            }
        }

        if (rawArgs.Count == 0 || rawArgs[0] is "--help" or "-h" or "help")
        {
            await PrintHelpAsync(ctx).ConfigureAwait(false);
            return CliExitCodes.Success;
        }

        string command = rawArgs[0].ToLowerInvariant();
        string[] cmdArgs = rawArgs.Skip(1).ToArray();

        return command switch
        {
            "ask" => await AskCommand.ExecuteAsync(ctx, cmdArgs, ct).ConfigureAwait(false),
            "chat" => await ChatCommand.ExecuteAsync(ctx, cmdArgs, ct).ConfigureAwait(false),
            "chain" => await ChainCommand.ExecuteAsync(ctx, cmdArgs, ct).ConfigureAwait(false),
            "service" => await ServiceCommand.ExecuteAsync(ctx, cmdArgs, ct).ConfigureAwait(false),
            "budget" => await BudgetCommand.ExecuteAsync(ctx, cmdArgs, ct).ConfigureAwait(false),
            "approve" => await HitlCommand.ExecuteApproveAsync(ctx, cmdArgs, ct).ConfigureAwait(false),
            "reject" => await HitlCommand.ExecuteRejectAsync(ctx, cmdArgs, ct).ConfigureAwait(false),
            "logs" => await LogsCommand.ExecuteAsync(ctx, cmdArgs, ct).ConfigureAwait(false),
            "health" or "diagnostics" => await RunDiagnosticsAsync(ctx, ct).ConfigureAwait(false),
            _ => await UnknownCommandAsync(ctx, command).ConfigureAwait(false),
        };
    }

    private static async Task<int> RunDiagnosticsAsync(CliContext ctx, CancellationToken ct)
    {
        CyberPipeOptions options = ctx.Options ?? new CyberPipeOptions();
        var diagnostics = new DiagnosticsService("Cli", options, new UnavailableDatabaseHealthProbe());
        HealthReportDto report = await diagnostics.GetHealthAsync(ct).ConfigureAwait(false);

        if (ctx.IsJson)
        {
            await ctx.Out.WriteLineAsync(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
            return CliExitCodes.Success;
        }

        await ctx.Out.WriteLineAsync($"Diagnostics report: {report.Status} (Process: {report.ProcessName}, Version: {report.Version})").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync($"  - Database: {report.Database.Status} ({report.Database.Detail ?? "OK"})").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync($"  - Transport: {report.Transport.Status} (Type: {report.Transport.ImplementationType})").ConfigureAwait(false);
        return CliExitCodes.Success;
    }

    private static async Task PrintHelpAsync(CliContext ctx)
    {
        await ctx.Out.WriteLineAsync("CyberPipe CLI — Local Personal Assistant Control").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync().ConfigureAwait(false);
        await ctx.Out.WriteLineAsync("Usage: cyberpipe [command] [options]").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync().ConfigureAwait(false);
        await ctx.Out.WriteLineAsync("Commands:").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync("  ask \"<text>\"              Send a single prompt to the assistant").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync("  chat                      Interactive TUI chat mode").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync("  chain list                List registered active chains").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync("  chain runs [run-id]       Inspect chain run states and checkpoints").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync("  chain run <id> [payload]  Manually trigger a chain (function call)").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync("  service <status|start|stop|restart>  Manage CyberPipe Windows Service").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync("  budget                    View daily token budget status and remaining cap").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync("  approve <id>              Approve pending HITL request").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync("  reject <id> [reason]      Reject pending HITL request").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync("  logs [-f] [lines]         View diagnostic log output").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync("  diagnostics               Collect and print system diagnostics report").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync().ConfigureAwait(false);
        await ctx.Out.WriteLineAsync("Options:").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync("  --json                    Output response as structured JSON").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync("  --verbose                 Display detailed diagnostic traces").ConfigureAwait(false);
        await ctx.Out.WriteLineAsync("  --help, -h                Display this help screen").ConfigureAwait(false);
    }

    private static async Task<int> UnknownCommandAsync(CliContext ctx, string command)
    {
        await ctx.Error.WriteLineAsync($"Error: Unknown command '{command}'.").ConfigureAwait(false);
        await ctx.Error.WriteLineAsync("Run 'cyberpipe --help' for available commands.").ConfigureAwait(false);
        return CliExitCodes.InvalidArgument;
    }
}

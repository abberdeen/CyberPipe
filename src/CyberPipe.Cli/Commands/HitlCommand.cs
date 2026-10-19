using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Cli.Commands;

public static class HitlCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> ExecuteApproveAsync(CliContext ctx, string[] args, CancellationToken ct = default)
    {
        if (args.Length == 0 || !Guid.TryParse(args[0], out Guid approvalId))
        {
            await ctx.Error.WriteLineAsync("Error: Valid Approval ID (Guid) required. Usage: cyberpipe approve <guid>").ConfigureAwait(false);
            return CliExitCodes.InvalidArgument;
        }

        if (ctx.HitlService == null)
        {
            await ctx.Error.WriteLineAsync("Error: HITL approval service is unavailable.").ConfigureAwait(false);
            return CliExitCodes.ServiceUnavailable;
        }

        try
        {
            HitlApprovalResult result = await ctx.HitlService.ApproveAsync(
                approvalId,
                ownerId: "Owner",
                channel: ChannelKind.Cli,
                cancellationToken: ct).ConfigureAwait(false);

            if (ctx.IsJson)
            {
                await ctx.Out.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions)).ConfigureAwait(false);
                return CliExitCodes.Success;
            }

            await ctx.Out.WriteLineAsync($"Approval '{approvalId}' approved successfully.").ConfigureAwait(false);
            return CliExitCodes.Success;
        }
        catch (HitlAlreadyResolvedException ex)
        {
            await ctx.Error.WriteLineAsync($"Error: Request '{approvalId}' is already resolved: {ex.Message}").ConfigureAwait(false);
            return CliExitCodes.AlreadyResolved;
        }
        catch (HitlNotFoundException ex)
        {
            await ctx.Error.WriteLineAsync($"Error: Approval request '{approvalId}' not found: {ex.Message}").ConfigureAwait(false);
            return CliExitCodes.NotFound;
        }
        catch (HitlAccessDeniedException ex)
        {
            await ctx.Error.WriteLineAsync($"Error: Access denied: {ex.Message}").ConfigureAwait(false);
            return CliExitCodes.AccessDenied;
        }
        catch (Exception ex)
        {
            await ctx.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
            return CliExitCodes.GeneralError;
        }
    }

    public static async Task<int> ExecuteRejectAsync(CliContext ctx, string[] args, CancellationToken ct = default)
    {
        if (args.Length == 0 || !Guid.TryParse(args[0], out Guid approvalId))
        {
            await ctx.Error.WriteLineAsync("Error: Valid Approval ID (Guid) required. Usage: cyberpipe reject <guid> [reason]").ConfigureAwait(false);
            return CliExitCodes.InvalidArgument;
        }

        if (ctx.HitlService == null)
        {
            await ctx.Error.WriteLineAsync("Error: HITL approval service is unavailable.").ConfigureAwait(false);
            return CliExitCodes.ServiceUnavailable;
        }

        string? reason = args.Length > 1 ? string.Join(" ", args.Skip(1)) : null;

        try
        {
            HitlApprovalResult result = await ctx.HitlService.RejectAsync(
                approvalId,
                ownerId: "Owner",
                channel: ChannelKind.Cli,
                reason: reason,
                cancellationToken: ct).ConfigureAwait(false);

            if (ctx.IsJson)
            {
                await ctx.Out.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions)).ConfigureAwait(false);
                return CliExitCodes.Success;
            }

            await ctx.Out.WriteLineAsync($"Approval '{approvalId}' rejected successfully. Reason: {reason ?? "None"}").ConfigureAwait(false);
            return CliExitCodes.Success;
        }
        catch (HitlAlreadyResolvedException ex)
        {
            await ctx.Error.WriteLineAsync($"Error: Request '{approvalId}' is already resolved: {ex.Message}").ConfigureAwait(false);
            return CliExitCodes.AlreadyResolved;
        }
        catch (HitlNotFoundException ex)
        {
            await ctx.Error.WriteLineAsync($"Error: Approval request '{approvalId}' not found: {ex.Message}").ConfigureAwait(false);
            return CliExitCodes.NotFound;
        }
        catch (HitlAccessDeniedException ex)
        {
            await ctx.Error.WriteLineAsync($"Error: Access denied: {ex.Message}").ConfigureAwait(false);
            return CliExitCodes.AccessDenied;
        }
        catch (Exception ex)
        {
            await ctx.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
            return CliExitCodes.GeneralError;
        }
    }
}

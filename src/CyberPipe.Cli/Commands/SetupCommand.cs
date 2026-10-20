using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Cli.Commands;

public static class SetupCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> ExecuteAsync(CliContext ctx, string[] args, CancellationToken ct = default)
    {
        if (ctx.SetupWizard == null)
        {
            await ctx.Error.WriteLineAsync("Error: Setup wizard service is unavailable.").ConfigureAwait(false);
            return CliExitCodes.ServiceUnavailable;
        }

        try
        {
            SetupWizardStateDto state = await ctx.SetupWizard.GetStatusAsync(ct).ConfigureAwait(false);

            if (args.Length > 0 && args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                if (ctx.IsJson)
                {
                    await ctx.Out.WriteLineAsync(JsonSerializer.Serialize(state, JsonOptions)).ConfigureAwait(false);
                    return CliExitCodes.Success;
                }

                await ctx.Out.WriteLineAsync("CyberPipe Setup Status:").ConfigureAwait(false);
                await ctx.Out.WriteLineAsync($"  Completed: {state.IsCompleted}").ConfigureAwait(false);
                await ctx.Out.WriteLineAsync($"  Current Step: {state.CurrentStep}").ConfigureAwait(false);
                await ctx.Out.WriteLineAsync($"  Database Configured: {state.DatabaseConfigured}").ConfigureAwait(false);
                await ctx.Out.WriteLineAsync($"  Tier 1 Embedding Installed: {state.EmbeddingInstalled} (Skipped: {state.EmbeddingSkipped})").ConfigureAwait(false);
                await ctx.Out.WriteLineAsync($"  Configured Channels: {string.Join(", ", state.ConfiguredChannels)}").ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(state.WarningMessage))
                {
                    await ctx.Out.WriteLineAsync($"  Warning: {state.WarningMessage}").ConfigureAwait(false);
                }

                return CliExitCodes.Success;
            }

            if (state.IsCompleted)
            {
                if (ctx.IsJson)
                {
                    await ctx.Out.WriteLineAsync(JsonSerializer.Serialize(state, JsonOptions)).ConfigureAwait(false);
                }
                else
                {
                    await ctx.Out.WriteLineAsync("CyberPipe has already been configured. Use 'cyberpipe setup status' to view configuration.").ConfigureAwait(false);
                }

                return CliExitCodes.Success;
            }

            // Interactive or automated setup flow
            await ctx.Out.WriteLineAsync("=== CyberPipe First-Run Setup Wizard ===").ConfigureAwait(false);

            // Step 1: Database
            if (!state.DatabaseConfigured)
            {
                await ctx.Out.WriteLineAsync("--- Step 1: PostgreSQL Connection ---").ConfigureAwait(false);
                await ctx.Out.WriteAsync("PostgreSQL host [localhost]: ").ConfigureAwait(false);
                string host = (await ctx.In.ReadLineAsync(ct).ConfigureAwait(false))?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(host)) host = "localhost";

                await ctx.Out.WriteAsync("PostgreSQL port [5432]: ").ConfigureAwait(false);
                string portStr = (await ctx.In.ReadLineAsync(ct).ConfigureAwait(false))?.Trim() ?? "";
                int port = int.TryParse(portStr, out int p) ? p : 5432;

                await ctx.Out.WriteAsync("Database name [cyberpipe]: ").ConfigureAwait(false);
                string database = (await ctx.In.ReadLineAsync(ct).ConfigureAwait(false))?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(database)) database = "cyberpipe";

                await ctx.Out.WriteAsync("Username [cyberpipe]: ").ConfigureAwait(false);
                string username = (await ctx.In.ReadLineAsync(ct).ConfigureAwait(false))?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(username)) username = "cyberpipe";

                await ctx.Out.WriteAsync("Password: ").ConfigureAwait(false);
                string password = (await ctx.In.ReadLineAsync(ct).ConfigureAwait(false))?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(password))
                {
                    await ctx.Error.WriteLineAsync("Error: Password must not be empty.").ConfigureAwait(false);
                    return CliExitCodes.InvalidArgument;
                }

                var dbReq = new DatabaseSetupRequest
                {
                    Host = host,
                    Port = port,
                    Database = database,
                    Username = username,
                    Password = SecretValue.FromPlainText(password),
                };

                Result<SetupWizardStateDto> dbResult = await ctx.SetupWizard.ConfigureDatabaseAsync(dbReq, ct).ConfigureAwait(false);
                if (!dbResult.IsSuccess)
                {
                    await ctx.Error.WriteLineAsync($"Error configuring database: {dbResult.Error?.Message}").ConfigureAwait(false);
                    return CliExitCodes.GeneralError;
                }

                state = dbResult.Value!;
                await ctx.Out.WriteLineAsync("Database connected and schema migrations applied successfully.").ConfigureAwait(false);
            }

            // Step 2: Owner
            if (state.CurrentStep == SetupStep.Owner)
            {
                await ctx.Out.WriteLineAsync("--- Step 2: Owner Configuration ---").ConfigureAwait(false);
                await ctx.Out.WriteLineAsync("Note: Only Owner access is allowed.").ConfigureAwait(false);
                await ctx.Out.WriteAsync("Telegram User ID (optional, leave empty if not using Telegram): ").ConfigureAwait(false);
                string tgOwner = (await ctx.In.ReadLineAsync(ct).ConfigureAwait(false))?.Trim() ?? "";

                await ctx.Out.WriteAsync("Local CLI/Desktop Owner identifier [owner]: ").ConfigureAwait(false);
                string localOwner = (await ctx.In.ReadLineAsync(ct).ConfigureAwait(false))?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(localOwner) && string.IsNullOrWhiteSpace(tgOwner))
                {
                    localOwner = "owner";
                }

                var ownerReq = new OwnerSetupRequest
                {
                    TelegramOwnerId = string.IsNullOrWhiteSpace(tgOwner) ? null : tgOwner,
                    CliOwnerId = localOwner,
                    DesktopOwnerId = localOwner,
                };

                Result<SetupWizardStateDto> ownerResult = await ctx.SetupWizard.ConfigureOwnerAsync(ownerReq, ct).ConfigureAwait(false);
                if (!ownerResult.IsSuccess)
                {
                    await ctx.Error.WriteLineAsync($"Error configuring owner: {ownerResult.Error?.Message}").ConfigureAwait(false);
                    return CliExitCodes.InvalidArgument;
                }

                state = ownerResult.Value!;
                await ctx.Out.WriteLineAsync("Owner credentials registered.").ConfigureAwait(false);
            }

            // Step 3: Tier 1 Embedding
            if (state.CurrentStep == SetupStep.Tier1Embedding)
            {
                await ctx.Out.WriteLineAsync("--- Step 3: Tier 1 Embedding Model ---").ConfigureAwait(false);
                await ctx.Out.WriteLineAsync("CyberPipe recommends 'multilingual-e5-small' for fast multilingual semantic routing (Tier 1).").ConfigureAwait(false);
                await ctx.Out.WriteAsync("Install multilingual-e5-small now? (Y/n) [Y]: ").ConfigureAwait(false);
                string ans = (await ctx.In.ReadLineAsync(ct).ConfigureAwait(false))?.Trim() ?? "";
                bool install = !ans.Equals("n", StringComparison.OrdinalIgnoreCase) && !ans.Equals("no", StringComparison.OrdinalIgnoreCase);

                Result<SetupWizardStateDto> embedResult = await ctx.SetupWizard.ConfigureTier1EmbeddingAsync(install, ct).ConfigureAwait(false);
                if (!embedResult.IsSuccess)
                {
                    await ctx.Error.WriteLineAsync($"Error installing embedding model: {embedResult.Error?.Message}").ConfigureAwait(false);
                    return CliExitCodes.GeneralError;
                }

                state = embedResult.Value!;
                if (!install && !string.IsNullOrWhiteSpace(state.WarningMessage))
                {
                    await ctx.Out.WriteLineAsync($"Warning: {state.WarningMessage}").ConfigureAwait(false);
                }
            }

            // Step 4: Tier Models and Budget
            if (state.CurrentStep == SetupStep.TierModelsAndBudget)
            {
                await ctx.Out.WriteLineAsync("--- Step 4: Models & Token Budget ---").ConfigureAwait(false);
                await ctx.Out.WriteLineAsync("Default Budget: $0.50/day, 4,000 soft tokens, 12,000 hard tokens, 3 cloud / 5 local turns.").ConfigureAwait(false);
                Result<SetupWizardStateDto> budgetResult = await ctx.SetupWizard.ConfigureTierModelsAndBudgetAsync(null, null, ct).ConfigureAwait(false);
                if (!budgetResult.IsSuccess)
                {
                    await ctx.Error.WriteLineAsync($"Error configuring budget: {budgetResult.Error?.Message}").ConfigureAwait(false);
                    return CliExitCodes.GeneralError;
                }

                state = budgetResult.Value!;
            }

            // Step 5: First Channel
            if (state.CurrentStep == SetupStep.FirstChannel)
            {
                await ctx.Out.WriteLineAsync("--- Step 5: First Communication Channel ---").ConfigureAwait(false);
                await ctx.Out.WriteLineAsync("Select starting channel:").ConfigureAwait(false);
                await ctx.Out.WriteLineAsync("  1. CLI (Current terminal)").ConfigureAwait(false);
                await ctx.Out.WriteLineAsync("  2. Desktop UI").ConfigureAwait(false);
                await ctx.Out.WriteLineAsync("  3. Telegram Bot").ConfigureAwait(false);
                await ctx.Out.WriteAsync("Choice [1]: ").ConfigureAwait(false);

                string choice = (await ctx.In.ReadLineAsync(ct).ConfigureAwait(false))?.Trim() ?? "1";
                FirstChannelSetupRequest chReq;

                if (choice == "3")
                {
                    await ctx.Out.WriteAsync("Enter Telegram Bot Token: ").ConfigureAwait(false);
                    string botToken = (await ctx.In.ReadLineAsync(ct).ConfigureAwait(false))?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(botToken))
                    {
                        await ctx.Error.WriteLineAsync("Error: Telegram bot token is required.").ConfigureAwait(false);
                        return CliExitCodes.InvalidArgument;
                    }

                    chReq = new FirstChannelSetupRequest
                    {
                        Channel = ChannelKind.Telegram,
                        TelegramBotToken = SecretValue.FromPlainText(botToken),
                    };
                }
                else if (choice == "2")
                {
                    chReq = new FirstChannelSetupRequest { Channel = ChannelKind.Desktop };
                }
                else
                {
                    chReq = new FirstChannelSetupRequest { Channel = ChannelKind.Cli };
                }

                Result<SetupWizardStateDto> chResult = await ctx.SetupWizard.ConfigureFirstChannelAsync(chReq, ct).ConfigureAwait(false);
                if (!chResult.IsSuccess)
                {
                    await ctx.Error.WriteLineAsync($"Error setting up channel: {chResult.Error?.Message}").ConfigureAwait(false);
                    return CliExitCodes.GeneralError;
                }

                state = chResult.Value!;
            }

            // Completion
            Result<SetupCompletionSummary> completionResult = await ctx.SetupWizard.CompleteAsync(ct).ConfigureAwait(false);
            if (!completionResult.IsSuccess)
            {
                await ctx.Error.WriteLineAsync($"Error completing setup: {completionResult.Error?.Message}").ConfigureAwait(false);
                return CliExitCodes.GeneralError;
            }

            SetupCompletionSummary summary = completionResult.Value!;
            if (ctx.IsJson)
            {
                await ctx.Out.WriteLineAsync(JsonSerializer.Serialize(summary, JsonOptions)).ConfigureAwait(false);
                return CliExitCodes.Success;
            }

            await ctx.Out.WriteLineAsync("=== Setup Completed Successfully ===").ConfigureAwait(false);
            await ctx.Out.WriteLineAsync($"Completed At: {summary.CompletedAtUtc:u}").ConfigureAwait(false);
            await ctx.Out.WriteLineAsync($"Configured Channels: {string.Join(", ", summary.ConfiguredChannels)}").ConfigureAwait(false);
            await ctx.Out.WriteLineAsync($"Tier 1 Ready: {summary.Tier1Ready}").ConfigureAwait(false);
            await ctx.Out.WriteLineAsync($"Verification Message Sent: {summary.TestMessageSent}").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(summary.WarningMessage))
            {
                await ctx.Out.WriteLineAsync($"Notice: {summary.WarningMessage}").ConfigureAwait(false);
            }

            return CliExitCodes.Success;
        }
        catch (Exception ex)
        {
            await ctx.Error.WriteLineAsync($"Error during setup: {ex.Message}").ConfigureAwait(false);
            return CliExitCodes.GeneralError;
        }
    }
}

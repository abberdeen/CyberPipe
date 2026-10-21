using System.Text.Json;
using CyberPipe.Cli;
using CyberPipe.Contracts;
using CyberPipe.Setup;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class SetupWizardTests
{
    private sealed class FakeSecretStore : ISecretStore
    {
        public Dictionary<string, SecretValue> Secrets { get; } = new();
        public bool IsPlatformStore => false;

        public Task SetAsync(string key, SecretValue value, CancellationToken cancellationToken = default)
        {
            Secrets[key] = value;
            return Task.CompletedTask;
        }

        public Task<SecretValue?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            Secrets.TryGetValue(key, out SecretValue? val);
            return Task.FromResult(val);
        }

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Secrets.Remove(key));
        }

        public Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(Secrets.Keys.ToList());
        }
    }

    private sealed class FakeDatabaseBootstrapService : IDatabaseBootstrapService
    {
        public bool ShouldFailConnection { get; set; }
        public bool ShouldFailMigrations { get; set; }
        public List<(DatabaseOptions Options, string RevealedPassword)> CheckedConnections { get; } = new();
        public List<DatabaseOptions> AppliedMigrations { get; } = new();

        public Task<Result<bool>> CheckConnectionAsync(DatabaseOptions options, SecretValue? password, CancellationToken cancellationToken = default)
        {
            CheckedConnections.Add((options, password?.Reveal() ?? string.Empty));
            if (ShouldFailConnection)
            {
                return Task.FromResult(Result<bool>.Failure("ConnectionFailed", "Unable to open SQLite database: file unreachable."));
            }

            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<int>> ApplyMigrationsAsync(DatabaseOptions options, SecretValue? password, CancellationToken cancellationToken = default)
        {
            AppliedMigrations.Add(options);
            if (ShouldFailMigrations)
            {
                return Task.FromResult(Result<int>.Failure("MigrationError", "Migration script failed."));
            }

            return Task.FromResult(Result<int>.Success(6));
        }
    }

    private sealed class FakeModelInstaller : IModelInstaller
    {
        public List<string> InstalledModels { get; } = new();
        public bool ShouldFail { get; set; }

        public Task<Result<bool>> InstallModelAsync(string modelId, CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                return Task.FromResult(Result<bool>.Failure("InstallError", "Ollama runtime unavailable."));
            }

            InstalledModels.Add(modelId);
            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<bool> IsModelInstalledAsync(string modelId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(InstalledModels.Contains(modelId));
        }
    }

    private sealed class FakeOwnerRegistrationService : IOwnerRegistrationService
    {
        public Dictionary<ChannelKind, List<string>> RegisteredOwners { get; } = new();

        public Task<Result<bool>> RegisterOwnerAsync(ChannelKind channel, string ownerId, CancellationToken cancellationToken = default)
        {
            if (!RegisteredOwners.TryGetValue(channel, out List<string>? list))
            {
                list = new List<string>();
                RegisteredOwners[channel] = list;
            }

            if (!list.Contains(ownerId))
            {
                list.Add(ownerId);
            }

            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<bool> IsOwnerRegisteredAsync(ChannelKind channel, string ownerId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(RegisteredOwners.TryGetValue(channel, out List<string>? list) && list.Contains(ownerId));
        }
    }

    private sealed class FakeChannelVerifier : IChannelVerifier
    {
        public bool FailVerification { get; set; }
        public List<(ChannelKind Channel, string? Token)> VerifiedChannels { get; } = new();
        public List<(ChannelKind Channel, string TargetOwnerId, string Text)> SentMessages { get; } = new();

        public Task<Result<bool>> VerifyChannelAsync(ChannelKind channel, SecretValue? token = null, CancellationToken cancellationToken = default)
        {
            VerifiedChannels.Add((channel, token?.Reveal()));
            if (FailVerification)
            {
                return Task.FromResult(Result<bool>.Failure("VerificationError", "Channel token invalid."));
            }

            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<bool>> SendTestMessageAsync(ChannelKind channel, string targetOwnerId, string text, CancellationToken cancellationToken = default)
        {
            SentMessages.Add((channel, targetOwnerId, text));
            return Task.FromResult(Result<bool>.Success(true));
        }
    }

    private static (SetupWizardEngine Engine, FakeDatabaseBootstrapService Db, FakeSecretStore Secrets, FakeModelInstaller Models, FakeOwnerRegistrationService Owners, FakeChannelVerifier Channels, DefaultSetupStateRepository Repo) CreateTestSetup()
    {
        var repo = new DefaultSetupStateRepository();
        var db = new FakeDatabaseBootstrapService();
        var secrets = new FakeSecretStore();
        var owners = new FakeOwnerRegistrationService();
        var models = new FakeModelInstaller();
        var channels = new FakeChannelVerifier();

        var engine = new SetupWizardEngine(repo, db, secrets, owners, models, channels);
        return (engine, db, secrets, models, owners, channels, repo);
    }

    [Fact]
    public async Task InitialStatus_ReturnsDatabaseStep_NotCompleted()
    {
        var (engine, _, _, _, _, _, _) = CreateTestSetup();

        SetupWizardStateDto status = await engine.GetStatusAsync();

        Assert.False(status.IsCompleted);
        Assert.False(status.DatabaseConfigured);
        Assert.Equal(SetupStep.Database, status.CurrentStep);
    }

    [Fact]
    public async Task UnreachableDatabase_ReturnsError_AndDoesNotAdvanceOrPersistConfigured()
    {
        var (engine, db, secrets, _, _, _, repo) = CreateTestSetup();
        db.ShouldFailConnection = true;

        var request = new DatabaseSetupRequest
        {
            Path = "data/test.db",
        };

        Result<SetupWizardStateDto> result = await engine.ConfigureDatabaseAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Equal("ConnectionFailed", result.Error?.Code);

        // State not marked configured
        Assert.False(secrets.Secrets.ContainsKey(SecretKeys.DatabasePassword));

        // State not marked configured
        SetupWizardStateDto status = await engine.GetStatusAsync();
        Assert.False(status.DatabaseConfigured);
        Assert.Equal(SetupStep.Database, status.CurrentStep);
    }

    [Fact]
    public async Task ValidDatabase_AppliesMigrations_AndAdvancesToOwner()
    {
        var (engine, db, secrets, _, _, _, _) = CreateTestSetup();

        var request = new DatabaseSetupRequest
        {
            Path = "data/cyberpipe_prod.db",
        };

        Result<SetupWizardStateDto> result = await engine.ConfigureDatabaseAsync(request);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.DatabaseConfigured);
        Assert.Equal(SetupStep.Owner, result.Value.CurrentStep);
        Assert.Equal("data/cyberpipe_prod.db", result.Value.DatabasePath);

        // SQLite needs no password: nothing stored under the legacy database password key.
        Assert.False(secrets.Secrets.ContainsKey(SecretKeys.DatabasePassword));

        // Path present in state DTO, no secrets inside it
        string json = JsonSerializer.Serialize(result.Value);
        Assert.Contains("data/cyberpipe_prod.db", json);
    }

    [Fact]
    public async Task ConfigureOwner_RequiresAtLeastOneId()
    {
        var (engine, _, _, _, _, _, _) = CreateTestSetup();

        // Must configure DB first
        await engine.ConfigureDatabaseAsync(new DatabaseSetupRequest
        {
            Path = "data/test.db",
        });

        // Empty owner request must fail
        Result<SetupWizardStateDto> failResult = await engine.ConfigureOwnerAsync(new OwnerSetupRequest
        {
            TelegramOwnerId = null,
            DesktopOwnerId = "",
            CliOwnerId = "   ",
        });

        Assert.False(failResult.IsSuccess);
        Assert.Equal("OwnerRequired", failResult.Error?.Code);

        // Valid owner request succeeds
        Result<SetupWizardStateDto> successResult = await engine.ConfigureOwnerAsync(new OwnerSetupRequest
        {
            TelegramOwnerId = "12345678",
            DesktopOwnerId = "desktop-owner",
            CliOwnerId = "cli-owner",
        });

        Assert.True(successResult.IsSuccess);
        Assert.Equal(SetupStep.Tier1Embedding, successResult.Value!.CurrentStep);
        Assert.Equal("12345678", successResult.Value.TelegramOwnerId);
    }

    [Fact]
    public async Task Tier1Embedding_Install_RequestsMultilingualE5Small()
    {
        var (engine, _, _, models, _, _, _) = CreateTestSetup();

        await engine.ConfigureDatabaseAsync(new DatabaseSetupRequest
        {
            Password = SecretValue.FromPlainText("pass"),
        });
        await engine.ConfigureOwnerAsync(new OwnerSetupRequest { CliOwnerId = "owner" });

        Result<SetupWizardStateDto> result = await engine.ConfigureTier1EmbeddingAsync(install: true);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.EmbeddingInstalled);
        Assert.False(result.Value.EmbeddingSkipped);
        Assert.Null(result.Value.WarningMessage);
        Assert.Equal(SetupStep.TierModelsAndBudget, result.Value.CurrentStep);

        // Explicit model ID multilingual-e5-small must be passed to installer
        Assert.Contains("multilingual-e5-small", models.InstalledModels);
    }

    [Fact]
    public async Task Tier1Embedding_Skip_SetsWarning_AndOffersAgainLater()
    {
        var (engine, _, _, models, _, _, _) = CreateTestSetup();

        await engine.ConfigureDatabaseAsync(new DatabaseSetupRequest
        {
            Password = SecretValue.FromPlainText("pass"),
        });
        await engine.ConfigureOwnerAsync(new OwnerSetupRequest { CliOwnerId = "owner" });

        Result<SetupWizardStateDto> result = await engine.ConfigureTier1EmbeddingAsync(install: false);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.EmbeddingInstalled);
        Assert.True(result.Value.EmbeddingSkipped);
        Assert.NotNull(result.Value.WarningMessage);
        Assert.Contains("Tier 1", result.Value.WarningMessage);
        Assert.Empty(models.InstalledModels);

        // Complete rest of wizard
        await engine.ConfigureTierModelsAndBudgetAsync(null, null);
        await engine.ConfigureFirstChannelAsync(new FirstChannelSetupRequest { Channel = ChannelKind.Cli });
        await engine.CompleteAsync();

        // Should prompt on launch
        bool shouldPrompt = await engine.ShouldPromptEmbeddingOnLaunchAsync();
        Assert.True(shouldPrompt);

        // Install pending embedding later
        Result<bool> installLater = await engine.InstallPendingEmbeddingAsync();
        Assert.True(installLater.IsSuccess);
        Assert.Contains("multilingual-e5-small", models.InstalledModels);

        // No longer prompts
        Assert.False(await engine.ShouldPromptEmbeddingOnLaunchAsync());
    }

    [Fact]
    public async Task FirstChannel_Telegram_SavesBotTokenToSecretStore_NotState()
    {
        var (engine, _, secrets, _, _, channels, _) = CreateTestSetup();

        await engine.ConfigureDatabaseAsync(new DatabaseSetupRequest { Password = SecretValue.FromPlainText("pass") });
        await engine.ConfigureOwnerAsync(new OwnerSetupRequest { TelegramOwnerId = "12345" });
        await engine.ConfigureTier1EmbeddingAsync(install: true);
        await engine.ConfigureTierModelsAndBudgetAsync(null, null);

        Result<SetupWizardStateDto> result = await engine.ConfigureFirstChannelAsync(new FirstChannelSetupRequest
        {
            Channel = ChannelKind.Telegram,
            TelegramBotToken = SecretValue.FromPlainText("123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11"),
        });

        Assert.True(result.IsSuccess);
        Assert.Contains(ChannelKind.Telegram, result.Value!.ConfiguredChannels);

        // Token in SecretStore
        Assert.True(secrets.Secrets.ContainsKey(SecretKeys.TelegramBotToken));
        Assert.Equal("123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11", secrets.Secrets[SecretKeys.TelegramBotToken].Reveal());

        // Token NOT in state json
        string stateJson = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("ABC-DEF1234ghIkl", stateJson);
    }

    [Fact]
    public async Task CompleteAsync_SendsVerificationMessage_AndReturnsCleanSummary()
    {
        var (engine, _, secrets, _, _, channels, repo) = CreateTestSetup();

        await engine.ConfigureDatabaseAsync(new DatabaseSetupRequest { Password = SecretValue.FromPlainText("pass") });
        await engine.ConfigureOwnerAsync(new OwnerSetupRequest { CliOwnerId = "my-cli-owner" });
        await engine.ConfigureTier1EmbeddingAsync(install: true);
        await engine.ConfigureTierModelsAndBudgetAsync(null, null);
        await engine.ConfigureFirstChannelAsync(new FirstChannelSetupRequest { Channel = ChannelKind.Cli });

        Result<SetupCompletionSummary> summaryResult = await engine.CompleteAsync();

        Assert.True(summaryResult.IsSuccess);
        Assert.True(summaryResult.Value!.IsSuccess);
        Assert.True(summaryResult.Value.TestMessageSent);
        Assert.True(summaryResult.Value.Tier1Ready);

        // Channel verifier received the message
        Assert.Single(channels.SentMessages);
        Assert.Equal(ChannelKind.Cli, channels.SentMessages[0].Channel);
        Assert.Equal("my-cli-owner", channels.SentMessages[0].TargetOwnerId);

        // Repository marks completed
        Assert.True(await repo.IsSetupCompletedAsync());

        // Rerun check: setup status shows completed
        SetupWizardStateDto finalStatus = await engine.GetStatusAsync();
        Assert.True(finalStatus.IsCompleted);

        // Summary JSON contains no secrets
        string summaryJson = JsonSerializer.Serialize(summaryResult.Value);
        Assert.DoesNotContain("password", summaryJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", summaryJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InterruptedWizard_ResumesFromCurrentStep_WithoutReAskingCompletedSteps()
    {
        var (engine1, _, secrets, _, _, _, repo) = CreateTestSetup();

        // Run steps 1 and 2
        await engine1.ConfigureDatabaseAsync(new DatabaseSetupRequest { Password = SecretValue.FromPlainText("pass") });
        await engine1.ConfigureOwnerAsync(new OwnerSetupRequest { TelegramOwnerId = "tg-999" });

        // Simulate application restart with a new engine instance sharing the same repository
        var engine2 = new SetupWizardEngine(
            repo,
            new FakeDatabaseBootstrapService(),
            secrets,
            new FakeOwnerRegistrationService(),
            new FakeModelInstaller(),
            new FakeChannelVerifier());

        SetupWizardStateDto resumedState = await engine2.GetStatusAsync();

        Assert.True(resumedState.DatabaseConfigured);
        Assert.Equal("tg-999", resumedState.TelegramOwnerId);
        Assert.Equal(SetupStep.Tier1Embedding, resumedState.CurrentStep);
        Assert.False(resumedState.IsCompleted);
    }

    [Fact]
    public async Task StepBack_NavigatesToPreviousStep()
    {
        var (engine, _, _, _, _, _, _) = CreateTestSetup();

        await engine.ConfigureDatabaseAsync(new DatabaseSetupRequest { Password = SecretValue.FromPlainText("pass") });
        await engine.ConfigureOwnerAsync(new OwnerSetupRequest { CliOwnerId = "cli" });
        await engine.ConfigureTier1EmbeddingAsync(install: true);

        SetupWizardStateDto current = await engine.GetStatusAsync();
        Assert.Equal(SetupStep.TierModelsAndBudget, current.CurrentStep);

        // Step back should return to Tier1Embedding
        SetupWizardStateDto back1 = await engine.StepBackAsync();
        Assert.Equal(SetupStep.Tier1Embedding, back1.CurrentStep);

        // Step back again should return to Owner
        SetupWizardStateDto back2 = await engine.StepBackAsync();
        Assert.Equal(SetupStep.Owner, back2.CurrentStep);
    }

    [Fact]
    public async Task CannotComplete_WithoutConfiguredChannel()
    {
        var (engine, _, _, _, _, _, _) = CreateTestSetup();

        await engine.ConfigureDatabaseAsync(new DatabaseSetupRequest { Password = SecretValue.FromPlainText("pass") });
        await engine.ConfigureOwnerAsync(new OwnerSetupRequest { CliOwnerId = "cli" });

        // Attempt to call complete directly without channels
        Result<SetupCompletionSummary> result = await engine.CompleteAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("ChannelRequired", result.Error?.Code);
    }

    [Fact]
    public async Task Cli_SetupStatus_AndInteractiveExecution_WorkCorrectly()
    {
        var (engine, _, _, _, _, _, _) = CreateTestSetup();

        // 1. Initial status via CLI
        using var statusOut = new StringWriter();
        var statusCtx = new CliContext
        {
            SetupWizard = engine,
            Out = statusOut,
            IsJson = true,
        };

        int exit1 = await CliApp.RunAsync(["setup", "status"], statusCtx);
        Assert.Equal(CliExitCodes.Success, exit1);

        SetupWizardStateDto? parsedState = JsonSerializer.Deserialize<SetupWizardStateDto>(
            statusOut.ToString(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(parsedState);
        Assert.False(parsedState!.IsCompleted);
        Assert.Equal(SetupStep.Database, parsedState.CurrentStep);

        // 2. Interactive setup via CLI simulated input
        string simulatedInput = string.Join("\n",
            "data/cli-test.db", // database file path
            "",                // telegram id (none)
            "admin-cli",       // local cli owner
            "y",               // install embedding
            "1"                // first channel = CLI
        ) + "\n";

        using var interactiveIn = new StringReader(simulatedInput);
        using var interactiveOut = new StringWriter();
        using var interactiveErr = new StringWriter();

        var interactiveCtx = new CliContext
        {
            SetupWizard = engine,
            In = interactiveIn,
            Out = interactiveOut,
            Error = interactiveErr,
        };

        int exit2 = await CliApp.RunAsync(["setup"], interactiveCtx);
        Assert.Equal(CliExitCodes.Success, exit2);
        string outputText = interactiveOut.ToString();
        Assert.Contains("Setup Completed Successfully", outputText);

        // 3. Second run when already completed
        using var reRunOut = new StringWriter();
        var reRunCtx = new CliContext
        {
            SetupWizard = engine,
            Out = reRunOut,
        };

        int exit3 = await CliApp.RunAsync(["setup"], reRunCtx);
        Assert.Equal(CliExitCodes.Success, exit3);
        Assert.Contains("already been configured", reRunOut.ToString());
    }
}

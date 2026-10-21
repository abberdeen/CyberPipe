using CyberPipe.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CyberPipe.Setup;

/// <summary>
/// Реализация движка мастера первого запуска (TASK-063).
/// Управляет шагами: База данных -> Владелец -> Эмбеддинг Tier 1 -> Модели и бюджет -> Первый канал -> Завершение.
/// Обеспечивает возобновление с незавершённого шага, откат назад и изоляцию секретов.
/// </summary>
public sealed class SetupWizardEngine : ISetupWizardEngine
{
    public const string DefaultEmbeddingModelId = "multilingual-e5-small";

    private readonly ISetupStateRepository _stateRepository;
    private readonly IDatabaseBootstrapService _databaseBootstrap;
    private readonly ISecretStore _secretStore;
    private readonly IOwnerRegistrationService _ownerRegistration;
    private readonly IModelInstaller _modelInstaller;
    private readonly IChannelVerifier _channelVerifier;
    private readonly IModelCatalogProvider? _modelCatalog;
    private readonly ILogger<SetupWizardEngine> _logger;

    public SetupWizardEngine(
        ISetupStateRepository stateRepository,
        IDatabaseBootstrapService databaseBootstrap,
        ISecretStore secretStore,
        IOwnerRegistrationService ownerRegistration,
        IModelInstaller modelInstaller,
        IChannelVerifier channelVerifier,
        IModelCatalogProvider? modelCatalog = null,
        ILogger<SetupWizardEngine>? logger = null)
    {
        _stateRepository = stateRepository ?? throw new ArgumentNullException(nameof(stateRepository));
        _databaseBootstrap = databaseBootstrap ?? throw new ArgumentNullException(nameof(databaseBootstrap));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _ownerRegistration = ownerRegistration ?? throw new ArgumentNullException(nameof(ownerRegistration));
        _modelInstaller = modelInstaller ?? throw new ArgumentNullException(nameof(modelInstaller));
        _channelVerifier = channelVerifier ?? throw new ArgumentNullException(nameof(channelVerifier));
        _modelCatalog = modelCatalog;
        _logger = logger ?? NullLogger<SetupWizardEngine>.Instance;
    }

    /// <inheritdoc />
    public async Task<SetupWizardStateDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        SetupWizardStateDto? existing = await _stateRepository.GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        // Новое состояние для первого запуска
        var fresh = new SetupWizardStateDto
        {
            CurrentStep = SetupStep.Database,
            IsCompleted = false,
            DatabaseConfigured = false,
        };

        return await _stateRepository.SaveStateAsync(fresh, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<SetupWizardStateDto>> ConfigureDatabaseAsync(DatabaseSetupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return Result<SetupWizardStateDto>.Failure("InvalidArgument", "Database path must not be empty.");
        }

        var dbOptions = new DatabaseOptions
        {
            Path = request.Path,
        };

        _logger.LogInformation("Testing SQLite database at path {Path}...", request.Path);

        Result<bool> connectionResult = await _databaseBootstrap.CheckConnectionAsync(dbOptions, request.Password, cancellationToken).ConfigureAwait(false);
        if (!connectionResult.IsSuccess || !connectionResult.Value)
        {
            string error = connectionResult.Error?.Message ?? "Failed to open SQLite database at the provided path.";
            _logger.LogWarning("SQLite connectivity test failed: {Error}", error);
            return Result<SetupWizardStateDto>.Failure(
                connectionResult.Error?.Code ?? "DatabaseConnectionFailed",
                error);
        }

        // Применяем миграции
        _logger.LogInformation("Applying database migrations for {Path}...", request.Path);
        Result<int> migrationResult = await _databaseBootstrap.ApplyMigrationsAsync(dbOptions, request.Password, cancellationToken).ConfigureAwait(false);
        if (!migrationResult.IsSuccess)
        {
            string error = migrationResult.Error?.Message ?? "Failed to apply database schema migrations.";
            _logger.LogError("Database migration failed: {Error}", error);
            return Result<SetupWizardStateDto>.Failure(
                migrationResult.Error?.Code ?? "MigrationFailed",
                error);
        }

        SetupWizardStateDto current = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        SetupWizardStateDto updated = current with
        {
            DatabaseConfigured = true,
            DatabasePath = request.Path,
            CurrentStep = SetupStep.Owner,
        };

        await _stateRepository.SaveStateAsync(updated, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Database configured and migrated successfully. Advancing to Owner step.");

        return Result<SetupWizardStateDto>.Success(updated);
    }

    /// <inheritdoc />
    public async Task<Result<SetupWizardStateDto>> ConfigureOwnerAsync(OwnerSetupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SetupWizardStateDto current = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!current.DatabaseConfigured)
        {
            return Result<SetupWizardStateDto>.Failure("DatabaseNotConfigured", "Database must be successfully configured and migrated before setting up Owner.");
        }

        bool hasOwner = !string.IsNullOrWhiteSpace(request.TelegramOwnerId)
            || !string.IsNullOrWhiteSpace(request.DesktopOwnerId)
            || !string.IsNullOrWhiteSpace(request.CliOwnerId);

        if (!hasOwner)
        {
            return Result<SetupWizardStateDto>.Failure(
                "OwnerRequired",
                "At least one Owner identifier (Telegram user ID, Desktop owner ID, or CLI owner ID) must be provided.");
        }

        // Регистрируем владельца в белом списке TASK-073
        if (!string.IsNullOrWhiteSpace(request.TelegramOwnerId))
        {
            await _ownerRegistration.RegisterOwnerAsync(ChannelKind.Telegram, request.TelegramOwnerId.Trim(), cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(request.DesktopOwnerId))
        {
            await _ownerRegistration.RegisterOwnerAsync(ChannelKind.Desktop, request.DesktopOwnerId.Trim(), cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(request.CliOwnerId))
        {
            await _ownerRegistration.RegisterOwnerAsync(ChannelKind.Cli, request.CliOwnerId.Trim(), cancellationToken).ConfigureAwait(false);
        }

        SetupWizardStateDto updated = current with
        {
            TelegramOwnerId = string.IsNullOrWhiteSpace(request.TelegramOwnerId) ? current.TelegramOwnerId : request.TelegramOwnerId.Trim(),
            DesktopOwnerId = string.IsNullOrWhiteSpace(request.DesktopOwnerId) ? current.DesktopOwnerId : request.DesktopOwnerId.Trim(),
            CliOwnerId = string.IsNullOrWhiteSpace(request.CliOwnerId) ? current.CliOwnerId : request.CliOwnerId.Trim(),
            CurrentStep = SetupStep.Tier1Embedding,
        };

        await _stateRepository.SaveStateAsync(updated, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Owner configured successfully. Advancing to Tier1Embedding step.");

        return Result<SetupWizardStateDto>.Success(updated);
    }

    /// <inheritdoc />
    public async Task<Result<SetupWizardStateDto>> ConfigureTier1EmbeddingAsync(bool install, CancellationToken cancellationToken = default)
    {
        SetupWizardStateDto current = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!current.DatabaseConfigured)
        {
            return Result<SetupWizardStateDto>.Failure("DatabaseNotConfigured", "Database must be configured first.");
        }

        if (install)
        {
            _logger.LogInformation("Installing default embedding model {ModelId}...", DefaultEmbeddingModelId);
            Result<bool> installResult = await _modelInstaller.InstallModelAsync(DefaultEmbeddingModelId, cancellationToken).ConfigureAwait(false);
            if (!installResult.IsSuccess)
            {
                string error = installResult.Error?.Message ?? "Failed to install default embedding model.";
                _logger.LogWarning("Embedding model installation failed: {Error}", error);
                return Result<SetupWizardStateDto>.Failure(installResult.Error?.Code ?? "ModelInstallFailed", error);
            }

            SetupWizardStateDto updatedInstalled = current with
            {
                EmbeddingInstalled = true,
                EmbeddingSkipped = false,
                WarningMessage = null,
                CurrentStep = SetupStep.TierModelsAndBudget,
            };

            await _stateRepository.SaveStateAsync(updatedInstalled, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Tier 1 embedding model {ModelId} installed successfully.", DefaultEmbeddingModelId);
            return Result<SetupWizardStateDto>.Success(updatedInstalled);
        }

        // Пользователь пропустил установку эмбеддинга
        const string warning = "Tier 1 semantic routing is unavailable without an embedding model. Queries will fall back to Tier 2/3 LLM routing. Setup will offer installation again on next launch.";
        _logger.LogWarning("Tier 1 embedding installation skipped by owner. {Warning}", warning);

        SetupWizardStateDto updatedSkipped = current with
        {
            EmbeddingInstalled = false,
            EmbeddingSkipped = true,
            WarningMessage = warning,
            CurrentStep = SetupStep.TierModelsAndBudget,
        };

        await _stateRepository.SaveStateAsync(updatedSkipped, cancellationToken).ConfigureAwait(false);
        return Result<SetupWizardStateDto>.Success(updatedSkipped);
    }

    /// <inheritdoc />
    public async Task<Result<SetupWizardStateDto>> ConfigureTierModelsAndBudgetAsync(
        BudgetOptions? budget,
        IReadOnlyList<TierModelAssignment>? assignments,
        CancellationToken cancellationToken = default)
    {
        SetupWizardStateDto current = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!current.DatabaseConfigured)
        {
            return Result<SetupWizardStateDto>.Failure("DatabaseNotConfigured", "Database must be configured first.");
        }

        if (budget is not null)
        {
            if (budget.DailyCapUsd < 0)
            {
                return Result<SetupWizardStateDto>.Failure("InvalidArgument", "Daily cap USD must not be negative.");
            }

            if (budget.SoftTokenLimit <= 0 || budget.HardTokenLimit <= 0)
            {
                return Result<SetupWizardStateDto>.Failure("InvalidArgument", "Token limits must be positive.");
            }

            if (budget.SoftTokenLimit > budget.HardTokenLimit)
            {
                return Result<SetupWizardStateDto>.Failure("InvalidArgument", "Soft token limit must not exceed hard token limit.");
            }
        }

        SetupWizardStateDto updated = current with
        {
            Budget = budget ?? current.Budget ?? new BudgetOptions(),
            TierAssignments = assignments ?? current.TierAssignments,
            CurrentStep = SetupStep.FirstChannel,
        };

        await _stateRepository.SaveStateAsync(updated, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Models and budget configured. Advancing to FirstChannel step.");

        return Result<SetupWizardStateDto>.Success(updated);
    }

    /// <inheritdoc />
    public async Task<Result<SetupWizardStateDto>> ConfigureFirstChannelAsync(FirstChannelSetupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SetupWizardStateDto current = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!current.DatabaseConfigured)
        {
            return Result<SetupWizardStateDto>.Failure("DatabaseNotConfigured", "Database must be configured first.");
        }

        if (request.Channel == ChannelKind.Telegram)
        {
            if (request.TelegramBotToken is null || string.IsNullOrWhiteSpace(request.TelegramBotToken.Reveal()))
            {
                return Result<SetupWizardStateDto>.Failure("TelegramTokenRequired", "Telegram bot token must be provided when choosing Telegram as the first channel.");
            }

            _logger.LogInformation("Verifying Telegram bot token...");
            Result<bool> verifyResult = await _channelVerifier.VerifyChannelAsync(ChannelKind.Telegram, request.TelegramBotToken, cancellationToken).ConfigureAwait(false);
            if (!verifyResult.IsSuccess || !verifyResult.Value)
            {
                string error = verifyResult.Error?.Message ?? "Telegram bot verification failed: token appears invalid or unreachable.";
                _logger.LogWarning("Telegram verification failed: {Error}", error);
                return Result<SetupWizardStateDto>.Failure(verifyResult.Error?.Code ?? "ChannelVerificationFailed", error);
            }

            // Записываем токен в защищённое хранилище секретов
            await _secretStore.SetAsync(SecretKeys.TelegramBotToken, request.TelegramBotToken, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Desktop или CLI
            Result<bool> verifyResult = await _channelVerifier.VerifyChannelAsync(request.Channel, null, cancellationToken).ConfigureAwait(false);
            if (!verifyResult.IsSuccess || !verifyResult.Value)
            {
                string error = verifyResult.Error?.Message ?? $"{request.Channel} channel verification failed.";
                return Result<SetupWizardStateDto>.Failure(verifyResult.Error?.Code ?? "ChannelVerificationFailed", error);
            }
        }

        var channels = new List<ChannelKind>(current.ConfiguredChannels);
        if (!channels.Contains(request.Channel))
        {
            channels.Add(request.Channel);
        }

        SetupWizardStateDto updated = current with
        {
            ConfiguredChannels = channels,
            SelectedPrimaryChannel = request.Channel,
            CurrentStep = SetupStep.Completed,
        };

        await _stateRepository.SaveStateAsync(updated, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("First channel {Channel} configured successfully. Advancing to completion.", request.Channel);

        return Result<SetupWizardStateDto>.Success(updated);
    }

    /// <inheritdoc />
    public async Task<Result<SetupCompletionSummary>> CompleteAsync(CancellationToken cancellationToken = default)
    {
        SetupWizardStateDto current = await GetStatusAsync(cancellationToken).ConfigureAwait(false);

        if (!current.DatabaseConfigured)
        {
            return Result<SetupCompletionSummary>.Failure("DatabaseNotConfigured", "Setup cannot complete without a configured database.");
        }

        bool hasOwner = !string.IsNullOrWhiteSpace(current.TelegramOwnerId)
            || !string.IsNullOrWhiteSpace(current.DesktopOwnerId)
            || !string.IsNullOrWhiteSpace(current.CliOwnerId);

        if (!hasOwner)
        {
            return Result<SetupCompletionSummary>.Failure("OwnerRequired", "Setup cannot complete without an Owner configured.");
        }

        if (current.ConfiguredChannels.Count == 0)
        {
            return Result<SetupCompletionSummary>.Failure("ChannelRequired", "Setup cannot complete without at least one configured channel.");
        }

        ChannelKind primaryChannel = current.SelectedPrimaryChannel ?? current.ConfiguredChannels[0];
        string targetOwnerId = primaryChannel switch
        {
            ChannelKind.Telegram => current.TelegramOwnerId ?? "owner",
            ChannelKind.Desktop => current.DesktopOwnerId ?? "owner",
            ChannelKind.Cli => current.CliOwnerId ?? "owner",
            _ => "owner",
        };

        // Отправляем проверочное сообщение
        _logger.LogInformation("Sending verification message to channel {Channel}...", primaryChannel);
        Result<bool> sendResult = await _channelVerifier.SendTestMessageAsync(
            primaryChannel,
            targetOwnerId,
            "CyberPipe setup completed successfully! Assistant is ready.",
            cancellationToken).ConfigureAwait(false);

        bool testMessageSent = sendResult.IsSuccess && sendResult.Value;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        SetupWizardStateDto finalState = current with
        {
            IsCompleted = true,
            CurrentStep = SetupStep.Completed,
            CompletedAtUtc = now,
        };

        await _stateRepository.SaveStateAsync(finalState, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("CyberPipe setup wizard completed successfully at {CompletedAtUtc}.", now);

        var summary = new SetupCompletionSummary
        {
            IsSuccess = true,
            CompletedAtUtc = now,
            ConfiguredChannels = finalState.ConfiguredChannels,
            Tier1Ready = finalState.EmbeddingInstalled,
            TestMessageSent = testMessageSent,
            WarningMessage = finalState.WarningMessage,
        };

        return Result<SetupCompletionSummary>.Success(summary);
    }

    /// <inheritdoc />
    public async Task<SetupWizardStateDto> StepBackAsync(CancellationToken cancellationToken = default)
    {
        SetupWizardStateDto current = await GetStatusAsync(cancellationToken).ConfigureAwait(false);

        SetupStep previousStep = current.CurrentStep switch
        {
            SetupStep.Completed => SetupStep.FirstChannel,
            SetupStep.FirstChannel => SetupStep.TierModelsAndBudget,
            SetupStep.TierModelsAndBudget => SetupStep.Tier1Embedding,
            SetupStep.Tier1Embedding => SetupStep.Owner,
            SetupStep.Owner => SetupStep.Owner, // База уже мигрирована
            _ => SetupStep.Database,
        };

        SetupWizardStateDto updated = current with
        {
            CurrentStep = previousStep,
            IsCompleted = false,
        };

        return await _stateRepository.SaveStateAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ShouldPromptEmbeddingOnLaunchAsync(CancellationToken cancellationToken = default)
    {
        SetupWizardStateDto? state = await _stateRepository.GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            return false;
        }

        return state.IsCompleted && state.EmbeddingSkipped && !state.EmbeddingInstalled;
    }

    /// <inheritdoc />
    public async Task<Result<bool>> InstallPendingEmbeddingAsync(CancellationToken cancellationToken = default)
    {
        SetupWizardStateDto? state = await _stateRepository.GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            return Result<bool>.Failure("SetupNotFound", "Setup state not found.");
        }

        _logger.LogInformation("Installing pending embedding model {ModelId}...", DefaultEmbeddingModelId);
        Result<bool> installResult = await _modelInstaller.InstallModelAsync(DefaultEmbeddingModelId, cancellationToken).ConfigureAwait(false);
        if (!installResult.IsSuccess)
        {
            return installResult;
        }

        SetupWizardStateDto updated = state with
        {
            EmbeddingInstalled = true,
            EmbeddingSkipped = false,
            WarningMessage = null,
        };

        await _stateRepository.SaveStateAsync(updated, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Pending embedding model {ModelId} installed and state updated.", DefaultEmbeddingModelId);

        return Result<bool>.Success(true);
    }
}

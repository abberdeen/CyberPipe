namespace CyberPipe.Contracts;

/// <summary>
/// Шаги мастера первого запуска (TASK-063).
/// </summary>
public enum SetupStep
{
    /// <summary>Шаг проверки подключения к PostgreSQL и применения миграций.</summary>
    Database = 0,

    /// <summary>Шаг назначения идентификатора владельца (Owner).</summary>
    Owner = 1,

    /// <summary>Шаг предложения стандартного эмбеддинга multilingual-e5-small для Tier 1.</summary>
    Tier1Embedding = 2,

    /// <summary>Необязательный шаг настройки моделей и бюджета токенов.</summary>
    TierModelsAndBudget = 3,

    /// <summary>Шаг подключения первого канала (Telegram, Desktop или CLI).</summary>
    FirstChannel = 4,

    /// <summary>Мастер успешно завершён.</summary>
    Completed = 5,
}

/// <summary>
/// Состояние мастера первого запуска, сохраняемое в PostgreSQL (app.kv_settings).
/// Секреты (пароль БД, токен Telegram-бота) в это состояние НИКОГДА не пишутся.
/// </summary>
public sealed record SetupWizardStateDto
{
    /// <summary>Текущий шаг мастера.</summary>
    public SetupStep CurrentStep { get; init; } = SetupStep.Database;

    /// <summary>Признак полного завершения мастера настройки.</summary>
    public bool IsCompleted { get; init; }

    /// <summary>Признак успешного подключения и миграции базы данных.</summary>
    public bool DatabaseConfigured { get; init; }

    /// <summary>Хост базы данных (не секрет).</summary>
    public string? DatabaseHost { get; init; }

    /// <summary>Порт базы данных.</summary>
    public int? DatabasePort { get; init; }

    /// <summary>Имя базы данных.</summary>
    public string? DatabaseName { get; init; }

    /// <summary>Имя пользователя базы данных.</summary>
    public string? DatabaseUsername { get; init; }

    /// <summary>Идентификатор владельца в Telegram (если задан).</summary>
    public string? TelegramOwnerId { get; init; }

    /// <summary>Идентификатор владельца в Desktop (если задан).</summary>
    public string? DesktopOwnerId { get; init; }

    /// <summary>Идентификатор владельца в CLI (если задан).</summary>
    public string? CliOwnerId { get; init; }

    /// <summary>Признак установки стандартного эмбеддинга multilingual-e5-small.</summary>
    public bool EmbeddingInstalled { get; init; }

    /// <summary>Признак пропуска установки эмбеддинга при мастере первого запуска.</summary>
    public bool EmbeddingSkipped { get; init; }

    /// <summary>Настройки бюджета токенов, если изменены владельцем.</summary>
    public BudgetOptions? Budget { get; init; }

    /// <summary>Назначения моделей по ярусам маршрутизации.</summary>
    public IReadOnlyList<TierModelAssignment> TierAssignments { get; init; } = [];

    /// <summary>Список настроенных каналов связи.</summary>
    public IReadOnlyList<ChannelKind> ConfiguredChannels { get; init; } = [];

    /// <summary>Выбранный основной канал для тестовых сообщений.</summary>
    public ChannelKind? SelectedPrimaryChannel { get; init; }

    /// <summary>Метка времени завершения мастера в UTC.</summary>
    public DateTimeOffset? CompletedAtUtc { get; init; }

    /// <summary>Предупреждающее сообщение (например при пропуске эмбеддинга).</summary>
    public string? WarningMessage { get; init; }
}

/// <summary>
/// Запрос настройки базы данных в мастере первого запуска.
/// </summary>
public sealed record DatabaseSetupRequest
{
    /// <summary>Хост PostgreSQL.</summary>
    public string Host { get; init; } = "localhost";

    /// <summary>Порт PostgreSQL.</summary>
    public int Port { get; init; } = 5432;

    /// <summary>Имя базы данных.</summary>
    public string Database { get; init; } = "cyberpipe";

    /// <summary>Имя пользователя.</summary>
    public string Username { get; init; } = "cyberpipe";

    /// <summary>Пароль пользователя базы данных в защищённой обёртке.</summary>
    public required SecretValue Password { get; init; }
}

/// <summary>
/// Запрос настройки владельца (Owner). Хотя бы один идентификатор обязателен.
/// Доступ разрешён только владельцу.
/// </summary>
public sealed record OwnerSetupRequest
{
    /// <summary>Идентификатор Telegram владельца.</summary>
    public string? TelegramOwnerId { get; init; }

    /// <summary>Идентификатор Desktop владельца.</summary>
    public string? DesktopOwnerId { get; init; }

    /// <summary>Идентификатор CLI владельца.</summary>
    public string? CliOwnerId { get; init; }
}

/// <summary>
/// Запрос настройки первого канала связи.
/// </summary>
public sealed record FirstChannelSetupRequest
{
    /// <summary>Выбранный канал.</summary>
    public required ChannelKind Channel { get; init; }

    /// <summary>Токен Telegram-бота (только для канала Telegram).</summary>
    public SecretValue? TelegramBotToken { get; init; }
}

/// <summary>
/// Сводка завершения мастера первого запуска (без секретов).
/// </summary>
public sealed record SetupCompletionSummary
{
    /// <summary>Признак успешного завершения.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>Метка времени завершения настройки.</summary>
    public DateTimeOffset CompletedAtUtc { get; init; }

    /// <summary>Список настроенных каналов.</summary>
    public IReadOnlyList<ChannelKind> ConfiguredChannels { get; init; } = [];

    /// <summary>Готовность яруса Tier 1 (установлен ли эмбеддинг).</summary>
    public bool Tier1Ready { get; init; }

    /// <summary>Отправлено ли тестовое сообщение в выбранный канал.</summary>
    public bool TestMessageSent { get; init; }

    /// <summary>Предупреждения мастера (например пропуск эмбеддинга).</summary>
    public string? WarningMessage { get; init; }
}

/// <summary>
/// Репозиторий состояния мастера первого запуска в PostgreSQL (TASK-063).
/// Источник истины о завершённости настройки — база данных, не локальные файлы на диске.
/// </summary>
public interface ISetupStateRepository
{
    /// <summary>Возвращает сохранённое состояние мастера или null при чистой базе.</summary>
    Task<SetupWizardStateDto?> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Сохраняет или обновляет состояние мастера.</summary>
    Task<SetupWizardStateDto> SaveStateAsync(SetupWizardStateDto state, CancellationToken cancellationToken = default);

    /// <summary>Проверяет, завершена ли первоначальная настройка.</summary>
    Task<bool> IsSetupCompletedAsync(CancellationToken cancellationToken = default);

    /// <summary>Сбрасывает состояние мастера (для тестов).</summary>
    Task ResetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Шлюз проверки подключения и миграции базы данных для мастера первого запуска.
/// Реализации — TASK-010/TASK-011, в тестах мастера — фейк.
/// </summary>
public interface IDatabaseBootstrapService
{
    /// <summary>Проверяет подключение к PostgreSQL с указанными параметрами и паролем.</summary>
    Task<Result<bool>> CheckConnectionAsync(DatabaseOptions options, SecretValue password, CancellationToken cancellationToken = default);

    /// <summary>Применяет миграции схемы к базе данных.</summary>
    Task<Result<int>> ApplyMigrationsAsync(DatabaseOptions options, SecretValue password, CancellationToken cancellationToken = default);
}

/// <summary>
/// Сервис установки моделей (контракт TASK-081, используемый мастером TASK-063).
/// </summary>
public interface IModelInstaller
{
    /// <summary>Устанавливает модель по её идентификатору.</summary>
    Task<Result<bool>> InstallModelAsync(string modelId, CancellationToken cancellationToken = default);

    /// <summary>Проверяет, установлена ли модель в среде исполнения.</summary>
    Task<bool> IsModelInstalledAsync(string modelId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Каталог моделей (контракт TASK-080, используемый мастером TASK-063).
/// </summary>
public interface IModelCatalogProvider
{
    /// <summary>Возвращает дескрипторы моделей из каталогов.</summary>
    Task<IReadOnlyList<ModelDescriptorDto>> GetCatalogAsync(ModelHostingType? hostingType = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Сервис верификации каналов и отправки тестовых сообщений (контракты TASK-060/TASK-061/TASK-062).
/// </summary>
public interface IChannelVerifier
{
    /// <summary>Проверяет доступность и валидность канала.</summary>
    Task<Result<bool>> VerifyChannelAsync(ChannelKind channel, SecretValue? token = null, CancellationToken cancellationToken = default);

    /// <summary>Отправляет проверочное тестовое сообщение владельцу.</summary>
    Task<Result<bool>> SendTestMessageAsync(ChannelKind channel, string targetOwnerId, string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// Сервис белого списка владельцев (контракт TASK-073, потребляемый мастером TASK-063).
/// </summary>
public interface IOwnerRegistrationService
{
    /// <summary>Регистрирует идентификатор владельца для канала.</summary>
    Task<Result<bool>> RegisterOwnerAsync(ChannelKind channel, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Проверяет, зарегистрирован ли идентификатор как владелец.</summary>
    Task<bool> IsOwnerRegisteredAsync(ChannelKind channel, string ownerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Движок мастера первого запуска (TASK-063).
/// Содержит сценарий шагов, возобновление с места остановки, откат на шаг назад и фиксацию результата.
/// </summary>
public interface ISetupWizardEngine
{
    /// <summary>Возвращает текущее состояние мастера и признак первого запуска.</summary>
    Task<SetupWizardStateDto> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Шаг 1: Настройка подключения к базе данных и применение миграций.</summary>
    Task<Result<SetupWizardStateDto>> ConfigureDatabaseAsync(DatabaseSetupRequest request, CancellationToken cancellationToken = default);

    /// <summary>Шаг 2: Настройка идентификатора владельца (Owner).</summary>
    Task<Result<SetupWizardStateDto>> ConfigureOwnerAsync(OwnerSetupRequest request, CancellationToken cancellationToken = default);

    /// <summary>Шаг 3: Настройка или пропуск стандартного эмбеддинга multilingual-e5-small.</summary>
    Task<Result<SetupWizardStateDto>> ConfigureTier1EmbeddingAsync(bool install, CancellationToken cancellationToken = default);

    /// <summary>Шаг 4: Настройка моделей по ярусам и лимитов токен-бюджета (необязательно).</summary>
    Task<Result<SetupWizardStateDto>> ConfigureTierModelsAndBudgetAsync(BudgetOptions? budget, IReadOnlyList<TierModelAssignment>? assignments, CancellationToken cancellationToken = default);

    /// <summary>Шаг 5: Настройка первого канала связи.</summary>
    Task<Result<SetupWizardStateDto>> ConfigureFirstChannelAsync(FirstChannelSetupRequest request, CancellationToken cancellationToken = default);

    /// <summary>Завершает мастер: проверка работоспособности, тестовое сообщение, фиксация статуса в БД.</summary>
    Task<Result<SetupCompletionSummary>> CompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>Возвращает мастера на один шаг назад.</summary>
    Task<SetupWizardStateDto> StepBackAsync(CancellationToken cancellationToken = default);

    /// <summary>Проверяет необходимость предложить установку эмбеддинга при запуске, если он был пропущен.</summary>
    Task<bool> ShouldPromptEmbeddingOnLaunchAsync(CancellationToken cancellationToken = default);

    /// <summary>Устанавливает отложенный эмбеддинг после завершения мастера.</summary>
    Task<Result<bool>> InstallPendingEmbeddingAsync(CancellationToken cancellationToken = default);
}

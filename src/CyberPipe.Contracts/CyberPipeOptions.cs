namespace CyberPipe.Contracts;

/// <summary>
/// Корневая конфигурация CyberPipe. Потребители читают только опции, не файлы напрямую.
/// Приоритет источников (от низкого к высокому): значения по умолчанию, appsettings.json рядом со
/// службой, переменные окружения с префиксом CYBERPIPE_, файл переопределений из UI в каталоге
/// данных пользователя. Слияние выполняет <see cref="CyberPipeConfigurationLoader" />: объекты
/// сливаются по ключам, скаляры и массивы заменяются целиком источником с большим приоритетом.
/// Секреты в конфигурации не хранятся: только ссылки на ключи <see cref="SecretKeys" />.
/// Валидация при старте — <see cref="Validate" />; невалидная обязательная конфигурация останавливает
/// старт через <see cref="ConfigurationValidationException" /> без значений секретов в сообщении.
/// </summary>
public sealed record CyberPipeOptions
{
    /// <summary>Параметры подключения к PostgreSQL; пароль — только ссылка на секрет.</summary>
    public DatabaseOptions Database { get; init; } = new();

    /// <summary>Бюджет токенов и расходов с дефолтами Q23.</summary>
    public BudgetOptions Budget { get; init; } = new();

    /// <summary>Включение ярусов, назначение моделей и пороги семантического кэша.</summary>
    public RouterOptions Router { get; init; } = new();

    /// <summary>Выбор моделей и пути к каталогам моделей.</summary>
    public ModelOptions Models { get; init; } = new();

    /// <summary>Лимиты песочницы; лимит памяти без жёсткого дефолта (Q9).</summary>
    public SandboxOptions Sandbox { get; init; } = new();

    /// <summary>Каналы связи; токен Telegram — только ссылка на секрет.</summary>
    public ChannelOptions Channels { get; init; } = new();

    /// <summary>Идентификаторы владельца по каналам; других ролей в конфигурации нет.</summary>
    public OwnerOptions Owner { get; init; } = new();

    /// <summary>Целевые показатели производительности (targets, не limits и не ворота приёмки, Q11).</summary>
    public PerformanceTargetsOptions PerformanceTargets { get; init; } = new();

    /// <summary>
    /// Проверяет опции и возвращает ошибки с именами ключей в форме Section:Property.
    /// Значения секретов в ошибки не попадают: секретов-значений в конфигурации нет.
    /// </summary>
    /// <returns>Список ошибок; пуст при валидной конфигурации.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Database.Host))
            errors.Add("Database:Host must not be empty.");
        if (Database.Port is < 1 or > 65535)
            errors.Add("Database:Port must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(Database.Database))
            errors.Add("Database:Database must not be empty.");
        if (string.IsNullOrWhiteSpace(Database.Username))
            errors.Add("Database:Username must not be empty.");
        if (string.IsNullOrWhiteSpace(Database.PasswordSecretKey))
            errors.Add("Database:PasswordSecretKey must name a secret key.");

        if (Budget.DailyCapUsd < 0)
            errors.Add("Budget:DailyCapUsd must not be negative.");
        if (Budget.SoftTokenLimit <= 0)
            errors.Add("Budget:SoftTokenLimit must be positive.");
        if (Budget.HardTokenLimit <= 0)
            errors.Add("Budget:HardTokenLimit must be positive.");
        if (Budget.HardTokenLimit > 0 && Budget.SoftTokenLimit > Budget.HardTokenLimit)
            errors.Add("Budget:SoftTokenLimit must not exceed Budget:HardTokenLimit.");
        if (Budget.MaxCloudTurns < 0)
            errors.Add("Budget:MaxCloudTurns must not be negative.");
        if (Budget.MaxLocalTurns < 0)
            errors.Add("Budget:MaxLocalTurns must not be negative.");

        if (Router.Tier1SimilarityThreshold is < 0 or > 1)
            errors.Add("Router:Tier1SimilarityThreshold must be between 0 and 1.");
        if (Router.SemanticCacheThreshold is < 0 or > 1)
            errors.Add("Router:SemanticCacheThreshold must be between 0 and 1.");
        if (Router.SemanticCacheThreshold < Router.Tier1SimilarityThreshold)
            errors.Add("Router:SemanticCacheThreshold must not be lower than Router:Tier1SimilarityThreshold.");
        if (Router.SemanticCacheTtlMinutes <= 0)
            errors.Add("Router:SemanticCacheTtlMinutes must be positive.");
        if (Router.SemanticCacheMaxEntries <= 0)
            errors.Add("Router:SemanticCacheMaxEntries must be positive.");

        if (string.IsNullOrWhiteSpace(Models.EmbeddingModelId))
            errors.Add("Models:EmbeddingModelId must not be empty.");
        if (string.IsNullOrWhiteSpace(Models.LocalCatalogPath))
            errors.Add("Models:LocalCatalogPath must not be empty.");
        if (string.IsNullOrWhiteSpace(Models.CloudCatalogPath))
            errors.Add("Models:CloudCatalogPath must not be empty.");

        if (Sandbox.MemoryLimitMb is <= 0)
            errors.Add("Sandbox:MemoryLimitMb must be positive when set.");
        if (Sandbox.TimeoutSeconds <= 0)
            errors.Add("Sandbox:TimeoutSeconds must be positive.");

        if (string.IsNullOrWhiteSpace(Channels.TelegramBotTokenSecretKey))
            errors.Add("Channels:TelegramBotTokenSecretKey must name a secret key.");

        if (PerformanceTargets.Tier0TargetMilliseconds <= 0)
            errors.Add("PerformanceTargets:Tier0TargetMilliseconds must be positive.");
        if (PerformanceTargets.StopTargetMilliseconds <= 0)
            errors.Add("PerformanceTargets:StopTargetMilliseconds must be positive.");

        return errors;
    }
}

/// <summary>
/// Подключение к единственной PostgreSQL. Пароль значением здесь не хранится:
/// только <see cref="PasswordSecretKey" /> — ссылка на запись <see cref="ISecretStore" />.
/// Подключение и запрос пароля у пользователя — TASK-010.
/// </summary>
public sealed record DatabaseOptions
{
    /// <summary>Хост PostgreSQL.</summary>
    public string Host { get; init; } = "localhost";

    /// <summary>Порт PostgreSQL.</summary>
    public int Port { get; init; } = 5432;

    /// <summary>Имя базы данных.</summary>
    public string Database { get; init; } = "cyberpipe";

    /// <summary>Пользователь базы данных.</summary>
    public string Username { get; init; } = "cyberpipe";

    /// <summary>Ключ секрета с паролем в ISecretStore, не сам пароль.</summary>
    public string PasswordSecretKey { get; init; } = SecretKeys.DatabasePassword;
}

/// <summary>Бюджет с дефолтами Q23; все поля переопределяемы конфигурацией. Применение — TASK-033.</summary>
public sealed record BudgetOptions
{
    /// <summary>Дневной кап расходов в долларах США.</summary>
    public decimal DailyCapUsd { get; init; } = 0.50m;

    /// <summary>Мягкий лимит токенов на задачу.</summary>
    public int SoftTokenLimit { get; init; } = 4000;

    /// <summary>Жёсткий лимит токенов на задачу.</summary>
    public int HardTokenLimit { get; init; } = 12000;

    /// <summary>Максимум облачных ходов на задачу.</summary>
    public int MaxCloudTurns { get; init; } = 3;

    /// <summary>Максимум локальных ходов на задачу.</summary>
    public int MaxLocalTurns { get; init; } = 5;
}

/// <summary>Ярусы маршрутизации и семантический кэш. Применение — TASK-030..TASK-034.</summary>
public sealed record RouterOptions
{
    /// <summary>Ярус Tier0 (регулярные выражения) включён.</summary>
    public bool Tier0Enabled { get; init; } = true;

    /// <summary>Ярус Tier1 (эмбеддинги) включён.</summary>
    public bool Tier1Enabled { get; init; } = true;

    /// <summary>Ярус Tier2 (лёгкая модель) включён.</summary>
    public bool Tier2Enabled { get; init; } = true;

    /// <summary>Ярус Tier3 (тяжёлая модель) включён.</summary>
    public bool Tier3Enabled { get; init; } = true;

    /// <summary>Идентификатор модели яруса Tier2 из каталога TASK-080; пусто — не назначена.</summary>
    public string? Tier2ModelId { get; init; }

    /// <summary>Идентификатор модели яруса Tier3 из каталога TASK-080; пусто — не назначена.</summary>
    public string? Tier3ModelId { get; init; }

    /// <summary>Порог косинусной близости Tier1 для Matched.</summary>
    public double Tier1SimilarityThreshold { get; init; } = 0.80;

    /// <summary>Семантический кэш включён.</summary>
    public bool SemanticCacheEnabled { get; init; } = true;

    /// <summary>Порог близости семантического кэша; не ниже порога Tier1.</summary>
    public double SemanticCacheThreshold { get; init; } = 0.90;

    /// <summary>Время жизни записи семантического кэша в минутах.</summary>
    public int SemanticCacheTtlMinutes { get; init; } = 1440;

    /// <summary>Максимум записей семантического кэша.</summary>
    public int SemanticCacheMaxEntries { get; init; } = 1000;
}

/// <summary>
/// Выбор моделей. Модели не встроены (Q15): здесь только ключи выбора и пути к каталогам JSON;
/// сами каталоги — TASK-080, установка — TASK-081/TASK-063.
/// </summary>
public sealed record ModelOptions
{
    /// <summary>Стандартная мультиязычная эмбеддинг-модель Tier1, предлагаемая при первом запуске (Q10).</summary>
    public string EmbeddingModelId { get; init; } = "multilingual-e5-small";

    /// <summary>Путь к JSON-каталогу локальных моделей.</summary>
    public string LocalCatalogPath { get; init; } = "models/local-models.json";

    /// <summary>Путь к JSON-каталогу облачных моделей.</summary>
    public string CloudCatalogPath { get; init; } = "models/cloud-models.json";
}

/// <summary>
/// Песочница: лимит памяти задаётся настройкой и жёсткого дефолта не имеет (Q9);
/// null означает системное умолчание потребителя (TASK-023).
/// </summary>
public sealed record SandboxOptions
{
    /// <summary>Лимит памяти в мегабайтах; null — без заданного лимита.</summary>
    public long? MemoryLimitMb { get; init; }

    /// <summary>Тайм-аут выполнения в секундах.</summary>
    public int TimeoutSeconds { get; init; } = 60;
}

/// <summary>Каналы связи. Токен бота значением не хранится: только ссылка на секрет.</summary>
public sealed record ChannelOptions
{
    /// <summary>Канал Telegram включён.</summary>
    public bool TelegramEnabled { get; init; } = true;

    /// <summary>Ключ секрета с токеном бота в ISecretStore, не сам токен.</summary>
    public string TelegramBotTokenSecretKey { get; init; } = SecretKeys.TelegramBotToken;

    /// <summary>Канал Desktop включён; секретов не требует.</summary>
    public bool DesktopEnabled { get; init; } = true;

    /// <summary>Канал CLI включён; секретов не требует.</summary>
    public bool CliEnabled { get; init; } = true;
}

/// <summary>Идентификаторы владельца по каналам. Белый список и проверка — TASK-073.</summary>
public sealed record OwnerOptions
{
    /// <summary>Идентификаторы владельца в Telegram.</summary>
    public IReadOnlyList<string> TelegramOwnerIds { get; init; } = [];

    /// <summary>Идентификатор владельца в Desktop; пусто — не задан.</summary>
    public string? DesktopOwnerId { get; init; }

    /// <summary>Идентификатор владельца в CLI; пусто — не задан.</summary>
    public string? CliOwnerId { get; init; }
}

/// <summary>
/// Целевые показатели производительности (Q11): это targets — цели, а не limits,
/// не ворота приёмки и не основание останавливать старт при недостижении.
/// </summary>
public sealed record PerformanceTargetsOptions
{
    /// <summary>Целевое время ответа Tier0 в миллисекундах.</summary>
    public int Tier0TargetMilliseconds { get; init; } = 1;

    /// <summary>Целевое время реакции на остановку в миллисекундах.</summary>
    public int StopTargetMilliseconds { get; init; } = 50;
}

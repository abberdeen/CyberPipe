namespace CyberPipe.Contracts;

/// <summary>
/// Дескриптор модели каталога: размещение, провайдер, контекст и цена.
/// Каталоги и установка — TASK-080/TASK-081; потребители — TASK-031..TASK-033, TASK-082.
/// </summary>
public sealed record ModelDescriptorDto
{
    /// <summary>Стабильный строковый идентификатор записи каталога.</summary>
    public required string Id { get; init; }

    /// <summary>Имя модели для отображения.</summary>
    public required string Name { get; init; }

    /// <summary>Размещение модели: локально или в облаке.</summary>
    public required ModelHostingType HostingType { get; init; }

    /// <summary>Категория задач, для которой рекомендуется модель.</summary>
    public required ModelTaskCategory RecommendedFor { get; init; }

    /// <summary>Провайдер модели или среда исполнения для локальных моделей.</summary>
    public required string Provider { get; init; }

    /// <summary>Идентификатор модели у провайдера или в среде исполнения.</summary>
    public required string ModelId { get; init; }

    /// <summary>Краткое описание параметров модели.</summary>
    public required string ParamsSummary { get; init; }

    /// <summary>Полное окно контекста в токенах.</summary>
    public int ContextWindowTokens { get; init; }

    /// <summary>Безопасное окно контекста при запуске в токенах.</summary>
    public int DefaultContextTokens { get; init; }

    /// <summary>Размер квантованной модели в гигабайтах; только для локальных моделей.</summary>
    public double? Q4SizeGb { get; init; }

    /// <summary>Минимум оперативной памяти в гигабайтах; только для локальных моделей.</summary>
    public double? MinRamGb { get; init; }

    /// <summary>Цена входных токенов за миллион в долларах США; только для облачных моделей.</summary>
    public decimal? InputPricePer1mUsd { get; init; }

    /// <summary>Цена выходных токенов за миллион в долларах США; только для облачных моделей.</summary>
    public decimal? OutputPricePer1mUsd { get; init; }

    /// <summary>Лицензия или условия цены.</summary>
    public required string LicenseOrPricing { get; init; }

    /// <summary>Для каких задач модель подходит лучше всего.</summary>
    public required string BestFor { get; init; }
}

/// <summary>
/// Назначение модели конкретному ярусу маршрутизации. Выбор хранит область данных;
/// потребители — TASK-003 (конфигурация), TASK-031..TASK-033, TASK-080.
/// </summary>
public sealed record TierModelAssignment
{
    /// <summary>Ярус маршрутизации.</summary>
    public required IntentTier Tier { get; init; }

    /// <summary>Идентификатор назначенной модели из каталога TASK-080.</summary>
    public required string ModelId { get; init; }

    /// <summary>Размещение назначенной модели; может отсутствовать при разрешении по каталогу.</summary>
    public ModelHostingType? HostingType { get; init; }
}

/// <summary>
/// Вид локального рантайма моделей. Ollama реализован; остальные — задел под отдельные окна.
/// </summary>
public enum LocalRuntimeKind
{
    /// <summary>Локальный сервер Ollama.</summary>
    Ollama = 0,

    /// <summary>Локальный сервер LM Studio.</summary>
    LmStudio = 1,

    /// <summary>Сервер llama.cpp.</summary>
    LlamaCpp = 2,

    /// <summary>Произвольный локальный OpenAI-совместимый сервер.</summary>
    CustomOpenAiLocal = 3,
}

/// <summary>
/// Описание локального рантайма для карточки источника на экране моделей.
/// </summary>
public sealed record LocalRuntimeDescriptorDto
{
    /// <summary>Стабильный идентификатор рантайма.</summary>
    public required string Id { get; init; }

    /// <summary>Имя для отображения.</summary>
    public required string Name { get; init; }

    /// <summary>Вид рантайма.</summary>
    public required LocalRuntimeKind Kind { get; init; }

    /// <summary>Базовый адрес локального API.</summary>
    public required string ApiBaseUrl { get; init; }

    /// <summary>Рантайм умеет скачивать модели по тегу.</summary>
    public bool SupportsModelPull { get; init; }

    /// <summary>Рантайм умеет выгружать модель из памяти.</summary>
    public bool SupportsMemoryUnload { get; init; }

    /// <summary>Окно управления уже реализовано в приложении.</summary>
    public bool IsImplemented { get; init; } = true;
}

/// <summary>
/// Протокол удалённого провайдера моделей.
/// </summary>
public enum RemoteProviderProtocol
{
    /// <summary>OpenAI-совместимый Chat Completions (<c>/v1/chat/completions</c>, <c>/v1/models</c>).</summary>
    OpenAiCompatible = 0,

    /// <summary>Anthropic Messages API.</summary>
    Anthropic = 1,

    /// <summary>Google Gemini generateContent API.</summary>
    GoogleGemini = 2,
}

/// <summary>
/// Модель, доступная у динамического облачного провайдера.
/// </summary>
public sealed record RemoteModelEntryDto
{
    /// <summary>Идентификатор модели у провайдера.</summary>
    public required string ModelId { get; init; }

    /// <summary>Имя для отображения.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Рекомендуемая роль модели.</summary>
    public ModelTaskCategory RecommendedFor { get; init; } = ModelTaskCategory.HeavyReasoningTier3;

    /// <summary>Показывать модель в списках выбора на главном экране.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Окно контекста в токенах.</summary>
    public int ContextWindowTokens { get; init; } = 128000;

    /// <summary>Цена входных токенов за миллион в долларах США.</summary>
    public decimal? InputPricePer1mUsd { get; init; }

    /// <summary>Цена выходных токенов за миллион в долларах США.</summary>
    public decimal? OutputPricePer1mUsd { get; init; }

    /// <summary>Краткое описание.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Профиль динамического облачного провайдера. API-ключ хранится только в <see cref="ISecretStore"/>.
/// </summary>
public sealed record RemoteProviderProfileDto
{
    /// <summary>Стабильный идентификатор профиля (например, <c>openai</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Имя для отображения.</summary>
    public required string Name { get; init; }

    /// <summary>Протокол API.</summary>
    public required RemoteProviderProtocol Protocol { get; init; }

    /// <summary>Базовый адрес API без завершающего слэша.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>Имя секрета API-ключа в <see cref="ISecretStore"/>.</summary>
    public required string ApiKeySecretName { get; init; }

    /// <summary>Провайдер включён и его модели попадают в списки выбора.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Модели провайдера, выбранные или обнаруженные пользователем.</summary>
    public IReadOnlyList<RemoteModelEntryDto> Models { get; init; } = [];
}

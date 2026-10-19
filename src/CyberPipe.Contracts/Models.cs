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

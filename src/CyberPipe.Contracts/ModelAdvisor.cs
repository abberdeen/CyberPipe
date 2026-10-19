namespace CyberPipe.Contracts;

/// <summary>
/// Снимок возможностей ПК владельца для подбора локальной модели (TASK-082).
/// Неизвестные на данной ОС/конфигурации характеристики — null, а не ошибка:
/// проба не падает, советник трактует неизвестное консервативно.
/// </summary>
public sealed record HostHardwareProfile
{
    /// <summary>Общая RAM хоста в ГБ; null — определить не удалось.</summary>
    public double? TotalRamGb { get; init; }

    /// <summary>Свободная RAM хоста в ГБ; null — определить не удалось.</summary>
    public double? AvailableRamGb { get; init; }

    /// <summary>Число логических ядер CPU; null — определить не удалось.</summary>
    public int? CpuLogicalCores { get; init; }

    /// <summary>Поддержка AVX2; null — неизвестно (например, не-x86 архитектура).</summary>
    public bool? SupportsAvx2 { get; init; }

    /// <summary>Модель GPU; null — GPU нет или определить не удалось.</summary>
    public string? GpuModelName { get; init; }

    /// <summary>Выделенная VRAM в ГБ; null — неизвестно, 0 — GPU без своей памяти.</summary>
    public double? DedicatedVramGb { get; init; }

    /// <summary>Свободное место на диске каталога данных приложения в ГБ; null — неизвестно.</summary>
    public double? FreeDiskSpaceGb { get; init; }

    /// <summary>Полностью пустой профиль: данные железа недоступны.</summary>
    public static HostHardwareProfile Empty => new();
}

/// <summary>
/// Вердикт совместимости локальной модели с ПК владельца (TASK-082).
/// Вердикт — подсказка для UI, а не запрет установки.
/// </summary>
public enum HardwareFitVerdict
{
    /// <summary>Оптимально: требуемая память с запасом покрывается железом.</summary>
    Optimal = 0,

    /// <summary>Совместимо: запустится, но возможна заметная нагрузка на CPU/RAM.</summary>
    Compatible = 1,

    /// <summary>Недостаточно ресурсов: риск свопинга и фризов ПК.</summary>
    InsufficientResources = 2,
}

/// <summary>
/// Оценка совместимости одной локальной модели с ПК владельца (TASK-082).
/// </summary>
public sealed record ModelCompatibilityEvaluation
{
    /// <summary>Оцениваемая модель из каталога TASK-080.</summary>
    public required ModelDescriptorDto Model { get; init; }

    /// <summary>Вердикт совместимости.</summary>
    public required HardwareFitVerdict Verdict { get; init; }

    /// <summary>Требуемая память в ГБ с учётом весов Q4, KV-Cache и буферов рантайма.</summary>
    public required double RequiredMemoryGb { get; init; }

    /// <summary>Человекочитаемое объяснение для UI: требуемый и доступный объёмы и причина вердикта.</summary>
    public required string Explanation { get; init; }
}

/// <summary>
/// Проба железа ПК владельца (TASK-082). Не требует прав администратора,
/// не ходит в сеть; снимок кэшируется на время жизни процесса.
/// </summary>
public interface IHardwareCapabilitiesProbe
{
    /// <summary>Возвращает снимок возможностей ПК (закэшированный, если есть).</summary>
    Task<HostHardwareProfile> ProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>Сбрасывает кэш, следующий снимок снимается заново.</summary>
    void InvalidateCache();
}

/// <summary>
/// Советник совместимости локальных моделей с ПК владельца (TASK-082).
/// </summary>
public interface IModelCompatibilityAdvisor
{
    /// <summary>
    /// Оценивает все локальные модели каталога TASK-080 для текущего ПК:
    /// сортировка Optimal → Compatible → InsufficientResources, внутри группы —
    /// по возрастанию требуемой памяти; фильтр по категории Tier 2 / Tier 3.
    /// </summary>
    Task<IReadOnlyList<ModelCompatibilityEvaluation>> EvaluateLocalModelsAsync(
        ModelTaskCategory? filterByCategory = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает облачные модели каталога (8 записей) с фильтром по категории —
    /// без вердиктов и без пробы железа.
    /// </summary>
    IReadOnlyList<ModelDescriptorDto> GetCloudModels(ModelTaskCategory? filterByCategory = null);

    /// <summary>Возвращает снимок железа, на котором построена последняя оценка.</summary>
    Task<HostHardwareProfile> GetHardwareProfileAsync(CancellationToken cancellationToken = default);
}

namespace CyberPipe.Contracts;

/// <summary>
/// Политика повторов шага цепочки. Потребители: TASK-040 (определение), TASK-044 (исполнение).
/// </summary>
public sealed record ChainRetryPolicyDto
{
    /// <summary>Максимальное число попыток шага.</summary>
    public int MaxAttempts { get; init; } = 1;

    /// <summary>Пауза между попытками в миллисекундах.</summary>
    public int BackoffMs { get; init; }
}

/// <summary>
/// Политика пропуска запуска по расписанию. Потребители: TASK-040, TASK-045.
/// </summary>
public enum ChainMisfirePolicy
{
    /// <summary>Пропустить пропущенный запуск.</summary>
    DoNothing = 0,

    /// <summary>Выполнить один запуск сразу.</summary>
    FireOnceNow = 1,

    /// <summary>Игнорировать пропуски и следовать расписанию.</summary>
    IgnoreMisfires = 2,
}

/// <summary>
/// Секция триггера определения цепочки: ровно один вид из <see cref="ChainTriggerKind" />.
/// Потребитель: TASK-040, TASK-041.
/// </summary>
public sealed record ChainTriggerDto
{
    /// <summary>Вид триггера.</summary>
    public required ChainTriggerKind Kind { get; init; }

    /// <summary>Путь вебхука для вида Webhook; иначе отсутствует.</summary>
    public string? WebhookPath { get; init; }

    /// <summary>Cron-выражение для вида Cron; иначе отсутствует.</summary>
    public string? CronExpression { get; init; }

    /// <summary>Имя функции для вида FunctionCall; иначе отсутствует.</summary>
    public string? FunctionName { get; init; }

    /// <summary>Фильтр чата или команды для вида Chat; иначе отсутствует.</summary>
    public string? ChatFilter { get; init; }

    /// <summary>Дополнительные параметры триггера.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>();

    /// <inheritdoc />
    public bool Equals(ChainTriggerDto? other) =>
        other is not null
        && Kind == other.Kind
        && WebhookPath == other.WebhookPath
        && CronExpression == other.CronExpression
        && FunctionName == other.FunctionName
        && ChatFilter == other.ChatFilter
        && CollectionEquality.DictionaryEquals(Parameters, other.Parameters);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            Kind,
            WebhookPath,
            CronExpression,
            FunctionName,
            ChatFilter,
            CollectionEquality.DictionaryHash(Parameters));
}

/// <summary>
/// Секция навыка определения цепочки. Потребитель: TASK-040, TASK-044.
/// </summary>
public sealed record ChainSkillStepDto
{
    /// <summary>Идентификатор зарегистрированного навыка.</summary>
    public required string SkillId { get; init; }

    /// <summary>Отображение входов навыка: ключ — шаблон значения.</summary>
    public IReadOnlyDictionary<string, string> InputMapping { get; init; } =
        new Dictionary<string, string>();

    /// <inheritdoc />
    public bool Equals(ChainSkillStepDto? other) =>
        other is not null
        && SkillId == other.SkillId
        && CollectionEquality.DictionaryEquals(InputMapping, other.InputMapping);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(SkillId, CollectionEquality.DictionaryHash(InputMapping));
}

/// <summary>
/// Секция скриптового пакета определения цепочки. Потребитель: TASK-040, TASK-042, TASK-044.
/// </summary>
public sealed record ChainScriptStepDto
{
    /// <summary>Слаг пакета скрипта.</summary>
    public required string PackageSlug { get; init; }

    /// <summary>Версия пакета скрипта.</summary>
    public required string PackageVersion { get; init; }

    /// <summary>Отображение входов скрипта: ключ — шаблон значения.</summary>
    public IReadOnlyDictionary<string, string> InputMapping { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Привязки файловых входов скрипта по псевдонимам.</summary>
    public IReadOnlyDictionary<string, string> FileInputBindings { get; init; } =
        new Dictionary<string, string>();

    /// <inheritdoc />
    public bool Equals(ChainScriptStepDto? other) =>
        other is not null
        && PackageSlug == other.PackageSlug
        && PackageVersion == other.PackageVersion
        && CollectionEquality.DictionaryEquals(InputMapping, other.InputMapping)
        && CollectionEquality.DictionaryEquals(FileInputBindings, other.FileInputBindings);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            PackageSlug,
            PackageVersion,
            CollectionEquality.DictionaryHash(InputMapping),
            CollectionEquality.DictionaryHash(FileInputBindings));
}

/// <summary>
/// Секция доставки определения цепочки: логический канал, а не конкретная реализация.
/// Потребитель: TASK-040, TASK-044.
/// </summary>
public sealed record ChainDeliveryStepDto
{
    /// <summary>Логический канал доставки.</summary>
    public required ChannelKind Channel { get; init; }

    /// <summary>Адресат доставки в терминах канала.</summary>
    public required string Target { get; init; }

    /// <summary>Шаблон текста сообщения доставки.</summary>
    public required string MessageTemplate { get; init; }

    /// <summary>Признак отправки файлов результата вместе с сообщением.</summary>
    public bool SendFiles { get; init; }

    /// <inheritdoc />
    public bool Equals(ChainDeliveryStepDto? other) =>
        other is not null
        && Channel == other.Channel
        && Target == other.Target
        && MessageTemplate == other.MessageTemplate
        && SendFiles == other.SendFiles;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Channel, Target, MessageTemplate, SendFiles);
}

/// <summary>
/// Определение цепочки строго вида Trigger → Skill → Script → Delivery.
/// Валидация формата — TASK-040; исполнение — TASK-044.
/// </summary>
public sealed record ChainDefinitionDto
{
    /// <summary>Идентификатор цепочки.</summary>
    public required ChainId Id { get; init; }

    /// <summary>Имя цепочки.</summary>
    public required string Name { get; init; }

    /// <summary>Признак включённой цепочки; отключённая валидна, но не запускается.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Шаг триггера.</summary>
    public required ChainTriggerDto Trigger { get; init; }

    /// <summary>Шаг навыка.</summary>
    public required ChainSkillStepDto Skill { get; init; }

    /// <summary>Шаг скрипта.</summary>
    public required ChainScriptStepDto Script { get; init; }

    /// <summary>Шаг доставки.</summary>
    public required ChainDeliveryStepDto Delivery { get; init; }

    /// <summary>Признак обязательного HITL-подтверждения перед доставкой.</summary>
    public bool RequiresHitl { get; init; }

    /// <summary>Политика повторов шагов.</summary>
    public ChainRetryPolicyDto RetryPolicy { get; init; } = new();

    /// <summary>Политика пропусков расписания.</summary>
    public ChainMisfirePolicy MisfirePolicy { get; init; } = ChainMisfirePolicy.DoNothing;

    /// <summary>Версия определения; правка включённой цепочки её увеличивает.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Момент создания (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Момент последнего изменения (UTC).</summary>
    public required DateTimeOffset UpdatedAtUtc { get; init; }

    /// <inheritdoc />
    public bool Equals(ChainDefinitionDto? other) =>
        other is not null
        && Id == other.Id
        && Name == other.Name
        && IsEnabled == other.IsEnabled
        && Trigger == other.Trigger
        && Skill == other.Skill
        && Script == other.Script
        && Delivery == other.Delivery
        && RequiresHitl == other.RequiresHitl
        && RetryPolicy == other.RetryPolicy
        && MisfirePolicy == other.MisfirePolicy
        && Version == other.Version
        && CreatedAtUtc == other.CreatedAtUtc
        && UpdatedAtUtc == other.UpdatedAtUtc;

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            Id,
            Name,
            IsEnabled,
            Trigger,
            Skill,
            Script,
            Delivery,
            HashCode.Combine(RequiresHitl, RetryPolicy, MisfirePolicy, Version, CreatedAtUtc, UpdatedAtUtc));
}

/// <summary>
/// Стандартные коды ошибок валидации определения цепочки (TASK-040).
/// </summary>
public static class ChainErrorCodes
{
    /// <summary>Отсутствует обязательный шаг цепочки или его ключевое свойство.</summary>
    public const string MissingStep = "ChainDefinition.MissingStep";

    /// <summary>Неизвестный или неподдерживаемый вид триггера.</summary>
    public const string UnknownTriggerKind = "ChainDefinition.UnknownTriggerKind";

    /// <summary>Обнаружены конструкции ветвления, параллелизма, условий-переходов, LLM-шагов, графов или вторичных скриптов.</summary>
    public const string BranchingNotSupported = "ChainDefinition.BranchingNotSupported";

    /// <summary>Некорректный слаг пакета скрипта (требуется kebab-case).</summary>
    public const string InvalidSlug = "ChainDefinition.InvalidSlug";

    /// <summary>Некорректная версия пакета скрипта (требуется SemVer).</summary>
    public const string InvalidVersion = "ChainDefinition.InvalidVersion";

    /// <summary>Ссылка на неизвестный источник данных в шаблоне подстановки (разрешены только trigger, skill, script).</summary>
    public const string UnknownMappingSource = "ChainDefinition.UnknownMappingSource";

    /// <summary>Неизвестное поле верхнего уровня в определении цепочки.</summary>
    public const string UnknownProperty = "ChainDefinition.UnknownProperty";
}

/// <summary>
/// Ошибка валидации определения цепочки (TASK-040).
/// </summary>
public sealed record ChainValidationError
{
    /// <summary>Код ошибки из <see cref="ChainErrorCodes" />.</summary>
    public required string Code { get; init; }

    /// <summary>Путь свойства или шага, вызвавшего ошибку.</summary>
    public required string Path { get; init; }

    /// <summary>Человекочитаемое описание ошибки.</summary>
    public required string Message { get; init; }
}

/// <summary>
/// Результат валидации определения цепочки (TASK-040).
/// </summary>
public sealed record ChainValidationResult
{
    /// <summary>Признак успешной валидации (ошибок нет).</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>Список обнаруженных ошибок.</summary>
    public IReadOnlyList<ChainValidationError> Errors { get; init; } = Array.Empty<ChainValidationError>();

    /// <summary>Успешный результат валидации.</summary>
    public static ChainValidationResult Success { get; } = new();

    /// <summary>Создаёт результат валидации с ошибками.</summary>
    public static ChainValidationResult Failure(params ChainValidationError[] errors) =>
        new() { Errors = errors };

    /// <summary>Создаёт результат валидации с ошибками.</summary>
    public static ChainValidationResult Failure(IEnumerable<ChainValidationError> errors) =>
        new() { Errors = errors.ToArray() };
}

/// <summary>
/// Исключение, выбрасываемое при обнаружении ошибок валидации цепочки (TASK-040).
/// </summary>
public sealed class ChainValidationException : Exception
{
    /// <summary>Ошибки валидации.</summary>
    public IReadOnlyList<ChainValidationError> Errors { get; }

    /// <summary>Инициализирует исключение с коллекцией ошибок.</summary>
    public ChainValidationException(IReadOnlyList<ChainValidationError> errors)
        : base($"Chain definition validation failed with {errors.Count} error(s): {string.Join("; ", errors.Select(e => $"[{e.Code}] {e.Path}: {e.Message}"))}")
    {
        Errors = errors ?? Array.Empty<ChainValidationError>();
    }

    /// <summary>Инициализирует исключение с одной ошибкой.</summary>
    public ChainValidationException(string code, string path, string message)
        : this(new[] { new ChainValidationError { Code = code, Path = path, Message = message } })
    {
    }
}

/// <summary>
/// Валидатор определений цепочек (TASK-040).
/// Проверяет строго линейную форму Trigger → Skill → Script → Delivery и отклоняет ветвления.
/// </summary>
public interface IChainDefinitionValidator
{
    /// <summary>Валидирует объектное определение цепочки.</summary>
    ChainValidationResult Validate(ChainDefinitionDto definition);

    /// <summary>Валидирует строковое JSON-определение цепочки, включая проверку неизвестных свойств и запрещённых структур ветвления.</summary>
    ChainValidationResult ValidateJson(string json);
}

/// <summary>
/// Репозиторий определений цепочек для загрузки и управления жизненным циклом (TASK-040).
/// </summary>
public interface IChainDefinitionRepository
{
    /// <summary>Возвращает определение цепочки по идентификатору или null, если не найдено.</summary>
    Task<ChainDefinitionDto?> GetByIdAsync(ChainId id, CancellationToken cancellationToken = default);

    /// <summary>Возвращает список всех активных (включённых) цепочек.</summary>
    Task<IReadOnlyList<ChainDefinitionDto>> ListEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>Сохраняет или обновляет определение цепочки. При обновлении включённой цепочки версия инкрементируется.</summary>
    Task<ChainDefinitionDto> UpsertAsync(ChainDefinitionDto definition, CancellationToken cancellationToken = default);

    /// <summary>Устанавливает флаг активности цепочки.</summary>
    Task<bool> SetEnabledAsync(ChainId id, bool isEnabled, CancellationToken cancellationToken = default);
}

/// <summary>
/// Загрузчик определений цепочек из конфигурации и репозитория (TASK-040).
/// Слияние источников запрещено — один источник на вызов.
/// </summary>
public interface IChainDefinitionLoader
{
    /// <summary>Загружает и валидирует определение цепочки из JSON-строки конфигурации.</summary>
    Task<ChainDefinitionDto> LoadFromConfigAsync(string json, CancellationToken cancellationToken = default);

    /// <summary>Загружает и валидирует определение цепочки из файла конфигурации.</summary>
    Task<ChainDefinitionDto> LoadFromConfigFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Загружает и валидирует определение цепочки из репозитория по идентификатору.</summary>
    Task<ChainDefinitionDto?> LoadFromRepositoryAsync(ChainId id, CancellationToken cancellationToken = default);
}


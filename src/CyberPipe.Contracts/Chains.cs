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

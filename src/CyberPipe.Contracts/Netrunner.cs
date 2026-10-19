using System.Text.Json.Serialization;

namespace CyberPipe.Contracts;

/// <summary>
/// Секция runtime манифеста скриптового пакета. JSON-имена полей — <c>runtime.language</c> и далее.
/// Валидация — TASK-042; исполнение — TASK-043.
/// </summary>
public sealed record NetrunnerRuntimeDto
{
    /// <summary>Язык скрипта (<c>runtime.language</c>).</summary>
    [JsonPropertyName("language")]
    public required string Language { get; init; }

    /// <summary>Версия языка (<c>runtime.languageVersion</c>).</summary>
    [JsonPropertyName("languageVersion")]
    public required string LanguageVersion { get; init; }

    /// <summary>Среда исполнения (<c>runtime.environment</c>).</summary>
    [JsonPropertyName("environment")]
    public required string Environment { get; init; }

    /// <summary>Версия среды исполнения (<c>runtime.environmentVersion</c>).</summary>
    [JsonPropertyName("environmentVersion")]
    public required string EnvironmentVersion { get; init; }

    /// <summary>Файл точки входа; значение начинается с <c>skill-hack.</c> (<c>runtime.entrypoint</c>).</summary>
    [JsonPropertyName("entrypoint")]
    public required string Entrypoint { get; init; }

    /// <summary>Тайм-аут исполнения в миллисекундах.</summary>
    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; init; }

    /// <summary>Лимит памяти в мегабайтах; жёсткого значения по умолчанию нет.</summary>
    [JsonPropertyName("maxMemoryMb")]
    public int? MaxMemoryMb { get; init; }

    /// <summary>Признак детерминированного исполнения.</summary>
    [JsonPropertyName("deterministic")]
    public bool Deterministic { get; init; }
}

/// <summary>
/// Файловый вход скриптового пакета. Потребитель: TASK-042, TASK-043.
/// </summary>
public sealed record NetrunnerInputFileDto
{
    /// <summary>Псевдоним файла.</summary>
    [JsonPropertyName("alias")]
    public required string Alias { get; init; }

    /// <summary>Признак обязательного файла.</summary>
    [JsonPropertyName("required")]
    public bool Required { get; init; }

    /// <summary>Допустимые расширения файла.</summary>
    [JsonPropertyName("allowedExtensions")]
    public IReadOnlyList<string> AllowedExtensions { get; init; } = [];

    /// <summary>Допустимые медиатипы файла.</summary>
    [JsonPropertyName("mimeTypes")]
    public IReadOnlyList<string> MimeTypes { get; init; } = [];

    /// <summary>Максимальный размер файла в байтах.</summary>
    [JsonPropertyName("maxSizeBytes")]
    public long MaxSizeBytes { get; init; }

    /// <summary>Описание файла; может отсутствовать.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <inheritdoc />
    public bool Equals(NetrunnerInputFileDto? other) =>
        other is not null
        && Alias == other.Alias
        && Required == other.Required
        && CollectionEquality.ListEquals(AllowedExtensions, other.AllowedExtensions)
        && CollectionEquality.ListEquals(MimeTypes, other.MimeTypes)
        && MaxSizeBytes == other.MaxSizeBytes
        && Description == other.Description;

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            Alias,
            Required,
            CollectionEquality.ListHash(AllowedExtensions),
            CollectionEquality.ListHash(MimeTypes),
            MaxSizeBytes,
            Description);
}

/// <summary>
/// Файловый выход скриптового пакета. Потребитель: TASK-042, TASK-043.
/// </summary>
public sealed record NetrunnerOutputFileDto
{
    /// <summary>Псевдоним файла.</summary>
    [JsonPropertyName("alias")]
    public required string Alias { get; init; }

    /// <summary>Шаблон имени выходного файла.</summary>
    [JsonPropertyName("filenamePattern")]
    public required string FilenamePattern { get; init; }

    /// <summary>Медиатип выходного файла.</summary>
    [JsonPropertyName("mimeType")]
    public required string MimeType { get; init; }

    /// <summary>Признак обязательного файла.</summary>
    [JsonPropertyName("required")]
    public bool Required { get; init; }

    /// <summary>Максимальный размер файла в байтах.</summary>
    [JsonPropertyName("maxSizeBytes")]
    public long MaxSizeBytes { get; init; }

    /// <summary>Описание файла; может отсутствовать.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <inheritdoc />
    public bool Equals(NetrunnerOutputFileDto? other) =>
        other is not null
        && Alias == other.Alias
        && FilenamePattern == other.FilenamePattern
        && MimeType == other.MimeType
        && Required == other.Required
        && MaxSizeBytes == other.MaxSizeBytes
        && Description == other.Description;

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Alias, FilenamePattern, MimeType, Required, MaxSizeBytes, Description);
}

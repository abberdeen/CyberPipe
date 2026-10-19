using System.Text.Json.Serialization;

namespace CyberPipe.Contracts;

/// <summary>
/// Секция ввода-вывода манифеста скриптового пакета. Потребитель: TASK-042, TASK-043.
/// </summary>
public sealed record NetrunnerIoDto
{
    /// <summary>Файл JSON-схемы входа.</summary>
    [JsonPropertyName("inputSchemaFile")]
    public required string InputSchemaFile { get; init; }

    /// <summary>Файл JSON-схемы выхода.</summary>
    [JsonPropertyName("outputSchemaFile")]
    public required string OutputSchemaFile { get; init; }

    /// <summary>Форматы входных данных.</summary>
    [JsonPropertyName("inputFormats")]
    public IReadOnlyList<string> InputFormats { get; init; } = [];

    /// <summary>Форматы выходных данных.</summary>
    [JsonPropertyName("outputFormats")]
    public IReadOnlyList<string> OutputFormats { get; init; } = [];

    /// <summary>Файловые входы пакета.</summary>
    [JsonPropertyName("inputFiles")]
    public IReadOnlyList<NetrunnerInputFileDto> InputFiles { get; init; } = [];

    /// <summary>Файловые выходы пакета.</summary>
    [JsonPropertyName("outputFiles")]
    public IReadOnlyList<NetrunnerOutputFileDto> OutputFiles { get; init; } = [];

    /// <inheritdoc />
    public bool Equals(NetrunnerIoDto? other) =>
        other is not null
        && InputSchemaFile == other.InputSchemaFile
        && OutputSchemaFile == other.OutputSchemaFile
        && CollectionEquality.ListEquals(InputFormats, other.InputFormats)
        && CollectionEquality.ListEquals(OutputFormats, other.OutputFormats)
        && CollectionEquality.ListEquals(InputFiles, other.InputFiles)
        && CollectionEquality.ListEquals(OutputFiles, other.OutputFiles);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            InputSchemaFile,
            OutputSchemaFile,
            CollectionEquality.ListHash(InputFormats),
            CollectionEquality.ListHash(OutputFormats),
            CollectionEquality.ListHash(InputFiles),
            CollectionEquality.ListHash(OutputFiles));
}

/// <summary>
/// Секция разрешений манифеста скриптового пакета. Потребитель: TASK-042, TASK-043, TASK-023.
/// </summary>
public sealed record NetrunnerPermissionsDto
{
    /// <summary>Признак разрешённого сетевого доступа; по умолчанию запрещён.</summary>
    [JsonPropertyName("allowNetwork")]
    public bool AllowNetwork { get; init; }

    /// <summary>Разрешённые домены при включённой сети.</summary>
    [JsonPropertyName("allowedDomains")]
    public IReadOnlyList<string> AllowedDomains { get; init; } = [];

    /// <summary>Область файловой системы; допустима только изолированная рабочая область запуска.</summary>
    [JsonPropertyName("filesystemScope")]
    public required string FilesystemScope { get; init; }

    /// <summary>Ссылки на требуемые секреты хранилища, без самих значений.</summary>
    [JsonPropertyName("requiredVaultSecretRefs")]
    public IReadOnlyList<string> RequiredVaultSecretRefs { get; init; } = [];

    /// <inheritdoc />
    public bool Equals(NetrunnerPermissionsDto? other) =>
        other is not null
        && AllowNetwork == other.AllowNetwork
        && CollectionEquality.ListEquals(AllowedDomains, other.AllowedDomains)
        && FilesystemScope == other.FilesystemScope
        && CollectionEquality.ListEquals(RequiredVaultSecretRefs, other.RequiredVaultSecretRefs);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            AllowNetwork,
            CollectionEquality.ListHash(AllowedDomains),
            FilesystemScope,
            CollectionEquality.ListHash(RequiredVaultSecretRefs));
}

/// <summary>
/// Секция целостности манифеста: SHA-256 файлов пакета. Потребитель: TASK-042.
/// </summary>
public sealed record NetrunnerIntegrityDto
{
    /// <summary>Хэши SHA-256 по именам файлов пакета.</summary>
    [JsonPropertyName("filesSha256")]
    public IReadOnlyDictionary<string, string> FilesSha256 { get; init; } =
        new Dictionary<string, string>();

    /// <inheritdoc />
    public bool Equals(NetrunnerIntegrityDto? other) =>
        other is not null && CollectionEquality.DictionaryEquals(FilesSha256, other.FilesSha256);

    /// <inheritdoc />
    public override int GetHashCode() => CollectionEquality.DictionaryHash(FilesSha256);
}

/// <summary>
/// Манифест скриптового пакета как тип: <c>netrunner.manifest.json</c>.
/// Обязательные поля runtime: language, languageVersion, environment, environmentVersion, entrypoint
/// (файл <c>skill-hack.*</c>). Валидация — TASK-042; исполнение — TASK-043; потребитель — TASK-044.
/// </summary>
public sealed record NetrunnerManifestDto
{
    /// <summary>Слаг пакета в kebab-case.</summary>
    [JsonPropertyName("slug")]
    public required string Slug { get; init; }

    /// <summary>Имя пакета.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Версия пакета в SemVer.</summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>Описание пакета.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>Секция среды исполнения.</summary>
    [JsonPropertyName("runtime")]
    public required NetrunnerRuntimeDto Runtime { get; init; }

    /// <summary>Секция ввода-вывода.</summary>
    [JsonPropertyName("io")]
    public required NetrunnerIoDto Io { get; init; }

    /// <summary>Секция разрешений.</summary>
    [JsonPropertyName("permissions")]
    public required NetrunnerPermissionsDto Permissions { get; init; }

    /// <summary>Секция целостности файлов.</summary>
    [JsonPropertyName("integrity")]
    public required NetrunnerIntegrityDto Integrity { get; init; }

    /// <inheritdoc />
    public bool Equals(NetrunnerManifestDto? other) =>
        other is not null
        && Slug == other.Slug
        && Name == other.Name
        && Version == other.Version
        && Description == other.Description
        && Runtime == other.Runtime
        && Io == other.Io
        && Permissions == other.Permissions
        && Integrity == other.Integrity;

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Slug, Name, Version, Description, Runtime, Io, Permissions, Integrity);
}

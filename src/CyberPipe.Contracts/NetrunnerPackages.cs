namespace CyberPipe.Contracts;

/// <summary>
/// Стандартные коды ошибок валидации пакета скрипта Netrunner (TASK-042).
/// </summary>
public static class PackageErrorCodes
{
    /// <summary>Отсутствует один из обязательных файлов пакета.</summary>
    public const string MissingFile = "Package.MissingFile";

    /// <summary>Отсутствует обязательное поле секции runtime манифеста.</summary>
    public const string MissingRuntimeField = "Manifest.MissingRuntimeField";

    /// <summary>Точка входа не начинается с skill-hack.* или указан main.*.</summary>
    public const string EntrypointNotSkillHack = "Manifest.EntrypointNotSkillHack";

    /// <summary>Несовпадение слага, версии или имени каталога с манифестом.</summary>
    public const string IdentityMismatch = "Manifest.IdentityMismatch";

    /// <summary>Некорректная JSON-схема ввода или вывода.</summary>
    public const string InvalidSchema = "Package.InvalidSchema";

    /// <summary>Несовпадение контрольной суммы SHA-256 файла.</summary>
    public const string HashMismatch = "Package.HashMismatch";

    /// <summary>Файл на диске не перечислен в секции integrity манифеста.</summary>
    public const string UnlistedFile = "Package.UnlistedFile";

    /// <summary>Некорректная тестовая фикстура (отсутствует sampleInput или expectedOutput/invariants).</summary>
    public const string InvalidFixture = "Package.InvalidFixture";

    /// <summary>Недопустимое значение filesystemScope (разрешено только IsolatedRunWorkspaceOnly).</summary>
    public const string FilesystemScopeNotAllowed = "Manifest.FilesystemScopeNotAllowed";

    /// <summary>Версия пакета уже зарегистрирована с другим хэшем манифеста.</summary>
    public const string VersionAlreadyRegistered = "Package.VersionAlreadyRegistered";
}

/// <summary>
/// Ошибка валидации пакета скрипта (TASK-042).
/// </summary>
public sealed record PackageError
{
    /// <summary>Код ошибки из <see cref="PackageErrorCodes" />.</summary>
    public required string Code { get; init; }

    /// <summary>Файл или поле манифеста, связанное с ошибкой.</summary>
    public required string File { get; init; }

    /// <summary>Описание ошибки.</summary>
    public required string Message { get; init; }
}

/// <summary>
/// Результат валидации пакета скрипта Netrunner (TASK-042).
/// Все ошибки собираются в единый список (валидация не обрывается на первой ошибке).
/// </summary>
public sealed record PackageValidationResult
{
    /// <summary>Признак успешной валидации (ошибок нет).</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>Разобранный манифест при успешном чтении.</summary>
    public NetrunnerManifestDto? Manifest { get; init; }

    /// <summary>Список обнаруженных ошибок валидации.</summary>
    public IReadOnlyList<PackageError> Errors { get; init; } = [];

    /// <summary>Создаёт успешный результат валидации.</summary>
    public static PackageValidationResult Success(NetrunnerManifestDto manifest) =>
        new() { Manifest = manifest, Errors = [] };

    /// <summary>Создаёт результат валидации с ошибками.</summary>
    public static PackageValidationResult Failure(params PackageError[] errors) =>
        new() { Errors = errors };

    /// <summary>Создаёт результат валидации с ошибками.</summary>
    public static PackageValidationResult Failure(IEnumerable<PackageError> errors) =>
        new() { Errors = errors.ToArray() };
}

/// <summary>
/// Валидатор скриптовых пакетов Netrunner (TASK-042).
/// </summary>
public interface INetrunnerPackageValidator
{
    /// <summary>
    /// Валидирует пакет скрипта в указанном каталоге без его исполнения.
    /// </summary>
    /// <param name="packageDir">Путь к каталогу пакета.</param>
    /// <returns>Результат валидации.</returns>
    PackageValidationResult ValidatePackage(string packageDir);
}

/// <summary>
/// Запись о зарегистрированном пакете скрипта (TASK-042).
/// </summary>
public sealed record PackageRegistrationRecord
{
    /// <summary>Слаг пакета.</summary>
    public required string Slug { get; init; }

    /// <summary>Версия пакета.</summary>
    public required string Version { get; init; }

    /// <summary>Хэш SHA-256 файла манифеста.</summary>
    public required string ManifestHash { get; init; }

    /// <summary>Каталог пакета.</summary>
    public required string PackageDir { get; init; }

    /// <summary>Момент регистрации (UTC).</summary>
    public required DateTimeOffset RegisteredAtUtc { get; init; }

    /// <summary>Манифест пакета.</summary>
    public required NetrunnerManifestDto Manifest { get; init; }
}

/// <summary>
/// Репозиторий реестра пакетов скриптов (TASK-042, TASK-011).
/// </summary>
public interface IPackageRegistryRepository
{
    /// <summary>Возвращает зарегистрированный пакет по слагу и версии.</summary>
    Task<PackageRegistrationRecord?> GetPackageAsync(string slug, string version, CancellationToken cancellationToken = default);

    /// <summary>Регистрирует проверенный пакет скрипта.</summary>
    Task<PackageRegistrationRecord> RegisterPackageAsync(NetrunnerManifestDto manifest, string packageDir, string manifestHash, CancellationToken cancellationToken = default);
}

/// <summary>
/// Исключение при ошибке регистрации пакета скрипта (TASK-042).
/// </summary>
public sealed class PackageRegistrationException : Exception
{
    /// <summary>Код ошибки из <see cref="PackageErrorCodes" />.</summary>
    public string Code { get; }

    /// <summary>Создаёт исключение регистрации пакета.</summary>
    public PackageRegistrationException(string code, string message)
        : base($"[{code}] {message}")
    {
        Code = code;
    }
}

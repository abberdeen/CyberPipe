using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CyberPipe.Contracts;

namespace CyberPipe.Chains.Packages;

/// <summary>
/// Валидатор скриптовых пакетов Netrunner (TASK-042).
/// Проверяет наличие всех файлов, манифест, точку входа skill-hack.*, JSON-схемы,
/// контрольные суммы SHA-256 и фикстуру tests/sample.fixture.json без исполнения кода.
/// </summary>
public sealed partial class NetrunnerPackageValidator : INetrunnerPackageValidator
{
    private static readonly string[] MandatoryFiles =
    [
        "netrunner.manifest.json",
        "SKILL.md",
        "schema.input.json",
        "schema.output.json",
        "tests/sample.fixture.json",
    ];

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled)]
    private static partial Regex KebabCaseSlugRegex();

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$", RegexOptions.Compiled)]
    private static partial Regex SemVerRegex();

    /// <inheritdoc />
    public PackageValidationResult ValidatePackage(string packageDir)
    {
        if (string.IsNullOrWhiteSpace(packageDir) || !Directory.Exists(packageDir))
        {
            return PackageValidationResult.Failure(new PackageError
            {
                Code = PackageErrorCodes.MissingFile,
                File = packageDir ?? "$",
                Message = $"Package directory '{packageDir}' does not exist.",
            });
        }

        var errors = new List<PackageError>();
        var dirInfo = new DirectoryInfo(packageDir);

        // 1. Проверка наличия обязательных файлов
        foreach (string relativePath in MandatoryFiles)
        {
            string fullPath = Path.Combine(packageDir, relativePath);
            if (!File.Exists(fullPath))
            {
                errors.Add(new PackageError
                {
                    Code = PackageErrorCodes.MissingFile,
                    File = relativePath,
                    Message = $"Mandatory package file '{relativePath}' is missing.",
                });
            }
        }

        // 2. Чтение и валидация манифеста
        string manifestPath = Path.Combine(packageDir, "netrunner.manifest.json");
        NetrunnerManifestDto? manifest = null;

        if (File.Exists(manifestPath))
        {
            manifest = ValidateManifest(manifestPath, dirInfo, errors);
        }

        // 3. Валидация JSON-схем
        ValidateJsonSchemaFile(Path.Combine(packageDir, "schema.input.json"), "schema.input.json", errors);
        ValidateJsonSchemaFile(Path.Combine(packageDir, "schema.output.json"), "schema.output.json", errors);

        // 4. Валидация тестовой фикстуры tests/sample.fixture.json
        string fixturePath = Path.Combine(packageDir, "tests", "sample.fixture.json");
        if (File.Exists(fixturePath))
        {
            ValidateFixture(fixturePath, errors);
        }

        // 5. Валидация контрольных сумм SHA-256 и файлов на диске
        if (manifest?.Integrity?.FilesSha256 is not null)
        {
            ValidateIntegrity(packageDir, manifest.Integrity.FilesSha256, errors);
        }

        return errors.Count == 0 && manifest is not null
            ? PackageValidationResult.Success(manifest)
            : PackageValidationResult.Failure(errors);
    }

    private static NetrunnerManifestDto? ValidateManifest(string manifestPath, DirectoryInfo dirInfo, List<PackageError> errors)
    {
        string manifestJson;
        try
        {
            manifestJson = File.ReadAllText(manifestPath);
        }
        catch (Exception ex)
        {
            errors.Add(new PackageError
            {
                Code = PackageErrorCodes.MissingFile,
                File = "netrunner.manifest.json",
                Message = $"Cannot read manifest: {ex.Message}",
            });
            return null;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(manifestJson);
        }
        catch (JsonException ex)
        {
            errors.Add(new PackageError
            {
                Code = PackageErrorCodes.InvalidSchema,
                File = "netrunner.manifest.json",
                Message = $"Malformed JSON in manifest: {ex.Message}",
            });
            return null;
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new PackageError
                {
                    Code = PackageErrorCodes.InvalidSchema,
                    File = "netrunner.manifest.json",
                    Message = "Root of manifest must be an object.",
                });
                return null;
            }

            // Проверка обязательных полей runtime
            if (!root.TryGetProperty("runtime", out JsonElement runtimeElement) || runtimeElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new PackageError
                {
                    Code = PackageErrorCodes.MissingRuntimeField,
                    File = "runtime",
                    Message = "Runtime section is missing or not an object.",
                });
            }
            else
            {
                CheckRuntimeField(runtimeElement, "language", errors);
                CheckRuntimeField(runtimeElement, "languageVersion", errors);
                CheckRuntimeField(runtimeElement, "environment", errors);
                CheckRuntimeField(runtimeElement, "environmentVersion", errors);
                CheckRuntimeField(runtimeElement, "entrypoint", errors);

                if (runtimeElement.TryGetProperty("entrypoint", out JsonElement epElement) && epElement.ValueKind == JsonValueKind.String)
                {
                    string? ep = epElement.GetString();
                    if (!string.IsNullOrWhiteSpace(ep))
                    {
                        string epFileName = Path.GetFileName(ep);
                        if (epFileName.StartsWith("main.", StringComparison.OrdinalIgnoreCase) ||
                            !epFileName.StartsWith("skill-hack.", StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add(new PackageError
                            {
                                Code = PackageErrorCodes.EntrypointNotSkillHack,
                                File = ep,
                                Message = $"Entrypoint '{ep}' must start with 'skill-hack.' (ADR-004), not 'main.*'.",
                            });
                        }

                        // Проверяем наличие точки входа на диске
                        string entrypointFullPath = Path.Combine(dirInfo.FullName, ep);
                        if (!File.Exists(entrypointFullPath))
                        {
                            errors.Add(new PackageError
                            {
                                Code = PackageErrorCodes.MissingFile,
                                File = ep,
                                Message = $"Entrypoint file '{ep}' does not exist on disk.",
                            });
                        }
                    }
                }
            }

            // Десериализация в DTO
            NetrunnerManifestDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<NetrunnerManifestDto>(manifestJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
            }
            catch (Exception ex)
            {
                errors.Add(new PackageError
                {
                    Code = PackageErrorCodes.InvalidSchema,
                    File = "netrunner.manifest.json",
                    Message = $"Deserialization error: {ex.Message}",
                });
                return null;
            }

            if (dto is null)
            {
                return null;
            }

            // Проверка slug (kebab-case)
            if (string.IsNullOrWhiteSpace(dto.Slug) || !KebabCaseSlugRegex().IsMatch(dto.Slug))
            {
                errors.Add(new PackageError
                {
                    Code = PackageErrorCodes.IdentityMismatch,
                    File = "netrunner.manifest.json",
                    Message = $"Slug '{dto.Slug}' must be lowercase kebab-case.",
                });
            }

            // Проверка version (SemVer)
            if (string.IsNullOrWhiteSpace(dto.Version) || !SemVerRegex().IsMatch(dto.Version))
            {
                errors.Add(new PackageError
                {
                    Code = PackageErrorCodes.IdentityMismatch,
                    File = "netrunner.manifest.json",
                    Message = $"Version '{dto.Version}' must be valid SemVer.",
                });
            }

            // Проверка соответствия имени папки <slug>/<version> или <slug>@<version>
            bool matchesFolder = (dirInfo.Name.Equals(dto.Version, StringComparison.OrdinalIgnoreCase) && dirInfo.Parent?.Name.Equals(dto.Slug, StringComparison.OrdinalIgnoreCase) == true)
                || dirInfo.Name.Equals($"{dto.Slug}@{dto.Version}", StringComparison.OrdinalIgnoreCase)
                || dirInfo.Name.Equals($"{dto.Slug}-{dto.Version}", StringComparison.OrdinalIgnoreCase)
                || dirInfo.Name.Equals(dto.Slug, StringComparison.OrdinalIgnoreCase);

            if (!matchesFolder)
            {
                errors.Add(new PackageError
                {
                    Code = PackageErrorCodes.IdentityMismatch,
                    File = dirInfo.Name,
                    Message = $"Package directory '{dirInfo.Name}' does not match slug '{dto.Slug}' and version '{dto.Version}'.",
                });
            }

            // Проверка permissions.filesystemScope
            if (!string.Equals(dto.Permissions?.FilesystemScope, "IsolatedRunWorkspaceOnly", StringComparison.Ordinal))
            {
                errors.Add(new PackageError
                {
                    Code = PackageErrorCodes.FilesystemScopeNotAllowed,
                    File = "permissions.filesystemScope",
                    Message = $"FilesystemScope '{dto.Permissions?.FilesystemScope}' is not allowed. Only 'IsolatedRunWorkspaceOnly' is permitted.",
                });
            }

            return dto;
        }
    }

    private static void CheckRuntimeField(JsonElement runtimeElement, string fieldName, List<PackageError> errors)
    {
        if (!runtimeElement.TryGetProperty(fieldName, out JsonElement val) ||
            val.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(val.GetString()))
        {
            errors.Add(new PackageError
            {
                Code = PackageErrorCodes.MissingRuntimeField,
                File = $"runtime.{fieldName}",
                Message = $"Mandatory runtime field '{fieldName}' is missing or empty.",
            });
        }
    }

    private static void ValidateJsonSchemaFile(string fullPath, string relativeName, List<PackageError> errors)
    {
        if (!File.Exists(fullPath))
        {
            return; // MissingFile handled in step 1
        }

        try
        {
            string content = File.ReadAllText(fullPath);
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind != JsonValueKind.Object && doc.RootElement.ValueKind != JsonValueKind.True && doc.RootElement.ValueKind != JsonValueKind.False)
            {
                errors.Add(new PackageError
                {
                    Code = PackageErrorCodes.InvalidSchema,
                    File = relativeName,
                    Message = "Schema root must be a JSON object or boolean.",
                });
            }
        }
        catch (JsonException ex)
        {
            errors.Add(new PackageError
            {
                Code = PackageErrorCodes.InvalidSchema,
                File = relativeName,
                Message = $"Malformed JSON in schema: {ex.Message}",
            });
        }
    }

    private static void ValidateFixture(string fullPath, List<PackageError> errors)
    {
        try
        {
            string content = File.ReadAllText(fullPath);
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new PackageError
                {
                    Code = PackageErrorCodes.InvalidFixture,
                    File = "tests/sample.fixture.json",
                    Message = "Fixture root must be a JSON object.",
                });
                return;
            }

            JsonElement root = doc.RootElement;
            bool hasSampleInput = root.TryGetProperty("sampleInput", out _) || root.TryGetProperty("sample_input", out _);
            bool hasExpectedOutput = root.TryGetProperty("expectedOutput", out _) || root.TryGetProperty("expected_output", out _);
            bool hasInvariants = root.TryGetProperty("invariants", out _) || root.TryGetProperty("expectedInvariants", out _);

            if (!hasSampleInput || (!hasExpectedOutput && !hasInvariants))
            {
                errors.Add(new PackageError
                {
                    Code = PackageErrorCodes.InvalidFixture,
                    File = "tests/sample.fixture.json",
                    Message = "Fixture must contain 'sampleInput' and either 'expectedOutput' or 'invariants'.",
                });
            }
        }
        catch (JsonException ex)
        {
            errors.Add(new PackageError
            {
                Code = PackageErrorCodes.InvalidFixture,
                File = "tests/sample.fixture.json",
                Message = $"Malformed JSON in fixture: {ex.Message}",
            });
        }
    }

    private static void ValidateIntegrity(string packageDir, IReadOnlyDictionary<string, string> filesSha256, List<PackageError> errors)
    {
        // 1. Проверяем каждый файл из манифеста
        foreach (var (relativeFile, expectedHash) in filesSha256)
        {
            string normalizedRelative = relativeFile.Replace('\\', '/');
            string diskPath = Path.Combine(packageDir, normalizedRelative);

            if (!File.Exists(diskPath))
            {
                errors.Add(new PackageError
                {
                    Code = PackageErrorCodes.MissingFile,
                    File = normalizedRelative,
                    Message = $"File '{normalizedRelative}' listed in integrity was not found on disk.",
                });
                continue;
            }

            string actualHash = ComputeSha256(diskPath);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new PackageError
                {
                    Code = PackageErrorCodes.HashMismatch,
                    File = normalizedRelative,
                    Message = $"SHA-256 hash mismatch for '{normalizedRelative}'. Expected: {expectedHash}, Actual: {actualHash}.",
                });
            }
        }

        // 2. Проверяем файлы на диске: нет ли файлов, не упомянутых в манифесте
        var manifestSet = new HashSet<string>(filesSha256.Keys.Select(k => k.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
        manifestSet.Add("netrunner.manifest.json");

        foreach (string file in Directory.EnumerateFiles(packageDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(packageDir, file).Replace('\\', '/');
            if (!manifestSet.Contains(relative))
            {
                errors.Add(new PackageError
                {
                    Code = PackageErrorCodes.UnlistedFile,
                    File = relative,
                    Message = $"File '{relative}' on disk is not listed in integrity.filesSha256.",
                });
            }
        }
    }

    /// <summary>
    /// Вычисляет шестнадцатеричный SHA-256 хэш файла в нижнем регистре.
    /// </summary>
    public static string ComputeSha256(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

using System.Text.Json;
using CyberPipe.Chains.Packages;
using CyberPipe.Contracts;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class NetrunnerPackageValidatorTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly NetrunnerPackageValidator _validator = new();
    private readonly InMemoryPackageRegistryRepository _registry = new();

    public NetrunnerPackageValidatorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cyberpipe-tests-packages", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Ignore temp cleanup errors
        }
    }

    private string CreatePackage(
        string slug = "test-pkg",
        string version = "1.0.0",
        string entrypoint = "skill-hack.py",
        string? scriptBody = null,
        string filesystemScope = "IsolatedRunWorkspaceOnly",
        bool omitFile = false,
        string? fileToOmit = null,
        string? corruptFile = null,
        string? invalidSchemaFile = null,
        string? invalidFixtureContent = null,
        Action<Dictionary<string, object>>? mutateManifest = null)
    {
        string packageDir = Path.Combine(_tempRoot, slug, version);
        Directory.CreateDirectory(packageDir);
        Directory.CreateDirectory(Path.Combine(packageDir, "tests"));

        // 1. Files content
        string skillMd = "# Test Package Skill\nDocumentation for test package.";
        string schemaInput = """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "properties": {
                "input": { "type": "string" }
              },
              "required": ["input"]
            }
            """;
        string schemaOutput = """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "properties": {
                "output": { "type": "string" }
              },
              "required": ["output"]
            }
            """;
        string fixture = invalidFixtureContent ?? """
            {
              "sampleInput": { "input": "test" },
              "expectedOutput": { "output": "processed" }
            }
            """;
        string script = scriptBody ?? "print('hello from skill-hack')\n";

        if (invalidSchemaFile == "schema.input.json")
        {
            schemaInput = "NOT_VALID_JSON_SCHEMA {{{";
        }
        else if (invalidSchemaFile == "schema.output.json")
        {
            schemaOutput = "NOT_VALID_JSON_SCHEMA {{{";
        }

        var filesToWrite = new Dictionary<string, string>
        {
            ["SKILL.md"] = skillMd,
            ["schema.input.json"] = schemaInput,
            ["schema.output.json"] = schemaOutput,
            ["tests/sample.fixture.json"] = fixture,
            [entrypoint] = script,
        };

        foreach (var (relPath, content) in filesToWrite)
        {
            if (omitFile && relPath == fileToOmit)
            {
                continue;
            }

            string full = Path.Combine(packageDir, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        // Compute hashes for manifest
        var hashes = new Dictionary<string, string>();
        foreach (var relPath in filesToWrite.Keys)
        {
            if (omitFile && relPath == fileToOmit)
            {
                continue;
            }

            string full = Path.Combine(packageDir, relPath);
            if (File.Exists(full))
            {
                hashes[relPath] = NetrunnerPackageValidator.ComputeSha256(full);
            }
        }

        // 2. Build manifest dictionary
        var manifestDict = new Dictionary<string, object>
        {
            ["slug"] = slug,
            ["name"] = "Test Package",
            ["version"] = version,
            ["description"] = "A package for tests",
            ["runtime"] = new Dictionary<string, object>
            {
                ["language"] = "python",
                ["languageVersion"] = "3.11",
                ["environment"] = "cpython",
                ["environmentVersion"] = "3.11.8",
                ["entrypoint"] = entrypoint,
                ["timeoutMs"] = 5000,
                ["deterministic"] = true,
            },
            ["io"] = new Dictionary<string, object>
            {
                ["inputSchemaFile"] = "schema.input.json",
                ["outputSchemaFile"] = "schema.output.json",
                ["inputFormats"] = new[] { "json" },
                ["outputFormats"] = new[] { "json" },
                ["inputFiles"] = Array.Empty<object>(),
                ["outputFiles"] = Array.Empty<object>(),
            },
            ["permissions"] = new Dictionary<string, object>
            {
                ["allowNetwork"] = false,
                ["allowedDomains"] = Array.Empty<string>(),
                ["filesystemScope"] = filesystemScope,
                ["requiredVaultSecretRefs"] = Array.Empty<string>(),
            },
            ["integrity"] = new Dictionary<string, object>
            {
                ["filesSha256"] = hashes,
            },
        };

        mutateManifest?.Invoke(manifestDict);

        string manifestJson = JsonSerializer.Serialize(manifestDict, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(packageDir, "netrunner.manifest.json"), manifestJson);

        // Corrupt file after hashing if requested
        if (corruptFile is not null)
        {
            string corruptPath = Path.Combine(packageDir, corruptFile);
            File.AppendAllText(corruptPath, "\n# TAMPERED_CONTENT");
        }

        return packageDir;
    }

    [Fact]
    public void ValidPackage_PassesValidation_WithMatchingHashes()
    {
        string packageDir = CreatePackage();

        PackageValidationResult result = _validator.ValidatePackage(packageDir);

        Assert.True(result.IsValid, $"Validation failed: {string.Join(", ", result.Errors.Select(e => e.Message))}");
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Manifest);
        Assert.Equal("skill-hack.py", result.Manifest.Runtime.Entrypoint);
        Assert.Equal("test-pkg", result.Manifest.Slug);
        Assert.Equal("1.0.0", result.Manifest.Version);
    }

    [Fact]
    public void MainPyEntrypoint_RejectedWithEntrypointNotSkillHack()
    {
        string packageDir = CreatePackage(entrypoint: "main.py");

        PackageValidationResult result = _validator.ValidatePackage(packageDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == PackageErrorCodes.EntrypointNotSkillHack && e.File.Contains("main.py"));
    }

    [Fact]
    public void MissingLanguageVersion_RejectedWithMissingRuntimeField()
    {
        string packageDir = CreatePackage(mutateManifest: dict =>
        {
            var runtime = (Dictionary<string, object>)dict["runtime"];
            runtime.Remove("languageVersion");
        });

        PackageValidationResult result = _validator.ValidatePackage(packageDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == PackageErrorCodes.MissingRuntimeField && e.File == "runtime.languageVersion");
    }

    [Fact]
    public void ModifiedFileAfterSigning_RejectedWithHashMismatch()
    {
        string packageDir = CreatePackage(corruptFile: "skill-hack.py");

        PackageValidationResult result = _validator.ValidatePackage(packageDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == PackageErrorCodes.HashMismatch && e.File == "skill-hack.py");
    }

    [Fact]
    public void MissingSampleFixture_RejectedWithMissingFile()
    {
        string packageDir = CreatePackage(omitFile: true, fileToOmit: "tests/sample.fixture.json");

        PackageValidationResult result = _validator.ValidatePackage(packageDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == PackageErrorCodes.MissingFile && e.File == "tests/sample.fixture.json");
    }

    [Fact]
    public void InvalidSchema_AndHostFilesystemScope_RejectedWithCorrespondingCodes()
    {
        string packageDir = CreatePackage(
            filesystemScope: "Host",
            invalidSchemaFile: "schema.input.json");

        PackageValidationResult result = _validator.ValidatePackage(packageDir);

        Assert.False(result.IsValid);
        // Both defects collected simultaneously
        Assert.Contains(result.Errors, e => e.Code == PackageErrorCodes.FilesystemScopeNotAllowed && e.File == "permissions.filesystemScope");
        Assert.Contains(result.Errors, e => e.Code == PackageErrorCodes.InvalidSchema && e.File == "schema.input.json");
    }

    [Fact]
    public void Validator_NeverExecutesScript_MarkerFileNotCreated()
    {
        string markerPath = Path.Combine(_tempRoot, "executed.marker");
        // Python code that writes a marker file IF executed
        string maliciousScript = $"""
            import os
            with open(r'{markerPath}', 'w') as f:
                f.write('EXECUTED')
            """;

        string packageDir = CreatePackage(scriptBody: maliciousScript);

        PackageValidationResult result = _validator.ValidatePackage(packageDir);

        Assert.True(result.IsValid);
        // The script MUST NOT be executed during validation!
        Assert.False(File.Exists(markerPath), "Validator executed the script! Script execution during validation is strictly forbidden.");
    }

    [Fact]
    public async Task PackageRegistration_IdempotentOnSameHash_ThrowsOnDifferentHash()
    {
        string packageDir = CreatePackage();
        PackageValidationResult validation = _validator.ValidatePackage(packageDir);
        Assert.True(validation.IsValid);

        string manifestPath = Path.Combine(packageDir, "netrunner.manifest.json");
        string manifestHash1 = NetrunnerPackageValidator.ComputeSha256(manifestPath);

        // 1. First registration
        PackageRegistrationRecord reg1 = await _registry.RegisterPackageAsync(
            validation.Manifest!,
            packageDir,
            manifestHash1);

        Assert.Equal("test-pkg", reg1.Slug);
        Assert.Equal("1.0.0", reg1.Version);

        // 2. Repeated registration with identical hash -> idempotent
        PackageRegistrationRecord reg2 = await _registry.RegisterPackageAsync(
            validation.Manifest!,
            packageDir,
            manifestHash1);

        Assert.Equal(reg1.ManifestHash, reg2.ManifestHash);

        // 3. Repeated registration with different hash -> throws PackageRegistrationException
        string manifestHash2 = "different_hash_000000000000000000000000000000000000000000000000000000000000";
        var ex = await Assert.ThrowsAsync<PackageRegistrationException>(() =>
            _registry.RegisterPackageAsync(validation.Manifest!, packageDir, manifestHash2));

        Assert.Equal(PackageErrorCodes.VersionAlreadyRegistered, ex.Code);
    }
}

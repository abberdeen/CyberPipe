using System.Collections.Concurrent;
using System.Text.Json;
using CyberPipe.Chains.Packages;
using CyberPipe.Contracts;

namespace CyberPipe.Chains.Runners;

/// <summary>
/// Реестр раннеров скриптов Netrunner по парам (язык, среда) (TASK-043).
/// Поддерживает расширение новыми раннерами без изменения исполнителя цепочки.
/// Выполняет поиск по (Language, Environment) без учёта регистра.
/// Реализует dry-run проверку пакетов по тестовым фикстурам tests/sample.fixture.json.
/// </summary>
public sealed class ScriptRunnerRegistry : IScriptRunnerRegistry
{
    private readonly ConcurrentDictionary<string, IScriptRunner> _runners = new(StringComparer.OrdinalIgnoreCase);
    private readonly INetrunnerPackageValidator _packageValidator;

    /// <summary>
    /// Создаёт пустой реестр раннеров скриптов.
    /// </summary>
    public ScriptRunnerRegistry(INetrunnerPackageValidator? packageValidator = null)
    {
        _packageValidator = packageValidator ?? new NetrunnerPackageValidator();
    }

    /// <summary>
    /// Создаёт реестр и регистрирует стандартный набор раннеров (CSharp, Python, JavaScript).
    /// </summary>
    public static ScriptRunnerRegistry CreateDefault(
        ISandboxLauncher sandbox,
        ISchemaValidator? schemaValidator = null,
        INetrunnerPackageValidator? packageValidator = null)
    {
        ISchemaValidator schema = schemaValidator ?? new JsonSchemaDraft202012Validator();
        var registry = new ScriptRunnerRegistry(packageValidator);
        registry.RegisterRunner(new CSharpScriptRunner(sandbox, schema));
        registry.RegisterRunner(new PythonScriptRunner(sandbox, schema));
        registry.RegisterRunner(new JavaScriptScriptRunner(sandbox, schema));
        return registry;
    }

    /// <inheritdoc />
    public void RegisterRunner(IScriptRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        string key = MakeKey(runner.Language, runner.Environment);
        _runners[key] = runner;
    }

    /// <inheritdoc />
    public IScriptRunner? FindRunner(string language, string environment)
    {
        if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(environment))
        {
            return null;
        }

        string key = MakeKey(language, environment);
        if (_runners.TryGetValue(key, out IScriptRunner? runner))
        {
            return runner;
        }

        // Поиск по CanRun для раннеров с расширенными синонимами
        foreach (IScriptRunner r in _runners.Values)
        {
            if (string.Equals(r.Language, language, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.Environment, environment, StringComparison.OrdinalIgnoreCase))
            {
                return r;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IScriptRunner> GetAllRunners() =>
        _runners.Values.ToArray();

    /// <inheritdoc />
    public async Task<ScriptRunResult> RunAsync(ScriptRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string language = request.Manifest.Runtime.Language;
        string environment = request.Manifest.Runtime.Environment;

        IScriptRunner? runner = FindRunner(language, environment);
        if (runner is null)
        {
            // Также проверяем CanRun у каждого раннера (на случай синонимов вроде PythonSandbox)
            runner = _runners.Values.FirstOrDefault(r => r.CanRun(request.Manifest));
        }

        if (runner is null)
        {
            return ScriptRunResult.Failed(
                ScriptRunStatus.UnsupportedRuntime,
                $"Unsupported runtime: language '{language}', environment '{environment}'.");
        }

        return await runner.RunAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ScriptRunResult> DryRunPackageAsync(
        string packageDir,
        string workspaceDir,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packageDir);
        ArgumentNullException.ThrowIfNull(workspaceDir);

        // 1. Валидация пакета (TASK-042)
        PackageValidationResult valResult = _packageValidator.ValidatePackage(packageDir);
        if (!valResult.IsValid || valResult.Manifest is null)
        {
            string errStr = string.Join("; ", valResult.Errors.Select(e => $"[{e.Code}] {e.Message}"));
            return ScriptRunResult.Failed(
                ScriptRunStatus.SchemaValidationFailed,
                $"Dry-run package validation failed: {errStr}");
        }

        NetrunnerManifestDto manifest = valResult.Manifest;

        // 2. Чтение фикстуры tests/sample.fixture.json
        string fixtureFile = Path.Combine(packageDir, "tests", "sample.fixture.json");
        if (!File.Exists(fixtureFile))
        {
            return ScriptRunResult.Failed(
                ScriptRunStatus.SchemaValidationFailed,
                "Fixture file 'tests/sample.fixture.json' does not exist.");
        }

        string fixtureContent = await File.ReadAllTextAsync(fixtureFile, cancellationToken);
        using var fixtureDoc = JsonDocument.Parse(fixtureContent);

        if (!fixtureDoc.RootElement.TryGetProperty("sampleInput", out JsonElement sampleInputElem))
        {
            return ScriptRunResult.Failed(
                ScriptRunStatus.SchemaValidationFailed,
                "Fixture 'tests/sample.fixture.json' must contain 'sampleInput'.");
        }

        string sampleInputJson = sampleInputElem.GetRawText();

        JsonElement? expectedOutputElem = null;
        if (fixtureDoc.RootElement.TryGetProperty("expectedOutput", out JsonElement expOut))
        {
            expectedOutputElem = expOut.Clone();
        }

        // 3. Формирование запроса на запуск
        var request = new ScriptRunRequest
        {
            Manifest = manifest,
            PackageDir = packageDir,
            InputJson = sampleInputJson,
            RunWorkspaceDir = workspaceDir,
        };

        // 4. Исполнение через реестр
        ScriptRunResult runResult = await RunAsync(request, cancellationToken);
        if (runResult.Status != ScriptRunStatus.Succeeded)
        {
            return runResult;
        }

        // 5. Dry-run gate: сравнение с expectedOutput
        if (expectedOutputElem.HasValue)
        {
            if (string.IsNullOrWhiteSpace(runResult.OutputJson))
            {
                return ScriptRunResult.Failed(
                    ScriptRunStatus.ScriptFailed,
                    "Dry-run gate failed: script produced empty output, but expectedOutput was specified in fixture.",
                    exitCode: runResult.ExitCode,
                    stdoutTail: runResult.StdoutTail,
                    stderrTail: runResult.StderrTail,
                    durationMs: runResult.DurationMs);
            }

            try
            {
                using var actualDoc = JsonDocument.Parse(runResult.OutputJson);
                if (!AreJsonElementsEqual(expectedOutputElem.Value, actualDoc.RootElement))
                {
                    return ScriptRunResult.Failed(
                        ScriptRunStatus.ScriptFailed,
                        $"Dry-run gate failed: output does not match expectedOutput from sample.fixture.json. Expected: {expectedOutputElem.Value.GetRawText()}, Actual: {actualDoc.RootElement.GetRawText()}",
                        exitCode: runResult.ExitCode,
                        stdoutTail: runResult.StdoutTail,
                        stderrTail: runResult.StderrTail,
                        durationMs: runResult.DurationMs);
                }
            }
            catch (JsonException ex)
            {
                return ScriptRunResult.Failed(
                    ScriptRunStatus.SchemaValidationFailed,
                    $"Dry-run output JSON parsing failed: {ex.Message}",
                    exitCode: runResult.ExitCode,
                    stdoutTail: runResult.StdoutTail,
                    stderrTail: runResult.StderrTail,
                    durationMs: runResult.DurationMs);
            }
        }

        return runResult;
    }

    private static string MakeKey(string language, string environment) =>
        $"{language.Trim().ToLowerInvariant()}::{environment.Trim().ToLowerInvariant()}";

    private static bool AreJsonElementsEqual(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var bProps = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                int aCount = 0;
                foreach (JsonProperty aProp in a.EnumerateObject())
                {
                    aCount++;
                    if (!bProps.TryGetValue(aProp.Name, out JsonElement bVal) || !AreJsonElementsEqual(aProp.Value, bVal))
                    {
                        return false;
                    }
                }
                return aCount == bProps.Count;

            case JsonValueKind.Array:
                var aList = a.EnumerateArray().ToList();
                var bList = b.EnumerateArray().ToList();
                if (aList.Count != bList.Count)
                {
                    return false;
                }
                for (int i = 0; i < aList.Count; i++)
                {
                    if (!AreJsonElementsEqual(aList[i], bList[i]))
                    {
                        return false;
                    }
                }
                return true;

            case JsonValueKind.String:
                return string.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal);

            case JsonValueKind.Number:
                if (a.TryGetInt64(out long aInt) && b.TryGetInt64(out long bInt))
                {
                    return aInt == bInt;
                }
                if (a.TryGetDouble(out double aDouble) && b.TryGetDouble(out double bDouble))
                {
                    return Math.Abs(aDouble - bDouble) < 1e-9;
                }
                return false;

            case JsonValueKind.True:
            case JsonValueKind.False:
                return a.GetBoolean() == b.GetBoolean();

            case JsonValueKind.Null:
                return true;

            default:
                return a.GetRawText().Trim() == b.GetRawText().Trim();
        }
    }
}

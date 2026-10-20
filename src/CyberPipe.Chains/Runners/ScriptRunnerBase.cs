using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Chains.Runners;

/// <summary>
/// Базовый класс для раннеров скриптов Netrunner (TASK-043).
/// Реализует конвейер:
/// 1. Пред-валидация входных файлов (наличие, расширение, размер).
/// 2. Пред-валидация входного JSON по schema.input.json.
/// 3. Подготовка рабочего пространства (RunWorkspaceDir).
/// 4. Запуск через порт песочницы ISandboxLauncher (прямое создание процессов на хосте запрещено).
/// 5. Сбор и ограничение потоков stdout/stderr (потолок 64 КБ с меткой [truncated]).
/// 6. Обработка сигналов TimedOut, Cancelled, DeferRetry (код 75 или управляющий JSON).
/// 7. Пост-валидация выходного JSON по schema.output.json.
/// 8. Пост-валидация обязательных выходных файлов (io.outputFiles) и расчёт SHA-256.
/// </summary>
public abstract class ScriptRunnerBase : IScriptRunner
{
    private readonly ISandboxLauncher _sandbox;
    private readonly ISchemaValidator _schemaValidator;
    private readonly int _maxTailBytes;
    private readonly IAntiWipeGuard? _antiWipeGuard;

    /// <summary>
    /// Создаёт экземпляр базового раннера.
    /// </summary>
    protected ScriptRunnerBase(
        ISandboxLauncher sandbox,
        ISchemaValidator schemaValidator,
        int maxTailBytes = 65536,
        IAntiWipeGuard? antiWipeGuard = null)
    {
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _schemaValidator = schemaValidator ?? throw new ArgumentNullException(nameof(schemaValidator));
        _maxTailBytes = maxTailBytes > 0 ? maxTailBytes : 65536;
        _antiWipeGuard = antiWipeGuard;
    }

    /// <inheritdoc />
    public abstract string Language { get; }

    /// <inheritdoc />
    public abstract string Environment { get; }

    /// <inheritdoc />
    public abstract bool CanRun(NetrunnerManifestDto manifest);

    /// <summary>
    /// Формирует запрос на запуск изолированного процесса для конкретного языка/среды.
    /// </summary>
    protected abstract SandboxLaunchRequest CreateSandboxRequest(
        ScriptRunRequest request,
        string workspaceDir,
        string entrypointPath);

    /// <inheritdoc />
    public async Task<ScriptRunResult> RunAsync(ScriptRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return ScriptRunResult.Failed(
                ScriptRunStatus.Cancelled,
                "Script execution was cancelled.");
        }

        try
        {
            return await RunInternalAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return ScriptRunResult.Failed(
                ScriptRunStatus.Cancelled,
                "Script execution was cancelled.");
        }
    }

    private async Task<ScriptRunResult> RunInternalAsync(ScriptRunRequest request, CancellationToken cancellationToken)
    {
        // 1. Проверка поддержки среды
        if (!CanRun(request.Manifest))
        {
            return ScriptRunResult.Failed(
                ScriptRunStatus.UnsupportedRuntime,
                $"Runner for '{Language}/{Environment}' cannot run package with runtime '{request.Manifest.Runtime.Language}/{request.Manifest.Runtime.Environment}'.");
        }

        // Anti-wipe (TASK-070): входной payload скрипта проверяется до запуска песочницы.
        // Deny и RequireApproval — fail-closed: скрипт не выполняется.
        if (_antiWipeGuard is not null && !string.IsNullOrWhiteSpace(request.InputJson))
        {
            WipeGuardDecision decision = await _antiWipeGuard.EvaluateAsync(new DestructiveOperation
            {
                Kind = DestructiveOperationKind.SqlStatement,
                Target = request.PackageDir,
                StatementOrCommand = request.InputJson,
                Source = $"ScriptRunner:{request.Manifest.Slug}",
            }, cancellationToken).ConfigureAwait(false);
            if (decision.Outcome != WipeGuardDecisionOutcome.Allow)
            {
                return ScriptRunResult.Failed(
                    ScriptRunStatus.SchemaValidationFailed,
                    $"Anti-wipe guard {decision.Outcome} ({decision.ReasonCode}): {decision.Message}");
            }
        }

        // 2. Пред-валидация входных файлов
        foreach (NetrunnerInputFileDto desc in request.Manifest.Io.InputFiles)
        {
            if (desc.Required && !request.InputFiles.ContainsKey(desc.Alias))
            {
                return ScriptRunResult.Failed(
                    ScriptRunStatus.SchemaValidationFailed,
                    $"Required input file alias '{desc.Alias}' is missing from request.");
            }

            if (request.InputFiles.TryGetValue(desc.Alias, out string? filePath))
            {
                if (!File.Exists(filePath))
                {
                    return ScriptRunResult.Failed(
                        ScriptRunStatus.SchemaValidationFailed,
                        $"Input file for alias '{desc.Alias}' does not exist at path '{filePath}'.");
                }

                if (desc.AllowedExtensions.Count > 0)
                {
                    string ext = Path.GetExtension(filePath);
                    if (!desc.AllowedExtensions.Any(allowed => string.Equals(allowed, ext, StringComparison.OrdinalIgnoreCase)))
                    {
                        return ScriptRunResult.Failed(
                            ScriptRunStatus.SchemaValidationFailed,
                            $"Input file '{filePath}' extension '{ext}' is not in allowedExtensions [{string.Join(", ", desc.AllowedExtensions)}].");
                    }
                }

                if (desc.MaxSizeBytes > 0)
                {
                    var fi = new FileInfo(filePath);
                    if (fi.Length > desc.MaxSizeBytes)
                    {
                        return ScriptRunResult.Failed(
                            ScriptRunStatus.SchemaValidationFailed,
                            $"Input file '{filePath}' size ({fi.Length} bytes) exceeds maximum allowed size ({desc.MaxSizeBytes} bytes).");
                    }
                }
            }
        }

        // 3. Пред-валидация входного JSON по schema.input.json
        if (!string.IsNullOrWhiteSpace(request.Manifest.Io.InputSchemaFile))
        {
            string schemaPath = Path.Combine(request.PackageDir, request.Manifest.Io.InputSchemaFile);
            if (File.Exists(schemaPath))
            {
                string schemaJson = await File.ReadAllTextAsync(schemaPath, cancellationToken);
                IReadOnlyList<string> errors = _schemaValidator.Validate(schemaJson, request.InputJson ?? "{}");
                if (errors.Count > 0)
                {
                    return ScriptRunResult.Failed(
                        ScriptRunStatus.SchemaValidationFailed,
                        $"Input JSON validation failed: {string.Join("; ", errors)}");
                }
            }
        }

        // 4. Подготовка рабочего каталога запуска
        Directory.CreateDirectory(request.RunWorkspaceDir);

        string inputJsonFile = Path.Combine(request.RunWorkspaceDir, "input.json");
        await File.WriteAllTextAsync(inputJsonFile, request.InputJson ?? "{}", cancellationToken);

        foreach (var (alias, srcPath) in request.InputFiles)
        {
            if (File.Exists(srcPath))
            {
                string destFile = Path.Combine(request.RunWorkspaceDir, alias + Path.GetExtension(srcPath));
                if (!File.Exists(destFile))
                {
                    File.Copy(srcPath, destFile, overwrite: true);
                }
            }
        }

        // 5. Формирование запроса к песочнице
        string entrypointPath = Path.Combine(request.PackageDir, request.Manifest.Runtime.Entrypoint);
        SandboxLaunchRequest launchRequest = CreateSandboxRequest(request, request.RunWorkspaceDir, entrypointPath);

        // 6. Запуск через порт песочницы (прямой запуск процесса на хосте запрещён)
        var sw = Stopwatch.StartNew();
        SandboxLaunchResult sandboxResult;
        try
        {
            sandboxResult = await _sandbox.RunAsync(launchRequest, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return ScriptRunResult.Failed(
                ScriptRunStatus.Cancelled,
                "Script execution was cancelled.",
                durationMs: sw.ElapsedMilliseconds);
        }
        sw.Stop();

        // 7. Проверка отмены токеном
        if (cancellationToken.IsCancellationRequested)
        {
            return ScriptRunResult.Failed(
                ScriptRunStatus.Cancelled,
                "Script execution was cancelled.",
                durationMs: sw.ElapsedMilliseconds);
        }

        // 8. Сбор и ограничение потоков вывода
        string stdout = CapString(sandboxResult.Stdout, _maxTailBytes);
        string stderr = CapString(sandboxResult.Stderr, _maxTailBytes);

        // 9. Проверка тайм-аута
        if (sandboxResult.TimedOut)
        {
            return ScriptRunResult.Failed(
                ScriptRunStatus.TimedOut,
                $"Script execution timed out after {launchRequest.TimeoutMs}ms.",
                exitCode: sandboxResult.ExitCode,
                stdoutTail: stdout,
                stderrTail: stderr,
                durationMs: sw.ElapsedMilliseconds);
        }

        // 10. Проверка сигнала DeferRetry (код возврата 75 или управляющий JSON)
        int? retryAfterSeconds = null;
        bool isDeferRetry = sandboxResult.ExitCode == 75;

        string? resolvedOutputJson = TryResolveOutputJson(request.RunWorkspaceDir, sandboxResult.Stdout);
        if (resolvedOutputJson is not null && TryCheckDeferRetryJson(resolvedOutputJson, out int seconds))
        {
            isDeferRetry = true;
            retryAfterSeconds = seconds;
        }

        if (isDeferRetry)
        {
            return ScriptRunResult.Failed(
                ScriptRunStatus.DeferRetry,
                "DeferRetry requested by script.",
                exitCode: sandboxResult.ExitCode,
                stdoutTail: stdout,
                stderrTail: stderr,
                durationMs: sw.ElapsedMilliseconds,
                retryAfterSeconds: retryAfterSeconds ?? 60);
        }

        // 11. Проверка ненулевого кода возврата
        if (sandboxResult.ExitCode is not null and not 0)
        {
            return ScriptRunResult.Failed(
                ScriptRunStatus.ScriptFailed,
                $"Script failed with exit code {sandboxResult.ExitCode}. {sandboxResult.Error}".Trim(),
                exitCode: sandboxResult.ExitCode,
                stdoutTail: stdout,
                stderrTail: stderr,
                durationMs: sw.ElapsedMilliseconds);
        }

        // 12. Пост-валидация выходного JSON по schema.output.json
        if (!string.IsNullOrWhiteSpace(request.Manifest.Io.OutputSchemaFile))
        {
            string schemaPath = Path.Combine(request.PackageDir, request.Manifest.Io.OutputSchemaFile);
            if (File.Exists(schemaPath))
            {
                string schemaJson = await File.ReadAllTextAsync(schemaPath, cancellationToken);
                IReadOnlyList<string> errors = _schemaValidator.Validate(schemaJson, resolvedOutputJson ?? "{}");
                if (errors.Count > 0)
                {
                    return ScriptRunResult.Failed(
                        ScriptRunStatus.SchemaValidationFailed,
                        $"Output JSON validation failed: {string.Join("; ", errors)}",
                        exitCode: sandboxResult.ExitCode,
                        stdoutTail: stdout,
                        stderrTail: stderr,
                        durationMs: sw.ElapsedMilliseconds);
                }
            }
        }

        // 13. Сбор и валидация выходных файлов
        var outputFiles = new List<ScriptOutputFile>();
        foreach (NetrunnerOutputFileDto desc in request.Manifest.Io.OutputFiles)
        {
            string? matchedFile = FindMatchingFile(request.RunWorkspaceDir, desc.FilenamePattern);
            if (matchedFile is null || !File.Exists(matchedFile))
            {
                if (desc.Required)
                {
                    return ScriptRunResult.Failed(
                        ScriptRunStatus.SchemaValidationFailed,
                        $"Required output file '{desc.Alias}' (pattern '{desc.FilenamePattern}') was not produced.",
                        exitCode: sandboxResult.ExitCode,
                        stdoutTail: stdout,
                        stderrTail: stderr,
                        durationMs: sw.ElapsedMilliseconds);
                }
                continue;
            }

            var fi = new FileInfo(matchedFile);
            if (desc.MaxSizeBytes > 0 && fi.Length > desc.MaxSizeBytes)
            {
                return ScriptRunResult.Failed(
                    ScriptRunStatus.SchemaValidationFailed,
                    $"Output file '{desc.Alias}' size ({fi.Length} bytes) exceeds maximum allowed size ({desc.MaxSizeBytes} bytes).",
                    exitCode: sandboxResult.ExitCode,
                    stdoutTail: stdout,
                    stderrTail: stderr,
                    durationMs: sw.ElapsedMilliseconds);
            }

            string sha256 = ComputeSha256(matchedFile);
            outputFiles.Add(new ScriptOutputFile
            {
                Alias = desc.Alias,
                Path = matchedFile,
                Sha256 = sha256,
                SizeBytes = fi.Length,
            });
        }

        // 14. Успешный результат
        return ScriptRunResult.Success(
            resolvedOutputJson,
            outputFiles,
            sandboxResult.ExitCode ?? 0,
            stdout,
            stderr,
            sw.ElapsedMilliseconds);
    }

    private static string? TryResolveOutputJson(string workspaceDir, string stdout)
    {
        string outputFile = Path.Combine(workspaceDir, "output.json");
        if (File.Exists(outputFile))
        {
            try
            {
                string content = File.ReadAllText(outputFile).Trim();
                using var doc = JsonDocument.Parse(content);
                return content;
            }
            catch
            {
                // File exists but not valid JSON
            }
        }

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            string trimmed = stdout.Trim();
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                return trimmed;
            }
            catch
            {
                // stdout contains logs rather than raw JSON
            }
        }

        return null;
    }

    private static bool TryCheckDeferRetryJson(string json, out int retryAfterSeconds)
    {
        retryAfterSeconds = 60;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("$netrunnerControl", out JsonElement ctrl) &&
                string.Equals(ctrl.GetString(), "DeferRetry", StringComparison.OrdinalIgnoreCase))
            {
                if (doc.RootElement.TryGetProperty("retryAfterSeconds", out JsonElement sec) && sec.TryGetInt32(out int s))
                {
                    retryAfterSeconds = s;
                }
                return true;
            }
        }
        catch
        {
            // Ignore parse errors
        }
        return false;
    }

    private static string? FindMatchingFile(string directory, string pattern)
    {
        string exact = Path.Combine(directory, pattern);
        if (File.Exists(exact))
        {
            return exact;
        }

        try
        {
            string[] files = Directory.GetFiles(directory, pattern);
            if (files.Length > 0)
            {
                return files[0];
            }
        }
        catch
        {
            // Invalid pattern
        }

        return null;
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static string CapString(string? text, int maxBytes)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (text.Length <= maxBytes)
        {
            return text;
        }

        return text[..maxBytes] + "\n[truncated]";
    }
}

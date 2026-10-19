using CyberPipe.Contracts;

namespace CyberPipe.Chains.Runners;

/// <summary>
/// Раннер для скриптов Python (CPython) (TASK-043).
/// Исполняется через порт песочницы ISandboxLauncher без прямого запуска процессов на хосте.
/// </summary>
public sealed class PythonScriptRunner : ScriptRunnerBase
{
    private readonly string _executable;
    private readonly IReadOnlyList<string>? _customArgs;

    /// <summary>
    /// Создаёт экземпляр PythonScriptRunner.
    /// </summary>
    public PythonScriptRunner(
        ISandboxLauncher sandbox,
        ISchemaValidator schemaValidator,
        string? executable = null,
        IReadOnlyList<string>? customArgs = null,
        int maxTailBytes = 65536)
        : base(sandbox, schemaValidator, maxTailBytes)
    {
        _executable = executable ?? "python";
        _customArgs = customArgs;
    }

    /// <inheritdoc />
    public override string Language => "Python";

    /// <inheritdoc />
    public override string Environment => "CPython";

    /// <inheritdoc />
    public override bool CanRun(NetrunnerManifestDto manifest) =>
        string.Equals(manifest.Runtime.Language, "Python", StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(manifest.Runtime.Environment, "CPython", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(manifest.Runtime.Environment, "Python", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(manifest.Runtime.Environment, "PythonSandbox", StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    protected override SandboxLaunchRequest CreateSandboxRequest(
        ScriptRunRequest request,
        string workspaceDir,
        string entrypointPath)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NETRUNNER_INPUT_FILE"] = "input.json",
            ["NETRUNNER_INPUT_JSON"] = request.InputJson ?? "{}",
            ["NETRUNNER_ENTRYPOINT"] = entrypointPath,
            ["PYTHONUNBUFFERED"] = "1",
        };

        if (request.EnvironmentVariables is not null)
        {
            foreach (var (k, v) in request.EnvironmentVariables)
            {
                env[k] = v;
            }
        }

        IReadOnlyList<string> arguments = _customArgs ?? [entrypointPath, "input.json"];

        return new SandboxLaunchRequest(
            ExecutablePath: _executable,
            Arguments: arguments,
            WorkingDirectory: workspaceDir,
            Environment: env,
            TimeoutMs: request.TimeoutMs ?? (request.Manifest.Runtime.TimeoutMs > 0 ? request.Manifest.Runtime.TimeoutMs : 30000),
            MaxMemoryMb: request.MaxMemoryMb ?? request.Manifest.Runtime.MaxMemoryMb);
    }
}

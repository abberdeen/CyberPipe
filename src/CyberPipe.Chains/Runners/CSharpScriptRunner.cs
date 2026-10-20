using CyberPipe.Contracts;

namespace CyberPipe.Chains.Runners;

/// <summary>
/// Раннер для скриптов C# Roslyn (.csx) (TASK-043).
/// Исполняется через порт песочницы ISandboxLauncher без прямого запуска процессов на хосте.
/// </summary>
public sealed class CSharpScriptRunner : ScriptRunnerBase
{
    private readonly string _executable;
    private readonly IReadOnlyList<string>? _customArgs;

    /// <summary>
    /// Создаёт экземпляр CSharpScriptRunner.
    /// </summary>
    public CSharpScriptRunner(
        ISandboxLauncher sandbox,
        ISchemaValidator schemaValidator,
        string? executable = null,
        IReadOnlyList<string>? customArgs = null,
        int maxTailBytes = 65536,
        IAntiWipeGuard? antiWipeGuard = null)
        : base(sandbox, schemaValidator, maxTailBytes, antiWipeGuard)
    {
        _executable = executable ?? "dotnet";
        _customArgs = customArgs;
    }

    /// <inheritdoc />
    public override string Language => "CSharp";

    /// <inheritdoc />
    public override string Environment => "Roslyn";

    /// <inheritdoc />
    public override bool CanRun(NetrunnerManifestDto manifest) =>
        string.Equals(manifest.Runtime.Language, "CSharp", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(manifest.Runtime.Environment, "Roslyn", StringComparison.OrdinalIgnoreCase);

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

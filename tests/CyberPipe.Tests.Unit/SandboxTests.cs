using CyberPipe.Contracts;
using CyberPipe.Execution.Sandbox;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class SandboxTests
{
    private readonly CyberPipeOptions _options = new();
    private readonly string _payloadExe;

    public SandboxTests()
    {
        string location = typeof(CyberPipe.Sandbox.Payload.Program).Assembly.Location;
        string exe = Path.ChangeExtension(location, ".exe");
        _payloadExe = File.Exists(exe) ? exe : location;
    }

    [Fact]
    public async Task Sandbox_Validation_NonExistentFile_ReturnsExecutableNotFound()
    {
        var launcher = new WindowsSandboxLauncher(_options, NullLogger<WindowsSandboxLauncher>.Instance);
        var request = new SandboxLaunchRequest(
            ExecutablePath: Path.Combine(Path.GetTempPath(), "does-not-exist.exe"),
            Arguments: [],
            WorkingDirectory: Path.GetTempPath(),
            Environment: new Dictionary<string, string>(),
            TimeoutMs: 5000,
            MaxMemoryMb: null);

        SandboxLaunchResult result = await launcher.RunAsync(request);

        Assert.Null(result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains("ExecutableNotFound", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sandbox_Validation_NonAbsolutePath_ReturnsExecutableNotFound()
    {
        var launcher = new WindowsSandboxLauncher(_options, NullLogger<WindowsSandboxLauncher>.Instance);
        var request = new SandboxLaunchRequest(
            ExecutablePath: "notepad.exe",
            Arguments: [],
            WorkingDirectory: Path.GetTempPath(),
            Environment: new Dictionary<string, string>(),
            TimeoutMs: 5000,
            MaxMemoryMb: null);

        SandboxLaunchResult result = await launcher.RunAsync(request);

        Assert.Null(result.ExitCode);
        Assert.Contains("ExecutableNotFound", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sandbox_NormalProcess_CapturesStdoutAndExitCode()
    {
        var launcher = new WindowsSandboxLauncher(_options, NullLogger<WindowsSandboxLauncher>.Instance);
        var request = new SandboxLaunchRequest(
            ExecutablePath: _payloadExe,
            Arguments: ["--echo", "CyberPipe Sandbox OK", "--exit", "0"],
            WorkingDirectory: Path.GetDirectoryName(_payloadExe)!,
            Environment: new Dictionary<string, string>(),
            TimeoutMs: 10000,
            MaxMemoryMb: null);

        SandboxLaunchResult result = await launcher.RunAsync(request);

        Assert.Null(result.Error);
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.False(result.KilledByJobLimit);
        Assert.Contains("CyberPipe Sandbox OK", result.Stdout);
    }

    [Fact]
    public async Task Sandbox_NormalProcess_CapturesStderrAndNonZeroExitCode()
    {
        var launcher = new WindowsSandboxLauncher(_options, NullLogger<WindowsSandboxLauncher>.Instance);
        var request = new SandboxLaunchRequest(
            ExecutablePath: _payloadExe,
            Arguments: ["--err", "Custom error output", "--exit", "42"],
            WorkingDirectory: Path.GetDirectoryName(_payloadExe)!,
            Environment: new Dictionary<string, string>(),
            TimeoutMs: 10000,
            MaxMemoryMb: null);

        SandboxLaunchResult result = await launcher.RunAsync(request);

        Assert.Null(result.Error);
        Assert.Equal(42, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains("Custom error output", result.Stderr);
    }

    [Fact]
    public async Task Sandbox_HangingProcess_TerminatedByTimeout()
    {
        var launcher = new WindowsSandboxLauncher(_options, NullLogger<WindowsSandboxLauncher>.Instance);
        var request = new SandboxLaunchRequest(
            ExecutablePath: _payloadExe,
            Arguments: ["--hang", "10000"],
            WorkingDirectory: Path.GetDirectoryName(_payloadExe)!,
            Environment: new Dictionary<string, string>(),
            TimeoutMs: 500,
            MaxMemoryMb: null);

        SandboxLaunchResult result = await launcher.RunAsync(request);

        Assert.True(result.TimedOut);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Sandbox_MemoryLimit_KilledByJobObject()
    {
        var launcher = new WindowsSandboxLauncher(_options, NullLogger<WindowsSandboxLauncher>.Instance);
        // Request tight 30MB memory limit, payload tries to allocate 200MB
        var request = new SandboxLaunchRequest(
            ExecutablePath: _payloadExe,
            Arguments: ["--alloc-mb", "200"],
            WorkingDirectory: Path.GetDirectoryName(_payloadExe)!,
            Environment: new Dictionary<string, string>(),
            TimeoutMs: 10000,
            MaxMemoryMb: 30);

        SandboxLaunchResult result = await launcher.RunAsync(request);

        Assert.True(result.KilledByJobLimit || result.ExitCode != 0);
    }

    [Fact]
    public async Task Sandbox_EnvironmentIsolation_DoesNotInheritParentSecrets()
    {
        Environment.SetEnvironmentVariable("PARENT_SECRET_TOKEN", "super-secret-parent-token-12345");

        try
        {
            var launcher = new WindowsSandboxLauncher(_options, NullLogger<WindowsSandboxLauncher>.Instance);
            var request = new SandboxLaunchRequest(
                ExecutablePath: _payloadExe,
                Arguments: [
                    "--check-env", "PARENT_SECRET_TOKEN",
                    "--check-env", "ALLOWED_PUBLIC_KEY"
                ],
                WorkingDirectory: Path.GetDirectoryName(_payloadExe)!,
                Environment: new Dictionary<string, string>
                {
                    ["ALLOWED_PUBLIC_KEY"] = "public-key-value"
                },
                TimeoutMs: 10000,
                MaxMemoryMb: null);

            SandboxLaunchResult result = await launcher.RunAsync(request);

            Assert.Null(result.Error);
            Assert.Contains("<NOT_FOUND>", result.Stdout);
            Assert.Contains("public-key-value", result.Stdout);
            Assert.DoesNotContain("super-secret-parent-token-12345", result.Stdout);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PARENT_SECRET_TOKEN", null);
        }
    }

    [Fact]
    public async Task Sandbox_RepeatedLaunches_SucceedReliably()
    {
        var launcher = new WindowsSandboxLauncher(_options, NullLogger<WindowsSandboxLauncher>.Instance);

        for (int i = 0; i < 5; i++)
        {
            var request = new SandboxLaunchRequest(
                ExecutablePath: _payloadExe,
                Arguments: ["--echo", $"Run #{i}", "--exit", "0"],
                WorkingDirectory: Path.GetDirectoryName(_payloadExe)!,
                Environment: new Dictionary<string, string>(),
                TimeoutMs: 5000,
                MaxMemoryMb: null);

            SandboxLaunchResult result = await launcher.RunAsync(request);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains($"Run #{i}", result.Stdout);
        }
    }
}

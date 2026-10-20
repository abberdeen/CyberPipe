using System.Security.Cryptography;
using System.Text.Json;
using CyberPipe.Chains.Packages;
using CyberPipe.Chains.Runners;
using CyberPipe.Contracts;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class ScriptRunnerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _workspaceDir;

    public ScriptRunnerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cyberpipe_runner_tests_" + Guid.NewGuid().ToString("N"));
        _workspaceDir = Path.Combine(_tempRoot, "workspace");
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(_workspaceDir);
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

    #region Test Doubles

    private sealed class FakeSandboxLauncher : ISandboxLauncher, ISandboxProvider
    {
        public int InvocationsCount { get; private set; }
        public List<SandboxLaunchRequest> Invocations { get; } = [];
        public Func<SandboxLaunchRequest, CancellationToken, Task<SandboxLaunchResult>>? Handler { get; set; }

        public Task<SandboxLaunchResult> RunAsync(SandboxLaunchRequest request, CancellationToken cancellationToken = default)
        {
            InvocationsCount++;
            Invocations.Add(request);

            if (Handler is not null)
            {
                return Handler(request, cancellationToken);
            }

            return Task.FromResult(new SandboxLaunchResult(
                ExitCode: 0,
                TimedOut: false,
                KilledByJobLimit: false,
                Stdout: "{\"output\":\"processed\"}",
                Stderr: "",
                Error: null));
        }
    }

    #endregion

    #region Package Creation Helper

    private string CreatePackage(
        string slug,
        string language,
        string environment,
        string entrypoint,
        string? schemaInputProps = null,
        string? schemaOutputProps = null,
        string? sampleInputJson = null,
        string? expectedOutputJson = null,
        List<NetrunnerInputFileDto>? inputFiles = null,
        List<NetrunnerOutputFileDto>? outputFiles = null)
    {
        string version = "1.0.0";
        string packageDir = Path.Combine(_tempRoot, slug, version);
        Directory.CreateDirectory(packageDir);
        Directory.CreateDirectory(Path.Combine(packageDir, "tests"));

        string skillMd = "# Skill documentation";
        string schemaInput = $$"""
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "properties": {
                {{schemaInputProps ?? "\"input\": { \"type\": \"string\" }"}}
              },
              "required": ["input"],
              "additionalProperties": false
            }
            """;

        string schemaOutput = $$"""
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "properties": {
                {{schemaOutputProps ?? "\"output\": { \"type\": \"string\" }"}}
              },
              "required": ["output"],
              "additionalProperties": false
            }
            """;

        string fixture = $$"""
            {
              "sampleInput": {{sampleInputJson ?? "{\"input\":\"test\"}"}},
              "expectedOutput": {{expectedOutputJson ?? "{\"output\":\"processed\"}"}}
            }
            """;

        string script = "// Entrypoint script\n";

        File.WriteAllText(Path.Combine(packageDir, "SKILL.md"), skillMd);
        File.WriteAllText(Path.Combine(packageDir, "schema.input.json"), schemaInput);
        File.WriteAllText(Path.Combine(packageDir, "schema.output.json"), schemaOutput);
        File.WriteAllText(Path.Combine(packageDir, "tests", "sample.fixture.json"), fixture);
        File.WriteAllText(Path.Combine(packageDir, entrypoint), script);

        // Calculate hashes
        var hashes = new Dictionary<string, string>
        {
            ["SKILL.md"] = HashFile(Path.Combine(packageDir, "SKILL.md")),
            ["schema.input.json"] = HashFile(Path.Combine(packageDir, "schema.input.json")),
            ["schema.output.json"] = HashFile(Path.Combine(packageDir, "schema.output.json")),
            ["tests/sample.fixture.json"] = HashFile(Path.Combine(packageDir, "tests", "sample.fixture.json")),
            [entrypoint] = HashFile(Path.Combine(packageDir, entrypoint)),
        };

        var manifestObj = new
        {
            slug,
            name = "Test " + slug,
            version,
            description = "Test package",
            runtime = new
            {
                language,
                languageVersion = "1.0",
                environment,
                environmentVersion = "1.0",
                entrypoint,
                timeoutMs = 15000,
                maxMemoryMb = 128,
                deterministic = true,
            },
            io = new
            {
                inputSchemaFile = "schema.input.json",
                outputSchemaFile = "schema.output.json",
                inputFormats = new[] { "application/json" },
                outputFormats = new[] { "application/json" },
                inputFiles = inputFiles ?? [],
                outputFiles = outputFiles ?? [],
            },
            permissions = new
            {
                allowNetwork = false,
                allowedDomains = Array.Empty<string>(),
                filesystemScope = "IsolatedRunWorkspaceOnly",
                requiredVaultSecretRefs = Array.Empty<string>(),
            },
            integrity = new
            {
                filesSha256 = hashes,
            },
        };

        string manifestJson = JsonSerializer.Serialize(manifestObj, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(packageDir, "netrunner.manifest.json"), manifestJson);

        return packageDir;
    }

    private static string HashFile(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    #endregion

    [Fact]
    public async Task DryRunGate_ThreePackages_SucceedAndMatchExpectedOutput()
    {
        // 1. CSharp package
        string csPkg = CreatePackage("csharp-pkg", "CSharp", "Roslyn", "skill-hack.csx");
        // 2. Python package
        string pyPkg = CreatePackage("python-pkg", "Python", "CPython", "skill-hack.py");
        // 3. JavaScript package
        string jsPkg = CreatePackage("js-pkg", "JavaScript", "Node", "skill-hack.js");

        var fakeSandbox = new FakeSandboxLauncher();
        var registry = ScriptRunnerRegistry.CreateDefault(fakeSandbox);

        // Dry run CSharp
        ScriptRunResult csResult = await registry.DryRunPackageAsync(csPkg, Path.Combine(_workspaceDir, "cs"));
        Assert.Equal(ScriptRunStatus.Succeeded, csResult.Status);
        Assert.Equal("{\"output\":\"processed\"}", csResult.OutputJson);

        // Dry run Python
        ScriptRunResult pyResult = await registry.DryRunPackageAsync(pyPkg, Path.Combine(_workspaceDir, "py"));
        Assert.Equal(ScriptRunStatus.Succeeded, pyResult.Status);
        Assert.Equal("{\"output\":\"processed\"}", pyResult.OutputJson);

        // Dry run JS
        ScriptRunResult jsResult = await registry.DryRunPackageAsync(jsPkg, Path.Combine(_workspaceDir, "js"));
        Assert.Equal(ScriptRunStatus.Succeeded, jsResult.Status);
        Assert.Equal("{\"output\":\"processed\"}", jsResult.OutputJson);

        Assert.Equal(3, fakeSandbox.InvocationsCount);
    }

    [Fact]
    public async Task DryRunGate_MismatchWithExpectedOutput_FailsDryRunGate()
    {
        string pkg = CreatePackage("mismatch-pkg", "Python", "CPython", "skill-hack.py",
            expectedOutputJson: "{\"output\":\"expected_value\"}");

        var fakeSandbox = new FakeSandboxLauncher
        {
            Handler = (_, _) => Task.FromResult(new SandboxLaunchResult(
                ExitCode: 0,
                TimedOut: false,
                KilledByJobLimit: false,
                Stdout: "{\"output\":\"different_value\"}",
                Stderr: "",
                Error: null))
        };

        var registry = ScriptRunnerRegistry.CreateDefault(fakeSandbox);

        ScriptRunResult result = await registry.DryRunPackageAsync(pkg, _workspaceDir);

        Assert.Equal(ScriptRunStatus.ScriptFailed, result.Status);
        Assert.Contains("Dry-run gate failed", result.ErrorMessage);
    }

    [Fact]
    public async Task PreValidation_InputViolatingSchema_RejectedBeforeSandboxLaunch()
    {
        string pkg = CreatePackage("schema-in-pkg", "Python", "CPython", "skill-hack.py");

        var fakeSandbox = new FakeSandboxLauncher();
        var registry = ScriptRunnerRegistry.CreateDefault(fakeSandbox);

        var validator = new NetrunnerPackageValidator();
        var valResult = validator.ValidatePackage(pkg);
        Assert.True(valResult.IsValid);

        // Input missing required property 'input'
        var request = new ScriptRunRequest
        {
            Manifest = valResult.Manifest!,
            PackageDir = pkg,
            InputJson = "{\"unknownField\": 123}",
            RunWorkspaceDir = _workspaceDir,
        };

        ScriptRunResult result = await registry.RunAsync(request);

        Assert.Equal(ScriptRunStatus.SchemaValidationFailed, result.Status);
        Assert.Contains("Input JSON validation failed", result.ErrorMessage);
        Assert.Contains("required property 'input' is missing", result.ErrorMessage);

        // Sandbox was NOT called!
        Assert.Equal(0, fakeSandbox.InvocationsCount);
    }

    [Fact]
    public async Task PostValidation_OutputViolatingSchema_ReturnsSchemaValidationFailed()
    {
        string pkg = CreatePackage("schema-out-pkg", "Python", "CPython", "skill-hack.py");

        var fakeSandbox = new FakeSandboxLauncher
        {
            // Output has integer instead of required string 'output'
            Handler = (_, _) => Task.FromResult(new SandboxLaunchResult(
                ExitCode: 0,
                TimedOut: false,
                KilledByJobLimit: false,
                Stdout: "{\"output\": 9999}",
                Stderr: "",
                Error: null))
        };

        var registry = ScriptRunnerRegistry.CreateDefault(fakeSandbox);

        var validator = new NetrunnerPackageValidator();
        var valResult = validator.ValidatePackage(pkg);

        var request = new ScriptRunRequest
        {
            Manifest = valResult.Manifest!,
            PackageDir = pkg,
            InputJson = "{\"input\":\"valid\"}",
            RunWorkspaceDir = _workspaceDir,
        };

        ScriptRunResult result = await registry.RunAsync(request);

        Assert.Equal(ScriptRunStatus.SchemaValidationFailed, result.Status);
        Assert.Contains("Output JSON validation failed", result.ErrorMessage);
        Assert.Equal(1, fakeSandbox.InvocationsCount);
    }

    [Fact]
    public async Task InputFiles_Validation_MissingOrExceedingSizeOrBadExt_FailsBeforeSandbox()
    {
        var inputFiles = new List<NetrunnerInputFileDto>
        {
            new()
            {
                Alias = "sourceCsv",
                Required = true,
                AllowedExtensions = [".csv"],
                MaxSizeBytes = 100,
            }
        };

        string pkg = CreatePackage("input-file-pkg", "Python", "CPython", "skill-hack.py", inputFiles: inputFiles);

        var fakeSandbox = new FakeSandboxLauncher();
        var registry = ScriptRunnerRegistry.CreateDefault(fakeSandbox);
        var valResult = new NetrunnerPackageValidator().ValidatePackage(pkg);

        // 1. Missing required alias
        var requestMissing = new ScriptRunRequest
        {
            Manifest = valResult.Manifest!,
            PackageDir = pkg,
            InputJson = "{\"input\":\"test\"}",
            RunWorkspaceDir = _workspaceDir,
            InputFiles = new Dictionary<string, string>(),
        };
        var resMissing = await registry.RunAsync(requestMissing);
        Assert.Equal(ScriptRunStatus.SchemaValidationFailed, resMissing.Status);
        Assert.Contains("sourceCsv", resMissing.ErrorMessage);
        Assert.Equal(0, fakeSandbox.InvocationsCount);

        // 2. Bad extension
        string badFile = Path.Combine(_tempRoot, "data.txt");
        File.WriteAllText(badFile, "line1,line2");
        var requestBadExt = new ScriptRunRequest
        {
            Manifest = valResult.Manifest!,
            PackageDir = pkg,
            InputJson = "{\"input\":\"test\"}",
            RunWorkspaceDir = _workspaceDir,
            InputFiles = new Dictionary<string, string> { ["sourceCsv"] = badFile },
        };
        var resBadExt = await registry.RunAsync(requestBadExt);
        Assert.Equal(ScriptRunStatus.SchemaValidationFailed, resBadExt.Status);
        Assert.Contains("allowedExtensions", resBadExt.ErrorMessage);
        Assert.Equal(0, fakeSandbox.InvocationsCount);

        // 3. Oversized file
        string oversizedFile = Path.Combine(_tempRoot, "oversized.csv");
        File.WriteAllBytes(oversizedFile, new byte[200]); // > 100 maxSizeBytes
        var requestOversized = new ScriptRunRequest
        {
            Manifest = valResult.Manifest!,
            PackageDir = pkg,
            InputJson = "{\"input\":\"test\"}",
            RunWorkspaceDir = _workspaceDir,
            InputFiles = new Dictionary<string, string> { ["sourceCsv"] = oversizedFile },
        };
        var resOversized = await registry.RunAsync(requestOversized);
        Assert.Equal(ScriptRunStatus.SchemaValidationFailed, resOversized.Status);
        Assert.Contains("exceeds maximum allowed size", resOversized.ErrorMessage);
        Assert.Equal(0, fakeSandbox.InvocationsCount);
    }

    [Fact]
    public async Task OutputFiles_Validation_MissingRequired_ReturnsSchemaValidationFailed()
    {
        var outputFiles = new List<NetrunnerOutputFileDto>
        {
            new()
            {
                Alias = "summaryReport",
                FilenamePattern = "summary.json",
                MimeType = "application/json",
                Required = true,
                MaxSizeBytes = 5000,
            }
        };

        string pkg = CreatePackage("out-file-pkg", "Python", "CPython", "skill-hack.py", outputFiles: outputFiles);

        var fakeSandbox = new FakeSandboxLauncher
        {
            // Sandbox produces valid JSON on stdout, but does NOT create summary.json
            Handler = (_, _) => Task.FromResult(new SandboxLaunchResult(
                ExitCode: 0,
                TimedOut: false,
                KilledByJobLimit: false,
                Stdout: "{\"output\":\"processed\"}",
                Stderr: "",
                Error: null))
        };

        var registry = ScriptRunnerRegistry.CreateDefault(fakeSandbox);
        var valResult = new NetrunnerPackageValidator().ValidatePackage(pkg);

        var request = new ScriptRunRequest
        {
            Manifest = valResult.Manifest!,
            PackageDir = pkg,
            InputJson = "{\"input\":\"test\"}",
            RunWorkspaceDir = _workspaceDir,
        };

        ScriptRunResult result = await registry.RunAsync(request);

        Assert.Equal(ScriptRunStatus.SchemaValidationFailed, result.Status);
        Assert.Contains("Required output file 'summaryReport'", result.ErrorMessage);
    }

    [Fact]
    public async Task OutputFiles_Validation_ProducedOutputFiles_CollectedWithSha256AndSize()
    {
        var outputFiles = new List<NetrunnerOutputFileDto>
        {
            new()
            {
                Alias = "summaryReport",
                FilenamePattern = "summary.json",
                MimeType = "application/json",
                Required = true,
                MaxSizeBytes = 5000,
            }
        };

        string pkg = CreatePackage("out-collected-pkg", "Python", "CPython", "skill-hack.py", outputFiles: outputFiles);

        var fakeSandbox = new FakeSandboxLauncher
        {
            Handler = (req, _) =>
            {
                // Script writes output file into workspace
                string filePath = Path.Combine(req.WorkingDirectory, "summary.json");
                File.WriteAllText(filePath, "{\"status\":\"ok\"}");

                return Task.FromResult(new SandboxLaunchResult(
                    ExitCode: 0,
                    TimedOut: false,
                    KilledByJobLimit: false,
                    Stdout: "{\"output\":\"processed\"}",
                    Stderr: "",
                    Error: null));
            }
        };

        var registry = ScriptRunnerRegistry.CreateDefault(fakeSandbox);
        var valResult = new NetrunnerPackageValidator().ValidatePackage(pkg);

        var request = new ScriptRunRequest
        {
            Manifest = valResult.Manifest!,
            PackageDir = pkg,
            InputJson = "{\"input\":\"test\"}",
            RunWorkspaceDir = _workspaceDir,
        };

        ScriptRunResult result = await registry.RunAsync(request);

        Assert.Equal(ScriptRunStatus.Succeeded, result.Status);
        Assert.Single(result.OutputFiles);
        ScriptOutputFile collected = result.OutputFiles[0];
        Assert.Equal("summaryReport", collected.Alias);
        Assert.True(collected.SizeBytes > 0);
        Assert.False(string.IsNullOrWhiteSpace(collected.Sha256));
    }

    [Fact]
    public async Task Execution_Timeout_ReturnsTimedOutWithTail()
    {
        string pkg = CreatePackage("timeout-pkg", "Python", "CPython", "skill-hack.py");

        var fakeSandbox = new FakeSandboxLauncher
        {
            Handler = (_, _) => Task.FromResult(new SandboxLaunchResult(
                ExitCode: null,
                TimedOut: true,
                KilledByJobLimit: false,
                Stdout: "partial work before hang",
                Stderr: "warning: hang detected",
                Error: "Process was killed due to timeout"))
        };

        var registry = ScriptRunnerRegistry.CreateDefault(fakeSandbox);
        var valResult = new NetrunnerPackageValidator().ValidatePackage(pkg);

        var request = new ScriptRunRequest
        {
            Manifest = valResult.Manifest!,
            PackageDir = pkg,
            InputJson = "{\"input\":\"test\"}",
            RunWorkspaceDir = _workspaceDir,
            TimeoutMs = 1000,
        };

        ScriptRunResult result = await registry.RunAsync(request);

        Assert.Equal(ScriptRunStatus.TimedOut, result.Status);
        Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("partial work", result.StdoutTail);
        Assert.Contains("warning: hang", result.StderrTail);
    }

    [Fact]
    public async Task Execution_Cancellation_ReturnsCancelled()
    {
        string pkg = CreatePackage("cancel-pkg", "Python", "CPython", "skill-hack.py");

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled

        var fakeSandbox = new FakeSandboxLauncher
        {
            Handler = (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new SandboxLaunchResult(0, false, false, "", "", null));
            }
        };

        var registry = ScriptRunnerRegistry.CreateDefault(fakeSandbox);
        var valResult = new NetrunnerPackageValidator().ValidatePackage(pkg);

        var request = new ScriptRunRequest
        {
            Manifest = valResult.Manifest!,
            PackageDir = pkg,
            InputJson = "{\"input\":\"test\"}",
            RunWorkspaceDir = _workspaceDir,
        };

        ScriptRunResult result = await registry.RunAsync(request, cts.Token);

        Assert.Equal(ScriptRunStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task Registry_UnsupportedRuntime_ReturnsUnsupportedRuntimeWithPair()
    {
        string pkg = CreatePackage("ruby-pkg", "Ruby", "MRI", "skill-hack.rb");

        var fakeSandbox = new FakeSandboxLauncher();
        var registry = ScriptRunnerRegistry.CreateDefault(fakeSandbox);
        var valResult = new NetrunnerPackageValidator().ValidatePackage(pkg);

        var request = new ScriptRunRequest
        {
            Manifest = valResult.Manifest!,
            PackageDir = pkg,
            InputJson = "{\"input\":\"test\"}",
            RunWorkspaceDir = _workspaceDir,
        };

        ScriptRunResult result = await registry.RunAsync(request);

        Assert.Equal(ScriptRunStatus.UnsupportedRuntime, result.Status);
        Assert.Contains("Ruby", result.ErrorMessage);
        Assert.Contains("MRI", result.ErrorMessage);
    }

    [Fact]
    public async Task DeferRetry_ExitCode75_ReturnsDeferRetryWithRetrySeconds()
    {
        string pkg = CreatePackage("retry-exit-pkg", "Python", "CPython", "skill-hack.py");

        var fakeSandbox = new FakeSandboxLauncher
        {
            Handler = (_, _) => Task.FromResult(new SandboxLaunchResult(
                ExitCode: 75,
                TimedOut: false,
                KilledByJobLimit: false,
                Stdout: "{\"output\":\"not_ready\"}",
                Stderr: "service busy, retry later",
                Error: null))
        };

        var registry = ScriptRunnerRegistry.CreateDefault(fakeSandbox);
        var valResult = new NetrunnerPackageValidator().ValidatePackage(pkg);

        var request = new ScriptRunRequest
        {
            Manifest = valResult.Manifest!,
            PackageDir = pkg,
            InputJson = "{\"input\":\"test\"}",
            RunWorkspaceDir = _workspaceDir,
        };

        ScriptRunResult result = await registry.RunAsync(request);

        Assert.Equal(ScriptRunStatus.DeferRetry, result.Status);
        Assert.Equal(75, result.ExitCode);
        Assert.True(result.RetryAfterSeconds > 0);
    }

    [Fact]
    public async Task DeferRetry_NetrunnerControlJson_ReturnsDeferRetryWithRetrySeconds()
    {
        string pkg = CreatePackage("retry-ctrl-pkg", "Python", "CPython", "skill-hack.py");

        string deferJson = "{\"$netrunnerControl\":\"DeferRetry\",\"retryAfterSeconds\":300,\"reason\":\"Rate limit\"}";

        var fakeSandbox = new FakeSandboxLauncher
        {
            Handler = (_, _) => Task.FromResult(new SandboxLaunchResult(
                ExitCode: 0,
                TimedOut: false,
                KilledByJobLimit: false,
                Stdout: deferJson,
                Stderr: "",
                Error: null))
        };

        var registry = ScriptRunnerRegistry.CreateDefault(fakeSandbox);
        var valResult = new NetrunnerPackageValidator().ValidatePackage(pkg);

        var request = new ScriptRunRequest
        {
            Manifest = valResult.Manifest!,
            PackageDir = pkg,
            InputJson = "{\"input\":\"test\"}",
            RunWorkspaceDir = _workspaceDir,
        };

        ScriptRunResult result = await registry.RunAsync(request);

        Assert.Equal(ScriptRunStatus.DeferRetry, result.Status);
        Assert.Equal(300, result.RetryAfterSeconds);
    }

    [Fact]
    public async Task OutputTail_CappedAtCeilingWithTruncatedMarker()
    {
        string pkg = CreatePackage("tail-cap-pkg", "Python", "CPython", "skill-hack.py");

        // Generate 100 KB stdout
        string longStdout = new string('A', 100_000);

        var fakeSandbox = new FakeSandboxLauncher
        {
            Handler = (_, _) => Task.FromResult(new SandboxLaunchResult(
                ExitCode: 0,
                TimedOut: false,
                KilledByJobLimit: false,
                Stdout: longStdout,
                Stderr: "small err",
                Error: null))
        };

        var schemaValidator = new JsonSchemaDraft202012Validator();
        // Set maxTailBytes = 1024
        var runner = new PythonScriptRunner(fakeSandbox, schemaValidator, maxTailBytes: 1024);
        var registry = new ScriptRunnerRegistry();
        registry.RegisterRunner(runner);

        var valResult = new NetrunnerPackageValidator().ValidatePackage(pkg);

        var request = new ScriptRunRequest
        {
            Manifest = valResult.Manifest!,
            PackageDir = pkg,
            InputJson = "{\"input\":\"test\"}",
            RunWorkspaceDir = _workspaceDir,
        };

        ScriptRunResult result = await registry.RunAsync(request);

        Assert.Contains("[truncated]", result.StdoutTail);
        Assert.True(result.StdoutTail.Length <= 1024 + "\n[truncated]".Length);
    }

    private sealed class DenyGuard : IAntiWipeGuard
    {
        public Task<WipeGuardDecision> EvaluateAsync(DestructiveOperation operation, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WipeGuardDecision
            {
                Outcome = WipeGuardDecisionOutcome.Deny,
                ReasonCode = WipeGuardReasonCodes.DropStatement,
                Message = "DROP detected",
                OperationFingerprint = operation.ComputeFingerprint(),
            });

        public Task<WipeGuardDecision> EvaluateConfirmedAsync(DestructiveOperation operation, ApprovalProof proof, CancellationToken cancellationToken = default) =>
            EvaluateAsync(operation, cancellationToken);
    }

    [Fact]
    public async Task Runner_AntiWipeDeny_DoesNotLaunchSandbox()
    {
        string pkg = CreatePackage("guarded-pkg", "Python", "CPython", "skill-hack.py");
        var fakeSandbox = new FakeSandboxLauncher();
        var runner = new PythonScriptRunner(fakeSandbox, new JsonSchemaDraft202012Validator(), antiWipeGuard: new DenyGuard());
        var valResult = new NetrunnerPackageValidator().ValidatePackage(pkg);

        ScriptRunResult result = await runner.RunAsync(new ScriptRunRequest
        {
            Manifest = valResult.Manifest!,
            PackageDir = pkg,
            InputJson = "{\"sql\":\"DROP TABLE users\"}",
            RunWorkspaceDir = _workspaceDir,
        });

        Assert.NotEqual(ScriptRunStatus.Succeeded, result.Status);
        Assert.Contains("Anti-wipe", result.ErrorMessage ?? string.Empty);
        Assert.Equal(0, fakeSandbox.InvocationsCount);
    }

    [Fact]
    public void NoDirectProcessStart_InRunnersProjectSourceFiles()
    {
        // Criterion: Ни один раннер не вызывает Process.Start напрямую
        string solutionDir = FindSolutionDir();
        string runnersDir = Path.Combine(solutionDir, "src", "CyberPipe.Chains", "Runners");

        Assert.True(Directory.Exists(runnersDir), $"Runners directory '{runnersDir}' must exist.");

        string[] sourceFiles = Directory.GetFiles(runnersDir, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(sourceFiles);

        foreach (string file in sourceFiles)
        {
            string content = File.ReadAllText(file);
            Assert.DoesNotContain("Process.Start", content, StringComparison.Ordinal);
        }
    }

    private static string FindSolutionDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CyberPipe.slnx")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("CyberPipe.slnx root not found.");
    }
}

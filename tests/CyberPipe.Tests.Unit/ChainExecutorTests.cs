using System.Collections.Concurrent;
using CyberPipe.Chains;
using CyberPipe.Chains.Execution;
using CyberPipe.Contracts;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class ChainExecutorTests
{
    #region Test Doubles

    private sealed class FakeSkillInvoker : ISkillInvoker
    {
        public int InvocationsCount { get; private set; }
        public List<SkillInvocationRequest> Requests { get; } = [];
        public Func<SkillInvocationRequest, Task<SkillInvocationResult>>? Handler { get; set; }

        public Task<SkillInvocationResult> InvokeSkillAsync(SkillInvocationRequest request, CancellationToken cancellationToken = default)
        {
            InvocationsCount++;
            Requests.Add(request);

            if (Handler is not null)
            {
                return Handler(request);
            }

            return Task.FromResult(new SkillInvocationResult
            {
                IsSuccess = true,
                OutputJson = "{\"skillResult\":\"ok\"}",
                OutputFiles = ["artifact.txt"],
            });
        }
    }

    private sealed class FakeDeliveryPort : IDeliveryPort
    {
        public int InvocationsCount { get; private set; }
        public List<DeliveryRequest> Requests { get; } = [];
        public Func<DeliveryRequest, Task<DeliveryResult>>? Handler { get; set; }

        public Task<DeliveryResult> SendAsync(DeliveryRequest request, CancellationToken cancellationToken = default)
        {
            InvocationsCount++;
            Requests.Add(request);

            if (Handler is not null)
            {
                return Handler(request);
            }

            return Task.FromResult(new DeliveryResult
            {
                IsSuccess = true,
                DeliveredMessageId = MessageId.New(),
                DeliveredAtUtc = DateTimeOffset.UtcNow,
            });
        }
    }

    private sealed class FakeHitlApprovalGate : IHitlApprovalGate
    {
        public int InvocationsCount { get; private set; }
        public List<HitlApprovalRequest> Requests { get; } = [];
        public Func<HitlApprovalRequest, Task<HitlApprovalResult>>? Handler { get; set; }

        public Task<HitlApprovalResult> RequestApprovalAsync(HitlApprovalRequest request, CancellationToken cancellationToken = default)
        {
            InvocationsCount++;
            Requests.Add(request);

            if (Handler is not null)
            {
                return Handler(request);
            }

            return Task.FromResult(new HitlApprovalResult
            {
                ApprovalId = request.ApprovalId,
                Decision = HitlDecision.Approved,
                OwnerId = "owner-1",
                Channel = ChannelKind.Telegram,
                DecidedAtUtc = DateTimeOffset.UtcNow,
            });
        }
    }

    private sealed class FakeDelayedResumeScheduler : IDelayedResumeScheduler
    {
        public List<(RunId RunId, TimeSpan Delay)> Scheduled { get; } = [];

        public Task ScheduleResumeAsync(RunId runId, TimeSpan delay, CancellationToken cancellationToken = default)
        {
            Scheduled.Add((runId, delay));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeScriptRunner : IScriptRunner
    {
        public int InvocationsCount { get; private set; }
        public List<ScriptRunRequest> Requests { get; } = [];
        public Func<ScriptRunRequest, Task<ScriptRunResult>>? Handler { get; set; }

        public string Language => "Python";
        public string Environment => "CPython";

        public bool CanRun(NetrunnerManifestDto manifest) => true;

        public Task<ScriptRunResult> RunAsync(ScriptRunRequest request, CancellationToken cancellationToken = default)
        {
            InvocationsCount++;
            Requests.Add(request);

            if (Handler is not null)
            {
                return Handler(request);
            }

            return Task.FromResult(ScriptRunResult.Success(
                outputJson: "{\"scriptResult\":\"processed\"}",
                outputFiles: [],
                exitCode: 0,
                stdoutTail: "ok",
                stderrTail: "",
                durationMs: 10));
        }
    }

    private sealed class FakePackageRegistryRepository : IPackageRegistryRepository
    {
        private readonly ConcurrentDictionary<string, PackageRegistrationRecord> _packages = new(StringComparer.Ordinal);

        public void AddPackage(PackageRegistrationRecord record)
        {
            _packages[$"{record.Slug}@{record.Version}"] = record;
        }

        public Task<PackageRegistrationRecord?> GetPackageAsync(string slug, string version, CancellationToken cancellationToken = default)
        {
            _packages.TryGetValue($"{slug}@{version}", out PackageRegistrationRecord? record);
            return Task.FromResult(record);
        }

        public Task<PackageRegistrationRecord> RegisterPackageAsync(NetrunnerManifestDto manifest, string packageDir, string manifestHash, CancellationToken cancellationToken = default)
        {
            var record = new PackageRegistrationRecord
            {
                Slug = manifest.Slug,
                Version = manifest.Version,
                Manifest = manifest,
                PackageDir = packageDir,
                ManifestHash = manifestHash,
                RegisteredAtUtc = DateTimeOffset.UtcNow,
            };
            _packages[$"{manifest.Slug}@{manifest.Version}"] = record;
            return Task.FromResult(record);
        }
    }

    private sealed class DirectScriptRunnerRegistry : IScriptRunnerRegistry
    {
        private readonly IScriptRunner _runner;

        public DirectScriptRunnerRegistry(IScriptRunner runner)
        {
            _runner = runner;
        }

        public IScriptRunner? FindRunner(string language, string environment) => _runner;
        public void RegisterRunner(IScriptRunner runner) { }
        public IReadOnlyCollection<IScriptRunner> GetAllRunners() => [_runner];
        public Task<ScriptRunResult> RunAsync(ScriptRunRequest request, CancellationToken cancellationToken = default) =>
            _runner.RunAsync(request, cancellationToken);
        public Task<ScriptRunResult> DryRunPackageAsync(string packageDir, string workspaceDir, CancellationToken cancellationToken = default) =>
            Task.FromResult(ScriptRunResult.Success("{}", [], 0, "", "", 0));
    }

    #endregion

    #region Helper Methods

    private static (ChainDefinitionDto Definition, TriggerEvent Trigger, PackageRegistrationRecord Package) CreateTestChain(
        string slug = "test-pkg",
        bool requiresHitl = false,
        int maxAttempts = 1,
        int backoffMs = 0)
    {
        var chainId = ChainId.New();

        var trigger = new TriggerEvent
        {
            EventId = Guid.NewGuid(),
            ChainId = chainId,
            Kind = ChainTriggerKind.Webhook,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            PayloadJson = "{\"trigger\":\"hello\"}",
            IdempotencyKey = "key-" + Guid.NewGuid().ToString("N"),
            Source = "/webhook/test",
        };

        var manifest = new NetrunnerManifestDto
        {
            Slug = slug,
            Name = "Test Package",
            Version = "1.0.0",
            Description = "Test package description",
            Runtime = new NetrunnerRuntimeDto
            {
                Language = "Python",
                LanguageVersion = "3.11",
                Environment = "CPython",
                EnvironmentVersion = "3.11",
                Entrypoint = "skill-hack.py",
                TimeoutMs = 15000,
                Deterministic = true,
            },
            Io = new NetrunnerIoDto
            {
                InputSchemaFile = "schema.input.json",
                OutputSchemaFile = "schema.output.json",
            },
            Permissions = new NetrunnerPermissionsDto
            {
                FilesystemScope = "IsolatedRunWorkspaceOnly",
            },
            Integrity = new NetrunnerIntegrityDto(),
        };

        var pkgRecord = new PackageRegistrationRecord
        {
            Slug = slug,
            Version = "1.0.0",
            Manifest = manifest,
            PackageDir = Path.Combine(Path.GetTempPath(), "test_pkg_" + slug),
            ManifestHash = "hash123",
            RegisteredAtUtc = DateTimeOffset.UtcNow,
        };

        var definition = new ChainDefinitionDto
        {
            Id = chainId,
            Name = "Test Linear Chain",
            IsEnabled = true,
            RequiresHitl = requiresHitl,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            RetryPolicy = new ChainRetryPolicyDto
            {
                MaxAttempts = maxAttempts,
                BackoffMs = backoffMs,
            },
            Trigger = new ChainTriggerDto
            {
                Kind = ChainTriggerKind.Webhook,
                WebhookPath = "/webhook/test",
            },
            Skill = new ChainSkillStepDto
            {
                SkillId = "csharp.text-normalizer",
                InputMapping = new Dictionary<string, string> { ["text"] = "{{trigger}}" },
            },
            Script = new ChainScriptStepDto
            {
                PackageSlug = slug,
                PackageVersion = "1.0.0",
            },
            Delivery = new ChainDeliveryStepDto
            {
                Channel = ChannelKind.Telegram,
                Target = "@owner",
                MessageTemplate = "Chain completed! Result: {{scriptResult}}",
                SendFiles = true,
            },
        };

        return (definition, trigger, pkgRecord);
    }

    #endregion

    [Fact]
    public async Task LinearChain_SuccessfulRun_ExecutesInOrderAndCompletes()
    {
        var (def, trigger, pkg) = CreateTestChain();

        var repo = new InMemoryChainExecutionRepository();
        var defRepo = new InMemoryChainDefinitionRepository();
        await defRepo.UpsertAsync(def);

        var pkgRepo = new FakePackageRegistryRepository();
        pkgRepo.AddPackage(pkg);

        var skillInvoker = new FakeSkillInvoker();
        var scriptRunner = new FakeScriptRunner();
        var deliveryPort = new FakeDeliveryPort();
        var runnersRegistry = new DirectScriptRunnerRegistry(scriptRunner);

        var executor = new ChainExecutor(
            repo,
            defRepo,
            skillInvoker,
            runnersRegistry,
            pkgRepo,
            deliveryPort);

        // Act
        RunId runId = await executor.StartAsync(trigger);

        // Assert
        ChainRunState? state = await executor.GetRunAsync(runId);
        Assert.NotNull(state);
        Assert.Equal(ChainRunStatus.Completed, state.Status);

        // Check call counts: each step executed exactly once
        Assert.Equal(1, skillInvoker.InvocationsCount);
        Assert.Equal(1, scriptRunner.InvocationsCount);
        Assert.Equal(1, deliveryPort.InvocationsCount);

        // Check delivery received formatted text
        Assert.Single(deliveryPort.Requests);
        Assert.Equal("Chain completed! Result: processed", deliveryPort.Requests[0].Text);

        // Check checkpoints in repository
        IReadOnlyList<ChainCheckpointDto> checkpoints = await repo.GetCheckpointsAsync(runId);
        Assert.Equal(4, checkpoints.Count); // Trigger, Skill, Script, Delivery
        Assert.All(checkpoints, cp => Assert.Equal(ChainStepState.Completed, cp.State));
    }

    [Fact]
    public async Task ScriptFailure_DeliveryNotCalled_RunMarkedFailed_OnlyScriptRetried()
    {
        var (def, trigger, pkg) = CreateTestChain(maxAttempts: 3, backoffMs: 1);

        var repo = new InMemoryChainExecutionRepository();
        var defRepo = new InMemoryChainDefinitionRepository();
        await defRepo.UpsertAsync(def);

        var pkgRepo = new FakePackageRegistryRepository();
        pkgRepo.AddPackage(pkg);

        var skillInvoker = new FakeSkillInvoker();
        var scriptRunner = new FakeScriptRunner
        {
            Handler = _ => Task.FromResult(ScriptRunResult.Failed(
                ScriptRunStatus.ScriptFailed,
                "Fatal script error",
                exitCode: 1))
        };
        var deliveryPort = new FakeDeliveryPort();
        var runnersRegistry = new DirectScriptRunnerRegistry(scriptRunner);

        var executor = new ChainExecutor(
            repo,
            defRepo,
            skillInvoker,
            runnersRegistry,
            pkgRepo,
            deliveryPort);

        // Act
        RunId runId = await executor.StartAsync(trigger);

        // Assert
        ChainRunState? state = await executor.GetRunAsync(runId);
        Assert.NotNull(state);
        Assert.Equal(ChainRunStatus.Failed, state.Status);

        // Skill was called once (succeeded)
        Assert.Equal(1, skillInvoker.InvocationsCount);

        // Script was retried 3 times according to retryPolicy
        Assert.Equal(3, scriptRunner.InvocationsCount);

        // Delivery was NEVER called!
        Assert.Equal(0, deliveryPort.InvocationsCount);

        // Checkpoint for script is Failed
        IReadOnlyList<ChainCheckpointDto> checkpoints = await repo.GetCheckpointsAsync(runId);
        ChainCheckpointDto scriptCp = Assert.Single(checkpoints, c => c.Step == ChainStepKind.Script);
        Assert.Equal(ChainStepState.Failed, scriptCp.State);
        Assert.Equal(3, scriptCp.AttemptCount);
    }

    [Fact]
    public async Task RestartImitation_AfterScriptCheckpoint_ResumesFromDeliveryWithoutRepeatingSkillOrScript()
    {
        var (def, trigger, pkg) = CreateTestChain();

        var repo = new InMemoryChainExecutionRepository();
        var defRepo = new InMemoryChainDefinitionRepository();
        await defRepo.UpsertAsync(def);

        var pkgRepo = new FakePackageRegistryRepository();
        pkgRepo.AddPackage(pkg);

        // Simulate that Skill and Script have already completed in a prior instance
        RunId runId = RunId.New();
        await repo.CreateRunAsync(runId, def.Id, trigger);

        await repo.SaveCheckpointAsync(new ChainCheckpointDto
        {
            RunId = runId,
            ChainId = def.Id,
            Step = ChainStepKind.Trigger,
            State = ChainStepState.Completed,
            InputJson = trigger.PayloadJson,
            OutputJson = trigger.PayloadJson,
            CheckpointAtUtc = DateTimeOffset.UtcNow,
        });

        await repo.SaveCheckpointAsync(new ChainCheckpointDto
        {
            RunId = runId,
            ChainId = def.Id,
            Step = ChainStepKind.Skill,
            State = ChainStepState.Completed,
            InputJson = trigger.PayloadJson,
            OutputJson = "{\"skillOut\":\"saved\"}",
            CheckpointAtUtc = DateTimeOffset.UtcNow,
        });

        await repo.SaveCheckpointAsync(new ChainCheckpointDto
        {
            RunId = runId,
            ChainId = def.Id,
            Step = ChainStepKind.Script,
            State = ChainStepState.Completed,
            InputJson = "{\"skillOut\":\"saved\"}",
            OutputJson = "{\"scriptResult\":\"from_checkpoint\"}",
            CheckpointAtUtc = DateTimeOffset.UtcNow,
        });

        // Brand new instance of ChainExecutor simulating host restart
        var skillInvoker = new FakeSkillInvoker();
        var scriptRunner = new FakeScriptRunner();
        var deliveryPort = new FakeDeliveryPort();
        var runnersRegistry = new DirectScriptRunnerRegistry(scriptRunner);

        var executor = new ChainExecutor(
            repo,
            defRepo,
            skillInvoker,
            runnersRegistry,
            pkgRepo,
            deliveryPort);

        // Act: Resume
        await executor.ResumeAsync(runId);

        // Assert
        ChainRunState? state = await repo.GetRunAsync(runId);
        Assert.NotNull(state);
        Assert.Equal(ChainRunStatus.Completed, state.Status);

        // Skill and Script were NOT re-invoked!
        Assert.Equal(0, skillInvoker.InvocationsCount);
        Assert.Equal(0, scriptRunner.InvocationsCount);

        // Delivery was called exactly once with checkpoint output!
        Assert.Equal(1, deliveryPort.InvocationsCount);
        Assert.Equal("Chain completed! Result: from_checkpoint", deliveryPort.Requests[0].Text);
    }

    [Fact]
    public async Task RecoveryService_StopAndMarkCancelledByCrash_CancelsUnfinishedWithoutReexecution()
    {
        var (def, trigger, pkg) = CreateTestChain();

        var repo = new InMemoryChainExecutionRepository();
        var defRepo = new InMemoryChainDefinitionRepository();
        await defRepo.UpsertAsync(def);

        var pkgRepo = new FakePackageRegistryRepository();
        pkgRepo.AddPackage(pkg);

        RunId runId = RunId.New();
        await repo.CreateRunAsync(runId, def.Id, trigger);
        await repo.SaveCheckpointAsync(new ChainCheckpointDto
        {
            RunId = runId,
            ChainId = def.Id,
            Step = ChainStepKind.Skill,
            State = ChainStepState.Running, // Step was running when crash happened
            CheckpointAtUtc = DateTimeOffset.UtcNow,
        });

        var skillInvoker = new FakeSkillInvoker();
        var scriptRunner = new FakeScriptRunner();
        var deliveryPort = new FakeDeliveryPort();
        var executor = new ChainExecutor(repo, defRepo, skillInvoker, new DirectScriptRunnerRegistry(scriptRunner), pkgRepo, deliveryPort);

        var recoveryService = new ChainRecoveryService(repo, executor, CrashRecoveryPolicy.StopAndMarkCancelledByCrash);

        // Act
        int recovered = await recoveryService.RecoverAsync();

        // Assert
        Assert.Equal(1, recovered);

        ChainRunState? state = await repo.GetRunAsync(runId);
        Assert.NotNull(state);
        Assert.Equal(ChainRunStatus.CancelledByCrash, state.Status);

        // No steps were re-executed!
        Assert.Equal(0, skillInvoker.InvocationsCount);
        Assert.Equal(0, scriptRunner.InvocationsCount);
        Assert.Equal(0, deliveryPort.InvocationsCount);
    }

    [Fact]
    public async Task RequiresHitl_WaitingForApproval_ResumesOnlyAfterDecision()
    {
        var (def, trigger, pkg) = CreateTestChain(requiresHitl: true);

        var repo = new InMemoryChainExecutionRepository();
        var defRepo = new InMemoryChainDefinitionRepository();
        await defRepo.UpsertAsync(def);

        var pkgRepo = new FakePackageRegistryRepository();
        pkgRepo.AddPackage(pkg);

        var skillInvoker = new FakeSkillInvoker();
        var scriptRunner = new FakeScriptRunner();
        var deliveryPort = new FakeDeliveryPort();
        var hitlGate = new FakeHitlApprovalGate
        {
            // Gate approves
            Handler = req => Task.FromResult(new HitlApprovalResult
            {
                ApprovalId = req.ApprovalId,
                Decision = HitlDecision.Approved,
                OwnerId = "owner-42",
                Channel = ChannelKind.Telegram,
                DecidedAtUtc = DateTimeOffset.UtcNow,
            })
        };

        var executor = new ChainExecutor(
            repo,
            defRepo,
            skillInvoker,
            new DirectScriptRunnerRegistry(scriptRunner),
            pkgRepo,
            deliveryPort,
            hitlGate: hitlGate);

        // Act
        RunId runId = await executor.StartAsync(trigger);

        // Assert
        ChainRunState? state = await repo.GetRunAsync(runId);
        Assert.NotNull(state);
        Assert.Equal(ChainRunStatus.Completed, state.Status);

        // HITL was invoked
        Assert.Equal(1, hitlGate.InvocationsCount);
        // Delivery was called after approval
        Assert.Equal(1, deliveryPort.InvocationsCount);

        IReadOnlyList<ChainCheckpointDto> checkpoints = await repo.GetCheckpointsAsync(runId);
        Assert.Contains(checkpoints, cp => cp.Step == ChainStepKind.Hitl && cp.State == ChainStepState.Completed);
    }

    [Fact]
    public async Task RequiresHitl_RejectedByOwner_StopsBeforeDelivery()
    {
        var (def, trigger, pkg) = CreateTestChain(requiresHitl: true);

        var repo = new InMemoryChainExecutionRepository();
        var defRepo = new InMemoryChainDefinitionRepository();
        await defRepo.UpsertAsync(def);

        var pkgRepo = new FakePackageRegistryRepository();
        pkgRepo.AddPackage(pkg);

        var skillInvoker = new FakeSkillInvoker();
        var scriptRunner = new FakeScriptRunner();
        var deliveryPort = new FakeDeliveryPort();
        var hitlGate = new FakeHitlApprovalGate
        {
            // Owner rejects
            Handler = req => Task.FromResult(new HitlApprovalResult
            {
                ApprovalId = req.ApprovalId,
                Decision = HitlDecision.Rejected,
                Reason = "Incorrect calculation",
                OwnerId = "owner-42",
                Channel = ChannelKind.Telegram,
                DecidedAtUtc = DateTimeOffset.UtcNow,
            })
        };

        var executor = new ChainExecutor(
            repo,
            defRepo,
            skillInvoker,
            new DirectScriptRunnerRegistry(scriptRunner),
            pkgRepo,
            deliveryPort,
            hitlGate: hitlGate);

        // Act
        RunId runId = await executor.StartAsync(trigger);

        // Assert
        ChainRunState? state = await repo.GetRunAsync(runId);
        Assert.NotNull(state);
        Assert.Equal(ChainRunStatus.Failed, state.Status);
        Assert.Contains("Rejected", state.Error?.Code);

        // Delivery was NOT called
        Assert.Equal(0, deliveryPort.InvocationsCount);
    }

    [Fact]
    public async Task RepeatedStart_SameIdempotencyKey_ReturnsSameRunId()
    {
        var (def, trigger, pkg) = CreateTestChain();

        var repo = new InMemoryChainExecutionRepository();
        var defRepo = new InMemoryChainDefinitionRepository();
        await defRepo.UpsertAsync(def);

        var pkgRepo = new FakePackageRegistryRepository();
        pkgRepo.AddPackage(pkg);

        var skillInvoker = new FakeSkillInvoker();
        var scriptRunner = new FakeScriptRunner();
        var deliveryPort = new FakeDeliveryPort();

        var executor = new ChainExecutor(
            repo,
            defRepo,
            skillInvoker,
            new DirectScriptRunnerRegistry(scriptRunner),
            pkgRepo,
            deliveryPort);

        // Act: Start twice with exact same trigger (same ChainId + IdempotencyKey)
        RunId firstRunId = await executor.StartAsync(trigger);
        RunId secondRunId = await executor.StartAsync(trigger);

        // Assert
        Assert.Equal(firstRunId, secondRunId);

        // Pipeline ran only once
        Assert.Equal(1, skillInvoker.InvocationsCount);
        Assert.Equal(1, scriptRunner.InvocationsCount);
        Assert.Equal(1, deliveryPort.InvocationsCount);
    }

    [Fact]
    public async Task DeferRetry_SetsWaitingForRetryAndSchedulesDelayedResume()
    {
        var (def, trigger, pkg) = CreateTestChain();

        var repo = new InMemoryChainExecutionRepository();
        var defRepo = new InMemoryChainDefinitionRepository();
        await defRepo.UpsertAsync(def);

        var pkgRepo = new FakePackageRegistryRepository();
        pkgRepo.AddPackage(pkg);

        var skillInvoker = new FakeSkillInvoker();
        var scriptRunner = new FakeScriptRunner
        {
            Handler = _ => Task.FromResult(ScriptRunResult.Failed(
                ScriptRunStatus.DeferRetry,
                "Need external data",
                exitCode: 75,
                retryAfterSeconds: 180))
        };
        var deliveryPort = new FakeDeliveryPort();
        var resumeScheduler = new FakeDelayedResumeScheduler();

        var executor = new ChainExecutor(
            repo,
            defRepo,
            skillInvoker,
            new DirectScriptRunnerRegistry(scriptRunner),
            pkgRepo,
            deliveryPort,
            resumeScheduler: resumeScheduler);

        // Act
        RunId runId = await executor.StartAsync(trigger);

        // Assert
        ChainRunState? state = await repo.GetRunAsync(runId);
        Assert.NotNull(state);
        Assert.Equal(ChainRunStatus.WaitingForRetry, state.Status);

        // Delivery not called
        Assert.Equal(0, deliveryPort.InvocationsCount);

        // Scheduler notified with 180 seconds delay
        Assert.Single(resumeScheduler.Scheduled);
        Assert.Equal(runId, resumeScheduler.Scheduled[0].RunId);
        Assert.Equal(TimeSpan.FromSeconds(180), resumeScheduler.Scheduled[0].Delay);
    }
}

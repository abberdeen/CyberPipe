using System.Collections.Concurrent;
using CyberPipe.Chains;
using CyberPipe.Chains.Execution;
using CyberPipe.Chains.Hitl;
using CyberPipe.Contracts;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class HitlApprovalTests
{
    private sealed class FakeHitlCardSender : IHitlCardSender
    {
        public ChannelKind Channel { get; }
        public List<HitlApprovalRequest> SentRequests { get; } = [];
        public bool ShouldThrow { get; set; }

        public FakeHitlCardSender(ChannelKind channel)
        {
            Channel = channel;
        }

        public Task SendCardAsync(HitlApprovalRequest request, CancellationToken cancellationToken = default)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException($"Sender for {Channel} failed intentionally.");
            }

            SentRequests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOwnerGuard : IOwnerGuard
    {
        public HashSet<string> OwnerIds { get; } = ["owner-1", "owner-admin"];

        public Task<bool> IsOwnerAsync(string userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OwnerIds.Contains(userId));
        }
    }

    private sealed class FakeChainExecutor : IChainExecutor
    {
        public List<RunId> ResumedRuns { get; } = [];
        public List<RunId> CancelledRuns { get; } = [];

        public Task<RunId> StartAsync(TriggerEvent triggerEvent, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RunId(Guid.NewGuid()));

        public Task ResumeAsync(RunId runId, CancellationToken cancellationToken = default)
        {
            ResumedRuns.Add(runId);
            return Task.CompletedTask;
        }

        public Task CancelAsync(RunId runId, CancellationToken cancellationToken = default)
        {
            CancelledRuns.Add(runId);
            return Task.CompletedTask;
        }

        public Task<ChainRunState?> GetRunAsync(RunId runId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ChainRunState?>(null);
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

    private sealed class FakeSchemaValidator : ISchemaValidator
    {
        public List<string> ErrorsToReturn { get; set; } = [];

        public IReadOnlyList<string> Validate(string schemaJson, string payloadJson)
        {
            return ErrorsToReturn;
        }
    }

    private static (HitlApprovalRequest Request, Guid ApprovalId, RunId RunId, ChainId ChainId) CreateSampleRequest(
        TimeSpan? lifetime = null)
    {
        var approvalId = Guid.NewGuid();
        var runId = new RunId(Guid.NewGuid());
        var chainId = new ChainId(Guid.NewGuid());
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeSpan duration = lifetime ?? TimeSpan.FromHours(24);

        var request = new HitlApprovalRequest
        {
            ApprovalId = approvalId,
            RunId = runId,
            ChainId = chainId,
            Title = "Approval: Test Chain Output",
            PreviewText = "Results: 42 records processed",
            PayloadJson = "{\"status\": \"ready\", \"count\": 42}",
            AttachFiles = [new MessageAttachmentDto { FileName = "output.csv", ContentType = "text/csv" }],
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(duration),
        };

        return (request, approvalId, runId, chainId);
    }

    [Fact]
    public async Task Approve_ValidOwner_ResolvesAndResumesExecutor()
    {
        // Arrange
        var repo = new InMemoryHitlApprovalRepository();
        var telegramSender = new FakeHitlCardSender(ChannelKind.Telegram);
        var desktopSender = new FakeHitlCardSender(ChannelKind.Desktop);
        var ownerGuard = new FakeOwnerGuard();
        var executor = new FakeChainExecutor();
        var executionRepo = new InMemoryChainExecutionRepository();

        var service = new HitlApprovalService(
            repo,
            [telegramSender, desktopSender],
            ownerGuard,
            executor,
            executions: executionRepo);

        var (request, approvalId, runId, _) = CreateSampleRequest();
        await service.CreateAsync(request);

        // Cards were broadcast to both channels
        Assert.Single(telegramSender.SentRequests);
        Assert.Single(desktopSender.SentRequests);

        // Act
        HitlApprovalResult result = await service.ApproveAsync(approvalId, "owner-1", ChannelKind.Telegram);

        // Assert
        Assert.Equal(HitlDecision.Approved, result.Decision);
        Assert.Equal("owner-1", result.OwnerId);
        Assert.Equal(ChannelKind.Telegram, result.Channel);

        // Check repository state
        HitlApprovalRecord? stored = await repo.GetAsync(approvalId);
        Assert.NotNull(stored);
        Assert.Equal(HitlStatus.Resolved, stored.Status);
        Assert.NotNull(stored.Resolution);
        Assert.Equal(HitlDecision.Approved, stored.Resolution.Decision);

        // Executor was resumed once
        Assert.Single(executor.ResumedRuns);
        Assert.Equal(runId, executor.ResumedRuns[0]);

        // Checkpoint was recorded
        IReadOnlyList<ChainCheckpointDto> checkpoints = await executionRepo.GetCheckpointsAsync(runId);
        Assert.Contains(checkpoints, cp => cp.Step == ChainStepKind.Hitl && cp.State == ChainStepState.Completed);
    }

    [Fact]
    public async Task Edit_ValidPayloadAndSchema_ResolvesWithEditedPayloadAndResumes()
    {
        // Arrange
        var repo = new InMemoryHitlApprovalRepository();
        var sender = new FakeHitlCardSender(ChannelKind.Telegram);
        var ownerGuard = new FakeOwnerGuard();
        var executor = new FakeChainExecutor();
        var executionRepo = new InMemoryChainExecutionRepository();
        var defRepo = new InMemoryChainDefinitionRepository();
        var pkgRepo = new FakePackageRegistryRepository();
        var schemaValidator = new FakeSchemaValidator();

        string tempPkgDir = Path.Combine(Path.GetTempPath(), "test_pkg_hitl_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempPkgDir);
        string schemaFile = Path.Combine(tempPkgDir, "schema.output.json");
        await File.WriteAllTextAsync(schemaFile, "{\"type\": \"object\"}");

        var manifest = new NetrunnerManifestDto
        {
            Slug = "sample-worker",
            Name = "Sample Worker",
            Version = "1.0.0",
            Description = "Sample worker description",
            Runtime = new NetrunnerRuntimeDto
            {
                Language = "Python",
                LanguageVersion = "3.11",
                Environment = "CPython",
                EnvironmentVersion = "3.11",
                Entrypoint = "main.py",
                TimeoutMs = 15000,
                Deterministic = true,
            },
            Io = new NetrunnerIoDto
            {
                InputSchemaFile = "schema.input.json",
                OutputSchemaFile = "schema.output.json",
            },
            Permissions = new NetrunnerPermissionsDto { FilesystemScope = "IsolatedRunWorkspaceOnly" },
            Integrity = new NetrunnerIntegrityDto(),
        };

        var pkgRecord = new PackageRegistrationRecord
        {
            Slug = "sample-worker",
            Version = "1.0.0",
            PackageDir = tempPkgDir,
            Manifest = manifest,
            ManifestHash = "hash123",
            RegisteredAtUtc = DateTimeOffset.UtcNow,
        };
        pkgRepo.AddPackage(pkgRecord);

        var (request, approvalId, runId, chainId) = CreateSampleRequest();

        var chainDef = new ChainDefinitionDto
        {
            Id = chainId,
            Name = "Hitl Chain",
            IsEnabled = true,
            RequiresHitl = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            RetryPolicy = new ChainRetryPolicyDto(),
            Trigger = new ChainTriggerDto { Kind = ChainTriggerKind.Webhook, WebhookPath = "/test" },
            Skill = new ChainSkillStepDto { SkillId = "csharp.test", InputMapping = new Dictionary<string, string>() },
            Script = new ChainScriptStepDto { PackageSlug = "sample-worker", PackageVersion = "1.0.0" },
            Delivery = new ChainDeliveryStepDto { Channel = ChannelKind.Desktop, Target = "local", MessageTemplate = "Done: {{scriptResult}}" },
        };
        await defRepo.UpsertAsync(chainDef);

        var service = new HitlApprovalService(
            repo,
            [sender],
            ownerGuard,
            executor,
            schemaValidator: schemaValidator,
            chainDefinitions: defRepo,
            packages: pkgRepo,
            executions: executionRepo);

        await service.CreateAsync(request);

        string editedJson = "{\"status\": \"edited_by_owner\", \"count\": 99}";

        try
        {
            // Act
            HitlApprovalResult result = await service.EditAsync(approvalId, "owner-1", editedJson, ChannelKind.Desktop);

            // Assert
            Assert.Equal(HitlDecision.Edited, result.Decision);
            Assert.Equal(editedJson, result.EditedPayloadJson);

            HitlApprovalRecord? stored = await repo.GetAsync(approvalId);
            Assert.NotNull(stored);
            Assert.Equal(HitlStatus.Resolved, stored.Status);
            Assert.Equal(editedJson, stored.PayloadJson);

            // Executor resumed
            Assert.Single(executor.ResumedRuns);
            Assert.Equal(runId, executor.ResumedRuns[0]);

            // Checkpoint has edited payload
            IReadOnlyList<ChainCheckpointDto> checkpoints = await executionRepo.GetCheckpointsAsync(runId);
            ChainCheckpointDto hitlCp = Assert.Single(checkpoints, cp => cp.Step == ChainStepKind.Hitl);
            Assert.Equal(ChainStepState.Completed, hitlCp.State);
            Assert.Equal(editedJson, hitlCp.OutputJson);
        }
        finally
        {
            if (Directory.Exists(tempPkgDir))
            {
                Directory.Delete(tempPkgDir, true);
            }
        }
    }

    [Fact]
    public async Task Edit_InvalidJson_ThrowsInvalidEditedPayload_AndStaysPending()
    {
        var repo = new InMemoryHitlApprovalRepository();
        var ownerGuard = new FakeOwnerGuard();
        var executor = new FakeChainExecutor();

        var service = new HitlApprovalService(repo, [], ownerGuard, executor);
        var (request, approvalId, _, _) = CreateSampleRequest();
        await service.CreateAsync(request);

        // Act & Assert
        HitlInvalidEditedPayloadException ex = await Assert.ThrowsAsync<HitlInvalidEditedPayloadException>(() =>
            service.EditAsync(approvalId, "owner-1", "{ invalid json", ChannelKind.Telegram));

        Assert.Contains("valid JSON", ex.Message);

        HitlApprovalRecord? stored = await repo.GetAsync(approvalId);
        Assert.NotNull(stored);
        Assert.Equal(HitlStatus.Pending, stored.Status);
        Assert.Empty(executor.ResumedRuns);
    }

    [Fact]
    public async Task Edit_SchemaValidationFailed_ThrowsInvalidEditedPayload_AndStaysPending()
    {
        var repo = new InMemoryHitlApprovalRepository();
        var ownerGuard = new FakeOwnerGuard();
        var executor = new FakeChainExecutor();
        var defRepo = new InMemoryChainDefinitionRepository();
        var pkgRepo = new FakePackageRegistryRepository();
        var schemaValidator = new FakeSchemaValidator
        {
            ErrorsToReturn = ["Property 'count' must be positive integer"]
        };

        string tempPkgDir = Path.Combine(Path.GetTempPath(), "test_pkg_hitl_err_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempPkgDir);
        string schemaFile = Path.Combine(tempPkgDir, "schema.output.json");
        await File.WriteAllTextAsync(schemaFile, "{\"type\": \"object\"}");

        var manifest = new NetrunnerManifestDto
        {
            Slug = "worker-err",
            Name = "Worker Err",
            Version = "1.0.0",
            Description = "Worker err description",
            Runtime = new NetrunnerRuntimeDto
            {
                Language = "Python",
                LanguageVersion = "3.11",
                Environment = "CPython",
                EnvironmentVersion = "3.11",
                Entrypoint = "main.py",
                TimeoutMs = 15000,
                Deterministic = true,
            },
            Io = new NetrunnerIoDto
            {
                InputSchemaFile = "schema.input.json",
                OutputSchemaFile = "schema.output.json",
            },
            Permissions = new NetrunnerPermissionsDto { FilesystemScope = "IsolatedRunWorkspaceOnly" },
            Integrity = new NetrunnerIntegrityDto(),
        };

        var pkgRecord = new PackageRegistrationRecord
        {
            Slug = "worker-err",
            Version = "1.0.0",
            PackageDir = tempPkgDir,
            Manifest = manifest,
            ManifestHash = "hash123",
            RegisteredAtUtc = DateTimeOffset.UtcNow,
        };
        pkgRepo.AddPackage(pkgRecord);

        var (request, approvalId, _, chainId) = CreateSampleRequest();

        var chainDef = new ChainDefinitionDto
        {
            Id = chainId,
            Name = "Hitl Chain",
            IsEnabled = true,
            RequiresHitl = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            RetryPolicy = new ChainRetryPolicyDto(),
            Trigger = new ChainTriggerDto { Kind = ChainTriggerKind.Webhook, WebhookPath = "/test" },
            Skill = new ChainSkillStepDto { SkillId = "csharp.test", InputMapping = new Dictionary<string, string>() },
            Script = new ChainScriptStepDto { PackageSlug = "worker-err", PackageVersion = "1.0.0" },
            Delivery = new ChainDeliveryStepDto { Channel = ChannelKind.Desktop, Target = "local", MessageTemplate = "Done: {{scriptResult}}" },
        };
        await defRepo.UpsertAsync(chainDef);

        var service = new HitlApprovalService(
            repo,
            [],
            ownerGuard,
            executor,
            schemaValidator: schemaValidator,
            chainDefinitions: defRepo,
            packages: pkgRepo);

        await service.CreateAsync(request);

        try
        {
            HitlInvalidEditedPayloadException ex = await Assert.ThrowsAsync<HitlInvalidEditedPayloadException>(() =>
                service.EditAsync(approvalId, "owner-1", "{\"count\": -5}", ChannelKind.Telegram));

            Assert.Contains("Property 'count' must be positive integer", ex.Message);

            HitlApprovalRecord? stored = await repo.GetAsync(approvalId);
            Assert.NotNull(stored);
            Assert.Equal(HitlStatus.Pending, stored.Status);
            Assert.Empty(executor.ResumedRuns);
        }
        finally
        {
            if (Directory.Exists(tempPkgDir))
            {
                Directory.Delete(tempPkgDir, true);
            }
        }
    }

    [Fact]
    public async Task Reject_ValidOwner_CancelsRun_AndDoesNotResume()
    {
        var repo = new InMemoryHitlApprovalRepository();
        var ownerGuard = new FakeOwnerGuard();
        var executor = new FakeChainExecutor();
        var executionRepo = new InMemoryChainExecutionRepository();

        var service = new HitlApprovalService(repo, [], ownerGuard, executor, executions: executionRepo);
        var (request, approvalId, runId, chainId) = CreateSampleRequest();
        await executionRepo.CreateRunAsync(runId, chainId, new TriggerEvent
        {
            EventId = Guid.NewGuid(),
            ChainId = chainId,
            Kind = ChainTriggerKind.Webhook,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            PayloadJson = "{}",
            IdempotencyKey = "key-" + Guid.NewGuid(),
            Source = "/test",
        });
        await service.CreateAsync(request);

        // Act
        HitlApprovalResult result = await service.RejectAsync(
            approvalId,
            "owner-1",
            ChannelKind.Telegram,
            "Risk of financial loss");

        // Assert
        Assert.Equal(HitlDecision.Rejected, result.Decision);
        Assert.Equal("Risk of financial loss", result.Reason);

        // Executor was cancelled, never resumed
        Assert.Single(executor.CancelledRuns);
        Assert.Equal(runId, executor.CancelledRuns[0]);
        Assert.Empty(executor.ResumedRuns);

        // Status is Resolved with rejection info
        HitlApprovalRecord? stored = await repo.GetAsync(approvalId);
        Assert.NotNull(stored);
        Assert.Equal(HitlStatus.Resolved, stored.Status);

        // Checkpoint is Failed
        IReadOnlyList<ChainCheckpointDto> checkpoints = await executionRepo.GetCheckpointsAsync(runId);
        ChainCheckpointDto hitlCp = Assert.Single(checkpoints, cp => cp.Step == ChainStepKind.Hitl);
        Assert.Equal(ChainStepState.Failed, hitlCp.State);
        Assert.Equal("HitlRejected", hitlCp.Error?.Code);

        ChainRunState? run = await executionRepo.GetRunAsync(runId);
        Assert.NotNull(run);
        Assert.Equal(ChainRunStatus.Cancelled, run.Status);
    }

    [Fact]
    public async Task ExpireOverdue_TransitionsPendingToExpired_AndCancelsRun()
    {
        var repo = new InMemoryHitlApprovalRepository();
        var ownerGuard = new FakeOwnerGuard();
        var executor = new FakeChainExecutor();
        var executionRepo = new InMemoryChainExecutionRepository();

        var service = new HitlApprovalService(repo, [], ownerGuard, executor, executions: executionRepo);

        // Create an already-expired request
        var (request, approvalId, runId, chainId) = CreateSampleRequest(lifetime: TimeSpan.FromMinutes(-10));
        await executionRepo.CreateRunAsync(runId, chainId, new TriggerEvent
        {
            EventId = Guid.NewGuid(),
            ChainId = chainId,
            Kind = ChainTriggerKind.Webhook,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            PayloadJson = "{}",
            IdempotencyKey = "key-" + Guid.NewGuid(),
            Source = "/test",
        });
        await service.CreateAsync(request);

        // Act
        int expiredCount = await service.ExpireOverdueAsync(DateTimeOffset.UtcNow);

        // Assert
        Assert.Equal(1, expiredCount);

        HitlApprovalRecord? stored = await repo.GetAsync(approvalId);
        Assert.NotNull(stored);
        Assert.Equal(HitlStatus.Expired, stored.Status);

        Assert.Single(executor.CancelledRuns);
        Assert.Equal(runId, executor.CancelledRuns[0]);

        ChainRunState? expiredRun = await executionRepo.GetRunAsync(runId);
        Assert.NotNull(expiredRun);
        Assert.Equal(ChainRunStatus.Cancelled, expiredRun.Status);

        // Subsequent attempt to Approve throws HitlExpiredException
        await Assert.ThrowsAsync<HitlExpiredException>(() =>
            service.ApproveAsync(approvalId, "owner-1", ChannelKind.Telegram));

        // State remains Expired
        HitlApprovalRecord? storedAfter = await repo.GetAsync(approvalId);
        Assert.NotNull(storedAfter);
        Assert.Equal(HitlStatus.Expired, storedAfter.Status);
    }

    [Fact]
    public async Task NonOwner_ThrowsAccessDenied_AndStaysPending()
    {
        var repo = new InMemoryHitlApprovalRepository();
        var ownerGuard = new FakeOwnerGuard(); // only "owner-1" is authorized
        var executor = new FakeChainExecutor();

        var service = new HitlApprovalService(repo, [], ownerGuard, executor);
        var (request, approvalId, _, _) = CreateSampleRequest();
        await service.CreateAsync(request);

        // Act & Assert
        await Assert.ThrowsAsync<HitlAccessDeniedException>(() =>
            service.ApproveAsync(approvalId, "intruder", ChannelKind.Telegram));

        await Assert.ThrowsAsync<HitlAccessDeniedException>(() =>
            service.EditAsync(approvalId, "intruder", "{}", ChannelKind.Telegram));

        await Assert.ThrowsAsync<HitlAccessDeniedException>(() =>
            service.RejectAsync(approvalId, "intruder", ChannelKind.Telegram));

        HitlApprovalRecord? stored = await repo.GetAsync(approvalId);
        Assert.NotNull(stored);
        Assert.Equal(HitlStatus.Pending, stored.Status);
        Assert.Empty(executor.ResumedRuns);
        Assert.Empty(executor.CancelledRuns);
    }

    [Fact]
    public async Task AlreadyResolved_SecondDecisionFromAnotherChannel_ThrowsAlreadyResolved()
    {
        var repo = new InMemoryHitlApprovalRepository();
        var ownerGuard = new FakeOwnerGuard();
        var executor = new FakeChainExecutor();

        var service = new HitlApprovalService(repo, [], ownerGuard, executor);
        var (request, approvalId, _, _) = CreateSampleRequest();
        await service.CreateAsync(request);

        // First click from Telegram succeeds
        HitlApprovalResult first = await service.ApproveAsync(approvalId, "owner-1", ChannelKind.Telegram);
        Assert.Equal(HitlDecision.Approved, first.Decision);

        // Second click from Desktop throws AlreadyResolved
        await Assert.ThrowsAsync<HitlAlreadyResolvedException>(() =>
            service.ApproveAsync(approvalId, "owner-1", ChannelKind.Desktop));

        // Attempting to Reject also throws AlreadyResolved
        await Assert.ThrowsAsync<HitlAlreadyResolvedException>(() =>
            service.RejectAsync(approvalId, "owner-1", ChannelKind.Desktop));
    }

    [Fact]
    public async Task RestartSimulation_NewServiceInstanceOnSameRepo_ResolvesPending()
    {
        var repo = new InMemoryHitlApprovalRepository();
        var ownerGuard = new FakeOwnerGuard();
        var executor1 = new FakeChainExecutor();

        var service1 = new HitlApprovalService(repo, [], ownerGuard, executor1);
        var (request, approvalId, runId, _) = CreateSampleRequest();
        await service1.CreateAsync(request);

        // Simulate app restart: create a new service instance with fresh executor on the same repo
        var executor2 = new FakeChainExecutor();
        var service2 = new HitlApprovalService(repo, [], ownerGuard, executor2);

        // Act: service2 resolves the pending request created earlier
        HitlApprovalResult result = await service2.ApproveAsync(approvalId, "owner-1", ChannelKind.Desktop);

        // Assert
        Assert.Equal(HitlDecision.Approved, result.Decision);
        Assert.Single(executor2.ResumedRuns);
        Assert.Equal(runId, executor2.ResumedRuns[0]);

        HitlApprovalRecord? stored = await repo.GetAsync(approvalId);
        Assert.NotNull(stored);
        Assert.Equal(HitlStatus.Resolved, stored.Status);
    }

    [Fact]
    public async Task GateWaiter_RequestApprovalAsync_UnblocksWhenApproved()
    {
        var repo = new InMemoryHitlApprovalRepository();
        var ownerGuard = new FakeOwnerGuard();
        var executor = new FakeChainExecutor();

        var service = new HitlApprovalService(repo, [], ownerGuard, executor);
        var gate = new HitlApprovalGate(service);

        var (request, approvalId, _, _) = CreateSampleRequest();

        // Start waiting on the gate asynchronously
        Task<HitlApprovalResult> gateTask = gate.RequestApprovalAsync(request);

        // The request should now be pending in repo
        HitlApprovalRecord? pending = await repo.GetAsync(approvalId);
        Assert.NotNull(pending);
        Assert.Equal(HitlStatus.Pending, pending.Status);

        // Owner approves via the service
        HitlApprovalResult serviceResult = await service.ApproveAsync(approvalId, "owner-1", ChannelKind.Telegram);

        // The gate task should complete with the result
        HitlApprovalResult gateResult = await gateTask;

        Assert.Equal(HitlDecision.Approved, gateResult.Decision);
        Assert.Equal(approvalId, gateResult.ApprovalId);
        Assert.Equal(serviceResult.DecidedAtUtc, gateResult.DecidedAtUtc);
    }

    [Fact]
    public async Task CardSender_FailingChannel_DoesNotBlockRequestCreation()
    {
        var repo = new InMemoryHitlApprovalRepository();
        var failingSender = new FakeHitlCardSender(ChannelKind.Telegram) { ShouldThrow = true };
        var workingSender = new FakeHitlCardSender(ChannelKind.Desktop) { ShouldThrow = false };
        var ownerGuard = new FakeOwnerGuard();
        var executor = new FakeChainExecutor();

        var service = new HitlApprovalService(repo, [failingSender, workingSender], ownerGuard, executor);
        var (request, approvalId, _, _) = CreateSampleRequest();

        // Act: should not throw even though Telegram sender fails
        HitlApprovalRecord created = await service.CreateAsync(request);

        // Assert
        Assert.Equal(approvalId, created.ApprovalId);
        Assert.Single(workingSender.SentRequests);

        HitlApprovalRecord? stored = await repo.GetAsync(approvalId);
        Assert.NotNull(stored);
        Assert.Equal(HitlStatus.Pending, stored.Status);
    }

    [Fact]
    public async Task InMemoryRepository_ConcurrentTryResolve_OnlyOneSucceeds()
    {
        var repo = new InMemoryHitlApprovalRepository();
        var (request, approvalId, runId, chainId) = CreateSampleRequest();

        var record = new HitlApprovalRecord
        {
            ApprovalId = approvalId,
            RunId = runId,
            ChainId = chainId,
            Title = request.Title,
            PreviewText = request.PreviewText,
            PayloadJson = request.PayloadJson,
            CreatedAtUtc = request.CreatedAtUtc,
            ExpiresAtUtc = request.ExpiresAtUtc,
            Status = HitlStatus.Pending,
        };
        await repo.CreateAsync(record);

        // Run 10 parallel resolution attempts
        Task<bool>[] tasks = Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            return await repo.TryResolveAsync(approvalId, new HitlApprovalResult
            {
                ApprovalId = approvalId,
                Decision = i % 2 == 0 ? HitlDecision.Approved : HitlDecision.Rejected,
                OwnerId = $"owner-{i}",
                Channel = ChannelKind.Telegram,
                DecidedAtUtc = DateTimeOffset.UtcNow,
            });
        })).ToArray();

        bool[] results = await Task.WhenAll(tasks);

        // Exactly one attempt succeeded
        Assert.Single(results, r => r);
        Assert.Equal(9, results.Count(r => !r));

        HitlApprovalRecord? stored = await repo.GetAsync(approvalId);
        Assert.NotNull(stored);
        Assert.Equal(HitlStatus.Resolved, stored.Status);
    }
}

using System.Text.Json;
using CyberPipe.Cli;
using CyberPipe.Contracts;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class CliTests
{
    [Fact]
    public async Task Ask_OnSuccess_PrintsAnswerAndReturnsZero()
    {
        // Arrange
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var fakeRouter = new TestFakeRouter(replyIntent: "42 is the answer");
        var ctx = new CliContext
        {
            Out = stdout,
            Error = stderr,
            Router = fakeRouter,
        };

        // Act
        int exitCode = await CliApp.RunAsync(["ask", "What is the answer?"], ctx);

        // Assert
        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("42 is the answer", stdout.ToString());
        Assert.Empty(stderr.ToString());
        Assert.NotNull(fakeRouter.LastRequest);
        Assert.Equal("What is the answer?", fakeRouter.LastRequest.Text);
        Assert.Equal(ChannelKind.Cli, fakeRouter.LastRequest.Channel);
    }

    [Fact]
    public async Task Ask_OnRouterFailure_ReturnsNonZeroAndPrintsToStderr()
    {
        // Arrange
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var failingRouter = new FailingRouter("Router backend offline");
        var ctx = new CliContext
        {
            Out = stdout,
            Error = stderr,
            Router = failingRouter,
        };

        // Act
        int exitCode = await CliApp.RunAsync(["ask", "Hello"], ctx);

        // Assert
        Assert.Equal(CliExitCodes.GeneralError, exitCode);
        Assert.Contains("Router backend offline", stderr.ToString());
    }

    [Fact]
    public async Task Ask_OnBudgetDenied_ReturnsBudgetDeniedExitCode()
    {
        // Arrange
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var budgetDeniedRouter = new BudgetDeniedRouter("Daily limit of $0.50 exceeded");
        var ctx = new CliContext
        {
            Out = stdout,
            Error = stderr,
            Router = budgetDeniedRouter,
        };

        // Act
        int exitCode = await CliApp.RunAsync(["ask", "Run expensive query"], ctx);

        // Assert
        Assert.Equal(CliExitCodes.BudgetDenied, exitCode);
        Assert.Contains("Budget denied", stderr.ToString());
        Assert.Contains("Daily limit of $0.50 exceeded", stderr.ToString());
    }

    [Fact]
    public async Task Chat_TwoExchanges_PreserveSessionContext()
    {
        // Arrange: provide two messages then exit
        string simulatedInput = "First question" + Environment.NewLine +
                                "Second question" + Environment.NewLine +
                                "exit" + Environment.NewLine;
        var stdin = new StringReader(simulatedInput);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var recordingRouter = new SessionRecordingRouter();
        var ctx = new CliContext
        {
            In = stdin,
            Out = stdout,
            Error = stderr,
            Router = recordingRouter,
        };

        // Act
        int exitCode = await CliApp.RunAsync(["chat"], ctx);

        // Assert
        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(2, recordingRouter.RecordedRequests.Count);

        string firstSessionId = recordingRouter.RecordedRequests[0].Metadata["sessionId"];
        string secondSessionId = recordingRouter.RecordedRequests[1].Metadata["sessionId"];

        Assert.False(string.IsNullOrEmpty(firstSessionId));
        Assert.Equal(firstSessionId, secondSessionId);
        Assert.Equal("1", recordingRouter.RecordedRequests[0].Metadata["turn"]);
        Assert.Equal("2", recordingRouter.RecordedRequests[1].Metadata["turn"]);
    }

    [Fact]
    public async Task Chat_AbortTask_DoesNotTerminateCliProcess()
    {
        // Arrange: send a task, then /abort, then second message, then exit
        string simulatedInput = "First task" + Environment.NewLine +
                                "/abort" + Environment.NewLine +
                                "Second task" + Environment.NewLine +
                                "exit" + Environment.NewLine;
        var stdin = new StringReader(simulatedInput);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var router = new TestFakeRouter("OK");
        var ctx = new CliContext
        {
            In = stdin,
            Out = stdout,
            Error = stderr,
            Router = router,
        };

        // Act
        int exitCode = await CliApp.RunAsync(["chat"], ctx);

        // Assert: process finished cleanly with Success exit code
        Assert.Equal(CliExitCodes.Success, exitCode);
        string output = stdout.ToString();
        Assert.Contains("CyberPipe Interactive Chat", output);
        Assert.Contains("Goodbye!", output);
    }

    [Fact]
    public async Task ServiceStatus_OnRunningService_PrintsStatus()
    {
        // Arrange
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var fakeService = new TestFakeServiceController();
        await fakeService.StartAsync();

        var ctx = new CliContext
        {
            Out = stdout,
            Error = stderr,
            ServiceControl = fakeService,
        };

        // Act
        int exitCode = await CliApp.RunAsync(["service", "status"], ctx);

        // Assert
        Assert.Equal(CliExitCodes.Success, exitCode);
        string outText = stdout.ToString();
        Assert.Contains("Running", outText);
        Assert.Contains("1.0.0", outText);
    }

    [Fact]
    public async Task ServiceStatus_WhenServiceUnavailable_ReturnsDistinctExitCodeAndStartHint()
    {
        // Arrange
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var failingService = new FailingServiceController();
        var ctx = new CliContext
        {
            Out = stdout,
            Error = stderr,
            ServiceControl = failingService,
        };

        // Act
        int exitCode = await CliApp.RunAsync(["service", "status"], ctx);

        // Assert
        Assert.Equal(CliExitCodes.ServiceUnavailable, exitCode);
        string errText = stderr.ToString();
        Assert.Contains("Service is unavailable", errText);
        Assert.Contains("cyberpipe service start", errText);
    }

    [Fact]
    public async Task ServiceCommands_StartStopRestart_CallServiceControl()
    {
        // Arrange
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var fakeService = new TestFakeServiceController();
        var ctx = new CliContext
        {
            Out = stdout,
            Error = stderr,
            ServiceControl = fakeService,
        };

        // Act: Start
        int startCode = await CliApp.RunAsync(["service", "start"], ctx);
        Assert.Equal(CliExitCodes.Success, startCode);
        Assert.Equal(1, fakeService.StartCallCount);

        // Act: Restart
        int restartCode = await CliApp.RunAsync(["service", "restart"], ctx);
        Assert.Equal(CliExitCodes.Success, restartCode);
        Assert.Equal(1, fakeService.RestartCallCount);

        // Act: Stop
        int stopCode = await CliApp.RunAsync(["service", "stop"], ctx);
        Assert.Equal(CliExitCodes.Success, stopCode);
        Assert.Equal(1, fakeService.StopCallCount);
    }

    [Fact]
    public async Task ChainRun_OnValidId_RunsChainAndDisplaysOutcome()
    {
        // Arrange
        var chainId = new ChainId(Guid.NewGuid());
        var chainRepo = new TestFakeChainRepo([
            new ChainDefinitionDto
            {
                Id = chainId,
                Name = "Daily Report",
                IsEnabled = true,
                Trigger = new ChainTriggerDto { Kind = ChainTriggerKind.FunctionCall },
                Skill = new ChainSkillStepDto { SkillId = "reporter" },
                Script = new ChainScriptStepDto { PackageSlug = "rep", PackageVersion = "1.0.0" },
                Delivery = new ChainDeliveryStepDto { Channel = ChannelKind.Desktop, Target = "desktop-ui", MessageTemplate = "{result}" },
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            }
        ]);
        var triggerInvoker = new TestFakeTriggerInvoker();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var ctx = new CliContext
        {
            Out = stdout,
            Error = stderr,
            ChainRepo = chainRepo,
            TriggerInvoker = triggerInvoker,
        };

        // Act
        int exitCode = await CliApp.RunAsync(["chain", "run", chainId.Value.ToString(), "{\"filter\":\"all\"}"], ctx);

        // Assert
        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(chainId, triggerInvoker.LastInvokedChainId);
        Assert.Equal("{\"filter\":\"all\"}", triggerInvoker.LastPayloadJson);
        Assert.Contains("Daily Report", stdout.ToString());
        Assert.Contains("Delivery Target: Desktop", stdout.ToString());
    }

    [Fact]
    public async Task ChainRun_OnNonExistentId_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var chainRepo = new TestFakeChainRepo([]);
        var triggerInvoker = new TestFakeTriggerInvoker();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var ctx = new CliContext
        {
            Out = stdout,
            Error = stderr,
            ChainRepo = chainRepo,
            TriggerInvoker = triggerInvoker,
        };

        // Act
        int exitCode = await CliApp.RunAsync(["chain", "run", nonExistentId.ToString()], ctx);

        // Assert
        Assert.Equal(CliExitCodes.NotFound, exitCode);
        Assert.Contains("not found", stderr.ToString());
    }

    [Fact]
    public async Task Approve_OnPendingRequest_RecordsDecision()
    {
        // Arrange
        var approvalId = Guid.NewGuid();
        var hitlService = new ResolvingHitlApprovalService();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var ctx = new CliContext
        {
            Out = stdout,
            Error = stderr,
            HitlService = hitlService,
        };

        // Act: First approval
        int exitCode = await CliApp.RunAsync(["approve", approvalId.ToString()], ctx);

        // Assert
        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(approvalId, hitlService.LastApprovedId);
        Assert.Contains("approved successfully", stdout.ToString());
    }

    [Fact]
    public async Task Approve_OnAlreadyResolvedRequest_IsRejectedWithAlreadyResolvedCode()
    {
        // Arrange
        var approvalId = Guid.NewGuid();
        var hitlService = new ResolvingHitlApprovalService();
        var ctx = new CliContext
        {
            Out = new StringWriter(),
            Error = new StringWriter(),
            HitlService = hitlService,
        };

        // Act 1: First approval succeeds
        await CliApp.RunAsync(["approve", approvalId.ToString()], ctx);

        // Act 2: Second approval on same ID
        var stderr2 = new StringWriter();
        var ctx2 = new CliContext
        {
            Out = new StringWriter(),
            Error = stderr2,
            HitlService = hitlService,
        };
        int secondExitCode = await CliApp.RunAsync(["approve", approvalId.ToString()], ctx2);

        // Assert
        Assert.Equal(CliExitCodes.AlreadyResolved, secondExitCode);
        Assert.Contains("already resolved", stderr2.ToString());
    }

    [Fact]
    public async Task Reject_RecordsRejectionWithReason()
    {
        // Arrange
        var approvalId = Guid.NewGuid();
        var hitlService = new ResolvingHitlApprovalService();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var ctx = new CliContext
        {
            Out = stdout,
            Error = stderr,
            HitlService = hitlService,
        };

        // Act
        int exitCode = await CliApp.RunAsync(["reject", approvalId.ToString(), "Security", "violation"], ctx);

        // Assert
        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(approvalId, hitlService.LastRejectedId);
        Assert.Equal("Security violation", hitlService.LastRejectReason);
        Assert.Contains("rejected successfully", stdout.ToString());
    }

    [Fact]
    public async Task JsonFlag_BudgetAndChainList_PassesDtoContractSchemaRoundtrip()
    {
        // Arrange Budget
        var stdoutBudget = new StringWriter();
        var fakeBudget = new TestFakeBudgetGovernor(spentToday: 0.15m, remainingToday: 0.35m, tokensUsed: 800);
        var ctxBudget = new CliContext
        {
            Out = stdoutBudget,
            BudgetGovernor = fakeBudget,
        };

        // Act Budget
        int budgetExit = await CliApp.RunAsync(["budget", "--json"], ctxBudget);
        Assert.Equal(CliExitCodes.Success, budgetExit);

        // Roundtrip deserialization into contract DTO
        string budgetJson = stdoutBudget.ToString();
        var deserializedBudget = JsonSerializer.Deserialize<BudgetStatusDto>(budgetJson);
        Assert.NotNull(deserializedBudget);
        Assert.Equal(0.15m, deserializedBudget.SpentTodayUsd);
        Assert.Equal(0.35m, deserializedBudget.RemainingTodayUsd);
        Assert.Equal(800, deserializedBudget.TokensUsedToday);

        // Arrange Chain List
        var stdoutChains = new StringWriter();
        var chainId = new ChainId(Guid.NewGuid());
        var chainRepo = new TestFakeChainRepo([
            new ChainDefinitionDto
            {
                Id = chainId,
                Name = "JSON Test Chain",
                IsEnabled = true,
                Trigger = new ChainTriggerDto { Kind = ChainTriggerKind.Cron, CronExpression = "0 0 * * * ?" },
                Skill = new ChainSkillStepDto { SkillId = "skill-1" },
                Script = new ChainScriptStepDto { PackageSlug = "pkg-1", PackageVersion = "1.0.0" },
                Delivery = new ChainDeliveryStepDto { Channel = ChannelKind.Cli, Target = "stdout", MessageTemplate = "{res}" },
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            }
        ]);
        var ctxChains = new CliContext
        {
            Out = stdoutChains,
            ChainRepo = chainRepo,
        };

        // Act Chain List
        int chainsExit = await CliApp.RunAsync(["chain", "list", "--json"], ctxChains);
        Assert.Equal(CliExitCodes.Success, chainsExit);

        // Roundtrip deserialization into contract DTO
        string chainsJson = stdoutChains.ToString();
        var deserializedChains = JsonSerializer.Deserialize<List<ChainDefinitionDto>>(chainsJson);
        Assert.NotNull(deserializedChains);
        Assert.Single(deserializedChains);
        Assert.Equal("JSON Test Chain", deserializedChains[0].Name);
        Assert.Equal(chainId, deserializedChains[0].Id);
    }
}

// ----------------- Helper Fakes for CLI Tests -----------------

internal sealed class FailingRouter : IIntentRouter
{
    private readonly string _error;

    public FailingRouter(string error)
    {
        _error = error;
    }

    public Task<RouteResult> RouteAsync(RouteRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RouteResult
        {
            Status = RouteStatus.Failed,
            Tier = IntentTier.Tier0,
            Reason = _error,
        });
    }
}

internal sealed class BudgetDeniedRouter : IIntentRouter
{
    private readonly string _reason;

    public BudgetDeniedRouter(string reason)
    {
        _reason = reason;
    }

    public Task<RouteResult> RouteAsync(RouteRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RouteResult
        {
            Status = RouteStatus.BudgetDenied,
            Tier = IntentTier.Tier2,
            Reason = _reason,
        });
    }
}

internal sealed class SessionRecordingRouter : IIntentRouter
{
    public List<RouteRequest> RecordedRequests { get; } = [];

    public Task<RouteResult> RouteAsync(RouteRequest request, CancellationToken cancellationToken = default)
    {
        RecordedRequests.Add(request);
        return Task.FromResult(new RouteResult
        {
            Status = RouteStatus.Matched,
            Tier = IntentTier.Tier0,
            Intent = $"Turn response for: {request.Text}",
        });
    }
}

internal sealed class ResolvingHitlApprovalService : IHitlApprovalService
{
    private readonly HashSet<Guid> _resolved = [];
    public Guid? LastApprovedId { get; private set; }
    public Guid? LastRejectedId { get; private set; }
    public string? LastRejectReason { get; private set; }

    public Task<HitlApprovalRecord> CreateAsync(HitlApprovalRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new HitlApprovalRecord
        {
            ApprovalId = request.ApprovalId,
            RunId = request.RunId,
            ChainId = request.ChainId,
            Title = request.Title,
            PreviewText = request.PreviewText,
            PayloadJson = request.PayloadJson,
            CreatedAtUtc = request.CreatedAtUtc,
            ExpiresAtUtc = request.ExpiresAtUtc,
        });

    public Task<HitlApprovalResult> ApproveAsync(Guid approvalId, string ownerId, ChannelKind channel, CancellationToken cancellationToken = default)
    {
        if (!_resolved.Add(approvalId))
        {
            throw new HitlAlreadyResolvedException($"Approval '{approvalId}' is already resolved.");
        }

        LastApprovedId = approvalId;
        return Task.FromResult(new HitlApprovalResult
        {
            ApprovalId = approvalId,
            Decision = HitlDecision.Approved,
            OwnerId = ownerId,
            Channel = channel,
            DecidedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    public Task<HitlApprovalResult> EditAsync(Guid approvalId, string ownerId, string editedPayloadJson, ChannelKind channel, CancellationToken cancellationToken = default)
    {
        if (!_resolved.Add(approvalId))
        {
            throw new HitlAlreadyResolvedException($"Approval '{approvalId}' is already resolved.");
        }

        return Task.FromResult(new HitlApprovalResult
        {
            ApprovalId = approvalId,
            Decision = HitlDecision.Edited,
            EditedPayloadJson = editedPayloadJson,
            OwnerId = ownerId,
            Channel = channel,
            DecidedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    public Task<HitlApprovalResult> RejectAsync(Guid approvalId, string ownerId, ChannelKind channel, string? reason = null, CancellationToken cancellationToken = default)
    {
        if (!_resolved.Add(approvalId))
        {
            throw new HitlAlreadyResolvedException($"Approval '{approvalId}' is already resolved.");
        }

        LastRejectedId = approvalId;
        LastRejectReason = reason;
        return Task.FromResult(new HitlApprovalResult
        {
            ApprovalId = approvalId,
            Decision = HitlDecision.Rejected,
            Reason = reason,
            OwnerId = ownerId,
            Channel = channel,
            DecidedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    public Task<int> ExpireOverdueAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}

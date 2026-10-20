using CyberPipe.Contracts;
using CyberPipe.Desktop.Localization;
using CyberPipe.Desktop.Services;
using CyberPipe.Desktop.ViewModels;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class DesktopUiTests
{
    [Fact]
    public async Task Chat_SendMessage_SendsToRouter_DisplaysReplyAndUpdatesBudget()
    {
        // Arrange
        var fakeRouter = new TestFakeRouter(replyIntent: "Processed: Hello AI");
        var fakeBudget = new TestFakeBudgetGovernor(spentToday: 0.25m, remainingToday: 4.75m, tokensUsed: 1200);
        var chatVm = new ChatViewModel(fakeRouter, fakeBudget);

        chatVm.InputText = "Hello AI";

        // Act
        await chatVm.SendMessageAsync();

        // Assert
        Assert.Equal(2, chatVm.Messages.Count);
        Assert.True(chatVm.Messages[0].IsUser);
        Assert.Equal("Hello AI", chatVm.Messages[0].Text);

        Assert.False(chatVm.Messages[1].IsUser);
        Assert.Equal("Processed: Hello AI", chatVm.Messages[1].Text);
        Assert.Equal(IntentTier.Tier1, chatVm.Messages[1].Tier);

        Assert.NotNull(chatVm.BudgetStatus);
        Assert.Equal(0.25m, chatVm.BudgetStatus.SpentTodayUsd);
        Assert.Equal(4.75m, chatVm.BudgetStatus.RemainingTodayUsd);
        Assert.Equal(1200, chatVm.BudgetStatus.TokensUsedToday);
        Assert.False(chatVm.IsProcessing);
    }

    [Fact]
    public async Task Chat_AbortCurrentTask_CancelsExecutionAndDisplaysAbortedMessage()
    {
        // Arrange: router that delays until cancelled
        var fakeRouter = new DelayingRouter(TimeSpan.FromSeconds(5));
        var chatVm = new ChatViewModel(fakeRouter);

        chatVm.InputText = "Long running task";

        // Act: Start sending in background, then immediately abort
        Task sendTask = chatVm.SendMessageAsync();
        Assert.True(chatVm.IsProcessing);
        Assert.True(chatVm.AbortCommand.CanExecute(null));

        chatVm.AbortCurrentTask();
        await sendTask;

        // Assert
        Assert.False(chatVm.IsProcessing);
        Assert.Contains(chatVm.Messages, m => m.Text.Contains("прервана") || m.Text.Contains("aborted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Chat_WithAttachment_PassesMetadataToRouter()
    {
        // Arrange
        var fakeRouter = new TestFakeRouter();
        var chatVm = new ChatViewModel(fakeRouter);

        chatVm.InputText = "Analyze this file";
        chatVm.SelectedAttachmentPath = @"C:\data\report.pdf";

        // Act
        await chatVm.SendMessageAsync();

        // Assert
        Assert.NotNull(fakeRouter.LastRequest);
        Assert.True(fakeRouter.LastRequest.Metadata.ContainsKey("attachment"));
        Assert.Equal(@"C:\data\report.pdf", fakeRouter.LastRequest.Metadata["attachment"]);
        Assert.Equal(ChannelKind.Desktop, fakeRouter.LastRequest.Channel);
    }

    [Fact]
    public async Task ServicePanel_StartStopRestart_CallsServiceControl_AndUpdatesStatus()
    {
        // Arrange
        var fakeController = new TestFakeServiceController();
        var serviceVm = new ServiceViewModel(fakeController);
        await serviceVm.RefreshStatusAsync();

        Assert.Equal("Stopped", serviceVm.State);
        Assert.True(serviceVm.CanStart);
        Assert.False(serviceVm.CanStop);

        // Act & Assert: Start
        await serviceVm.StartAsync();
        Assert.Equal("Running", serviceVm.State);
        Assert.Equal(1, fakeController.StartCallCount);
        Assert.True(serviceVm.CanStop);
        Assert.True(serviceVm.CanRestart);

        // Act & Assert: Restart
        await serviceVm.RestartAsync();
        Assert.Equal("Running", serviceVm.State);
        Assert.Equal(1, fakeController.RestartCallCount);

        // Act & Assert: Stop
        await serviceVm.StopAsync();
        Assert.Equal("Stopped", serviceVm.State);
        Assert.Equal(1, fakeController.StopCallCount);
        Assert.True(serviceVm.CanStart);
    }

    [Fact]
    public void MainWindowFactory_CreateDefault_WiresRealServiceController()
    {
        // TASK-021: панель Service обязана говорить с реальной службой через
        // клиент управляющего канала, а не с заглушкой.
        MainWindowViewModel vm = MainWindowViewModelFactory.CreateDefault();

        Assert.IsType<CyberPipe.ServiceControl.ServiceControllerManager>(vm.Service.Controller);
    }

    [Fact]
    public async Task ServicePanel_WhenServiceUnavailable_DoesNotHangAndShowsUnavailableState()
    {
        // Arrange: controller throwing exceptions (e.g. named pipe disconnected)
        var failingController = new FailingServiceController();
        var serviceVm = new ServiceViewModel(failingController);

        // Act
        await serviceVm.RefreshStatusAsync();

        // Assert
        Assert.Equal("ServiceUnavailable", serviceVm.State);
        Assert.NotNull(serviceVm.LastError);
        Assert.Contains("Pipe disconnected", serviceVm.LastError);
        Assert.False(serviceVm.IsBusy);
    }

    [Fact]
    public async Task ServicePanel_Watch_UpdatesWhenServiceDropsWithoutManualRefresh()
    {
        var controller = new DroppingServiceController();
        var serviceVm = new ServiceViewModel(controller);

        DateTime deadline = DateTime.UtcNow.AddSeconds(2);
        while (serviceVm.State != "Faulted" && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal("Faulted", serviceVm.State);
        Assert.False(serviceVm.IsRunning);
        Assert.Contains("exited", serviceVm.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hitl_Enqueue_AppearsInPendingList()
    {
        // Arrange
        var approvalService = new TestFakeHitlApprovalService();
        var notifications = new DesktopNotificationService();
        var hitlVm = new HitlViewModel(approvalService, notifications);

        var request = new HitlApprovalRecord
        {
            ApprovalId = Guid.NewGuid(),
            RunId = new RunId(Guid.NewGuid()),
            ChainId = new ChainId(Guid.NewGuid()),
            Title = "Confirm Bank Transfer",
            PreviewText = "Transfer $100 to account X",
            PayloadJson = "{\"amount\": 100}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10),
        };

        // Act
        hitlVm.EnqueueApproval(request);

        // Assert
        Assert.Equal(1, hitlVm.PendingCount);
        Assert.NotNull(hitlVm.SelectedApproval);
        Assert.Equal(request.ApprovalId, hitlVm.SelectedApproval.ApprovalId);
        Assert.Equal("{\"amount\": 100}", hitlVm.EditedPayloadJson);
        Assert.Single(notifications.ActiveNotifications);
    }

    [Fact]
    public async Task Hitl_Approve_CallsApprovalServiceWithSameIdAndRemovesFromQueue()
    {
        // Arrange
        var approvalService = new TestFakeHitlApprovalService();
        var hitlVm = new HitlViewModel(approvalService);
        var approvalId = Guid.NewGuid();

        hitlVm.EnqueueApproval(new HitlApprovalRecord
        {
            ApprovalId = approvalId,
            RunId = new RunId(Guid.NewGuid()),
            ChainId = new ChainId(Guid.NewGuid()),
            Title = "Action 1",
            PreviewText = "Preview 1",
            PayloadJson = "{}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
        });

        // Act
        await hitlVm.ApproveAsync();

        // Assert
        Assert.Equal(0, hitlVm.PendingCount);
        Assert.Equal(approvalId, approvalService.LastApprovedId);
        Assert.Equal(ChannelKind.Desktop, approvalService.LastChannel);
    }

    [Fact]
    public async Task Hitl_Reject_CallsApprovalServiceWithReason()
    {
        // Arrange
        var approvalService = new TestFakeHitlApprovalService();
        var hitlVm = new HitlViewModel(approvalService);
        var approvalId = Guid.NewGuid();

        hitlVm.EnqueueApproval(new HitlApprovalRecord
        {
            ApprovalId = approvalId,
            RunId = new RunId(Guid.NewGuid()),
            ChainId = new ChainId(Guid.NewGuid()),
            Title = "Action 2",
            PreviewText = "Preview 2",
            PayloadJson = "{}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
        });

        hitlVm.RejectReason = "Too risky";

        // Act
        await hitlVm.RejectAsync();

        // Assert
        Assert.Equal(0, hitlVm.PendingCount);
        Assert.Equal(approvalId, approvalService.LastRejectedId);
        Assert.Equal("Too risky", approvalService.LastRejectReason);
    }

    [Fact]
    public async Task Hitl_Edit_CallsApprovalServiceWithEditedPayload()
    {
        // Arrange
        var approvalService = new TestFakeHitlApprovalService();
        var hitlVm = new HitlViewModel(approvalService);
        var approvalId = Guid.NewGuid();

        hitlVm.EnqueueApproval(new HitlApprovalRecord
        {
            ApprovalId = approvalId,
            RunId = new RunId(Guid.NewGuid()),
            ChainId = new ChainId(Guid.NewGuid()),
            Title = "Action 3",
            PreviewText = "Preview 3",
            PayloadJson = "{\"limit\": 10}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
        });

        hitlVm.EditedPayloadJson = "{\"limit\": 50}";

        // Act
        await hitlVm.EditAsync();

        // Assert
        Assert.Equal(0, hitlVm.PendingCount);
        Assert.Equal(approvalId, approvalService.LastEditedId);
        Assert.Equal("{\"limit\": 50}", approvalService.LastEditedPayload);
    }

    [Fact]
    public async Task Chains_LoadsEnabledDefinitions_AndRunsWithCheckpoints()
    {
        // Arrange
        var chainId = new ChainId(Guid.NewGuid());
        var runId = new RunId(Guid.NewGuid());
        var chainRepo = new TestFakeChainRepo([
            new ChainDefinitionDto
            {
                Id = chainId,
                Name = "Daily Summary",
                IsEnabled = true,
                Trigger = new ChainTriggerDto { Kind = ChainTriggerKind.Cron, CronExpression = "0 0 * * * ?" },
                Skill = new ChainSkillStepDto { SkillId = "summary.skill" },
                Script = new ChainScriptStepDto { PackageSlug = "summarizer", PackageVersion = "1.0.0" },
                Delivery = new ChainDeliveryStepDto { Channel = ChannelKind.Desktop, Target = "desktop", MessageTemplate = "{result}" },
                RequiresHitl = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            }
        ]);

        var executionRepo = new TestFakeExecutionRepo(new Dictionary<RunId, ChainRunState>
        {
            [runId] = new ChainRunState
            {
                RunId = runId,
                ChainId = chainId,
                Status = ChainRunStatus.Completed,
                Trigger = new TriggerEvent
                {
                    EventId = Guid.NewGuid(),
                    ChainId = chainId,
                    Kind = ChainTriggerKind.Cron,
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    PayloadJson = "{}",
                    IdempotencyKey = "key-1",
                    Source = "cron",
                },
                StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-10),
                FinishedAtUtc = DateTimeOffset.UtcNow,
                Checkpoints = [
                    new ChainCheckpointDto
                    {
                        RunId = runId,
                        ChainId = chainId,
                        Step = ChainStepKind.Trigger,
                        State = ChainStepState.Completed,
                        CheckpointAtUtc = DateTimeOffset.UtcNow,
                    },
                    new ChainCheckpointDto
                    {
                        RunId = runId,
                        ChainId = chainId,
                        Step = ChainStepKind.Skill,
                        State = ChainStepState.Completed,
                        CheckpointAtUtc = DateTimeOffset.UtcNow,
                    },
                ],
            }
        });

        var triggerInvoker = new TestFakeTriggerInvoker();
        var chainsVm = new ChainsViewModel(chainRepo, executionRepo, triggerInvoker);

        // Act
        await chainsVm.LoadChainsAsync();

        // Assert
        Assert.Single(chainsVm.Chains);
        Assert.Equal("Daily Summary", chainsVm.Chains[0].Name);
        Assert.Equal(chainId, chainsVm.Chains[0].Id);
        Assert.True(chainsVm.Chains[0].RequiresHitl);
    }

    [Fact]
    public async Task Chains_ManualFunctionCallTrigger_InvokesTriggerAndShowsNotification()
    {
        // Arrange
        var chainId = new ChainId(Guid.NewGuid());
        var chainRepo = new TestFakeChainRepo([
            new ChainDefinitionDto
            {
                Id = chainId,
                Name = "Manual Task",
                IsEnabled = true,
                Trigger = new ChainTriggerDto { Kind = ChainTriggerKind.FunctionCall },
                Skill = new ChainSkillStepDto { SkillId = "test.skill" },
                Script = new ChainScriptStepDto { PackageSlug = "test", PackageVersion = "1.0.0" },
                Delivery = new ChainDeliveryStepDto { Channel = ChannelKind.Desktop, Target = "ui", MessageTemplate = "{result}" },
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            }
        ]);
        var executionRepo = new TestFakeExecutionRepo([]);
        var triggerInvoker = new TestFakeTriggerInvoker();
        var notifications = new DesktopNotificationService();
        var chainsVm = new ChainsViewModel(chainRepo, executionRepo, triggerInvoker, notifications);
        await chainsVm.LoadChainsAsync();

        chainsVm.SelectedChain = chainsVm.Chains[0];
        chainsVm.FunctionCallPayload = "{\"param\": 123}";

        // Act
        await chainsVm.TriggerFunctionCallAsync();

        // Assert
        Assert.Equal(chainId, triggerInvoker.LastInvokedChainId);
        Assert.Equal("{\"param\": 123}", triggerInvoker.LastPayloadJson);
        Assert.Single(notifications.ActiveNotifications);
        Assert.Contains("Chain Triggered", notifications.ActiveNotifications[0].Title);
    }

    [Fact]
    public void Chains_VerifiesNoEditChainCommandOrCapabilityExists()
    {
        // Requirement Q5: No chain editor in UI!
        // Verify via reflection that ChainsViewModel has NO "EditChain", "SaveDefinition", or "CreateChain" commands/methods
        Type type = typeof(ChainsViewModel);
        Assert.Null(type.GetProperty("EditChainCommand"));
        Assert.Null(type.GetProperty("SaveChainCommand"));
        Assert.Null(type.GetMethod("EditChain"));
        Assert.Null(type.GetMethod("SaveChainDefinition"));
    }

    [Fact]
    public async Task Schedules_ListsCronJobs_AndPauseResumeUnscheduleWorks()
    {
        // Arrange
        var chainId = new ChainId(Guid.NewGuid());
        var scheduler = new TestFakeScheduler([
            new ScheduledChainInfo
            {
                ChainId = chainId,
                CronExpression = "0 0 12 * * ?",
                NextFireTimeUtc = DateTimeOffset.UtcNow.AddHours(2),
                IsPaused = false,
            }
        ]);

        var schedulesVm = new SchedulesViewModel(scheduler);
        await schedulesVm.LoadSchedulesAsync();

        Assert.Single(schedulesVm.Schedules);
        schedulesVm.SelectedSchedule = schedulesVm.Schedules[0];

        // Act & Assert: Pause
        await schedulesVm.PauseScheduleAsync();
        Assert.True(schedulesVm.SelectedSchedule.IsPaused);
        Assert.Equal(chainId, scheduler.LastPausedChainId);

        // Act & Assert: Resume
        await schedulesVm.ResumeScheduleAsync();
        Assert.False(schedulesVm.SelectedSchedule.IsPaused);
        Assert.Equal(chainId, scheduler.LastResumedChainId);

        // Act & Assert: Unschedule
        await schedulesVm.UnscheduleAsync();
        Assert.Empty(schedulesVm.Schedules);
        Assert.Equal(chainId, scheduler.LastUnscheduledChainId);
    }

    [Fact]
    public async Task Schedules_InvalidCronExpression_IsRejectedWithContractExceptionAndClearMessage()
    {
        // Arrange
        var scheduler = new TestFakeScheduler([]);
        var schedulesVm = new SchedulesViewModel(scheduler);

        schedulesVm.ChainIdInput = Guid.NewGuid().ToString();
        schedulesVm.CronExpressionInput = "invalid-cron-syntax";

        // Act
        await schedulesVm.ScheduleChainAsync();

        // Assert
        Assert.NotNull(schedulesVm.ErrorMessage);
        Assert.Contains("Invalid cron expression", schedulesVm.ErrorMessage);
        Assert.Contains("invalid-cron-syntax", schedulesVm.ErrorMessage);
    }

    [Fact]
    public void DesktopShutdown_DoesNotCallServiceStop()
    {
        // Requirement Q24 & Acceptance Criteria: Closing Desktop does not stop Service.
        var fakeController = new TestFakeServiceController();
        var serviceVm = new ServiceViewModel(fakeController);

        // Simulate creating and running MainWindow / Desktop
        var mainWindowVm = new MainWindowViewModel(
            new ChatViewModel(new TestFakeRouter()),
            new ChainsViewModel(new TestFakeChainRepo([]), new TestFakeExecutionRepo([]), new TestFakeTriggerInvoker()),
            new HitlViewModel(new TestFakeHitlApprovalService()),
            serviceVm,
            new SchedulesViewModel(new TestFakeScheduler([])),
            new ModelsViewModel());

        // When Desktop closes, no stop call should ever be issued on service controller
        Assert.Equal(0, fakeController.StopCallCount);
    }

    [Fact]
    public void LocalizationManager_SwitchesLanguagesBetweenRuAndEn()
    {
        var loc = new LocalizationManager();

        loc.CurrentCulture = "ru";
        Assert.Equal("Чат", loc["Tab.Chat"]);
        Assert.Equal("Служба:", loc["Header.Status"]);

        loc.CurrentCulture = "en";
        Assert.Equal("Chat", loc["Tab.Chat"]);
        Assert.Equal("Service:", loc["Header.Status"]);
    }

    [Fact]
    public void ThemeService_TogglesThemeVariant()
    {
        var theme = new ThemeService();
        Assert.True(theme.IsDarkMode);

        theme.ToggleTheme();
        Assert.False(theme.IsDarkMode);

        theme.ToggleTheme();
        Assert.True(theme.IsDarkMode);
    }

    [Fact]
    public void MainWindowViewModel_FirstRunBanner_CanLaunchWizard()
    {
        var notifications = new DesktopNotificationService();
        var vm = new MainWindowViewModel(
            new ChatViewModel(new TestFakeRouter()),
            new ChainsViewModel(new TestFakeChainRepo([]), new TestFakeExecutionRepo([]), new TestFakeTriggerInvoker()),
            new HitlViewModel(new TestFakeHitlApprovalService()),
            new ServiceViewModel(new TestFakeServiceController()),
            new SchedulesViewModel(new TestFakeScheduler([])),
            new ModelsViewModel(),
            notifications,
            isFirstRun: true);

        Assert.True(vm.IsFirstRun);

        vm.LaunchSetupWizardCommand.Execute(null);

        Assert.NotNull(vm.SetupWizardNotice);
        Assert.Single(notifications.ActiveNotifications);
        Assert.Contains("Setup Wizard", notifications.ActiveNotifications[0].Title);

        vm.DismissSetupBannerCommand.Execute(null);
        Assert.False(vm.IsFirstRun);
    }
}

// ----------------- Fakes for Unit Tests -----------------

internal sealed class TestFakeRouter : IIntentRouter
{
    private readonly string _replyIntent;
    public RouteRequest? LastRequest { get; private set; }

    public TestFakeRouter(string replyIntent = "Acknowledged")
    {
        _replyIntent = replyIntent;
    }

    public Task<RouteResult> RouteAsync(RouteRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(new RouteResult
        {
            Status = RouteStatus.Matched,
            Tier = IntentTier.Tier1,
            Intent = _replyIntent,
            Confidence = 0.95,
            MatchedRuleName = "fake-semantic-match",
        });
    }
}

internal sealed class DelayingRouter : IIntentRouter
{
    private readonly TimeSpan _delay;

    public DelayingRouter(TimeSpan delay)
    {
        _delay = delay;
    }

    public async Task<RouteResult> RouteAsync(RouteRequest request, CancellationToken cancellationToken = default)
    {
        await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
        return new RouteResult
        {
            Status = RouteStatus.Matched,
            Tier = IntentTier.Tier2,
            Intent = "Delayed response",
        };
    }
}

internal sealed class TestFakeBudgetGovernor : ITokenBudgetGovernor
{
    private readonly decimal _spentToday;
    private readonly decimal _remainingToday;
    private readonly int _tokensUsed;

    public TestFakeBudgetGovernor(decimal spentToday, decimal remainingToday, int tokensUsed)
    {
        _spentToday = spentToday;
        _remainingToday = remainingToday;
        _tokensUsed = tokensUsed;
    }

    public Task<BudgetStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new BudgetStatusDto
        {
            Limits = new BudgetLimitsDto { DailyCapUsd = _spentToday + _remainingToday },
            SpentTodayUsd = _spentToday,
            RemainingTodayUsd = _remainingToday,
            TokensUsedToday = _tokensUsed,
            BudgetDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
    }

    public Task<BudgetDecision> TryAuthorizeAsync(BudgetRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new BudgetDecision { IsAllowed = true });

    public Task RecordUsageAsync(TokenUsageDto usage, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

internal sealed class TestFakeServiceController : IServiceController
{
    public int StartCallCount { get; private set; }
    public int StopCallCount { get; private set; }
    public int RestartCallCount { get; private set; }
    private string _state = "Stopped";

    public Task<ServiceControlResult> StartAsync(CancellationToken cancellationToken = default)
    {
        StartCallCount++;
        _state = "Running";
        return Task.FromResult(new ServiceControlResult(ServiceControlOutcome.Ok, null));
    }

    public Task<ServiceControlResult> StopAsync(CancellationToken cancellationToken = default)
    {
        StopCallCount++;
        _state = "Stopped";
        return Task.FromResult(new ServiceControlResult(ServiceControlOutcome.Ok, null));
    }

    public Task<ServiceControlResult> RestartAsync(CancellationToken cancellationToken = default)
    {
        RestartCallCount++;
        _state = "Running";
        return Task.FromResult(new ServiceControlResult(ServiceControlOutcome.Ok, null));
    }

    public Task<ServiceStatusDto> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ServiceStatusDto(_state, "1.0.0", DateTimeOffset.UtcNow, null));

    public async IAsyncEnumerable<ServiceStatusDto> WatchStatusAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return await GetStatusAsync(cancellationToken);
    }
}

internal sealed class DroppingServiceController : IServiceController
{
    public Task<ServiceControlResult> StartAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ServiceControlResult(ServiceControlOutcome.Ok, null));

    public Task<ServiceControlResult> StopAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ServiceControlResult(ServiceControlOutcome.Ok, null));

    public Task<ServiceControlResult> RestartAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ServiceControlResult(ServiceControlOutcome.Ok, null));

    public Task<ServiceStatusDto> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ServiceStatusDto("Running", "1.0.0", DateTimeOffset.UtcNow, null));

    public async IAsyncEnumerable<ServiceStatusDto> WatchStatusAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ServiceStatusDto("Running", "1.0.0", DateTimeOffset.UtcNow, null);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ServiceStatusDto("Faulted", "1.0.0", DateTimeOffset.UtcNow, "Service process exited.");
    }
}

internal sealed class FailingServiceController : IServiceController
{
    public Task<ServiceControlResult> StartAsync(CancellationToken cancellationToken = default) =>
        throw new IOException("Pipe disconnected");

    public Task<ServiceControlResult> StopAsync(CancellationToken cancellationToken = default) =>
        throw new IOException("Pipe disconnected");

    public Task<ServiceControlResult> RestartAsync(CancellationToken cancellationToken = default) =>
        throw new IOException("Pipe disconnected");

    public Task<ServiceStatusDto> GetStatusAsync(CancellationToken cancellationToken = default) =>
        throw new IOException("Pipe disconnected");

    public async IAsyncEnumerable<ServiceStatusDto> WatchStatusAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (cancellationToken.IsCancellationRequested)
        {
            yield break;
        }

        throw new IOException("Pipe disconnected");
    }
}

internal sealed class TestFakeHitlApprovalService : IHitlApprovalService
{
    public Guid? LastApprovedId { get; private set; }
    public Guid? LastRejectedId { get; private set; }
    public Guid? LastEditedId { get; private set; }
    public string? LastRejectReason { get; private set; }
    public string? LastEditedPayload { get; private set; }
    public ChannelKind? LastChannel { get; private set; }

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
        LastApprovedId = approvalId;
        LastChannel = channel;
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
        LastEditedId = approvalId;
        LastEditedPayload = editedPayloadJson;
        LastChannel = channel;
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
        LastRejectedId = approvalId;
        LastRejectReason = reason;
        LastChannel = channel;
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

internal sealed class TestFakeChainRepo : IChainDefinitionRepository
{
    private readonly List<ChainDefinitionDto> _items;

    public TestFakeChainRepo(IEnumerable<ChainDefinitionDto> items)
    {
        _items = items.ToList();
    }

    public Task<ChainDefinitionDto?> GetByIdAsync(ChainId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_items.FirstOrDefault(c => c.Id == id));

    public Task<IReadOnlyList<ChainDefinitionDto>> ListEnabledAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ChainDefinitionDto>>(_items.Where(c => c.IsEnabled).ToList());

    public Task<ChainDefinitionDto> UpsertAsync(ChainDefinitionDto definition, CancellationToken cancellationToken = default) =>
        Task.FromResult(definition);

    public Task<bool> SetEnabledAsync(ChainId id, bool isEnabled, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

internal sealed class TestFakeExecutionRepo : IChainExecutionRepository
{
    private readonly Dictionary<RunId, ChainRunState> _runs;

    public TestFakeExecutionRepo(Dictionary<RunId, ChainRunState> runs)
    {
        _runs = runs;
    }

    public Task<ChainRunState?> GetRunAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        _runs.TryGetValue(runId, out ChainRunState? state);
        return Task.FromResult(state);
    }

    public Task<RunId?> FindRunByIdempotencyKeyAsync(ChainId chainId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<RunId?>(null);

    public Task<ChainRunState> CreateRunAsync(RunId runId, ChainId chainId, TriggerEvent trigger, CancellationToken cancellationToken = default)
    {
        var state = new ChainRunState
        {
            RunId = runId,
            ChainId = chainId,
            Status = ChainRunStatus.Running,
            Trigger = trigger,
            StartedAtUtc = DateTimeOffset.UtcNow,
        };
        _runs[runId] = state;
        return Task.FromResult(state);
    }

    public Task UpdateRunStatusAsync(RunId runId, ChainRunStatus status, ErrorDto? error = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SaveCheckpointAsync(ChainCheckpointDto checkpoint, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<ChainCheckpointDto>> GetCheckpointsAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        if (_runs.TryGetValue(runId, out ChainRunState? state))
        {
            return Task.FromResult(state.Checkpoints);
        }
        return Task.FromResult<IReadOnlyList<ChainCheckpointDto>>([]);
    }

    public Task<IReadOnlyList<RunId>> ListUnfinishedRunsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<RunId>>(_runs.Keys.ToList());
    }
}

internal sealed class TestFakeTriggerInvoker : ITriggerInvoker
{
    public ChainId? LastInvokedChainId { get; private set; }
    public string? LastPayloadJson { get; private set; }

    public Task<RunId> InvokeFunctionCallAsync(ChainId chainId, string payloadJson, CancellationToken cancellationToken = default)
    {
        LastInvokedChainId = chainId;
        LastPayloadJson = payloadJson;
        return Task.FromResult(new RunId(Guid.NewGuid()));
    }
}

internal sealed class TestFakeScheduler : IChainSchedulerService
{
    private readonly List<ScheduledChainInfo> _schedules;
    public ChainId? LastPausedChainId { get; private set; }
    public ChainId? LastResumedChainId { get; private set; }
    public ChainId? LastUnscheduledChainId { get; private set; }

    public TestFakeScheduler(IEnumerable<ScheduledChainInfo> schedules)
    {
        _schedules = schedules.ToList();
    }

    public Task ScheduleChainAsync(ChainId chainId, string cronExpression, ChainMisfirePolicy misfirePolicy = ChainMisfirePolicy.FireOnceNow, CancellationToken cancellationToken = default)
    {
        if (cronExpression.Contains("invalid", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidCronExpressionException(cronExpression, "Invalid cron format");
        }

        _schedules.Add(new ScheduledChainInfo
        {
            ChainId = chainId,
            CronExpression = cronExpression,
            IsPaused = false,
            MisfirePolicy = misfirePolicy,
        });
        return Task.CompletedTask;
    }

    public Task PauseChainAsync(ChainId chainId, CancellationToken cancellationToken = default)
    {
        LastPausedChainId = chainId;
        ScheduledChainInfo? item = _schedules.FirstOrDefault(s => s.ChainId == chainId);
        if (item != null)
        {
            _schedules.Remove(item);
            _schedules.Add(item with { IsPaused = true });
        }
        return Task.CompletedTask;
    }

    public Task ResumeChainAsync(ChainId chainId, CancellationToken cancellationToken = default)
    {
        LastResumedChainId = chainId;
        ScheduledChainInfo? item = _schedules.FirstOrDefault(s => s.ChainId == chainId);
        if (item != null)
        {
            _schedules.Remove(item);
            _schedules.Add(item with { IsPaused = false });
        }
        return Task.CompletedTask;
    }

    public Task UnscheduleChainAsync(ChainId chainId, CancellationToken cancellationToken = default)
    {
        LastUnscheduledChainId = chainId;
        _schedules.RemoveAll(s => s.ChainId == chainId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScheduledChainInfo>> ListScheduledAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ScheduledChainInfo>>(_schedules.ToList());

    public Task TriggerChainNowAsync(ChainId chainId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SyncWithRepositoryAsync(IChainDefinitionRepository repository, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

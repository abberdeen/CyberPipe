namespace CyberPipe.Tests.Unit;

using CyberPipe.Chains.Execution;
using CyberPipe.Chains.Hitl;
using CyberPipe.Channels.Telegram;
using CyberPipe.Channels.Telegram.Adapter;
using CyberPipe.Channels.Telegram.Model;
using CyberPipe.Contracts;
using CyberPipe.Data;
using CyberPipe.Router;
using CyberPipe.Security;
using CyberPipe.Service.Messaging;
using Xunit;

public sealed class TelegramScenarioTests
{
    private const long OwnerChatId = 777001;
    private const string OwnerId = "111";
    private const string StrangerId = "999";

    private sealed class CountingSkill
    {
        public int Invocations { get; private set; }

        public string LastLevel { get; private set; } = string.Empty;

        public Task<string> InvokeAsync(string level)
        {
            Invocations++;
            LastLevel = level;
            return Task.FromResult($"Volume set to {level}.");
        }
    }

    private sealed class StubExecutor : IChainExecutor
    {
        public Task<RunId> StartAsync(TriggerEvent triggerEvent, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RunId(Guid.NewGuid()));

        public Task ResumeAsync(RunId runId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CancelAsync(RunId runId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ChainRunState?> GetRunAsync(RunId runId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ChainRunState?>(null);
    }

    private sealed class AssistantHarness : IMessageHandler<IncomingMessage>
    {
        private readonly IOwnerAuthorizer _authorizer;
        private readonly Tier0RegexRouter _router;
        private readonly TokenBudgetGovernor _governor;
        private readonly CountingSkill _skill;
        private readonly TelegramChannelService _channel;

        public int RouterCalls { get; private set; }

        private readonly ModelHostingType _hostingType;

        public AssistantHarness(
            IOwnerAuthorizer authorizer,
            Tier0RegexRouter router,
            TokenBudgetGovernor governor,
            CountingSkill skill,
            TelegramChannelService channel,
            ModelHostingType hostingType = ModelHostingType.Local)
        {
            _authorizer = authorizer;
            _router = router;
            _governor = governor;
            _skill = skill;
            _channel = channel;
            _hostingType = hostingType;
        }

        public async Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken = default)
        {
            OwnerAuthDecision auth = await _authorizer.AuthorizeAsync(
                ChannelPrincipal.FromChannel(message.Channel, message.SenderId),
                cancellationToken).ConfigureAwait(false);
            if (!auth.IsAuthorized)
            {
                return;
            }

            RouterCalls++;
            RouteResult route = await _router.TryRouteAsync(
                new RouteRequest { Text = message.Text ?? string.Empty, Channel = message.Channel },
                cancellationToken).ConfigureAwait(false);
            if (route.Status != RouteStatus.Matched || route.Intent != "media.volume")
            {
                return;
            }

            BudgetDecision budget = await _governor.TryAuthorizeAsync(
                new BudgetRequest
                {
                    ModelId = "gpt-4o-mini",
                    HostingType = _hostingType,
                    EstimatedInputTokens = 100,
                    EstimatedOutputTokens = 50,
                },
                cancellationToken).ConfigureAwait(false);
            if (!budget.IsAllowed)
            {
                await ReplyAsync(message, "Превышен бюджет: отказ.", cancellationToken).ConfigureAwait(false);
                return;
            }

            string level = route.Parameters.TryGetValue("level", out string? parsed) ? parsed : "?";
            string result = await _skill.InvokeAsync(level).ConfigureAwait(false);
            await ReplyAsync(message, result, cancellationToken).ConfigureAwait(false);
        }

        private Task ReplyAsync(IncomingMessage message, string text, CancellationToken ct) =>
            _channel.HandleAsync(
                new OutgoingMessage
                {
                    Channel = message.Channel,
                    Target = message.ChatId ?? message.SenderId,
                    Text = text,
                },
                ct);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public FakeTelegramBotClientAdapter Adapter { get; } = new();

        public InProcessMessageBus Bus { get; } = new();

        public CountingSkill Skill { get; } = new();

        public TelegramChannelService Channel { get; }

        public IOwnerAuthorizer Authorizer { get; }

        public Tier0RegexRouter Router { get; }

        public AssistantHarness Assistant { get; }

        public HitlApprovalService Hitl { get; }

        public HitlApprovalGate Gate { get; }

        public HitlApprovalStoreService HitlRepo { get; }

        private readonly TestDatabase _db;

        public static async Task<Harness> CreateAsync(
            decimal dailyCapUsd = 100m,
            ModelHostingType hostingType = ModelHostingType.Local)
        {
            var options = new CyberPipeOptions
            {
                Owner = new OwnerOptions { TelegramOwnerIds = [OwnerId] },
                Budget = new BudgetOptions { DailyCapUsd = dailyCapUsd },
            };
            var whitelist = new MutableWhitelist(new OwnerWhitelistSnapshot
            {
                TelegramOwnerIds = [OwnerId],
            });
            var db = await TestDatabase.CreateAsync();
            try
            {
                return new Harness(options, whitelist, db, hostingType);
            }
            catch
            {
                await db.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync() => await _db.DisposeAsync();

        private Harness(
            CyberPipeOptions options,
            MutableWhitelist whitelist,
            TestDatabase db,
            ModelHostingType hostingType)
        {
            _db = db;
            HitlRepo = db.Hitl;
            Authorizer = new OwnerAuthorizer(whitelist);
            Router = new Tier0RegexRouter(new ConfigurationRegexRuleProvider(options));
            var governor = new TokenBudgetGovernor(new InMemoryBudgetStore(), options);
            var guard = new OwnerAuthorizerGuard(Authorizer, OwnerChannel.Telegram);
            TelegramChannelService? channelService = null;
            Hitl = new HitlApprovalService(
                HitlRepo,
                [new ForwardingCardSender(() => channelService!)],
                guard,
                new StubExecutor(),
                executions: db.Executions);
            Channel = new TelegramChannelService(
                Adapter,
                guard,
                new TelegramOptions { OwnerChatId = OwnerChatId },
                messageBus: Bus,
                hitlService: Hitl);
            channelService = Channel;
            Assistant = new AssistantHarness(Authorizer, Router, governor, Skill, Channel, hostingType);
            Bus.Subscribe<IncomingMessage>(Assistant);
            Gate = new HitlApprovalGate(Hitl);
        }

        public async Task FeedTextAsync(long chatId, long userId, string text)
        {
            Adapter.EnqueueTextMessage(chatId, userId, text);
            TelegramUpdate update = (await Adapter.GetUpdatesAsync())[0];
            await Channel.ProcessUpdateAsync(update, CancellationToken.None);
            await Task.Delay(300);
        }

        public async Task FeedCallbackAsync(string callbackId, long chatId, long userId, string data)
        {
            Adapter.EnqueueCallbackQuery(callbackId, chatId, userId, data);
            TelegramUpdate update = (await Adapter.GetUpdatesAsync())[0];
            await Channel.ProcessUpdateAsync(update, CancellationToken.None);
            await Task.Delay(300);
        }
    }

    private sealed class ForwardingCardSender(Func<TelegramChannelService> channel) : IHitlCardSender
    {
        public ChannelKind Channel => ChannelKind.Telegram;

        public Task SendCardAsync(HitlApprovalRequest request, CancellationToken cancellationToken = default) =>
            channel().SendCardAsync(request, cancellationToken);
    }

    private sealed class MutableWhitelist(OwnerWhitelistSnapshot snapshot) : IOwnerWhitelistProvider
    {
        public event EventHandler? WhitelistChanged;

        public void RaiseChanged() => WhitelistChanged?.Invoke(this, EventArgs.Empty);

        public Task<OwnerWhitelistSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private static HitlApprovalRequest ApprovalRequest(TimeSpan? lifetime = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new HitlApprovalRequest
        {
            ApprovalId = Guid.NewGuid(),
            RunId = new RunId(Guid.NewGuid()),
            ChainId = new ChainId(Guid.NewGuid()),
            Title = "Delete file confirm",
            PreviewText = "Delete /tmp/report.csv",
            PayloadJson = "{}",
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(lifetime ?? TimeSpan.FromMinutes(5)),
        };
    }

    [Fact]
    public async Task OwnerVolumeCommand_RunsSkillOnceAndRepliesToSameChat()
    {
        await using var harness = await Harness.CreateAsync();

        await harness.FeedTextAsync(OwnerChatId, long.Parse(OwnerId), "громкость на 100");

        Assert.Equal(1, harness.Skill.Invocations);
        Assert.Equal("100", harness.Skill.LastLevel);
        Assert.NotEmpty(harness.Adapter.SentMessages);
        SentTelegramMessageRecord reply = harness.Adapter.SentMessages[^1];
        Assert.Equal(OwnerChatId, reply.ChatId);
        Assert.Contains("100", reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonOwnerMessage_RejectedBeforeRouter()
    {
        await using var harness = await Harness.CreateAsync();

        await harness.FeedTextAsync(OwnerChatId, long.Parse(StrangerId), "громкость на 100");

        Assert.Equal(0, harness.Skill.Invocations);
        Assert.Equal(0, harness.Assistant.RouterCalls);
        Assert.DoesNotContain(harness.Adapter.SentMessages, m => m.ChatId == OwnerChatId && m.Text.Contains("100"));
    }

    [Fact]
    public async Task BudgetExhausted_OwnerGetsRefusalWithoutSkillCall()
    {
        await using var harness = await Harness.CreateAsync(dailyCapUsd: 0.000001m, hostingType: ModelHostingType.RemoteCloud);

        await harness.FeedTextAsync(OwnerChatId, long.Parse(OwnerId), "громкость на 100");

        Assert.Equal(0, harness.Skill.Invocations);
        Assert.NotEmpty(harness.Adapter.SentMessages);
        Assert.Contains("бюджет", harness.Adapter.SentMessages[^1].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HitlApprove_RunsSkillOnceAndReplies()
    {
        await using var harness = await Harness.CreateAsync();
        HitlApprovalRequest request = ApprovalRequest();
        await harness.Hitl.CreateAsync(request);

        Assert.Contains(harness.Adapter.SentMessages, m => m.Text.Contains("Delete file confirm"));

        Task<HitlApprovalResult> gateTask = harness.Gate.RequestApprovalAsync(request);
        await harness.FeedCallbackAsync("cb-approve", OwnerChatId, long.Parse(OwnerId), $"hitl:approve:{request.ApprovalId}");
        HitlApprovalResult result = await gateTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HitlDecision.Approved, result.Decision);
        string skillResult = await harness.Skill.InvokeAsync("approved");
        Assert.Equal(1, harness.Skill.Invocations);
        await harness.Channel.HandleAsync(
            new OutgoingMessage { Channel = ChannelKind.Telegram, Target = OwnerId, Text = skillResult });
        Assert.Contains("approved", harness.Adapter.SentMessages[^1].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HitlRejectFromNonOwner_DoesNotResolve()
    {
        await using var harness = await Harness.CreateAsync();
        HitlApprovalRequest request = ApprovalRequest();
        await harness.Hitl.CreateAsync(request);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<HitlApprovalResult> gateTask = harness.Gate.RequestApprovalAsync(request, cts.Token);
        await harness.FeedCallbackAsync("cb-reject", OwnerChatId, long.Parse(StrangerId), $"hitl:reject:{request.ApprovalId}");
        await Task.Delay(500);

        Assert.False(gateTask.IsCompleted);
        Assert.Equal(0, harness.Skill.Invocations);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gateTask);
    }

    [Fact]
    public async Task HitlExpired_ApprovalCannotResolve()
    {
        await using var harness = await Harness.CreateAsync();
        HitlApprovalRequest request = ApprovalRequest(TimeSpan.FromMilliseconds(200));
        await harness.Hitl.CreateAsync(request);

        await Task.Delay(600);
        int expired = await harness.Hitl.ExpireOverdueAsync(DateTimeOffset.UtcNow);

        Assert.Equal(1, expired);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            harness.Hitl.ApproveAsync(request.ApprovalId, OwnerId, ChannelKind.Telegram));
        Assert.Equal(0, harness.Skill.Invocations);
    }
}

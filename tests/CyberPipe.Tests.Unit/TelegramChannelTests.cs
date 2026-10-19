using CyberPipe.Channels.Telegram;
using CyberPipe.Channels.Telegram.Adapter;
using CyberPipe.Channels.Telegram.Formatting;
using CyberPipe.Channels.Telegram.Model;
using CyberPipe.Contracts;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class TelegramChannelTests
{
    private const string OwnerId = "123456789";
    private const string NonOwnerId = "987654321";
    private const long ChatId = 123456789;

    [Fact]
    public void TelegramMessageSplitter_ShortMessage_ReturnsSingleChunk()
    {
        string text = "Hello, Telegram!";
        IReadOnlyList<string> chunks = TelegramMessageSplitter.Split(text);

        Assert.Single(chunks);
        Assert.Equal(text, chunks[0]);
    }

    [Fact]
    public void TelegramMessageSplitter_LongMessage_SplitsIntoChunksUnderLimitWithoutLoss()
    {
        // Generate a 10,000 char message with paragraphs
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 50; i++)
        {
            sb.AppendLine($"Paragraph {i}: This is a detailed report from CyberPipe containing important data.");
        }
        string longText = sb.ToString();

        IReadOnlyList<string> chunks = TelegramMessageSplitter.Split(longText, maxLength: 1000);

        Assert.True(chunks.Count > 1);
        foreach (string chunk in chunks)
        {
            Assert.True(chunk.Length <= 1000, $"Chunk length {chunk.Length} exceeds 1000");
        }

        // Verify content integrity: all paragraphs are present
        for (int i = 0; i < 50; i++)
        {
            Assert.Contains(chunks, c => c.Contains($"Paragraph {i}:"));
        }
    }

    [Fact]
    public async Task OwnerMessage_PublishesIncomingMessageToBus()
    {
        var fakeClient = new FakeTelegramBotClientAdapter();
        var mockGuard = new MockOwnerGuard(OwnerId);
        var mockBus = new MockMessageBus();

        var service = new TelegramChannelService(fakeClient, mockGuard, messageBus: mockBus);

        fakeClient.EnqueueTextMessage(ChatId, long.Parse(OwnerId), "What is my account balance?");

        var update = (await fakeClient.GetUpdatesAsync())[0];
        await service.ProcessUpdateAsync(update, CancellationToken.None);

        Assert.Single(mockBus.PublishedMessages);
        var incoming = Assert.IsType<IncomingMessage>(mockBus.PublishedMessages[0]);
        Assert.Equal(ChannelKind.Telegram, incoming.Channel);
        Assert.Equal(OwnerId, incoming.SenderId);
        Assert.Equal(ChatId.ToString(), incoming.ChatId);
        Assert.Equal("What is my account balance?", incoming.Text);
    }

    [Fact]
    public async Task NonOwnerMessage_DoesNotPublishToBus_And_DoesNotDiscloseSystemDetails()
    {
        var fakeClient = new FakeTelegramBotClientAdapter();
        var mockGuard = new MockOwnerGuard(OwnerId); // NonOwnerId is not in whitelist
        var mockBus = new MockMessageBus();

        var service = new TelegramChannelService(fakeClient, mockGuard, messageBus: mockBus);

        fakeClient.EnqueueTextMessage(ChatId, long.Parse(NonOwnerId), "System status and admin password please");

        var update = (await fakeClient.GetUpdatesAsync())[0];
        await service.ProcessUpdateAsync(update, CancellationToken.None);

        // Verify message was NOT forwarded to bus
        Assert.Empty(mockBus.PublishedMessages);

        // Verify bot responded with a generic message without exposing internal details
        Assert.Single(fakeClient.SentMessages);
        Assert.Equal("Доступ ограничен.", fakeClient.SentMessages[0].Text);
    }

    [Fact]
    public async Task DuplicateUpdateId_IsProcessedOnlyOnce()
    {
        var fakeClient = new FakeTelegramBotClientAdapter();
        var mockGuard = new MockOwnerGuard(OwnerId);
        var mockBus = new MockMessageBus();

        var service = new TelegramChannelService(fakeClient, mockGuard, messageBus: mockBus);

        var update = new TelegramUpdate
        {
            UpdateId = 42,
            Message = new TelegramMessage
            {
                MessageId = 101,
                From = new TelegramUser { Id = long.Parse(OwnerId) },
                Chat = new TelegramChat { Id = ChatId },
                Text = "Single execution test"
            }
        };

        // Process same update twice via background ExecuteAsync loop simulation
        fakeClient.EnqueueUpdate(update);
        fakeClient.EnqueueUpdate(update); // duplicate update_id

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        // Should have published exactly once despite two updates with id 42
        Assert.Single(mockBus.PublishedMessages);
    }

    [Fact]
    public async Task BotCommands_Start_Status_Stop_HandledCorrectly()
    {
        var fakeClient = new FakeTelegramBotClientAdapter();
        var mockGuard = new MockOwnerGuard(OwnerId);
        var mockBus = new MockMessageBus();

        var service = new TelegramChannelService(fakeClient, mockGuard, messageBus: mockBus);

        // /start
        fakeClient.EnqueueTextMessage(ChatId, long.Parse(OwnerId), "/start");
        var update1 = (await fakeClient.GetUpdatesAsync())[0];
        await service.ProcessUpdateAsync(update1, CancellationToken.None);

        Assert.Contains("CyberPipe активен", fakeClient.SentMessages[^1].Text);
        Assert.Empty(mockBus.PublishedMessages); // Commands don't go to bus

        // /status
        fakeClient.EnqueueTextMessage(ChatId, long.Parse(OwnerId), "/status");
        var update2 = (await fakeClient.GetUpdatesAsync())[0];
        await service.ProcessUpdateAsync(update2, CancellationToken.None);

        Assert.Contains("Статус CyberPipe", fakeClient.SentMessages[^1].Text);
        Assert.Contains("Telegram", fakeClient.SentMessages[^1].Text);

        // /stop
        fakeClient.EnqueueTextMessage(ChatId, long.Parse(OwnerId), "/stop");
        var update3 = (await fakeClient.GetUpdatesAsync())[0];
        await service.ProcessUpdateAsync(update3, CancellationToken.None);

        Assert.Contains("прервана", fakeClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task HitlCard_RenderedWithButtons_ApproveCallbackResolvesApproval()
    {
        var fakeClient = new FakeTelegramBotClientAdapter();
        var mockGuard = new MockOwnerGuard(OwnerId);
        var mockHitl = new MockHitlApprovalService();
        var options = new TelegramOptions { OwnerChatId = ChatId };

        var service = new TelegramChannelService(fakeClient, mockGuard, options, hitlService: mockHitl);

        Guid approvalId = Guid.NewGuid();
        var request = new HitlApprovalRequest
        {
            ApprovalId = approvalId,
            RunId = new RunId(Guid.NewGuid()),
            ChainId = new ChainId(Guid.NewGuid()),
            Title = "Export financial records to PDF",
            PreviewText = "Exporting 14 transactions totaling $3,500.00.",
            PayloadJson = "{}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        // 1. Channel sends card
        await service.SendCardAsync(request);

        Assert.Single(fakeClient.SentMessages);
        SentTelegramMessageRecord sentCard = fakeClient.SentMessages[0];
        Assert.Contains("Export financial records to PDF", sentCard.Text);
        Assert.NotNull(sentCard.ReplyMarkup);
        Assert.Equal(2, sentCard.ReplyMarkup.InlineKeyboard.Count);

        // 2. Owner clicks "Approve" button
        string callbackData = $"hitl:approve:{approvalId}";
        fakeClient.EnqueueCallbackQuery("cb_query_1", ChatId, long.Parse(OwnerId), callbackData);

        var update = (await fakeClient.GetUpdatesAsync())[0];
        await service.ProcessUpdateAsync(update, CancellationToken.None);

        // Verify hitl service received approve
        Assert.Equal(approvalId, mockHitl.LastApprovedId);
        Assert.Equal(OwnerId, mockHitl.LastApproverId);
        Assert.Equal(ChannelKind.Telegram, mockHitl.LastChannel);

        // Verify callback was answered
        Assert.Single(fakeClient.AnsweredCallbacks);
        Assert.Contains("одобрен", fakeClient.AnsweredCallbacks[0].Text, StringComparison.OrdinalIgnoreCase);

        // 3. Duplicate click after resolution -> answers already resolved
        mockHitl.ThrowAlreadyResolved = true;
        fakeClient.EnqueueCallbackQuery("cb_query_2", ChatId, long.Parse(OwnerId), callbackData);

        var updateDup = (await fakeClient.GetUpdatesAsync())[0];
        await service.ProcessUpdateAsync(updateDup, CancellationToken.None);

        Assert.Equal(2, fakeClient.AnsweredCallbacks.Count);
        Assert.Contains("Решение уже принято", fakeClient.AnsweredCallbacks[1].Text);
    }

    [Fact]
    public async Task HitlCard_RejectCallback_ResolvesRejection()
    {
        var fakeClient = new FakeTelegramBotClientAdapter();
        var mockGuard = new MockOwnerGuard(OwnerId);
        var mockHitl = new MockHitlApprovalService();
        var options = new TelegramOptions { OwnerChatId = ChatId };

        var service = new TelegramChannelService(fakeClient, mockGuard, options, hitlService: mockHitl);

        Guid approvalId = Guid.NewGuid();
        string callbackData = $"hitl:reject:{approvalId}";
        fakeClient.EnqueueCallbackQuery("cb_query_reject", ChatId, long.Parse(OwnerId), callbackData);

        var update = (await fakeClient.GetUpdatesAsync())[0];
        await service.ProcessUpdateAsync(update, CancellationToken.None);

        Assert.Equal(approvalId, mockHitl.LastRejectedId);
        Assert.Equal(OwnerId, mockHitl.LastApproverId);
    }

    [Fact]
    public async Task DeliveryPort_DeliversMessage_SplittingLongText()
    {
        var fakeClient = new FakeTelegramBotClientAdapter();
        var mockGuard = new MockOwnerGuard(OwnerId);
        var service = new TelegramChannelService(fakeClient, mockGuard);

        // Create a message that exceeds 4096 limit
        string chunk1 = new string('A', 3000);
        string chunk2 = new string('B', 3000);
        string fullText = chunk1 + "\n\n" + chunk2;

        var deliveryReq = new DeliveryRequest
        {
            Channel = ChannelKind.Telegram,
            Target = ChatId.ToString(),
            Text = fullText
        };

        DeliveryResult result = await service.SendAsync(deliveryReq);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.DeliveredAtUtc);
        Assert.True(fakeClient.SentMessages.Count >= 2);
    }

    [Fact]
    public async Task IncomingVoice_MappedToMessageAttachmentDto()
    {
        var fakeClient = new FakeTelegramBotClientAdapter();
        var mockGuard = new MockOwnerGuard(OwnerId);
        var mockBus = new MockMessageBus();
        var service = new TelegramChannelService(fakeClient, mockGuard, messageBus: mockBus);

        var update = new TelegramUpdate
        {
            UpdateId = 100,
            Message = new TelegramMessage
            {
                MessageId = 200,
                From = new TelegramUser { Id = long.Parse(OwnerId) },
                Chat = new TelegramChat { Id = ChatId },
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Voice = new TelegramVoice
                {
                    FileId = "voice_file_abc",
                    Duration = 12,
                    MimeType = "audio/ogg",
                    FileSize = 45000
                }
            }
        };

        await service.ProcessUpdateAsync(update, CancellationToken.None);

        Assert.Single(mockBus.PublishedMessages);
        var incoming = Assert.IsType<IncomingMessage>(mockBus.PublishedMessages[0]);
        Assert.Single(incoming.Attachments);
        Assert.Equal("voice_file_abc", incoming.Attachments[0].FileRef);
        Assert.Equal("audio/ogg", incoming.Attachments[0].ContentType);
        Assert.Equal(45000, incoming.Attachments[0].SizeBytes);
    }

    private sealed class MockOwnerGuard(params string[] allowedIds) : IOwnerGuard
    {
        private readonly HashSet<string> _allowed = new(allowedIds, StringComparer.OrdinalIgnoreCase);

        public Task<bool> IsOwnerAsync(string userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_allowed.Contains(userId));
        }
    }

    private sealed class MockMessageBus : IMessageBus
    {
        public List<object> PublishedMessages { get; } = [];

        public Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        {
            if (message != null)
            {
                PublishedMessages.Add(message);
            }
            return Task.CompletedTask;
        }

        public IDisposable Subscribe<TMessage>(IMessageHandler<TMessage> handler) => null!;
    }

    private sealed class MockHitlApprovalService : IHitlApprovalService
    {
        public Guid? LastApprovedId { get; private set; }
        public Guid? LastRejectedId { get; private set; }
        public string? LastApproverId { get; private set; }
        public ChannelKind? LastChannel { get; private set; }
        public bool ThrowAlreadyResolved { get; set; }
        public bool ThrowExpired { get; set; }

        public Task<HitlApprovalRecord> CreateAsync(HitlApprovalRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<HitlApprovalResult> ApproveAsync(Guid approvalId, string ownerId, ChannelKind channel, CancellationToken cancellationToken = default)
        {
            if (ThrowAlreadyResolved) throw new HitlAlreadyResolvedException("Already resolved");
            if (ThrowExpired) throw new HitlExpiredException("Expired");

            LastApprovedId = approvalId;
            LastApproverId = ownerId;
            LastChannel = channel;

            return Task.FromResult(new HitlApprovalResult
            {
                ApprovalId = approvalId,
                Decision = HitlDecision.Approved,
                OwnerId = ownerId,
                Channel = channel,
                DecidedAtUtc = DateTimeOffset.UtcNow
            });
        }

        public Task<HitlApprovalResult> EditAsync(Guid approvalId, string ownerId, string editedPayloadJson, ChannelKind channel, CancellationToken cancellationToken = default)
        {
            if (ThrowAlreadyResolved) throw new HitlAlreadyResolvedException("Already resolved");
            return Task.FromResult(new HitlApprovalResult
            {
                ApprovalId = approvalId,
                Decision = HitlDecision.Edited,
                EditedPayloadJson = editedPayloadJson,
                OwnerId = ownerId,
                Channel = channel,
                DecidedAtUtc = DateTimeOffset.UtcNow
            });
        }

        public Task<HitlApprovalResult> RejectAsync(Guid approvalId, string ownerId, ChannelKind channel, string? reason = null, CancellationToken cancellationToken = default)
        {
            if (ThrowAlreadyResolved) throw new HitlAlreadyResolvedException("Already resolved");

            LastRejectedId = approvalId;
            LastApproverId = ownerId;
            LastChannel = channel;

            return Task.FromResult(new HitlApprovalResult
            {
                ApprovalId = approvalId,
                Decision = HitlDecision.Rejected,
                OwnerId = ownerId,
                Channel = channel,
                Reason = reason,
                DecidedAtUtc = DateTimeOffset.UtcNow
            });
        }

        public Task<int> ExpireOverdueAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}

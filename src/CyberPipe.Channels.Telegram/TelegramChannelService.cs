using System.Collections.Concurrent;
using CyberPipe.Channels.Telegram.Adapter;
using CyberPipe.Channels.Telegram.Formatting;
using CyberPipe.Channels.Telegram.Model;
using CyberPipe.Contracts;
using Microsoft.Extensions.Hosting;

namespace CyberPipe.Channels.Telegram;

/// <summary>
/// Сервис Telegram-канала: long polling, приём сообщений, доставка результатов и HITL-кнопки (TASK-060).
/// </summary>
public sealed class TelegramChannelService : BackgroundService, IHitlCardSender, IDeliveryPort, IMessageHandler<OutgoingMessage>
{
    private readonly ITelegramBotClientAdapter _client;
    private readonly IOwnerGuard _ownerGuard;
    private readonly IMessageBus? _messageBus;
    private readonly IHitlApprovalService? _hitlService;
    private readonly TelegramOptions _options;

    private readonly ConcurrentDictionary<long, DateTimeOffset> _processedUpdateIds = new();
    private readonly ConcurrentDictionary<long, Guid> _pendingEdits = new();
    private long _lastUpdateId;

    public ChannelKind Channel => ChannelKind.Telegram;

    public TelegramChannelService(
        ITelegramBotClientAdapter client,
        IOwnerGuard ownerGuard,
        TelegramOptions? options = null,
        IMessageBus? messageBus = null,
        IHitlApprovalService? hitlService = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _ownerGuard = ownerGuard ?? throw new ArgumentNullException(nameof(ownerGuard));
        _options = options ?? new TelegramOptions();
        _messageBus = messageBus;
        _hitlService = hitlService;
    }

    /// <inheritdoc />
    public async Task SendCardAsync(HitlApprovalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        long targetChatId = _options.OwnerChatId ?? 0;
        if (targetChatId == 0)
        {
            return;
        }

        string cardText = $"⚠️ *Запрос подтверждения владельца (HITL)*\n\n" +
                          $"*{EscapeMarkdown(request.Title)}*\n" +
                          $"{EscapeMarkdown(request.PreviewText)}";

        var markup = new TelegramInlineKeyboardMarkup
        {
            InlineKeyboard =
            [
                [
                    new TelegramInlineKeyboardButton { Text = "✅ Одобрить", CallbackData = $"hitl:approve:{request.ApprovalId}" },
                    new TelegramInlineKeyboardButton { Text = "❌ Отклонить", CallbackData = $"hitl:reject:{request.ApprovalId}" }
                ],
                [
                    new TelegramInlineKeyboardButton { Text = "✏️ Изменить", CallbackData = $"hitl:edit:{request.ApprovalId}" }
                ]
            ]
        };

        await _client.SendTextMessageAsync(targetChatId, cardText, markup, parseMode: "Markdown", ct: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DeliveryResult> SendAsync(DeliveryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Channel != ChannelKind.Telegram)
        {
            return new DeliveryResult
            {
                IsSuccess = false,
                Error = new ErrorDto
                {
                    Code = "ChannelMismatch",
                    Message = $"Expected Telegram channel, got '{request.Channel}'."
                }
            };
        }

        if (!long.TryParse(request.Target, out long chatId))
        {
            if (_options.OwnerChatId.HasValue)
            {
                chatId = _options.OwnerChatId.Value;
            }
            else
            {
                return new DeliveryResult
                {
                    IsSuccess = false,
                    Error = new ErrorDto
                    {
                        Code = "InvalidTargetChat",
                        Message = $"Target '{request.Target}' is not a valid Telegram chat ID."
                    }
                };
            }
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(request.Text))
            {
                IReadOnlyList<string> chunks = TelegramMessageSplitter.Split(request.Text);
                foreach (string chunk in chunks)
                {
                    await _client.SendTextMessageAsync(chatId, chunk, ct: cancellationToken).ConfigureAwait(false);
                }
            }

            foreach (MessageAttachmentDto attachment in request.AttachFiles)
            {
                if (!string.IsNullOrWhiteSpace(attachment.FileRef) && File.Exists(attachment.FileRef))
                {
                    await using var fs = File.OpenRead(attachment.FileRef);
                    await _client.SendDocumentAsync(chatId, fs, attachment.FileName, caption: null, ct: cancellationToken).ConfigureAwait(false);
                }
            }

            return new DeliveryResult
            {
                IsSuccess = true,
                DeliveredAtUtc = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            return new DeliveryResult
            {
                IsSuccess = false,
                Error = new ErrorDto
                {
                    Code = "DeliveryFailed",
                    Message = ex.Message
                }
            };
        }
    }

    /// <inheritdoc />
    public async Task HandleAsync(OutgoingMessage message, CancellationToken cancellationToken = default)
    {
        if (message.Channel != ChannelKind.Telegram)
        {
            return;
        }

        if (!long.TryParse(message.Target, out long chatId))
        {
            if (_options.OwnerChatId.HasValue)
            {
                chatId = _options.OwnerChatId.Value;
            }
            else
            {
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            IReadOnlyList<string> chunks = TelegramMessageSplitter.Split(message.Text);
            foreach (string chunk in chunks)
            {
                await _client.SendTextMessageAsync(chatId, chunk, ct: cancellationToken).ConfigureAwait(false);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<TelegramUpdate> updates = await _client.GetUpdatesAsync(
                    offset: _lastUpdateId + 1,
                    limit: 100,
                    timeoutSeconds: _options.PollingTimeoutSeconds,
                    ct: stoppingToken).ConfigureAwait(false);

                foreach (TelegramUpdate update in updates)
                {
                    _lastUpdateId = Math.Max(_lastUpdateId, update.UpdateId);

                    // Idempotency: skip already processed update IDs
                    if (!_processedUpdateIds.TryAdd(update.UpdateId, DateTimeOffset.UtcNow))
                    {
                        continue;
                    }

                    await ProcessUpdateAsync(update, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Network error backoff
                try
                {
                    await Task.Delay(Math.Max(500, _options.PollingIntervalMs), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    public async Task ProcessUpdateAsync(TelegramUpdate update, CancellationToken ct)
    {
        if (update.CallbackQuery != null)
        {
            await HandleCallbackQueryAsync(update.CallbackQuery, ct).ConfigureAwait(false);
            return;
        }

        if (update.Message != null)
        {
            await HandleIncomingMessageAsync(update.Message, ct).ConfigureAwait(false);
        }
    }

    private async Task HandleCallbackQueryAsync(TelegramCallbackQuery callback, CancellationToken ct)
    {
        string userIdStr = callback.From.Id.ToString();
        bool isOwner = await _ownerGuard.IsOwnerAsync(userIdStr, ct).ConfigureAwait(false);

        if (!isOwner)
        {
            await _client.AnswerCallbackQueryAsync(callback.Id, "Доступ запрещён.", showAlert: true, ct: ct).ConfigureAwait(false);
            return;
        }

        string data = callback.Data ?? string.Empty;
        if (_hitlService == null)
        {
            await _client.AnswerCallbackQueryAsync(callback.Id, "Сервис HITL недоступен.", showAlert: true, ct: ct).ConfigureAwait(false);
            return;
        }

        if (data.StartsWith("hitl:approve:", StringComparison.OrdinalIgnoreCase))
        {
            string idStr = data["hitl:approve:".Length..];
            if (Guid.TryParse(idStr, out Guid approvalId))
            {
                try
                {
                    await _hitlService.ApproveAsync(approvalId, userIdStr, ChannelKind.Telegram, ct).ConfigureAwait(false);
                    await _client.AnswerCallbackQueryAsync(callback.Id, "✅ Запрос одобрен.", showAlert: false, ct: ct).ConfigureAwait(false);
                }
                catch (HitlAlreadyResolvedException)
                {
                    await _client.AnswerCallbackQueryAsync(callback.Id, "Решение уже принято ранее.", showAlert: true, ct: ct).ConfigureAwait(false);
                }
                catch (HitlExpiredException)
                {
                    await _client.AnswerCallbackQueryAsync(callback.Id, "Срок действия запроса истёк.", showAlert: true, ct: ct).ConfigureAwait(false);
                }
            }
        }
        else if (data.StartsWith("hitl:reject:", StringComparison.OrdinalIgnoreCase))
        {
            string idStr = data["hitl:reject:".Length..];
            if (Guid.TryParse(idStr, out Guid approvalId))
            {
                try
                {
                    await _hitlService.RejectAsync(approvalId, userIdStr, ChannelKind.Telegram, "Отклонено владельцем в Telegram", ct).ConfigureAwait(false);
                    await _client.AnswerCallbackQueryAsync(callback.Id, "❌ Запрос отклонён.", showAlert: false, ct: ct).ConfigureAwait(false);
                }
                catch (HitlAlreadyResolvedException)
                {
                    await _client.AnswerCallbackQueryAsync(callback.Id, "Решение уже принято ранее.", showAlert: true, ct: ct).ConfigureAwait(false);
                }
                catch (HitlExpiredException)
                {
                    await _client.AnswerCallbackQueryAsync(callback.Id, "Срок действия запроса истёк.", showAlert: true, ct: ct).ConfigureAwait(false);
                }
            }
        }
        else if (data.StartsWith("hitl:edit:", StringComparison.OrdinalIgnoreCase))
        {
            string idStr = data["hitl:edit:".Length..];
            if (Guid.TryParse(idStr, out Guid approvalId))
            {
                _pendingEdits[callback.From.Id] = approvalId;
                await _client.AnswerCallbackQueryAsync(callback.Id, "✏️ Отправьте исправленный JSON следующим сообщением.", showAlert: true, ct: ct).ConfigureAwait(false);
                await _client.SendTextMessageAsync(callback.Message?.Chat.Id ?? callback.From.Id, "Отправьте отредактированные параметры для запроса подтверждения:", ct: ct).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleIncomingMessageAsync(TelegramMessage message, CancellationToken ct)
    {
        string userIdStr = (message.From?.Id ?? 0).ToString();
        bool isOwner = await _ownerGuard.IsOwnerAsync(userIdStr, ct).ConfigureAwait(false);

        if (!isOwner)
        {
            // Do not forward non-owner messages to router; do not disclose system details
            await _client.SendTextMessageAsync(message.Chat.Id, "Доступ ограничен.", ct: ct).ConfigureAwait(false);
            return;
        }

        string text = message.Text?.Trim() ?? string.Empty;

        // Check if this message is an edited payload response for a pending HITL request
        if (message.From != null && _pendingEdits.TryRemove(message.From.Id, out Guid pendingApprovalId))
        {
            if (_hitlService != null)
            {
                try
                {
                    await _hitlService.EditAsync(pendingApprovalId, userIdStr, text, ChannelKind.Telegram, ct).ConfigureAwait(false);
                    await _client.SendTextMessageAsync(message.Chat.Id, "✅ Изменения применены. Выполнение цепочки возобновлено.", ct: ct).ConfigureAwait(false);
                }
                catch (HitlException ex)
                {
                    await _client.SendTextMessageAsync(message.Chat.Id, $"❌ Ошибка подтверждения: {ex.Message}", ct: ct).ConfigureAwait(false);
                }
            }
            return;
        }

        // Standard bot commands
        if (string.Equals(text, "/start", StringComparison.OrdinalIgnoreCase))
        {
            await _client.SendTextMessageAsync(
                message.Chat.Id,
                "👋 Привет! CyberPipe активен и готов к работе.\nОтправьте команду или текстовый запрос.",
                ct: ct).ConfigureAwait(false);
            return;
        }

        if (string.Equals(text, "/status", StringComparison.OrdinalIgnoreCase))
        {
            string statusReport = "🟢 *Статус CyberPipe*\n\n" +
                                  "• Служба: Работает\n" +
                                  "• Канал: Telegram (Long Polling)\n" +
                                  $"• Серверное время (UTC): {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}";
            await _client.SendTextMessageAsync(message.Chat.Id, statusReport, parseMode: "Markdown", ct: ct).ConfigureAwait(false);
            return;
        }

        if (string.Equals(text, "/stop", StringComparison.OrdinalIgnoreCase))
        {
            await _client.SendTextMessageAsync(message.Chat.Id, "⏹️ Текущая задача прервана.", ct: ct).ConfigureAwait(false);
            return;
        }

        // Build attachments for voice or document
        var attachments = new List<MessageAttachmentDto>();
        if (message.Voice != null)
        {
            attachments.Add(new MessageAttachmentDto
            {
                FileName = $"voice_{message.Voice.FileId}.ogg",
                ContentType = message.Voice.MimeType ?? "audio/ogg",
                FileRef = message.Voice.FileId,
                SizeBytes = message.Voice.FileSize
            });
        }

        if (message.Document != null)
        {
            attachments.Add(new MessageAttachmentDto
            {
                FileName = message.Document.FileName ?? $"doc_{message.Document.FileId}",
                ContentType = message.Document.MimeType ?? "application/octet-stream",
                FileRef = message.Document.FileId,
                SizeBytes = message.Document.FileSize
            });
        }

        var incoming = new IncomingMessage
        {
            Id = new MessageId(Guid.NewGuid().ToString("N")),
            Channel = ChannelKind.Telegram,
            SenderId = userIdStr,
            ChatId = message.Chat.Id.ToString(),
            Text = message.Text,
            Attachments = attachments,
            SentAtUtc = DateTimeOffset.FromUnixTimeSeconds(message.Date)
        };

        if (_messageBus != null)
        {
            await _messageBus.PublishAsync(incoming, ct).ConfigureAwait(false);
        }
    }

    private static string EscapeMarkdown(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input.Replace("_", "\\_")
                    .Replace("*", "\\*")
                    .Replace("[", "\\[")
                    .Replace("`", "\\`");
    }
}

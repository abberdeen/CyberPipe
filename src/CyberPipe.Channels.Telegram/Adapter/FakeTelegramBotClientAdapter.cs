using System.Collections.Concurrent;
using CyberPipe.Channels.Telegram.Model;

namespace CyberPipe.Channels.Telegram.Adapter;

public sealed record SentTelegramMessageRecord(
    long ChatId,
    string Text,
    TelegramInlineKeyboardMarkup? ReplyMarkup,
    string? ParseMode);

public sealed record AnsweredCallbackRecord(
    string CallbackQueryId,
    string? Text,
    bool ShowAlert);

/// <summary>
/// Фейковый адаптер Telegram-клиента для автономных unit и component тестов (TASK-060).
/// </summary>
public sealed class FakeTelegramBotClientAdapter : ITelegramBotClientAdapter
{
    private readonly ConcurrentQueue<TelegramUpdate> _incomingUpdates = new();
    private readonly List<SentTelegramMessageRecord> _sentMessages = [];
    private readonly List<AnsweredCallbackRecord> _answeredCallbacks = [];
    private readonly object _lock = new();
    private long _autoMessageId = 1000;
    private long _autoUpdateId = 1;

    public IReadOnlyList<SentTelegramMessageRecord> SentMessages
    {
        get
        {
            lock (_lock)
            {
                return _sentMessages.ToList();
            }
        }
    }

    public IReadOnlyList<AnsweredCallbackRecord> AnsweredCallbacks
    {
        get
        {
            lock (_lock)
            {
                return _answeredCallbacks.ToList();
            }
        }
    }

    public void EnqueueUpdate(TelegramUpdate update)
    {
        _incomingUpdates.Enqueue(update);
    }

    public void EnqueueTextMessage(long chatId, long userId, string text, long? updateId = null)
    {
        long uid = updateId ?? Interlocked.Increment(ref _autoUpdateId);
        long mid = Interlocked.Increment(ref _autoMessageId);

        var update = new TelegramUpdate
        {
            UpdateId = uid,
            Message = new TelegramMessage
            {
                MessageId = mid,
                From = new TelegramUser { Id = userId, IsBot = false, FirstName = "Owner" },
                Chat = new TelegramChat { Id = chatId, Type = "private" },
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Text = text
            }
        };

        _incomingUpdates.Enqueue(update);
    }

    public void EnqueueCallbackQuery(
        string callbackQueryId,
        long chatId,
        long userId,
        string data,
        long? updateId = null)
    {
        long uid = updateId ?? Interlocked.Increment(ref _autoUpdateId);
        long mid = Interlocked.Increment(ref _autoMessageId);

        var update = new TelegramUpdate
        {
            UpdateId = uid,
            CallbackQuery = new TelegramCallbackQuery
            {
                Id = callbackQueryId,
                From = new TelegramUser { Id = userId, IsBot = false, FirstName = "Owner" },
                Message = new TelegramMessage
                {
                    MessageId = mid,
                    Chat = new TelegramChat { Id = chatId, Type = "private" },
                    Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                },
                Data = data
            }
        };

        _incomingUpdates.Enqueue(update);
    }

    public Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
        long offset = 0,
        int limit = 100,
        int timeoutSeconds = 30,
        CancellationToken ct = default)
    {
        var list = new List<TelegramUpdate>();
        while (list.Count < limit && _incomingUpdates.TryDequeue(out TelegramUpdate? update))
        {
            if (update.UpdateId >= offset)
            {
                list.Add(update);
            }
        }

        return Task.FromResult<IReadOnlyList<TelegramUpdate>>(list);
    }

    public Task<TelegramMessage> SendTextMessageAsync(
        long chatId,
        string text,
        TelegramInlineKeyboardMarkup? replyMarkup = null,
        string? parseMode = null,
        CancellationToken ct = default)
    {
        long mid = Interlocked.Increment(ref _autoMessageId);
        lock (_lock)
        {
            _sentMessages.Add(new SentTelegramMessageRecord(chatId, text, replyMarkup, parseMode));
        }

        var message = new TelegramMessage
        {
            MessageId = mid,
            Chat = new TelegramChat { Id = chatId, Type = "private" },
            Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Text = text
        };

        return Task.FromResult(message);
    }

    public Task<bool> AnswerCallbackQueryAsync(
        string callbackQueryId,
        string? text = null,
        bool showAlert = false,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            _answeredCallbacks.Add(new AnsweredCallbackRecord(callbackQueryId, text, showAlert));
        }

        return Task.FromResult(true);
    }

    public Task<TelegramMessage> SendDocumentAsync(
        long chatId,
        Stream documentStream,
        string fileName,
        string? caption = null,
        CancellationToken ct = default)
    {
        long mid = Interlocked.Increment(ref _autoMessageId);
        lock (_lock)
        {
            _sentMessages.Add(new SentTelegramMessageRecord(chatId, $"[Document: {fileName}] {caption}", null, null));
        }

        var message = new TelegramMessage
        {
            MessageId = mid,
            Chat = new TelegramChat { Id = chatId, Type = "private" },
            Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Text = caption,
            Document = new TelegramDocument { FileId = "doc_file_1", FileName = fileName }
        };

        return Task.FromResult(message);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _sentMessages.Clear();
            _answeredCallbacks.Clear();
            while (_incomingUpdates.TryDequeue(out _)) { }
        }
    }
}

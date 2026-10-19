using CyberPipe.Channels.Telegram.Model;

namespace CyberPipe.Channels.Telegram.Adapter;

/// <summary>
/// Адаптер клиента Telegram Bot API (TASK-060).
/// Изолирует библиотеку Telegram API от ядра и позволяет подставлять фейковый клиент в тестах.
/// </summary>
public interface ITelegramBotClientAdapter
{
    Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
        long offset = 0,
        int limit = 100,
        int timeoutSeconds = 30,
        CancellationToken ct = default);

    Task<TelegramMessage> SendTextMessageAsync(
        long chatId,
        string text,
        TelegramInlineKeyboardMarkup? replyMarkup = null,
        string? parseMode = null,
        CancellationToken ct = default);

    Task<bool> AnswerCallbackQueryAsync(
        string callbackQueryId,
        string? text = null,
        bool showAlert = false,
        CancellationToken ct = default);

    Task<TelegramMessage> SendDocumentAsync(
        long chatId,
        Stream documentStream,
        string fileName,
        string? caption = null,
        CancellationToken ct = default);
}

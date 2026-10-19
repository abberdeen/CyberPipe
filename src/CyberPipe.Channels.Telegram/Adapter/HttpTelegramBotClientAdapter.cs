using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CyberPipe.Channels.Telegram.Model;

namespace CyberPipe.Channels.Telegram.Adapter;

/// <summary>
/// Реальный адаптер Telegram Bot API поверх HttpClient (TASK-060).
/// Гарантирует маскирование токена бота в исключениях и логах.
/// </summary>
public sealed class HttpTelegramBotClientAdapter : ITelegramBotClientAdapter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly HttpClient _httpClient;
    private readonly string _botToken;
    private readonly string _baseUrl;

    public HttpTelegramBotClientAdapter(HttpClient httpClient, string botToken, string? apiEndpoint = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _botToken = !string.IsNullOrWhiteSpace(botToken) ? botToken : throw new ArgumentException("Bot token must not be empty.", nameof(botToken));
        string root = !string.IsNullOrWhiteSpace(apiEndpoint) ? apiEndpoint.TrimEnd('/') : "https://api.telegram.org";
        _baseUrl = $"{root}/bot{_botToken}";
    }

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
        long offset = 0,
        int limit = 100,
        int timeoutSeconds = 30,
        CancellationToken ct = default)
    {
        string url = $"{_baseUrl}/getUpdates?offset={offset}&limit={limit}&timeout={timeoutSeconds}";
        try
        {
            var response = await _httpClient.GetFromJsonAsync<TelegramApiResponse<IReadOnlyList<TelegramUpdate>>>(url, JsonOpts, ct).ConfigureAwait(false);
            if (response != null && response.Ok && response.Result != null)
            {
                return response.Result;
            }

            return [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new HttpRequestException($"Failed to getUpdates: {Sanitize(ex.Message)}", ex);
        }
    }

    public async Task<TelegramMessage> SendTextMessageAsync(
        long chatId,
        string text,
        TelegramInlineKeyboardMarkup? replyMarkup = null,
        string? parseMode = null,
        CancellationToken ct = default)
    {
        string url = $"{_baseUrl}/sendMessage";
        var payload = new
        {
            chat_id = chatId,
            text,
            reply_markup = replyMarkup,
            parse_mode = parseMode
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(url, payload, JsonOpts, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<TelegramApiResponse<TelegramMessage>>(JsonOpts, ct).ConfigureAwait(false);
            if (result != null && result.Ok && result.Result != null)
            {
                return result.Result;
            }

            throw new InvalidOperationException($"Telegram sendMessage returned non-ok result: {result?.Description}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new HttpRequestException($"Failed to sendMessage to chat {chatId}: {Sanitize(ex.Message)}", ex);
        }
    }

    public async Task<bool> AnswerCallbackQueryAsync(
        string callbackQueryId,
        string? text = null,
        bool showAlert = false,
        CancellationToken ct = default)
    {
        string url = $"{_baseUrl}/answerCallbackQuery";
        var payload = new
        {
            callback_query_id = callbackQueryId,
            text,
            show_alert = showAlert
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(url, payload, JsonOpts, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new HttpRequestException($"Failed to answerCallbackQuery: {Sanitize(ex.Message)}", ex);
        }
    }

    public async Task<TelegramMessage> SendDocumentAsync(
        long chatId,
        Stream documentStream,
        string fileName,
        string? caption = null,
        CancellationToken ct = default)
    {
        string url = $"{_baseUrl}/sendDocument";
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(chatId.ToString()), "chat_id");
        if (!string.IsNullOrEmpty(caption))
        {
            content.Add(new StringContent(caption), "caption");
        }

        var streamContent = new StreamContent(documentStream);
        content.Add(streamContent, "document", fileName);

        try
        {
            using var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<TelegramApiResponse<TelegramMessage>>(JsonOpts, ct).ConfigureAwait(false);
            if (result != null && result.Ok && result.Result != null)
            {
                return result.Result;
            }

            throw new InvalidOperationException($"Telegram sendDocument returned non-ok: {result?.Description}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new HttpRequestException($"Failed to sendDocument: {Sanitize(ex.Message)}", ex);
        }
    }

    private string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(_botToken)) return input;
        return input.Replace(_botToken, "[REDACTED_BOT_TOKEN]");
    }

    private sealed record TelegramApiResponse<T>
    {
        public bool Ok { get; init; }
        public T? Result { get; init; }
        public string? Description { get; init; }
        public int? ErrorCode { get; init; }
    }
}

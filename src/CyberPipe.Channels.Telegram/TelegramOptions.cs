namespace CyberPipe.Channels.Telegram;

/// <summary>
/// Опции конфигурации Telegram-канала (TASK-060).
/// </summary>
public sealed record TelegramOptions
{
    /// <summary>Секретный токен Telegram-бота (никогда не логируется в открытом виде).</summary>
    public string BotToken { get; init; } = string.Empty;

    /// <summary>Интервал между запросами long polling при отсутствии обновлений (мс).</summary>
    public int PollingIntervalMs { get; init; } = 500;

    /// <summary>Тайм-аут long polling запроса на сервере Telegram (секунды).</summary>
    public int PollingTimeoutSeconds { get; init; } = 30;

    /// <summary>Основной идентификатор чата владельца для проактивных уведомлений и HITL.</summary>
    public long? OwnerChatId { get; init; }

    /// <summary>Пользовательский URL эндпоинта Telegram Bot API (например для локального Bot API сервера или мока).</summary>
    public string? ApiEndpoint { get; init; }
}

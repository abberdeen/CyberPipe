namespace CyberPipe.Channels.Telegram.Model;

public sealed record TelegramUser
{
    public long Id { get; init; }
    public bool IsBot { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Username { get; init; }
}

public sealed record TelegramChat
{
    public long Id { get; init; }
    public string? Type { get; init; }
    public string? Title { get; init; }
    public string? Username { get; init; }
}

public sealed record TelegramVoice
{
    public string FileId { get; init; } = string.Empty;
    public string? FileUniqueId { get; init; }
    public int Duration { get; init; }
    public string? MimeType { get; init; }
    public long? FileSize { get; init; }
}

public sealed record TelegramDocument
{
    public string FileId { get; init; } = string.Empty;
    public string? FileUniqueId { get; init; }
    public string? FileName { get; init; }
    public string? MimeType { get; init; }
    public long? FileSize { get; init; }
}

public sealed record TelegramPhotoSize
{
    public string FileId { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public long? FileSize { get; init; }
}

public sealed record TelegramInlineKeyboardButton
{
    public required string Text { get; init; }
    public string? CallbackData { get; init; }
    public string? Url { get; init; }
}

public sealed record TelegramInlineKeyboardMarkup
{
    public IReadOnlyList<IReadOnlyList<TelegramInlineKeyboardButton>> InlineKeyboard { get; init; } = [];
}

public sealed record TelegramMessage
{
    public long MessageId { get; init; }
    public TelegramUser? From { get; init; }
    public required TelegramChat Chat { get; init; }
    public long Date { get; init; }
    public string? Text { get; init; }
    public TelegramVoice? Voice { get; init; }
    public TelegramDocument? Document { get; init; }
    public IReadOnlyList<TelegramPhotoSize> Photo { get; init; } = [];
}

public sealed record TelegramCallbackQuery
{
    public required string Id { get; init; }
    public required TelegramUser From { get; init; }
    public TelegramMessage? Message { get; init; }
    public string? Data { get; init; }
}

public sealed record TelegramUpdate
{
    public long UpdateId { get; init; }
    public TelegramMessage? Message { get; init; }
    public TelegramCallbackQuery? CallbackQuery { get; init; }
}

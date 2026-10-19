namespace CyberPipe.Channels.Telegram.Formatting;

/// <summary>
/// Утилита разбивки длинных сообщений под лимит Telegram (4096 символов) (TASK-060).
/// Гарантирует сохранение порядка, отсутствие потерь текста и разбиение по мягким границам (абзацы, предложения).
/// </summary>
public static class TelegramMessageSplitter
{
    public const int MaxTelegramMessageLength = 4096;

    public static IReadOnlyList<string> Split(string? text, int maxLength = MaxTelegramMessageLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        if (text.Length <= maxLength)
        {
            return [text];
        }

        var chunks = new List<string>();
        int currentPos = 0;

        while (currentPos < text.Length)
        {
            int remaining = text.Length - currentPos;
            if (remaining <= maxLength)
            {
                chunks.Add(text.Substring(currentPos));
                break;
            }

            int endPos = currentPos + maxLength;
            int splitPoint = FindSplitPoint(text, currentPos, endPos);

            string chunk = text.Substring(currentPos, splitPoint - currentPos);
            chunks.Add(chunk);

            currentPos = splitPoint;
            // Skip leading whitespace at start of next chunk if split occurred on whitespace
            if (currentPos < text.Length && (text[currentPos] == '\n' || text[currentPos] == ' '))
            {
                currentPos++;
            }
        }

        return chunks;
    }

    private static int FindSplitPoint(string text, int startPos, int endPos)
    {
        int minSplit = startPos + (endPos - startPos) / 2;

        // Try paragraph break (\n\n)
        for (int i = endPos - 1; i >= minSplit; i--)
        {
            if (text[i] == '\n' && i > startPos && text[i - 1] == '\n')
            {
                return i + 1;
            }
        }

        // Try single newline (\n)
        for (int i = endPos - 1; i >= minSplit; i--)
        {
            if (text[i] == '\n')
            {
                return i + 1;
            }
        }

        // Try sentence boundary (. / ! / ?)
        for (int i = endPos - 1; i >= minSplit; i--)
        {
            char c = text[i];
            if ((c == '.' || c == '!' || c == '?') && i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]))
            {
                return i + 1;
            }
        }

        // Try space
        for (int i = endPos - 1; i >= minSplit; i--)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        return endPos;
    }
}

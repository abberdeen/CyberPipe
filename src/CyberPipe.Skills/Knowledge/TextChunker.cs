namespace CyberPipe.Skills.Knowledge;

/// <summary>
/// Фрагмент (чанк) текста после разбиения (TASK-053).
/// </summary>
public sealed record TextChunk(
    int ChunkIndex,
    string Text,
    int StartOffset,
    int EndOffset);

/// <summary>
/// Утилита разбиения длинных текстов на фрагменты с перекрытием для семантического поиска (TASK-053).
/// </summary>
public static class TextChunker
{
    public static IReadOnlyList<TextChunk> ChunkText(
        string? text,
        int chunkSize = 500,
        int chunkOverlap = 100)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        if (chunkSize <= 0)
        {
            chunkSize = 500;
        }

        if (chunkOverlap < 0 || chunkOverlap >= chunkSize)
        {
            chunkOverlap = Math.Max(0, chunkSize / 5);
        }

        int step = chunkSize - chunkOverlap;
        if (step <= 0)
        {
            step = 1;
        }

        string raw = text.Trim();
        if (raw.Length <= chunkSize)
        {
            return [new TextChunk(0, raw, 0, raw.Length)];
        }

        var chunks = new List<TextChunk>();
        int index = 0;
        int currentPos = 0;

        while (currentPos < raw.Length)
        {
            int length = Math.Min(chunkSize, raw.Length - currentPos);
            int endPos = currentPos + length;

            // If we're not at the very end of text, attempt to break at sentence or word boundary
            if (endPos < raw.Length)
            {
                int boundary = FindSoftBoundary(raw, currentPos, endPos, step);
                if (boundary > currentPos)
                {
                    length = boundary - currentPos;
                    endPos = boundary;
                }
            }

            string chunkContent = raw.Substring(currentPos, length).Trim();
            if (!string.IsNullOrEmpty(chunkContent))
            {
                chunks.Add(new TextChunk(index++, chunkContent, currentPos, endPos));
            }

            if (endPos >= raw.Length)
            {
                break;
            }

            currentPos += step;
            if (currentPos >= raw.Length)
            {
                break;
            }
        }

        return chunks;
    }

    private static int FindSoftBoundary(string text, int startPos, int endPos, int minStep)
    {
        int minBoundary = startPos + minStep;

        // Try paragraph break (\n\n)
        for (int i = endPos - 1; i >= minBoundary; i--)
        {
            if (text[i] == '\n' && i > startPos && text[i - 1] == '\n')
            {
                return i + 1;
            }
        }

        // Try sentence break (. ! ?)
        for (int i = endPos - 1; i >= minBoundary; i--)
        {
            char c = text[i];
            if ((c == '.' || c == '!' || c == '?') && i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]))
            {
                return i + 1;
            }
        }

        // Try whitespace break
        for (int i = endPos - 1; i >= minBoundary; i--)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i + 1;
            }
        }

        return endPos;
    }
}

using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CyberPipe.Data.Persistence;

/// <summary>
/// Конвертер меток времени в UTC-текст legacy-формата "yyyy-MM-dd HH:mm:ss.fff".
/// Тот же формат, что писал сырой SQL-слой: существующие .db-файлы читаются без
/// конвертации, а строковое сравнение сохраняет хронологический порядок в ORDER BY.
/// </summary>
internal sealed class UtcTimestampConverter : ValueConverter<DateTimeOffset, string>
{
    /// <summary>Формат UTC-текста меток времени.</summary>
    internal const string Format = "yyyy-MM-dd HH:mm:ss.fff";

    /// <summary>Создаёт конвертер DateTimeOffset → UTC-текст.</summary>
    public UtcTimestampConverter()
        : base(
            v => v.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture),
            s => Parse(s))
    {
    }

    /// <summary>Разбирает UTC-текст; терпит legacy-варианты (секунды без миллисекунд, CURRENT_TIMESTAMP).</summary>
    /// <param name="value">Текст метки времени.</param>
    /// <returns>Момент в UTC.</returns>
    internal static DateTimeOffset Parse(string value)
    {
        if (DateTime.TryParseExact(
                value, Format, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime exact))
            return new DateTimeOffset(exact, TimeSpan.Zero);
        if (DateTimeOffset.TryParse(
                value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces,
                out DateTimeOffset parsed))
            return parsed.ToUniversalTime();
        throw new InvalidOperationException($"Unexpected timestamp value '{value}'.");
    }
}

/// <summary>Nullable-вариант конвертера меток времени в UTC-текст.</summary>
internal sealed class NullableUtcTimestampConverter : ValueConverter<DateTimeOffset?, string?>
{
    /// <summary>Создаёт конвертер nullable-метки в UTC-текст.</summary>
    public NullableUtcTimestampConverter()
        : base(
            v => v == null ? null : v.Value.UtcDateTime.ToString(UtcTimestampConverter.Format, CultureInfo.InvariantCulture),
            s => s == null ? null : UtcTimestampConverter.Parse(s))
    {
    }
}

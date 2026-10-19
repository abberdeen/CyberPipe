using System.Text;
using System.Text.RegularExpressions;
using CyberPipe.Contracts;

namespace CyberPipe.Logging;

/// <summary>
/// Классификация секретов для логов по смыслу имени, а не только по реестру: маскируется любой
/// <see cref="SecretValue" />, любой ключ из <see cref="SecretKeys" /> и любое имя свойства,
/// похожее на пароль, токен, ключ API или учётные данные, на любом уровне вложенности.
/// Неизвестные имена проходят как есть только если не похожи на секрет.
/// </summary>
public static class SecretRedaction
{
    /// <summary>Замена, записываемая в лог вместо значения секрета.</summary>
    public const string Redacted = "[REDACTED]";

    private static readonly HashSet<string> KnownSecretNames = BuildKnownNames();

    private static readonly string[] SecretNameFragments =
    [
        "password", "passwd", "bottoken", "secret", "apikey", "credential", "privatekey",
    ];

    private static readonly Regex KeyValueSecretPattern = new(
        @"(?<key>[A-Za-z0-9_.\-]*(?:password|passwd|token|secret|api[_-]?key|credential)[A-Za-z0-9_.\-]*)\s*[:=]\s*(?:""[^""]*""|'[^']*'|[^\s;,}]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ConnectionStringPasswordPattern = new(
        @"(?<key>password|pwd)\s*=\s*[^;]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Проверяет, является ли имя свойства именем секрета по реестру или по смыслу.</summary>
    /// <param name="propertyName">Имя свойства или ключа словаря.</param>
    /// <returns>True, если значение под этим именем нужно маскировать.</returns>
    public static bool IsSecretPropertyName(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return false;

        string normalized = Normalize(propertyName);
        if (KnownSecretNames.Contains(normalized))
            return true;

        foreach (string fragment in SecretNameFragments)
        {
            if (normalized.Contains(fragment, StringComparison.Ordinal))
                return true;
        }

        // Token как значение секрета маскируется; метрики расхода токенов секретом не являются.
        return normalized.Contains("token", StringComparison.Ordinal) && !IsTokenMetricName(normalized);
    }

    private static bool IsTokenMetricName(string normalized) =>
        normalized.EndsWith("tokens", StringComparison.Ordinal)
        || normalized.Contains("tokencount", StringComparison.Ordinal)
        || normalized.Contains("tokensused", StringComparison.Ordinal)
        || normalized.Contains("tokenlimit", StringComparison.Ordinal)
        || normalized.Contains("tokenbudget", StringComparison.Ordinal)
        || normalized.Contains("tokencap", StringComparison.Ordinal);

    /// <summary>Маскирует значения секретов в свободном тексте причины или сообщения об ошибке.</summary>
    /// <param name="text">Исходный текст; может быть null.</param>
    /// <returns>Текст с заменой значений секретов на [REDACTED]; null сохраняется.</returns>
    public static string? RedactText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        string result = ConnectionStringPasswordPattern.Replace(text, "${key}=" + Redacted);
        return KeyValueSecretPattern.Replace(result, "${key}=" + Redacted);
    }

    private static HashSet<string> BuildKnownNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in SecretKeys.All)
        {
            names.Add(Normalize(key));
            string lastSegment = key.Split('.', StringSplitOptions.RemoveEmptyEntries)[^1];
            names.Add(Normalize(lastSegment));
        }

        return names;
    }

    private static string Normalize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}

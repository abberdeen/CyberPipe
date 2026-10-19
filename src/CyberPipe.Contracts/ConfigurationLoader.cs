using System.Text.Json;
using System.Text.Json.Nodes;

namespace CyberPipe.Contracts;

/// <summary>Ошибка валидации конфигурации при старте. Содержит имена ключей, не значения секретов.</summary>
public sealed class ConfigurationValidationException : Exception
{
    /// <summary>Создаёт исключение по списку ошибок валидации.</summary>
    /// <param name="errors">Ошибки с именами ключей.</param>
    public ConfigurationValidationException(IReadOnlyList<string> errors)
        : base("Invalid configuration: " + string.Join("; ", errors)) => Errors = errors;

    /// <summary>Ошибки валидации с именами ключей.</summary>
    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// Загрузка конфигурации из трёх источников. Приоритет от низкого к высокому:
/// значения по умолчанию <see cref="CyberPipeOptions" />, appsettings.json рядом со службой,
/// переменные окружения с префиксом CYBERPIPE_ (разделитель уровней — двойное подчёркивание,
/// например CYBERPIPE_Budget__DailyCapUsd), файл переопределений из UI в каталоге данных
/// пользователя. Объекты сливаются по ключам без учёта регистра; скаляры и массивы заменяются
/// целиком источником с большим приоритетом. После слияния выполняется валидация, и невалидная
/// обязательная конфигурация останавливает старт <see cref="ConfigurationValidationException" />.
/// </summary>
public static class CyberPipeConfigurationLoader
{
    /// <summary>Префикс переменных окружения CyberPipe.</summary>
    public const string EnvironmentPrefix = "CYBERPIPE_";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>Путь файла переопределений из UI по умолчанию: каталог данных пользователя.</summary>
    /// <returns>Полный путь к файлу переопределений.</returns>
    public static string GetDefaultUiOverridesPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CyberPipe",
            "ui-overrides.json");

    /// <summary>Загружает конфигурацию из каталога службы и реального окружения процесса.</summary>
    /// <param name="baseDirectory">Каталог с appsettings.json; по умолчанию каталог приложения.</param>
    /// <param name="uiOverridesPath">Путь файла переопределений из UI; по умолчанию каталог данных пользователя.</param>
    /// <returns>Слитая и провалидированная конфигурация.</returns>
    public static CyberPipeOptions Load(string? baseDirectory = null, string? uiOverridesPath = null)
    {
        string appsettingsPath = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "appsettings.json");
        return LoadFromSources(
            File.Exists(appsettingsPath) ? appsettingsPath : null,
            environmentVariables: null,
            uiOverridesPath ?? GetDefaultUiOverridesPath());
    }

    /// <summary>Загружает конфигурацию из явно заданных источников; основа тестов слияния.</summary>
    /// <param name="appsettingsPath">Путь к appsettings.json или null.</param>
    /// <param name="environmentVariables">Переменные окружения; null — реальное окружение процесса.</param>
    /// <param name="uiOverridesPath">Путь файла переопределений из UI или null.</param>
    /// <returns>Слитая и провалидированная конфигурация.</returns>
    /// <exception cref="ConfigurationValidationException">Конфигурация невалидна; старт останавливается.</exception>
    public static CyberPipeOptions LoadFromSources(
        string? appsettingsPath,
        IReadOnlyDictionary<string, string?>? environmentVariables,
        string? uiOverridesPath)
    {
        JsonObject merged = JsonSerializer.SerializeToNode(new CyberPipeOptions(), SerializerOptions)!.AsObject();

        if (appsettingsPath is not null && File.Exists(appsettingsPath))
            MergeInto(merged, ReadJsonObject(appsettingsPath));

        MergeInto(merged, EnvironmentToJson(environmentVariables));

        if (uiOverridesPath is not null && File.Exists(uiOverridesPath))
            MergeInto(merged, ReadJsonObject(uiOverridesPath));

        CyberPipeOptions options =
            merged.Deserialize<CyberPipeOptions>(SerializerOptions) ?? new CyberPipeOptions();
        IReadOnlyList<string> errors = options.Validate();
        if (errors.Count > 0)
            throw new ConfigurationValidationException(errors);

        return options;
    }

    private static JsonObject ReadJsonObject(string path)
    {
        string text = File.ReadAllText(path);
        return JsonNode.Parse(text) as JsonObject
            ?? throw new ConfigurationValidationException([$"{Path.GetFileName(path)}: root must be a JSON object."]);
    }

    private static JsonObject EnvironmentToJson(IReadOnlyDictionary<string, string?>? variables)
    {
        var root = new JsonObject();
        IEnumerable<KeyValuePair<string, string?>> source = variables is not null
            ? variables
            : Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .Select(e => new KeyValuePair<string, string?>((string)e.Key, e.Value as string));

        foreach ((string key, string? value) in source)
        {
            if (value is null || !key.StartsWith(EnvironmentPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string path = key[EnvironmentPrefix.Length..];
            string[] segments = path.Split("__", StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                continue;

            JsonObject node = root;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (node[segments[i]] is not JsonObject child)
                {
                    child = new JsonObject();
                    node[segments[i]] = child;
                }

                node = child;
            }

            node[segments[^1]] = ParseScalar(value);
        }

        return root;
    }

    private static JsonNode? ParseScalar(string value)
    {
        if (bool.TryParse(value, out bool b))
            return JsonValue.Create(b);
        if (long.TryParse(value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out long l))
            return JsonValue.Create(l);
        if (decimal.TryParse(value, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out decimal d))
            return JsonValue.Create(d);
        return JsonValue.Create(value);
    }

    private static void MergeInto(JsonObject target, JsonObject source)
    {
        foreach ((string key, JsonNode? sourceNode) in source.ToList())
        {
            string? existingKey = target.Select(p => p.Key)
                .FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

            if (existingKey is not null
                && target[existingKey] is JsonObject targetChild
                && sourceNode is JsonObject sourceChild)
            {
                MergeInto(targetChild, sourceChild);
            }
            else if (sourceNode is not null)
            {
                target[existingKey ?? key] = sourceNode.DeepClone();
            }
        }
    }
}

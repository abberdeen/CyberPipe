using System.Text.Json;
using System.Text.Json.Serialization;
using CyberPipe.Contracts;

namespace CyberPipe.Chains;

/// <summary>
/// JSON-сериализатор для определений цепочек (TASK-040).
/// </summary>
public static class ChainDefinitionSerializer
{
    /// <summary>Стандартные опции сериализации цепочек в JSON.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            },
        };
        return options;
    }

    /// <summary>Сериализует определение цепочки в JSON-строку.</summary>
    /// <param name="definition">Определение цепочки.</param>
    /// <returns>Строка JSON.</returns>
    public static string Serialize(ChainDefinitionDto definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return JsonSerializer.Serialize(definition, Options);
    }

    /// <summary>Десериализует определение цепочки из JSON-строки.</summary>
    /// <param name="json">Строка JSON.</param>
    /// <returns>Определение цепочки.</returns>
    public static ChainDefinitionDto Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<ChainDefinitionDto>(json, Options)
            ?? throw new InvalidOperationException("Deserialized chain definition was null.");
    }
}

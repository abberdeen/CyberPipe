using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyberPipe.Contracts;

/// <summary>
/// Типизированный идентификатор цепочки. Потребители: TASK-040, TASK-041, TASK-044.
/// </summary>
/// <param name="Value">Значение идентификатора.</param>
[JsonConverter(typeof(ChainIdJsonConverter))]
public readonly record struct ChainId(Guid Value)
{
    /// <summary>Пустой идентификатор.</summary>
    public static ChainId Empty => new(Guid.Empty);

    /// <summary>Создаёт новый уникальный идентификатор цепочки.</summary>
    /// <returns>Новый идентификатор.</returns>
    public static ChainId New() => new(Guid.NewGuid());

    /// <summary>Разбирает идентификатор из строки.</summary>
    /// <param name="value">Строковое представление.</param>
    /// <returns>Идентификатор цепочки.</returns>
    public static ChainId Parse(string value) => new(Guid.Parse(value));

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// JSON-конвертер для <see cref="ChainId"/>, поддерживающий как строку GUID, так и объект { "value": "..." }.
/// </summary>
public sealed class ChainIdJsonConverter : JsonConverter<ChainId>
{
    /// <inheritdoc />
    public override ChainId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new ChainId(reader.GetGuid());
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.TryGetProperty("value", out JsonElement val) ||
                doc.RootElement.TryGetProperty("Value", out val))
            {
                return new ChainId(val.GetGuid());
            }
        }

        throw new JsonException($"Cannot convert token {reader.TokenType} to ChainId.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ChainId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}

/// <summary>
/// Типизированный идентификатор запуска цепочки. Потребители: TASK-044, TASK-046.
/// </summary>
/// <param name="Value">Значение идентификатора.</param>
[JsonConverter(typeof(RunIdJsonConverter))]
public readonly record struct RunId(Guid Value)
{
    /// <summary>Пустой идентификатор.</summary>
    public static RunId Empty => new(Guid.Empty);

    /// <summary>Создаёт новый уникальный идентификатор запуска.</summary>
    /// <returns>Новый идентификатор.</returns>
    public static RunId New() => new(Guid.NewGuid());

    /// <summary>Разбирает идентификатор из строки.</summary>
    /// <param name="value">Строковое представление.</param>
    /// <returns>Идентификатор запуска.</returns>
    public static RunId Parse(string value) => new(Guid.Parse(value));

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// JSON-конвертер для <see cref="RunId"/>, поддерживающий как строку GUID, так и объект { "value": "..." }.
/// </summary>
public sealed class RunIdJsonConverter : JsonConverter<RunId>
{
    /// <inheritdoc />
    public override RunId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new RunId(reader.GetGuid());
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.TryGetProperty("value", out JsonElement val) ||
                doc.RootElement.TryGetProperty("Value", out val))
            {
                return new RunId(val.GetGuid());
            }
        }

        throw new JsonException($"Cannot convert token {reader.TokenType} to RunId.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, RunId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}

/// <summary>
/// Типизированный идентификатор сообщения канала или шины. Потребители: TASK-060..TASK-062, TASK-090.
/// </summary>
/// <param name="Value">Строковое значение идентификатора.</param>
[JsonConverter(typeof(MessageIdJsonConverter))]
public readonly record struct MessageId(string Value)
{
    /// <summary>Пустой идентификатор.</summary>
    public static MessageId Empty => new(string.Empty);

    /// <summary>Создаёт новый уникальный идентификатор сообщения.</summary>
    /// <returns>Новый идентификатор.</returns>
    public static MessageId New() => new(Guid.NewGuid().ToString());

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// JSON-конвертер для <see cref="MessageId"/>, поддерживающий как строковое значение, так и объект { "value": "..." }.
/// </summary>
public sealed class MessageIdJsonConverter : JsonConverter<MessageId>
{
    /// <inheritdoc />
    public override MessageId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new MessageId(reader.GetString() ?? string.Empty);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.TryGetProperty("value", out JsonElement val) ||
                doc.RootElement.TryGetProperty("Value", out val))
            {
                return new MessageId(val.GetString() ?? string.Empty);
            }
        }

        throw new JsonException($"Cannot convert token {reader.TokenType} to MessageId.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, MessageId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}

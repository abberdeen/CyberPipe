using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Chains.Runners;

/// <summary>
/// Реализация валидатора JSON Schema Draft 2020-12 на базе System.Text.Json (TASK-043).
/// Проверяет типы, обязательные поля, свойства, массивы, допустимость дополнительных полей и диапазоны.
/// </summary>
public sealed class JsonSchemaDraft202012Validator : ISchemaValidator
{
    /// <inheritdoc />
    public IReadOnlyList<string> Validate(string schemaJson, string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return ["Schema JSON is empty."];
        }

        JsonDocument schemaDoc;
        try
        {
            schemaDoc = JsonDocument.Parse(schemaJson);
        }
        catch (JsonException ex)
        {
            return [$"Invalid schema JSON: {ex.Message}"];
        }

        using (schemaDoc)
        {
            JsonDocument payloadDoc;
            try
            {
                payloadDoc = JsonDocument.Parse(payloadJson ?? "{}");
            }
            catch (JsonException ex)
            {
                return [$"Invalid payload JSON: {ex.Message}"];
            }

            using (payloadDoc)
            {
                var errors = new List<string>();
                ValidateElement(schemaDoc.RootElement, payloadDoc.RootElement, "$", errors);
                return errors;
            }
        }
    }

    private static void ValidateElement(JsonElement schema, JsonElement payload, string path, List<string> errors)
    {
        // 1. Проверка типа ($type)
        if (schema.TryGetProperty("type", out JsonElement typeElem))
        {
            string? expectedType = typeElem.GetString();
            if (!string.IsNullOrEmpty(expectedType) && !MatchesType(expectedType, payload))
            {
                errors.Add($"At '{path}': expected type '{expectedType}', but found '{payload.ValueKind.ToString().ToLowerInvariant()}'.");
                return;
            }
        }

        // 2. Проверка enum
        if (schema.TryGetProperty("enum", out JsonElement enumElem) && enumElem.ValueKind == JsonValueKind.Array)
        {
            bool match = false;
            foreach (JsonElement allowed in enumElem.EnumerateArray())
            {
                if (JsonElementsEqual(allowed, payload))
                {
                    match = true;
                    break;
                }
            }

            if (!match)
            {
                errors.Add($"At '{path}': value is not in allowed enum list.");
            }
        }

        // 3. Проверки для чисел (minimum, maximum)
        if (payload.ValueKind == JsonValueKind.Number)
        {
            if (payload.TryGetDouble(out double numVal))
            {
                if (schema.TryGetProperty("minimum", out JsonElement minElem) && minElem.TryGetDouble(out double minVal) && numVal < minVal)
                {
                    errors.Add($"At '{path}': value {numVal} is less than minimum {minVal}.");
                }

                if (schema.TryGetProperty("maximum", out JsonElement maxElem) && maxElem.TryGetDouble(out double maxVal) && numVal > maxVal)
                {
                    errors.Add($"At '{path}': value {numVal} is greater than maximum {maxVal}.");
                }
            }
        }

        // 4. Проверки для строк (minLength, maxLength)
        if (payload.ValueKind == JsonValueKind.String)
        {
            string strVal = payload.GetString() ?? string.Empty;
            if (schema.TryGetProperty("minLength", out JsonElement minLenElem) && minLenElem.TryGetInt32(out int minLen) && strVal.Length < minLen)
            {
                errors.Add($"At '{path}': string length {strVal.Length} is less than minLength {minLen}.");
            }

            if (schema.TryGetProperty("maxLength", out JsonElement maxLenElem) && maxLenElem.TryGetInt32(out int maxLen) && strVal.Length > maxLen)
            {
                errors.Add($"At '{path}': string length {strVal.Length} is greater than maxLength {maxLen}.");
            }
        }

        // 5. Проверки для объектов (required, properties, additionalProperties)
        if (payload.ValueKind == JsonValueKind.Object)
        {
            var definedProps = new HashSet<string>(StringComparer.Ordinal);
            if (schema.TryGetProperty("properties", out JsonElement propsElem) && propsElem.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty prop in propsElem.EnumerateObject())
                {
                    definedProps.Add(prop.Name);
                }
            }

            // Проверка required
            if (schema.TryGetProperty("required", out JsonElement reqElem) && reqElem.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement req in reqElem.EnumerateArray())
                {
                    string? reqProp = req.GetString();
                    if (!string.IsNullOrEmpty(reqProp) && !payload.TryGetProperty(reqProp, out _))
                    {
                        errors.Add($"At '{path}': required property '{reqProp}' is missing.");
                    }
                }
            }

            // Проверка additionalProperties
            if (schema.TryGetProperty("additionalProperties", out JsonElement addlElem))
            {
                if (addlElem.ValueKind == JsonValueKind.False)
                {
                    foreach (JsonProperty prop in payload.EnumerateObject())
                    {
                        if (!definedProps.Contains(prop.Name))
                        {
                            errors.Add($"At '{path}': additional property '{prop.Name}' is not allowed.");
                        }
                    }
                }
            }

            // Рекурсивная валидация каждого существующего свойства объекта
            if (propsElem.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty prop in propsElem.EnumerateObject())
                {
                    if (payload.TryGetProperty(prop.Name, out JsonElement childPayload))
                    {
                        ValidateElement(prop.Value, childPayload, $"{path}.{prop.Name}", errors);
                    }
                }
            }
        }

        // 6. Проверки для массивов (items)
        if (payload.ValueKind == JsonValueKind.Array)
        {
            if (schema.TryGetProperty("items", out JsonElement itemsSchema) && itemsSchema.ValueKind == JsonValueKind.Object)
            {
                int index = 0;
                foreach (JsonElement item in payload.EnumerateArray())
                {
                    ValidateElement(itemsSchema, item, $"{path}[{index}]", errors);
                    index++;
                }
            }
        }
    }

    private static bool MatchesType(string expectedType, JsonElement element) =>
        expectedType.ToLowerInvariant() switch
        {
            "object" => element.ValueKind == JsonValueKind.Object,
            "array" => element.ValueKind == JsonValueKind.Array,
            "string" => element.ValueKind == JsonValueKind.String,
            "number" => element.ValueKind == JsonValueKind.Number,
            "integer" => element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out _),
            "boolean" => element.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => element.ValueKind == JsonValueKind.Null,
            _ => true,
        };

    private static bool JsonElementsEqual(JsonElement a, JsonElement b) =>
        a.ValueKind == b.ValueKind && a.GetRawText().Trim() == b.GetRawText().Trim();
}

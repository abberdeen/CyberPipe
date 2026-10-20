using System.Text.Json;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Skills;

/// <summary>Общий разбор JSON-нагрузки навыков хоста.</summary>
internal static class HostSkillJson
{
    public static string? OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static string ReadTitle(JsonElement root, string fallback)
    {
        if (root.TryGetProperty("title", out JsonElement title) && !string.IsNullOrWhiteSpace(title.GetString()))
        {
            return title.GetString()!;
        }

        return fallback;
    }

    public static bool? ReadBool(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        return null;
    }

    /// <summary>true — свойства нет или оно прочитано. false — свойство есть, но это не целое число.</summary>
    public static bool TryReadOptionalInt(JsonElement root, string name, out int? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out JsonElement property))
        {
            return true;
        }

        if (!TryReadInt(property, out int number))
        {
            return false;
        }

        value = number;
        return true;
    }

    public static bool TryReadVolumeStep(
        string payloadJson,
        bool readForce,
        string skillId,
        out int step,
        out bool force,
        out HostSkillResult? error)
    {
        step = 5;
        force = false;
        error = null;
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return ValidateStep(step, out error);
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(payloadJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("step", out JsonElement stepProperty))
                {
                    if (!TryReadInt(stepProperty, out step))
                    {
                        error = HostSkillResult.ValidationError("Invalid step format in payload.");
                        return false;
                    }
                }

                if (readForce)
                {
                    force = ReadBool(root, "force") ?? false;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            error = HostSkillResult.ValidationError($"Invalid JSON payload for {skillId}.");
            return false;
        }

        return ValidateStep(step, out error);
    }

    public static TimeSpan? ReadTimerDuration(JsonElement root)
    {
        if (root.TryGetProperty("durationSeconds", out JsonElement seconds) && seconds.TryGetDouble(out double durationSeconds))
        {
            return TimeSpan.FromSeconds(durationSeconds);
        }

        if (root.TryGetProperty("seconds", out JsonElement shortSeconds) && shortSeconds.TryGetDouble(out double secondsValue))
        {
            return TimeSpan.FromSeconds(secondsValue);
        }

        if (!root.TryGetProperty("duration", out JsonElement duration))
        {
            return null;
        }

        if (duration.ValueKind == JsonValueKind.String && TimeSpan.TryParse(duration.GetString(), out TimeSpan parsed))
        {
            return parsed;
        }

        return duration.TryGetDouble(out double numericDuration)
            ? TimeSpan.FromSeconds(numericDuration)
            : null;
    }

    public static TimeOnly? ReadTimeOfDay(JsonElement root)
    {
        if (TryReadTime(root, "time", out TimeOnly time))
        {
            return time;
        }

        return TryReadTime(root, "timeOfDay", out TimeOnly timeOfDay) ? timeOfDay : null;
    }

    public static List<DayOfWeek>? ReadDaysOfWeek(JsonElement root)
    {
        if (!root.TryGetProperty("daysOfWeek", out JsonElement days) || days.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var result = new List<DayOfWeek>();
        foreach (JsonElement day in days.EnumerateArray())
        {
            if (day.ValueKind == JsonValueKind.String &&
                Enum.TryParse(day.GetString(), ignoreCase: true, out DayOfWeek parsed))
            {
                result.Add(parsed);
            }
        }

        return result;
    }

    public static bool TryReadId(
        string payloadJson,
        string propertyName,
        string invalidJsonMessage,
        string missingIdMessage,
        out Guid id,
        out HostSkillResult? error)
    {
        id = default;
        error = null;
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            error = HostSkillResult.ValidationError(missingIdMessage);
            return false;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(payloadJson);
            if (TryReadGuid(doc.RootElement, propertyName, out id))
            {
                return true;
            }
        }
        catch (JsonException)
        {
            error = HostSkillResult.ValidationError(invalidJsonMessage);
            return false;
        }

        error = HostSkillResult.ValidationError(missingIdMessage);
        return false;
    }

    public static string Cancelled(string idName, Guid id, bool cancelled) =>
        $"{{\"{idName}\":\"{id}\",\"cancelled\":{(cancelled ? "true" : "false")}}}";

    private static bool TryReadInt(JsonElement value, out int number)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number))
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
        {
            return true;
        }

        number = 0;
        return false;
    }

    private static bool TryReadGuid(JsonElement root, string propertyName, out Guid id)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out JsonElement property) &&
            Guid.TryParse(property.GetString(), out id))
        {
            return true;
        }

        if (root.ValueKind == JsonValueKind.String && Guid.TryParse(root.GetString(), out id))
        {
            return true;
        }

        id = default;
        return false;
    }

    private static bool TryReadTime(JsonElement root, string name, out TimeOnly time)
    {
        time = default;
        return root.TryGetProperty(name, out JsonElement value) &&
               value.ValueKind == JsonValueKind.String &&
               TimeOnly.TryParse(value.GetString(), out time);
    }

    private static bool ValidateStep(int step, out HostSkillResult? error)
    {
        if (step is <= 0 or > 100)
        {
            error = HostSkillResult.ValidationError($"Volume step must be between 1 and 100. Received: {step}.");
            return false;
        }

        error = null;
        return true;
    }
}

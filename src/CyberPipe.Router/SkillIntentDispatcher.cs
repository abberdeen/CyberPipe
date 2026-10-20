using System.Text.Json;
using System.Text.Json.Nodes;
using CyberPipe.Contracts;

namespace CyberPipe.Router;

/// <summary>
/// Диспетчер сопоставления распознанных намерений (Tier0..Tier3) с вызовами навыков (TASK-050/TASK-030).
/// Если результат маршрутизации Matched, сопоставляет намерение с навыком, формирует payloadJson,
/// вызывает ISkillInvoker и возвращает обогащённый RouteResult с человекочитаемым ResponseText.
/// </summary>
public static class SkillIntentDispatcher
{
    public static async Task<RouteResult> DispatchAsync(
        ISkillInvoker? invoker,
        RouteResult route,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);

        if (route.Status != RouteStatus.Matched || string.IsNullOrWhiteSpace(route.Intent))
        {
            return route;
        }

        // Если ResponseText уже задан, не перезаписываем
        if (!string.IsNullOrWhiteSpace(route.ResponseText))
        {
            return route;
        }

        string intent = route.Intent;

        // Встроенные текстовые намерения, не требующие вызова ISkillInvoker
        if (intent.Equals("system.greeting", StringComparison.OrdinalIgnoreCase))
        {
            return route with { ResponseText = "Привет! Чем могу помочь?" };
        }

        if (intent.Equals("system.help", StringComparison.OrdinalIgnoreCase))
        {
            return route with
            {
                ResponseText = "Доступные команды:\n" +
                               "• Громкость: «какая громкость», «сделай громкость на 50%», «тише», «громче», «выключи звук», «включи звук»\n" +
                               "• Медиа: «включи музыку», «стоп», «пауза»\n" +
                               "• Статус: «статус системы», «как дела»\n" +
                               "• Таймеры: «поставь таймер на 10 минут»"
            };
        }

        if (intent.Equals("system.cancel", StringComparison.OrdinalIgnoreCase))
        {
            return route with { ResponseText = "Действие отменено." };
        }

        if (invoker is null)
        {
            return route with { ResponseText = $"[Распознано намерение: {intent}]" };
        }

        string skillId = MapIntentToSkillId(intent);
        string payloadJson = BuildSkillPayload(intent, route.Parameters);

        try
        {
            SkillInvocationResult invocation = await invoker.InvokeSkillAsync(
                new SkillInvocationRequest
                {
                    SkillId = skillId,
                    PayloadJson = payloadJson,
                    RunId = new RunId(Guid.NewGuid()),
                },
                cancellationToken).ConfigureAwait(false);

            if (invocation.IsSuccess)
            {
                string displayText = !string.IsNullOrWhiteSpace(invocation.Message)
                    ? invocation.Message
                    : FormatOutputJson(skillId, invocation.OutputJson);

                return route with { ResponseText = displayText };
            }

            string errorMsg = invocation.Message ?? invocation.Error?.Message ?? "Выполнение навыка не удалось.";
            return route with { ResponseText = $"Ошибка выполнения ({skillId}): {errorMsg}" };
        }
        catch (Exception ex)
        {
            return route with { ResponseText = $"Ошибка вызова навыка {skillId}: {ex.Message}" };
        }
    }

    public static string MapIntentToSkillId(string intent) => intent.ToLowerInvariant() switch
    {
        "media.volume" or "volume.set" => "volume.set",
        "volume.get" => "volume.get",
        "volume.up" => "volume.up",
        "volume.down" => "volume.down",
        "volume.mute" => "volume.mute",
        "volume.unmute" => "volume.unmute",
        "volume.toggle_mute" => "volume.toggle_mute",
        "media.play" => "media.play",
        "media.stop" or "media.pause" => "media.pause",
        "media.playpause" => "media.playpause",
        "media.next" => "media.next",
        "media.previous" => "media.previous",
        "system.status" => "system.status",
        "system.timer" or "timer.set" => "timer.set",
        "timer.cancel" => "timer.cancel",
        "timer.list" => "timer.list",
        "alarm.set" => "alarm.set",
        "alarm.cancel" => "alarm.cancel",
        "alarm.list" => "alarm.list",
        _ => intent,
    };

    public static string BuildSkillPayload(string intent, IReadOnlyDictionary<string, string> parameters)
    {
        var json = new JsonObject();
        string lower = intent.ToLowerInvariant();

        switch (lower)
        {
            case "media.volume":
            case "volume.set":
                if (parameters.TryGetValue("level", out string? lvlStr) && int.TryParse(lvlStr, out int lvl))
                {
                    json["volume"] = lvl;
                }
                else if (parameters.TryGetValue("volume", out string? vStr) && int.TryParse(vStr, out int v))
                {
                    json["volume"] = v;
                }
                break;

            case "volume.up":
            case "volume.down":
                if (parameters.TryGetValue("step", out string? sStr) && int.TryParse(sStr, out int step))
                {
                    json["step"] = step;
                }
                break;

            case "media.play":
                if (parameters.TryGetValue("query", out string? q))
                {
                    json["title"] = q;
                }
                break;

            case "system.timer":
            case "timer.set":
                int seconds = 60;
                if (parameters.TryGetValue("duration", out string? dStr) && int.TryParse(dStr, out int d))
                {
                    seconds = d;
                    if (parameters.TryGetValue("unit", out string? unit))
                    {
                        string u = unit.ToLowerInvariant();
                        if (u.StartsWith("мин") || u.StartsWith("min"))
                        {
                            seconds = d * 60;
                        }
                        else if (u.StartsWith("час") || u.StartsWith("h"))
                        {
                            seconds = d * 3600;
                        }
                    }
                }
                json["durationSeconds"] = seconds;
                json["title"] = "Timer";
                break;

            default:
                foreach (var (k, val) in parameters)
                {
                    json[k] = val;
                }
                break;
        }

        return json.ToJsonString();
    }

    private static string FormatOutputJson(string skillId, string outputJson)
    {
        if (string.IsNullOrWhiteSpace(outputJson) || outputJson == "{}")
        {
            return "Команда успешно выполнена.";
        }

        try
        {
            using var doc = JsonDocument.Parse(outputJson);
            var root = doc.RootElement;
            if (skillId == "system.status" && root.ValueKind == JsonValueKind.Object)
            {
                var sb = new System.Text.StringBuilder("Статус хоста:\n");
                if (root.TryGetProperty("machineName", out var m)) sb.AppendLine($"• Компьютер: {m.GetString()}");
                if (root.TryGetProperty("cpuUsagePercent", out var c)) sb.AppendLine($"• Загрузка CPU: {c.GetDouble():F1}%");
                if (root.TryGetProperty("availableMemoryMb", out var av) && root.TryGetProperty("totalMemoryMb", out var tot))
                    sb.AppendLine($"• Память: {av.GetInt64()} MB свободно из {tot.GetInt64()} MB");
                if (root.TryGetProperty("uptime", out var up)) sb.AppendLine($"• Аптайм: {up.GetString()}");
                return sb.ToString().TrimEnd();
            }
        }
        catch
        {
            // fallback
        }

        return outputJson;
    }
}

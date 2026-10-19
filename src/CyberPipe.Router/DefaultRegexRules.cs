using CyberPipe.Contracts;

namespace CyberPipe.Router;

/// <summary>
/// Стартовые правила по умолчанию для Tier 0 regex-роутера (TASK-030).
/// Охватывают типовые команды: приветствие, статус, таймеры, громкость, медиа.
/// </summary>
public static class DefaultRegexRules
{
    public static IReadOnlyList<RegexRuleDefinition> Rules { get; } =
    [
        new()
        {
            Name = "Timer",
            Pattern = @"^(поставь\s+)?таймер(\s+на)?\s+(?<duration>\d+)\s*(?<unit>секунд[уы]?|минут[уы]?|час[аов]?|sec|min|h)",
            Intent = "system.timer",
            Priority = 50,
            IgnoreCase = true,
            TimeoutMs = 200
        },
        new()
        {
            Name = "Volume",
            Pattern = @"^(сделай\s+)?громкость\s+(на\s+)?(?<level>\d+)(%?)",
            Intent = "media.volume",
            Priority = 50,
            IgnoreCase = true,
            TimeoutMs = 200
        },
        new()
        {
            Name = "MediaPlay",
            Pattern = @"^(включи|играй|плей|play)(\s+(?<query>.+))?$",
            Intent = "media.play",
            Priority = 30,
            IgnoreCase = true,
            TimeoutMs = 200
        },
        new()
        {
            Name = "MediaStop",
            Pattern = @"^(стоп|останови|пауза|stop|pause)$",
            Intent = "media.stop",
            Priority = 30,
            IgnoreCase = true,
            TimeoutMs = 200
        },
        new()
        {
            Name = "Status",
            Pattern = @"^(статус|как\s+дела|состояние|здоровье|status|health)(\b|!|\.|\?)",
            Intent = "system.status",
            Priority = 20,
            IgnoreCase = true,
            TimeoutMs = 200
        },
        new()
        {
            Name = "Greeting",
            Pattern = @"^(привет|здравствуй(те)?|добрый\s+(день|вечер|утро)|салют|hello|hi|hey)(\b|!|\.|\?)",
            Intent = "system.greeting",
            Priority = 10,
            IgnoreCase = true,
            TimeoutMs = 200
        }
    ];
}

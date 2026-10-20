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
            Pattern = @"^((сделай|поставь|установи)\s+)?громкость\s+(на\s+)?(?<level>\d+)(%?)$|^((set)\s+)?volume\s+(to\s+)?(?<level>\d+)(%?)$|^(сделай\s+)?громкость\s+(на\s+)?(?<level>\d+)(%?)",
            Intent = "media.volume",
            Priority = 50,
            IgnoreCase = true,
            TimeoutMs = 200
        },
        new()
        {
            Name = "VolumeGet",
            Pattern = @"^(какая\s+)?(текущая\s+)?громкость(\s+системы)?(\s*\?)?$|^уровень\s+(громкости|звука)(\s*\?)?$|^(get\s+)?(current\s+)?volume(\s*\?)?$|^what\s+is\s+the\s+volume(\s*\?)?$",
            Intent = "volume.get",
            Priority = 45,
            IgnoreCase = true,
            TimeoutMs = 200
        },
        new()
        {
            Name = "VolumeUp",
            Pattern = @"^((прибавь|увеличь|добавь|сделай)\s+)?громче$|^(прибавь|увеличь|добавь)\s+громкость(\s+на\s+(?<step>\d+)(%?))?$|^(volume\s+up|louder)$",
            Intent = "volume.up",
            Priority = 48,
            IgnoreCase = true,
            TimeoutMs = 200
        },
        new()
        {
            Name = "VolumeDown",
            Pattern = @"^((убавь|уменьши|сделай)\s+)?тише$|^(убавь|уменьши)\s+громкость(\s+на\s+(?<step>\d+)(%?))?$|^(volume\s+down|quieter)$",
            Intent = "volume.down",
            Priority = 48,
            IgnoreCase = true,
            TimeoutMs = 200
        },
        new()
        {
            Name = "VolumeMute",
            Pattern = @"^(выключи(ть)?\s+звук|без\s+звука|заглуши(ть)?|мут|mute(\s+sound)?)$",
            Intent = "volume.mute",
            Priority = 48,
            IgnoreCase = true,
            TimeoutMs = 200
        },
        new()
        {
            Name = "VolumeUnmute",
            Pattern = @"^(включи(ть)?\s+звук|со\s+звуком|размут(ить)?|unmute(\s+sound)?)$",
            Intent = "volume.unmute",
            Priority = 48,
            IgnoreCase = true,
            TimeoutMs = 200
        },
        new()
        {
            Name = "VolumeToggleMute",
            Pattern = @"^(переключи(ть)?\s+звук|toggle\s+mute)$",
            Intent = "volume.toggle_mute",
            Priority = 48,
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

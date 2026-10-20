using CyberPipe.Contracts;

namespace CyberPipe.Router;

/// <summary>
/// Стандартный набор эталонных примеров для семантического роутера Tier 1 (TASK-031).
/// Включает русский и английский варианты для базовых намерений.
/// </summary>
public static class DefaultSemanticExamples
{
    /// <summary>
    /// Возвращает список встроенных эталонов.
    /// </summary>
    public static IReadOnlyList<SemanticExampleDefinition> GetDefaults() =>
    [
        // system.help
        new() { Text = "помощь", Intent = "system.help" },
        new() { Text = "что ты умеешь", Intent = "system.help" },
        new() { Text = "покажи команды", Intent = "system.help" },
        new() { Text = "справка по системе", Intent = "system.help" },
        new() { Text = "help", Intent = "system.help" },
        new() { Text = "what can you do", Intent = "system.help" },
        new() { Text = "show commands", Intent = "system.help" },

        // system.status
        new() { Text = "статус системы", Intent = "system.status" },
        new() { Text = "состояние сервиса", Intent = "system.status" },
        new() { Text = "как дела", Intent = "system.status" },
        new() { Text = "проверь статус", Intent = "system.status" },
        new() { Text = "system status", Intent = "system.status" },
        new() { Text = "health check", Intent = "system.status" },
        new() { Text = "how are you", Intent = "system.status" },

        // system.cancel
        new() { Text = "отмени операцию", Intent = "system.cancel" },
        new() { Text = "прерви цепочку", Intent = "system.cancel" },
        new() { Text = "стоп", Intent = "system.cancel" },
        new() { Text = "отмена", Intent = "system.cancel" },
        new() { Text = "cancel operation", Intent = "system.cancel" },
        new() { Text = "stop chain", Intent = "system.cancel" },
        new() { Text = "abort", Intent = "system.cancel" },

        // diagnostics.run
        new() { Text = "запусти диагностику", Intent = "diagnostics.run" },
        new() { Text = "проверь систему и сеть", Intent = "diagnostics.run" },
        new() { Text = "диагностика хоста", Intent = "diagnostics.run" },
        new() { Text = "run diagnostics", Intent = "diagnostics.run" },
        new() { Text = "system diagnostics", Intent = "diagnostics.run" },

        // knowledge.search
        new() { Text = "найди в заметках", Intent = "knowledge.search" },
        new() { Text = "поиск по базе знаний", Intent = "knowledge.search" },
        new() { Text = "что я знаю про это", Intent = "knowledge.search" },
        new() { Text = "вспомни документ", Intent = "knowledge.search" },
        new() { Text = "search notes", Intent = "knowledge.search" },
        new() { Text = "search knowledge base", Intent = "knowledge.search" },

        // volume.get
        new() { Text = "какая громкость", Intent = "volume.get" },
        new() { Text = "текущая громкость", Intent = "volume.get" },
        new() { Text = "уровень звука", Intent = "volume.get" },
        new() { Text = "проверь громкость", Intent = "volume.get" },
        new() { Text = "what is the volume", Intent = "volume.get" },
        new() { Text = "current volume", Intent = "volume.get" },
        new() { Text = "get volume", Intent = "volume.get" },

        // volume.set
        new() { Text = "установи громкость", Intent = "media.volume" },
        new() { Text = "поставь звук", Intent = "media.volume" },
        new() { Text = "измени громкость", Intent = "media.volume" },
        new() { Text = "set volume", Intent = "media.volume" },

        // volume.up
        new() { Text = "сделай громче", Intent = "volume.up" },
        new() { Text = "увеличь громкость", Intent = "volume.up" },
        new() { Text = "прибавь звук", Intent = "volume.up" },
        new() { Text = "make it louder", Intent = "volume.up" },
        new() { Text = "turn up the volume", Intent = "volume.up" },

        // volume.down
        new() { Text = "сделай тише", Intent = "volume.down" },
        new() { Text = "уменьши громкость", Intent = "volume.down" },
        new() { Text = "убавь звук", Intent = "volume.down" },
        new() { Text = "make it quieter", Intent = "volume.down" },
        new() { Text = "turn down the volume", Intent = "volume.down" },

        // volume.mute
        new() { Text = "выключи звук", Intent = "volume.mute" },
        new() { Text = "заглуши звук", Intent = "volume.mute" },
        new() { Text = "mute audio", Intent = "volume.mute" },

        // volume.unmute
        new() { Text = "включи звук", Intent = "volume.unmute" },
        new() { Text = "разглуши звук", Intent = "volume.unmute" },
        new() { Text = "unmute audio", Intent = "volume.unmute" },
    ];
}

/// <summary>
/// Провайдер эталонных примеров с поддержкой конфигурации.
/// </summary>
public sealed class ConfigurationSemanticExampleProvider : ISemanticExampleProvider
{
    private readonly IReadOnlyList<SemanticExampleDefinition> _examples;

    public ConfigurationSemanticExampleProvider(CyberPipeOptions? options = null)
    {
        var configured = options?.Router?.Tier1Examples;
        if (configured is not null && configured.Count > 0)
        {
            var combined = new List<SemanticExampleDefinition>(DefaultSemanticExamples.GetDefaults());
            combined.AddRange(configured);
            _examples = combined;
        }
        else
        {
            _examples = DefaultSemanticExamples.GetDefaults();
        }
    }

    public IReadOnlyList<SemanticExampleDefinition> GetExamples() => _examples;
}

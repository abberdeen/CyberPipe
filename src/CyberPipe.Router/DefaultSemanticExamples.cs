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

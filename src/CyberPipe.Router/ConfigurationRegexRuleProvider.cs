using CyberPipe.Contracts;

namespace CyberPipe.Router;

/// <summary>
/// Провайдер правил для Tier 0 из конфигурации с резервным набором по умолчанию.
/// </summary>
public sealed class ConfigurationRegexRuleProvider : IRegexRuleProvider
{
    private readonly CyberPipeOptions _options;

    public ConfigurationRegexRuleProvider(CyberPipeOptions? options = null)
    {
        _options = options ?? new CyberPipeOptions();
    }

    public IReadOnlyList<RegexRuleDefinition> GetRules()
    {
        if (_options.Router.RegexRules != null && _options.Router.RegexRules.Count > 0)
        {
            return _options.Router.RegexRules;
        }

        return DefaultRegexRules.Rules;
    }
}

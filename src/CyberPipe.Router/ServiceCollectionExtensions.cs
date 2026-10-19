using CyberPipe.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CyberPipe.Router;

/// <summary>
/// Модуль регистрации маршрутизации Tier 0 → Tier 1 → Tier 2/3, бюджета и семантического кэша (TASK-090).
/// </summary>
public static class RouterServiceCollectionExtensions
{
    /// <summary>Регистрирует роутеры, губернатора бюджета, кэш и ценовой провайдер.</summary>
    public static IServiceCollection AddCyberPipeRouter(this IServiceCollection services, CyberPipeOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.TryAddSingleton<IRegexRuleProvider>(sp =>
            new ConfigurationRegexRuleProvider(sp.GetService<CyberPipeOptions>() ?? options));
        services.TryAddSingleton<Tier0RegexRouter>();
        services.TryAddSingleton<IEmbeddingProvider>(sp =>
            new OnnxEmbeddingProvider(
                (sp.GetService<CyberPipeOptions>() ?? options).Models.EmbeddingModelPath,
                NullLogger.Instance));
        services.TryAddSingleton<Tier1SemanticRouter>();
        services.TryAddSingleton<ISemanticCache, SemanticCache>();
        services.TryAddSingleton<IBudgetStore, InMemoryBudgetStore>();
        services.TryAddSingleton<ITokenBudgetGovernor, TokenBudgetGovernor>();
        services.TryAddSingleton<Tier23LlmRouter>();

        return services;
    }
}

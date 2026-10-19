using CyberPipe.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CyberPipe.Security;

/// <summary>
/// Модуль регистрации безопасности (TASK-090): секреты, anti-wipe, аудио-безопасность,
/// egress-санитизация и авторизация Owner.
/// </summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>Регистрирует сервисы безопасности.</summary>
    public static IServiceCollection AddCyberPipeSecurity(this IServiceCollection services, CyberPipeOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.TryAddSingleton<ISecretStore, PlatformSecretStore>();
        services.TryAddSingleton(sp => new StaticOptionsMonitor<CyberPipeOptions>(
            sp.GetService<CyberPipeOptions>() ?? options));
        services.TryAddSingleton<IOptionsMonitor<CyberPipeOptions>>(sp =>
            sp.GetRequiredService<StaticOptionsMonitor<CyberPipeOptions>>());
        services.TryAddSingleton<IOwnerWhitelistProvider>(sp =>
            new OptionsOwnerWhitelistProvider(sp.GetRequiredService<IOptionsMonitor<CyberPipeOptions>>()));
        services.TryAddSingleton<IOwnerAuthorizer, OwnerAuthorizer>();
        services.TryAddSingleton<IOwnerGuard, AnyChannelOwnerGuard>();
        services.AddKeyedSingleton<IOwnerGuard>(
            "telegram",
            (sp, key) => new OwnerAuthorizerGuard(sp.GetRequiredService<IOwnerAuthorizer>(), OwnerChannel.Telegram));
        services.AddKeyedSingleton<IOwnerGuard>(
            "desktop",
            (sp, key) => new OwnerAuthorizerGuard(sp.GetRequiredService<IOwnerAuthorizer>(), OwnerChannel.Desktop));
        services.AddKeyedSingleton<IOwnerGuard>(
            "cli",
            (sp, key) => new OwnerAuthorizerGuard(sp.GetRequiredService<IOwnerAuthorizer>(), OwnerChannel.Cli));
        services.TryAddSingleton<IAntiWipeGuard>(sp =>
            new AntiWipeGuard((sp.GetService<CyberPipeOptions>() ?? options).AntiWipe));
        services.TryAddSingleton<IAudioContentSafetyEvaluator>(sp =>
            new AudioContentSafetyEvaluator(
                sp.GetService<IEmbeddingProvider>(),
                (sp.GetService<CyberPipeOptions>() ?? options).AudioContentSafety));
        services.TryAddSingleton<IKnownSecretsProvider, DefaultKnownSecretsProvider>();
        services.TryAddSingleton<IEgressSanitizer>(sp => new EgressSanitizer(
            (sp.GetService<CyberPipeOptions>() ?? options).EgressSanitizer,
            sp.GetService<IKnownSecretsProvider>()));

        return services;
    }
}

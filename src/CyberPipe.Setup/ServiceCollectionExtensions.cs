using CyberPipe.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CyberPipe.Setup;

/// <summary>
/// Модуль регистрации мастера первого запуска (TASK-090): движок мастера,
/// состояние, реестр владельцев (он же белый список) и адаптеры портов.
/// </summary>
public static class SetupServiceCollectionExtensions
{
    /// <summary>Регистрирует сервисы мастера.</summary>
    public static IServiceCollection AddCyberPipeSetup(this IServiceCollection services, CyberPipeOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // Реестр владельцев мастера переопределяет поставщика белого списка
        // модуля безопасности: регистрации применяются немедленно и без перезапуска.
        services.AddSingleton(sp => new SetupOwnerRegistry((sp.GetService<CyberPipeOptions>() ?? options).Owner));
        services.AddSingleton<IOwnerRegistrationService>(sp => sp.GetRequiredService<SetupOwnerRegistry>());
        services.AddSingleton<IOwnerWhitelistProvider>(sp => sp.GetRequiredService<SetupOwnerRegistry>());

        services.TryAddSingleton<ISetupStateRepository, DefaultSetupStateRepository>();
        services.TryAddSingleton<SetupWizardEngine>();

        return services;
    }
}

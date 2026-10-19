using CyberPipe.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CyberPipe.ServiceControl;

/// <summary>
/// Модуль регистрации управления службой (TASK-090): менеджер управления и SCM.
/// </summary>
public static class ServiceControlServiceCollectionExtensions
{
    /// <summary>Регистрирует управление службой.</summary>
    public static IServiceCollection AddCyberPipeServiceControl(
        this IServiceCollection services,
        CyberPipeOptions options,
        IServiceControlChannel channel)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(channel);

        services.AddSingleton(channel);
        if (OperatingSystem.IsWindows())
        {
            services.TryAddSingleton<IScmManager, WindowsScmManager>();
        }

        services.TryAddSingleton<ServiceControllerManager>();

        return services;
    }
}

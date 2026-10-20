using CyberPipe.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberPipe.Models;

/// <summary>
/// Модуль регистрации каталогов, установщика и советника моделей (TASK-090).
/// Каталоги 15+8 читаются eagerly (fail-fast при отсутствии файлов).
/// </summary>
public static class ModelsServiceCollectionExtensions
{
    /// <summary>Регистрирует каталог, провайдеры, установщики и советник.</summary>
    public static IServiceCollection AddCyberPipeModels(this IServiceCollection services, CyberPipeOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        string baseDirectory = AppContext.BaseDirectory;
        string localPath = ResolvePath(options.Models.LocalCatalogPath, baseDirectory);
        string cloudPath = ResolvePath(options.Models.CloudCatalogPath, baseDirectory);
        string embeddingPath = Path.Combine(baseDirectory, "models", "embedding-models.json");

        services.AddSingleton<IModelCatalog>(sp => FileModelCatalog.Load(localPath, cloudPath, embeddingPath));
        services.TryAddSingleton<IChatModelProvider>(sp => new OllamaChatModelProvider(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<IModelCatalog>(),
            sp.GetService<IOptions<LocalModelInstallerOptions>>(),
            sp.GetService<CyberPipeOptions>() ?? options,
            sp.GetService<Microsoft.Extensions.Logging.ILogger<OllamaChatModelProvider>>()));
        services.TryAddSingleton(sp => new HttpClient());
        services.TryAddSingleton<IDiskSpaceProbe, DriveDiskSpaceProbe>();
        services.TryAddSingleton<ILocalRuntimeInstaller, OllamaLocalRuntimeInstaller>();
        services.TryAddSingleton<ILocalModelInstaller, OllamaLocalModelInstaller>();
        services.TryAddSingleton<IModelInstaller, SetupModelInstallerAdapter>();
        services.TryAddSingleton<IModelCatalogProvider, SetupModelCatalogProviderAdapter>();
        services.TryAddSingleton<IHardwareCapabilitiesProbe, SystemHardwareProbe>();
        services.TryAddSingleton<IModelCompatibilityAdvisor, ModelCompatibilityAdvisor>();

        return services;
    }

    private static string ResolvePath(string configuredPath, string baseDirectory) =>
        Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(baseDirectory, configuredPath.Replace('/', Path.DirectorySeparatorChar));
}

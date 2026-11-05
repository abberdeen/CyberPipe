using CyberPipe.Contracts;
using CyberPipe.Desktop.ViewModels;
using CyberPipe.Models;
using Microsoft.Extensions.Logging;

namespace CyberPipe.Desktop.Services;

/// <summary>
/// Собирает экран моделей: каталоги из <c>models</c>, установщики Ollama и советник по железу.
/// Ошибка загрузки не роняет окно — экран открывается без установщика.
/// </summary>
public static class DesktopModelScreen
{
    public static ModelsViewModel Create(IDesktopNotificationService notifications, ILogger? logger)
    {
        IModelCatalog? catalog = null;
        ILocalModelInstaller? localModelInstaller = null;
        ILocalRuntimeInstaller? localRuntimeInstaller = null;
        IModelCompatibilityAdvisor? hardwareAdvisor = null;

        try
        {
            if (TryResolveCatalogPaths(out string localPath, out string cloudPath, out string embeddingPath))
            {
                catalog = FileModelCatalog.Load(localPath, cloudPath, embeddingPath);
                var httpClient = new HttpClient();
                localRuntimeInstaller = new OllamaLocalRuntimeInstaller(httpClient);
                localModelInstaller = new OllamaLocalModelInstaller(httpClient, catalog);
                hardwareAdvisor = new ModelCompatibilityAdvisor(catalog, new SystemHardwareProbe());
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to initialize local model catalog / installer in Desktop.");
        }

        LocalModelInstallerViewModel? localInstallerVm = null;
        OllamaManagerViewModel? ollamaManager = null;
        IRemoteProviderService? remoteProviders = null;
        string overridesPath = CyberPipeConfigurationLoader.GetDefaultUiOverridesPath();
        if (catalog is not null && localModelInstaller is not null && localRuntimeInstaller is not null)
        {
            IReadOnlyList<ModelDescriptorDto> localModels = catalog.Find(hostingType: ModelHostingType.Local);
            localInstallerVm = new LocalModelInstallerViewModel(
                localModels,
                localModelInstaller,
                localRuntimeInstaller,
                hardwareAdvisor,
                notifications);
            ollamaManager = new OllamaManagerViewModel(
                localModels,
                localModelInstaller,
                localRuntimeInstaller,
                hardwareAdvisor,
                notifications);
            remoteProviders = new RemoteProviderService(
                overridesPath,
                httpClient: new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
                cloudCatalog: catalog.CloudModels);
        }

        return new ModelsViewModel(
            assignments: null,
            catalog: catalog?.Find(),
            notifications: notifications,
            installer: localInstallerVm,
            modelCatalog: catalog,
            options: Program.Options,
            uiOverridesPath: overridesPath,
            ollamaManager: ollamaManager,
            remoteProviders: remoteProviders);
    }

    private static bool TryResolveCatalogPaths(out string localPath, out string cloudPath, out string embeddingPath)
    {
        string baseDir = AppContext.BaseDirectory;
        localPath = Path.Combine(baseDir, "models", "local-models.json");
        cloudPath = Path.Combine(baseDir, "models", "cloud-models.json");
        embeddingPath = Path.Combine(baseDir, "models", "embedding-models.json");

        if (!File.Exists(localPath) || !File.Exists(cloudPath) || !File.Exists(embeddingPath))
        {
            string candidateDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
            if (Directory.Exists(candidateDir))
            {
                localPath = Path.Combine(candidateDir, "local-models.json");
                cloudPath = Path.Combine(candidateDir, "cloud-models.json");
                embeddingPath = Path.Combine(candidateDir, "embedding-models.json");
            }
        }

        if (!File.Exists(localPath) || !File.Exists(cloudPath) || !File.Exists(embeddingPath))
        {
            string sourceCatalogs = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "src", "CyberPipe.Models", "Catalogs"));
            if (Directory.Exists(sourceCatalogs))
            {
                localPath = Path.Combine(sourceCatalogs, "local-models.json");
                cloudPath = Path.Combine(sourceCatalogs, "cloud-models.json");
                embeddingPath = Path.Combine(sourceCatalogs, "embedding-models.json");
            }
        }

        return File.Exists(localPath) && File.Exists(cloudPath) && File.Exists(embeddingPath);
    }
}

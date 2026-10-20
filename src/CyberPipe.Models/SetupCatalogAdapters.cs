using CyberPipe.Contracts;

namespace CyberPipe.Models;

/// <summary>
/// Адаптеры портов мастера первого запуска (TASK-063) поверх каталога и установщика (TASK-090).
/// </summary>
public sealed class SetupModelInstallerAdapter : IModelInstaller
{
    private readonly ILocalModelInstaller _installer;
    private readonly IModelCatalog _catalog;

    /// <summary>Создаёт адаптер.</summary>
    public SetupModelInstallerAdapter(ILocalModelInstaller installer, IModelCatalog catalog)
    {
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <inheritdoc />
    public async Task<Result<bool>> InstallModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return Result<bool>.Failure("Model.NoRuntimeId", "Model id is required.");
        }

        if (IsEmbeddingModel(modelId))
        {
            Result<EmbeddingInstallResult> embedResult = await _installer.EnsureEmbeddingAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return embedResult.IsSuccess
                ? Result<bool>.Success(true)
                : Result<bool>.Failure(embedResult.Error!);
        }

        ModelDescriptorDto? descriptor = _catalog.GetById(modelId);
        if (descriptor is null)
        {
            return Result<bool>.Failure("Model.NotInCatalog", $"Model '{modelId}' is not in the catalog.");
        }

        Result<ModelProbeResult> result = await _installer.InstallAsync(descriptor, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(result.Error!);
    }

    /// <inheritdoc />
    public async Task<bool> IsModelInstalledAsync(string modelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        if (IsEmbeddingModel(modelId))
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string sentinel = Path.Combine(localAppData, "CyberPipe", "embeddings", "multilingual-e5-small", "model.onnx");
            return File.Exists(sentinel) && new FileInfo(sentinel).Length > 0;
        }

        string runtimeId = _catalog.GetById(modelId)?.ModelId ?? modelId;
        IReadOnlyList<InstalledLocalModel> installed =
            await _installer.ListInstalledAsync(cancellationToken).ConfigureAwait(false);
        return installed.Any(m => string.Equals(m.ModelId, runtimeId, StringComparison.Ordinal));
    }

    private bool IsEmbeddingModel(string modelId)
    {
        if (string.Equals(modelId, "multilingual-e5-small", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(modelId, "intfloat/multilingual-e5-small", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(modelId, _catalog.EmbeddingModel?.Model?.Id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(modelId, _catalog.EmbeddingModel?.Model?.ModelId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        ModelDescriptorDto? descriptor = _catalog.GetById(modelId);
        return descriptor is not null &&
            (descriptor.RecommendedFor == ModelTaskCategory.EmbeddingTier1 ||
             string.Equals(descriptor.Provider, "ONNX", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Поставщик каталога для мастера первого запуска поверх <see cref="IModelCatalog" /> (TASK-090).
/// </summary>
public sealed class SetupModelCatalogProviderAdapter : IModelCatalogProvider
{
    private readonly IModelCatalog _catalog;

    /// <summary>Создаёт адаптер.</summary>
    public SetupModelCatalogProviderAdapter(IModelCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ModelDescriptorDto>> GetCatalogAsync(
        ModelHostingType? hostingType = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_catalog.Find(hostingType: hostingType));
}
